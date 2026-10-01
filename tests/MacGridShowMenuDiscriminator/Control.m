#import <Cocoa/Cocoa.h>
#import <objc/runtime.h>
#include <stdatomic.h>
#include <limits.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>
#include <string.h>

/// Fixed preallocated facts are populated in callbacks; JSON is constructed only after shutdown.
typedef struct {
    BOOL owner, current, attached, installing, frame, baseline, attachmentEqual, epochEqual, queued, returned, exception;
} Entry;
typedef struct { unsigned phase; BOOL attachmentEqual, epochEqual, current, attached; } Life;
static Entry entries[16]; static Life lives[16];
static NSUInteger entryCount, lifeCount, entryOverflow, lifeOverflow;
static atomic_uint offMainEntries;
static NSUInteger requests, dispatches, opens, closes, detaches;
static BOOL readyPublished, finishConsumed, normalShutdown;
static NSString *sessionDirectory;
static NSTimer *finishTimer;

/// Synthetic Table with stable root identity and next-turn admission; no action warmup exists.
@interface PairTable : NSAccessibilityElement <NSMenuDelegate>
@property(nonatomic,weak) NSView *ownerView;
@property(nonatomic,strong) NSMenu *ownedMenu;
@property BOOL attached, queued;
- (BOOL)admit;
- (void)present;
- (void)shutdown;
@end
@interface PairGroup : NSView
@property(nonatomic,strong) PairTable *table;
@end
@implementation PairGroup
- (BOOL)isAccessibilityElement { return YES; }
- (NSString *)accessibilityRole { return NSAccessibilityGroupRole; }
- (NSArray *)accessibilityChildren { return self.table ? @[self.table] : @[]; }
@end

static void Lifetime(PairTable *table,unsigned phase) {
    if (lifeCount>=16) { lifeOverflow++; return; }
    lives[lifeCount++]=(Life){phase,readyPublished && table.attached,readyPublished && table.attached,
        [(PairGroup *)table.ownerView table]==table,table.attached};
}
@implementation PairTable
- (BOOL)isAccessibilityElement { return YES; }
- (BOOL)isAccessibilityEnabled { return YES; }
- (NSString *)accessibilityRole { return NSAccessibilityTableRole; }
- (NSString *)accessibilityIdentifier { return @"mote.csv.table"; }
- (id)accessibilityParent { return self.ownerView; }
- (id)accessibilityWindow { return self.ownerView.window; }
- (id)accessibilityTopLevelUIElement { return self.ownerView.window; }
- (NSInteger)accessibilityRowCount { return 0; }
- (NSInteger)accessibilityColumnCount { return 0; }
- (NSArray *)accessibilityRows { return @[]; }
- (NSArray *)accessibilityColumns { return @[]; }
- (NSArray *)accessibilityChildren { return @[]; }
/// The control's frame-present fact denotes its initialized synthetic frame, not a document frame.
- (BOOL)admit {
    Entry entry={0};
    entry.owner=self.ownerView!=nil; entry.current=[(PairGroup *)self.ownerView table]==self;
    entry.attached=self.attached; entry.frame=self.ownerView.window!=nil;
    entry.baseline=readyPublished; entry.attachmentEqual=readyPublished && self.attached; entry.epochEqual=readyPublished;
    BOOL result=NO;
    @try {
        if (entry.owner && entry.current && entry.attached && entry.frame && !self.queued && requests==0) {
            self.queued=YES; requests++; entry.queued=YES; Lifetime(self,0);
            [self performSelector:@selector(present) withObject:nil afterDelay:0]; result=YES;
        }
    } @catch (NSException *exception) { (void)exception; entry.exception=YES; }
    entry.returned=result;
    if (entryCount<16) entries[entryCount++]=entry; else entryOverflow++;
    return result;
}
- (void)present {
    dispatches++; Lifetime(self,1);
    if (!self.attached || finishConsumed || !self.ownerView.window) return;
    NSMenu *menu=[[NSMenu alloc] initWithTitle:@"Synthetic control"];
    [menu addItemWithTitle:@"Synthetic no-op" action:NULL keyEquivalent:@""];
    menu.delegate=self; self.ownedMenu=menu;
    [menu popUpMenuPositioningItem:nil atLocation:NSMakePoint(30,30) inView:self.ownerView];
    self.ownedMenu=nil;
}
- (void)menuWillOpen:(NSMenu *)menu { if (menu==self.ownedMenu) { opens++; Lifetime(self,2); } }
- (void)menuDidClose:(NSMenu *)menu { if (menu==self.ownedMenu) { closes++; Lifetime(self,3); } }
/// Marker cleanup cancels only this owner menu and then closes its synthetic window normally.
- (void)shutdown {
    if (normalShutdown) return;
    [NSObject cancelPreviousPerformRequestsWithTarget:self];
    [self.ownedMenu cancelTracking];
    [self.ownerView.window close]; self.attached=NO; [(PairGroup *)self.ownerView setTable:nil];
    detaches++; Lifetime(self,4);
    normalShutdown=YES; [finishTimer invalidate]; finishTimer=nil;
    [NSApp stop:nil];
    [NSApp postEvent:[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint
        modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0] atStart:NO];
}
@end

/// Every actual action entry, including off-main refusal, is accounted; no owner lookup runs off-main.
static BOOL ShowMenu(PairTable *table,SEL selector) {
    (void)selector;
    if (![NSThread isMainThread]) { unsigned count=atomic_load_explicit(&offMainEntries,memory_order_relaxed);
        while (count<UINT_MAX && !atomic_compare_exchange_weak_explicit(&offMainEntries,&count,count+1,memory_order_relaxed,memory_order_relaxed)) {}
        return NO; }
    return [table admit];
}
static NSString *Session(const char *arg) {
    NSString *cache=[[NSFileManager defaultManager].currentDirectoryPath stringByAppendingPathComponent:@".cache"];
    NSString *root=[cache stringByResolvingSymlinksInPath];
    if (![cache isEqualToString:root]) return nil;
    NSString *original=[@(arg) stringByStandardizingPath];
    NSString *path=[original stringByResolvingSymlinksInPath]; BOOL directory=NO;
    if (![original isAbsolutePath] || ![original isEqualToString:path]) return nil;
    return [path hasPrefix:[root stringByAppendingString:@"/"]] &&
        [[NSFileManager defaultManager] fileExistsAtPath:path isDirectory:&directory] && directory ? path : nil;
}
/// Markers are fixed-size, fixed-token regular files inside the already confined session directory.
static BOOL PairFinishMarker(NSString *name,NSString *token) {
    NSString *path=[sessionDirectory stringByAppendingPathComponent:name];
    NSData *expected=[token dataUsingEncoding:NSUTF8StringEncoding];
    if (expected.length>32) return NO;
    int descriptor=open(path.fileSystemRepresentation,O_RDONLY|O_NOFOLLOW|O_NONBLOCK);
    if (descriptor<0) return NO;
    struct stat facts; char bytes[33];
    BOOL valid=fstat(descriptor,&facts)==0 && S_ISREG(facts.st_mode) && facts.st_size==(off_t)expected.length;
    ssize_t count=valid ? read(descriptor,bytes,sizeof(bytes)) : -1;
    close(descriptor);
    return valid && count==(ssize_t)expected.length && memcmp(bytes,expected.bytes,expected.length)==0;
}
/// Export uses only a fixed Boolean/counter vocabulary, never AX strings, pointers or source content.
static NSDictionary *Facts(void) {
    NSMutableArray *action=[NSMutableArray array], *lifetime=[NSMutableArray array];
    for (NSUInteger i=0;i<entryCount;i++) {
        Entry e=entries[i];
        [action addObject:@{@"on_main_thread":@YES,@"owner_lookup":@(e.owner),@"receiver_equal_current_root":@(e.current),
            @"attached":@(e.attached),@"installing":@(e.installing),@"frame_present":@(e.frame),
            @"ready_baseline_available":@(e.baseline),@"attachment_equal_baseline":@(e.attachmentEqual),
            @"epoch_equal_baseline":@(e.epochEqual),@"queue_result":@(e.queued),@"method_return":@(e.returned),@"caught_exception":@(e.exception)}];
    }
    NSArray *phases=@[@"queue",@"dispatch",@"open",@"close",@"detach"];
    for (NSUInteger i=0;i<lifeCount;i++) {
        Life e=lives[i]; [lifetime addObject:@{@"phase":phases[e.phase],@"attachment_equal_baseline":@(e.attachmentEqual),
            @"epoch_equal_baseline":@(e.epochEqual),@"receiver_equal_current_root":@(e.current),@"attached":@(e.attached)}];
    }
    unsigned off=atomic_load_explicit(&offMainEntries,memory_order_relaxed);
    NSUInteger extra=MIN((NSUInteger)off,16-action.count);
    for (NSUInteger i=0;i<extra;i++) [action addObject:@{@"on_main_thread":@NO,@"owner_lookup":NSNull.null,
        @"receiver_equal_current_root":NSNull.null,@"attached":NSNull.null,@"installing":NSNull.null,@"frame_present":NSNull.null,
        @"ready_baseline_available":NSNull.null,@"attachment_equal_baseline":NSNull.null,@"epoch_equal_baseline":NSNull.null,
        @"queue_result":@NO,@"method_return":@NO,@"caught_exception":@NO}];
    return @{@"schema":@"mote-grid-action-server-v1",@"ready_published":@(readyPublished),@"finish_consumed":@(finishConsumed),
        @"normal_shutdown":@(normalShutdown),@"dispatcher_observation_available":@NO,
        @"callback_entries":@(entryCount+entryOverflow+off),@"off_main_entries":@(off),@"entry_overflow":@(entryOverflow+off-extra),
        @"lifetime_overflow":@(lifeOverflow),@"action_entries":action,@"lifetime":lifetime,
        @"requests":@(requests),@"dispatches":@(dispatches),@"opens":@(opens),@"closes":@(closes),@"detaches":@(detaches),@"server_calls":@[]};
}

/// Standalone runtime-B owner. The wrapper supplies a fresh client and the hard process watchdog.
int main(int argc,const char *argv[]) {
    @autoreleasepool {
        if (argc!=2 || !(sessionDirectory=Session(argv[1]))) return 2;
        [NSApplication sharedApplication]; [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
        Class cls=objc_allocateClassPair(PairTable.class,"MoteGridMinimalRuntimeB",0);
        if (!cls || !class_addMethod(cls,@selector(accessibilityPerformShowMenu),(IMP)ShowMenu,"B@:")) return 2;
        objc_registerClassPair(cls);
        NSWindow *window=[[NSWindow alloc] initWithContentRect:NSMakeRect(100,100,480,300)
            styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable backing:NSBackingStoreBuffered defer:NO];
        window.releasedWhenClosed=NO; window.title=@"mote synthetic shared-client control";
        PairGroup *group=[[PairGroup alloc] initWithFrame:NSMakeRect(0,0,480,300)];
        PairTable *table=[[cls alloc] init]; group.table=table; table.ownerView=group;
        table.accessibilityFrame=NSMakeRect(110,110,100,25); window.contentView=group;
        table.attached=YES; [window orderFront:nil]; // Never activate or change global focus.
        finishTimer=[NSTimer timerWithTimeInterval:0.05 repeats:YES block:^(NSTimer *timer) {
            (void)timer;
            if (!finishConsumed && PairFinishMarker(@"finish",@"mote-grid-pair-finish-v1\n")) {
                finishConsumed=YES; [table.ownedMenu cancelTracking];
                [table performSelector:@selector(shutdown) withObject:nil afterDelay:0 inModes:@[NSDefaultRunLoopMode]];
            }
        }];
        [[NSRunLoop mainRunLoop] addTimer:finishTimer forMode:NSDefaultRunLoopMode];
        [[NSRunLoop mainRunLoop] addTimer:finishTimer forMode:NSEventTrackingRunLoopMode];
        readyPublished=window.visible && table.attached && table.ownerView==group && group.table==table;
        if (!readyPublished || ![[@"mote-grid-pair-ready-v1\n" dataUsingEncoding:NSUTF8StringEncoding]
            writeToFile:[sessionDirectory stringByAppendingPathComponent:@"ready"] atomically:YES]) return 1;
        [NSApp run]; [finishTimer invalidate]; [NSObject cancelPreviousPerformRequestsWithTarget:table];
        NSData *json=[NSJSONSerialization dataWithJSONObject:Facts() options:NSJSONWritingPrettyPrinted error:NULL];
        BOOL written=json && [json writeToFile:[sessionDirectory stringByAppendingPathComponent:@"server.json"] atomically:YES];
        return written && normalShutdown ? 0 : 1;
    }
}
