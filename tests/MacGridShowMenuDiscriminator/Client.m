#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>
#include <limits.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>
#include <stdlib.h>
#include <string.h>

/// Every destination resolves beneath the repository cache; symlink escapes are rejected.
static NSString *Session(const char *arg) {
    NSString *cache = [[NSFileManager defaultManager].currentDirectoryPath stringByAppendingPathComponent:@".cache"];
    NSString *root = [cache stringByResolvingSymlinksInPath];
    if (![cache isEqualToString:root]) return nil;
    NSString *original = [@(arg) stringByStandardizingPath];
    NSString *path = [original stringByResolvingSymlinksInPath];
    if (![original isAbsolutePath] || ![original isEqualToString:path]) return nil;
    BOOL directory = NO;
    return [path hasPrefix:[root stringByAppendingString:@"/"]] &&
        [[NSFileManager defaultManager] fileExistsAtPath:path isDirectory:&directory] && directory ? path : nil;
}
/// Exact fixed-size marker reads reject symbolic links and never allocate from an untrusted file size.
static BOOL ReadyMarker(NSString *path) {
    static const char token[]="mote-grid-pair-ready-v1\n";
    int descriptor=open(path.fileSystemRepresentation,O_RDONLY|O_NOFOLLOW|O_NONBLOCK);
    if (descriptor<0) return NO;
    struct stat facts; char bytes[sizeof(token)];
    BOOL valid=fstat(descriptor,&facts)==0 && S_ISREG(facts.st_mode) && facts.st_size==(off_t)(sizeof(token)-1);
    ssize_t count=valid ? read(descriptor,bytes,sizeof(bytes)) : -1;
    close(descriptor);
    return valid && count==(ssize_t)(sizeof(token)-1) && memcmp(bytes,token,sizeof(token)-1)==0;
}
static BOOL Write(NSString *path, NSDictionary *value) {
    NSData *data = [NSJSONSerialization dataWithJSONObject:value options:NSJSONWritingPrettyPrinted error:NULL];
    return data && [data writeToFile:path atomically:YES];
}

/// Numeric API accounting includes ownership and timeout calls, not merely substantive reads.
@interface Probe : NSObject
@property pid_t pid;
@property double deadline, auditDeadline;
@property NSUInteger admissions, auditStart, nodes, attempts;
@property AXError lastChildCountError;
@property BOOL auditing, exhausted;
@property(nonatomic,strong) NSMutableArray *calls;
- (BOOL)admit;
- (BOOL)owned:(AXUIElementRef)element;
- (id)read:(AXUIElementRef)element attribute:(CFStringRef)attribute operation:(NSString *)operation error:(AXError *)error;
- (NSArray *)children:(AXUIElementRef)element attribute:(CFStringRef)attribute limit:(CFIndex)limit;
- (NSDictionary *)find:(AXUIElementRef)application;
@end
@implementation Probe
- (BOOL)admit {
    double now = NSProcessInfo.processInfo.systemUptime;
    if (self.admissions >= 12000 || now >= self.deadline ||
        (self.auditing && (self.admissions-self.auditStart >= 128 || now >= self.auditDeadline))) {
        self.exhausted = YES; return NO;
    }
    self.admissions++; return YES;
}
- (void)record:(NSString *)operation error:(AXError)error {
    [self.calls addObject:@{@"sequence": @(self.admissions), @"phase": self.auditing ? @"post-reply" : @"prelude",
        @"operation":operation, @"error":@(error)}];
}
- (BOOL)owned:(AXUIElementRef)element {
    if (!element || CFGetTypeID(element)!=AXUIElementGetTypeID() || ![self admit]) return NO;
    pid_t actual = 0; AXError error = AXUIElementGetPid(element,&actual);
    [self record:@"pid" error:error];
    if (error != kAXErrorSuccess || actual != self.pid || ![self admit]) return NO;
    error = AXUIElementSetMessagingTimeout(element,1.0); [self record:@"timeout" error:error];
    return error == kAXErrorSuccess;
}
- (id)read:(AXUIElementRef)element attribute:(CFStringRef)attribute operation:(NSString *)operation error:(AXError *)error {
    *error = kAXErrorCannotComplete;
    if (![self owned:element] || ![self admit]) return nil;
    CFTypeRef raw = NULL; *error = AXUIElementCopyAttributeValue(element,attribute,&raw);
    [self record:operation error:*error]; return CFBridgingRelease(raw);
}
- (NSArray *)children:(AXUIElementRef)element attribute:(CFStringRef)attribute limit:(CFIndex)limit {
    self.lastChildCountError=kAXErrorCannotComplete;
    if (![self owned:element] || ![self admit]) return nil;
    CFIndex count = 0; AXError error = AXUIElementGetAttributeValueCount(element,attribute,&count);
    [self record:@"child-count" error:error]; self.lastChildCountError=error;
    if (error!=kAXErrorSuccess || count<0 || count>limit) return nil;
    if (!count) return @[];
    if (![self owned:element] || ![self admit]) return nil;
    CFArrayRef raw = NULL; error = AXUIElementCopyAttributeValues(element,attribute,0,count,&raw);
    [self record:@"children" error:error]; NSArray *items = CFBridgingRelease(raw);
    if (error!=kAXErrorSuccess || ![items isKindOfClass:NSArray.class] || items.count!=(NSUInteger)count) return nil;
    for (id item in items) if (![self owned:(__bridge AXUIElementRef)item]) return nil;
    return items;
}
/// Only fixed role/identifier classification is retained; Table/Row/Column subtrees are pruned.
- (NSDictionary *)find:(AXUIElementRef)application {
    self.attempts++;
    NSArray *windows = [self children:application attribute:kAXWindowsAttribute limit:1];
    if (windows.count!=1) return nil;
    NSMutableArray *queue = [NSMutableArray arrayWithObject:@{@"element":windows[0],@"depth":@0,@"parent_group":NSNull.null}];
    NSMutableArray *seen = [NSMutableArray array]; id table = nil, group = nil;
    for (NSUInteger cursor=0; cursor<queue.count; cursor++) {
        if (cursor>=256) return nil;
        id node = queue[cursor][@"element"]; NSUInteger depth = [queue[cursor][@"depth"] unsignedIntegerValue];
        for (id prior in seen) if (CFEqual((__bridge CFTypeRef)prior,(__bridge CFTypeRef)node)) return nil;
        [seen addObject:node]; self.nodes++;
        AXError error; id role = [self read:(__bridge AXUIElementRef)node attribute:kAXRoleAttribute operation:@"role" error:&error];
        if (error!=0 || ![role isKindOfClass:NSString.class]) return nil;
        if ([role isEqualToString:@"AXTable"]) {
            id identifier = [self read:(__bridge AXUIElementRef)node attribute:kAXIdentifierAttribute operation:@"identifier" error:&error];
            if (error==0 && [identifier isKindOfClass:NSString.class] && [identifier isEqualToString:@"mote.csv.table"]) {
                if (table) return nil; table=node; group=queue[cursor][@"parent_group"];
            }
            continue;
        }
        if ([role isEqualToString:@"AXRow"] || [role isEqualToString:@"AXColumn"]) continue;
        NSArray *children = [self children:(__bridge AXUIElementRef)node attribute:kAXChildrenAttribute limit:128];
        // Only the documented unsupported-attribute result denotes a normal leaf.
        if (!children && self.lastChildCountError==kAXErrorAttributeUnsupported) continue;
        if (!children) return nil;
        if ((depth>=12 && children.count) || queue.count+children.count>256) return nil;
        for (id child in children) [queue addObject:@{@"element":child,@"depth":@(depth+1),
            @"parent_group":[role isEqualToString:@"AXGroup"] ? node : NSNull.null}];
    }
    return table ? @{@"table":table,@"window":windows[0],@"group":group ?: NSNull.null} : nil;
}
@end

/// Canonical JSON Boolean objects avoid boxing C relational expressions as numeric scalars.
static NSNumber *PairBoolean(BOOL value) { return value ? @YES : @NO; }

static id EqualObject(id actual, id expected) {
    if (!actual || !expected || expected==NSNull.null || CFGetTypeID((__bridge CFTypeRef)actual)!=AXUIElementGetTypeID()) return NSNull.null;
    return PairBoolean(CFEqual((__bridge CFTypeRef)actual,(__bridge CFTypeRef)expected));
}
/// One post-reply traversal, then bounded graph checks; unknown is never silently false.
static NSDictionary *Audit(Probe *probe, AXUIElementRef app, NSDictionary *before) {
    probe.auditing=YES; probe.auditStart=probe.admissions;
    probe.auditDeadline=MIN(probe.deadline,NSProcessInfo.processInfo.systemUptime+3);
    NSMutableDictionary *facts = [@{@"unique_current_table":NSNull.null,@"retained_equal_current":NSNull.null,
        @"parent_equal_discovered_group":NSNull.null,@"window_equal_discovered_window":NSNull.null,
        @"top_level_equal_window":NSNull.null,@"reciprocal_child_occurrences":NSNull.null,
        @"parent_cycle":NSNull.null,@"parent_reaches_window":NSNull.null} mutableCopy];
    NSDictionary *after=[probe find:app];
    if (after) { facts[@"unique_current_table"]=@YES; facts[@"retained_equal_current"]=EqualObject(before[@"table"],after[@"table"]); }
    AXUIElementRef table=(__bridge AXUIElementRef)before[@"table"]; AXError error;
    id parent=[probe read:table attribute:kAXParentAttribute operation:@"parent" error:&error];
    BOOL validParent=error==0 && parent && [probe owned:(__bridge AXUIElementRef)parent];
    if (validParent) facts[@"parent_equal_discovered_group"]=EqualObject(parent,before[@"group"]);
    id window=[probe read:table attribute:kAXWindowAttribute operation:@"window" error:&error];
    if (error==0 && window && [probe owned:(__bridge AXUIElementRef)window]) facts[@"window_equal_discovered_window"]=EqualObject(window,before[@"window"]);
    id top=[probe read:table attribute:kAXTopLevelUIElementAttribute operation:@"top-level" error:&error];
    if (error==0 && top && [probe owned:(__bridge AXUIElementRef)top]) facts[@"top_level_equal_window"]=EqualObject(top,before[@"window"]);
    if (validParent) {
        NSArray *children=[probe children:(__bridge AXUIElementRef)parent attribute:kAXChildrenAttribute limit:128];
        if (children) { NSUInteger occurrences=0; for (id child in children) if (CFEqual((__bridge CFTypeRef)child,table)) occurrences++;
            facts[@"reciprocal_child_occurrences"]=@(occurrences); }
        NSMutableArray *seen=[NSMutableArray arrayWithObject:before[@"table"]]; id current=parent;
        for (NSUInteger link=0; link<6 && current; link++) {
            BOOL cycle=NO; for (id prior in seen) if (CFEqual((__bridge CFTypeRef)prior,(__bridge CFTypeRef)current)) cycle=YES;
            if (cycle) { facts[@"parent_cycle"]=@YES; break; }
            [seen addObject:current];
            if (CFEqual((__bridge CFTypeRef)current,(__bridge CFTypeRef)before[@"window"])) {
                facts[@"parent_cycle"]=@NO; facts[@"parent_reaches_window"]=@YES; break;
            }
            id next=[probe read:(__bridge AXUIElementRef)current attribute:kAXParentAttribute operation:@"parent-walk" error:&error];
            current=error==0 && next && [probe owned:(__bridge AXUIElementRef)next] ? next : nil;
        }
    }
    facts[@"audit_admissions"]=@(probe.admissions-probe.auditStart); facts[@"audit_exhausted"]=@(probe.exhausted);
    return facts;
}

/// Fresh exact-PID client: no activation, prompt, global input, selection mutation or action retry.
int main(int argc,const char *argv[]) {
    @autoreleasepool {
        if (argc!=3) return 2;
        char *end=NULL; long parsed=strtol(argv[1],&end,10);
        NSString *directory=Session(argv[2]);
        if (!end || *end || parsed<=0 || parsed>INT_MAX || parsed==getpid() || !directory) return 2;
        Probe *probe=[Probe new]; probe.pid=(pid_t)parsed; probe.deadline=NSProcessInfo.processInfo.systemUptime+55; probe.calls=[NSMutableArray array];
        BOOL trusted=AXIsProcessTrusted(), ready=NO;
        NSString *readyPath=[directory stringByAppendingPathComponent:@"ready"];
        for (int attempt=0; attempt<20 && NSProcessInfo.processInfo.systemUptime<probe.deadline; attempt++) {
            if (ReadyMarker(readyPath)) { ready=YES; break; }
            [NSThread sleepForTimeInterval:0.15];
        }
        AXUIElementRef app=trusted && ready ? AXUIElementCreateApplication(probe.pid) : NULL;
        NSDictionary *found=app ? [probe find:app] : nil;
        NSMutableDictionary *actions=[@{@"attempts":@0,@"names_error":NSNull.null,@"names_count":NSNull.null,
            @"advertised":NSNull.null,@"original_error":NSNull.null,@"begin_recorded":@NO,@"end_recorded":@NO} mutableCopy];
        BOOL returned=NO;
        if (found) {
            AXUIElementRef table=(__bridge AXUIElementRef)found[@"table"]; CFArrayRef raw=NULL;
            if ([probe owned:table] && [probe admit]) {
                AXError error=AXUIElementCopyActionNames(table,&raw); [probe record:@"action-names" error:error]; actions[@"names_error"]=@(error);
                NSArray *names=CFBridgingRelease(raw); BOOL valid=error==0 && [names isKindOfClass:NSArray.class] && names.count<=16;
                if (valid) for (id name in names) if (![name isKindOfClass:NSString.class]) valid=NO;
                if (valid) { actions[@"names_count"]=@(names.count); actions[@"advertised"]=@([names containsObject:@"AXShowMenu"]); }
                if (valid && [names containsObject:@"AXShowMenu"] && [probe owned:table] && [probe admit]) {
                    actions[@"attempts"]=@1; actions[@"begin_recorded"]=@YES;
                    if (!Write([directory stringByAppendingPathComponent:@"client.json"],@{@"schema":@"mote-grid-action-client-v1",@"status":@"action-begun",@"actions":actions})) { CFRelease(app); return 1; }
                    error=AXUIElementPerformAction(table,kAXShowMenuAction); [probe record:@"show-menu" error:error];
                    actions[@"original_error"]=@(error); actions[@"end_recorded"]=@YES; returned=YES;
                    // Preserve the primary reply before making any identity/parent request.
                    if (!Write([directory stringByAppendingPathComponent:@"client.json"],@{@"schema":@"mote-grid-action-client-v1",@"status":@"reply-observed",@"actions":actions})) { CFRelease(app); return 1; }
                }
            }
        }
        NSDictionary *identity=returned ? Audit(probe,app,found) : @{};
        NSDictionary *report=@{@"schema":@"mote-grid-action-client-v1",@"status":returned ? @"reply-observed" : @"unresolved",
            @"trusted":@(trusted),@"ready":@(ready),@"owned_target":PairBoolean(found!=nil),
            @"discovery":@{@"attempts":@(probe.attempts),@"nodes":@(probe.nodes),@"admissions":@(probe.admissions)},
            @"actions":actions,@"identity":identity,@"client_calls":probe.calls,
            @"cleanup":@{@"finish_after_reply":returned ? @YES : NSNull.null,@"finish_after_audit":returned ? @YES : NSNull.null}};
        BOOL written=Write([directory stringByAppendingPathComponent:@"client.json"],report);
        BOOL finished=written && [[@"mote-grid-pair-finish-v1\n" dataUsingEncoding:NSUTF8StringEncoding]
            writeToFile:[directory stringByAppendingPathComponent:@"finish"] atomically:YES];
        if (app) CFRelease(app);
        return written && finished && returned ? 0 : 1;
    }
}
