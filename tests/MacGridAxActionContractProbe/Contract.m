#import <Cocoa/Cocoa.h>
#import <ApplicationServices/ApplicationServices.h>
#import <objc/runtime.h>
#import <objc/message.h>
#include <limits.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

/// An independent synthetic element; it never attaches to mote or a user document.
@interface ActionElement : NSAccessibilityElement <NSMenuDelegate>
@property(nonatomic, weak) NSView *ownerView;
@property(nonatomic, copy) NSString *variant;
@property(nonatomic) NSInteger requests, modernCalls, legacyCalls, opens, closes;
@property(nonatomic) BOOL inspecting;
@property(nonatomic, strong) NSMenu *ownedMenu;
- (BOOL)admitMenu;
- (void)presentMenu;
- (NSDictionary *)facts;
@end

@implementation ActionElement
- (NSString *)accessibilityRole { return NSAccessibilityTableRole; }
- (NSString *)accessibilityLabel { return self.variant; }
- (NSString *)accessibilityIdentifier { return self.variant; }
- (id)accessibilityParent { return self.ownerView; }
- (id)accessibilityWindow { return self.ownerView.window; }
- (id)accessibilityTopLevelUIElement { return self.ownerView.window; }
- (BOOL)isAccessibilityElement { return YES; }
- (NSInteger)accessibilityRowCount { return 0; }
- (NSInteger)accessibilityColumnCount { return 0; }
- (NSArray *)accessibilityRows { return @[]; }
- (NSArray *)accessibilityColumns { return @[]; }
- (NSArray *)accessibilityChildren { return @[]; }
- (NSString *)accessibilityHelp {
    return [NSString stringWithFormat:@"requests=%ld opens=%ld closes=%ld modern=%ld legacy=%ld",
        (long)self.requests, (long)self.opens, (long)self.closes,
        (long)self.modernCalls, (long)self.legacyCalls];
}
/// Admission is the same next-turn mechanism for every control; direct inspection has no effect.
- (BOOL)admitMenu {
    if (self.inspecting) return YES;
    if (!self.ownerView.window || self.requests != 0) return NO;
    self.requests++;
    [self performSelector:@selector(presentMenu) withObject:nil afterDelay:0];
    return YES;
}
/// Cancellation belongs to this exact menu and runs in its nested tracking mode.
- (void)presentMenu {
    NSMenu *menu = [[NSMenu alloc] initWithTitle:@"Synthetic action control"];
    [menu addItemWithTitle:@"Synthetic no-op" action:NULL keyEquivalent:@""];
    menu.delegate = self;
    self.ownedMenu = menu;
    [menu performSelector:@selector(cancelTracking) withObject:nil afterDelay:0.15
        inModes:@[NSDefaultRunLoopMode, NSEventTrackingRunLoopMode]];
    [menu popUpMenuPositioningItem:nil atLocation:NSMakePoint(30, 30) inView:self.ownerView];
    [NSObject cancelPreviousPerformRequestsWithTarget:menu];
    self.ownedMenu = nil;
}
- (void)menuWillOpen:(NSMenu *)menu { if (menu == self.ownedMenu) self.opens++; }
- (void)menuDidClose:(NSMenu *)menu { if (menu == self.ownedMenu) self.closes++; }
- (NSDictionary *)facts {
    return @{ @"variant": self.variant, @"requests": @(self.requests),
        @"modernCalls": @(self.modernCalls), @"legacyCalls": @(self.legacyCalls),
        @"opens": @(self.opens), @"closes": @(self.closes) };
}
@end

/// The compiler supplies the platform BOOL encoding for the baseline action and getter.
@interface CompiledModern : ActionElement @end
@implementation CompiledModern
- (BOOL)isAccessibilityEnabled { return YES; }
- (BOOL)accessibilityPerformShowMenu {
    if (!self.inspecting) self.modernCalls++;
    return [self admitMenu];
}
@end

/// A narrow production-precedent control, not a proposal to relax any mote predicate.
@interface CompiledLegacyBridge : CompiledModern @end
@implementation CompiledLegacyBridge
- (NSArray *)accessibilityActionNames { return @[NSAccessibilityShowMenuAction]; }
- (void)accessibilityPerformAction:(NSString *)action {
    if (![action isEqualToString:NSAccessibilityShowMenuAction]) return;
    if (!self.inspecting) self.legacyCalls++;
    [self admitMenu];
}
@end

/// A runtime IMP with the same one-byte C BOOL ABI as the compiled baseline.
static BOOL RuntimeShowMenu(ActionElement *self, SEL selector) {
    (void)selector;
    if (!self.inspecting) self.modernCalls++;
    return [self admitMenu];
}
static BOOL RuntimeEnabled(id self, SEL selector) { (void)self; (void)selector; return YES; }
static NSArray *RuntimeActionNames(id self, SEL selector) {
    (void)self; (void)selector; return @[NSAccessibilityShowMenuAction];
}
static void RuntimePerformAction(ActionElement *self, SEL selector, NSString *action) {
    (void)selector;
    if (![action isEqualToString:NSAccessibilityShowMenuAction]) return;
    if (!self.inspecting) self.legacyCalls++;
    [self admitMenu];
}

/// Runtime variants differ only in declared metadata, getter spelling or the explicit legacy bridge.
static Class RuntimeClass(NSString *name, const char *encoding, BOOL wrongGetter, BOOL legacy) {
    Class cls = objc_allocateClassPair(ActionElement.class, name.UTF8String, 0);
    if (!cls) return Nil;
    Method enabled = class_getInstanceMethod(CompiledModern.class, @selector(isAccessibilityEnabled));
    BOOL added = class_addMethod(cls, @selector(accessibilityPerformShowMenu), (IMP)RuntimeShowMenu, encoding);
    added &= class_addMethod(cls, NSSelectorFromString(wrongGetter ? @"accessibilityEnabled" : @"isAccessibilityEnabled"),
        (IMP)RuntimeEnabled, method_getTypeEncoding(enabled));
    if (legacy) {
        Method names = class_getInstanceMethod(CompiledLegacyBridge.class, @selector(accessibilityActionNames));
        Method action = class_getInstanceMethod(CompiledLegacyBridge.class, @selector(accessibilityPerformAction:));
        added &= class_addMethod(cls, @selector(accessibilityActionNames), (IMP)RuntimeActionNames, method_getTypeEncoding(names));
        added &= class_addMethod(cls, @selector(accessibilityPerformAction:), (IMP)RuntimePerformAction, method_getTypeEncoding(action));
    }
    if (!added) { objc_disposeClassPair(cls); return Nil; }
    objc_registerClassPair(cls);
    return cls;
}

/// The authoritative child array exposes only our seven immutable synthetic elements.
@interface ControlView : NSView
@property(nonatomic, copy) NSArray<ActionElement *> *elements;
@end
@implementation ControlView
- (BOOL)isAccessibilityElement { return YES; }
- (NSString *)accessibilityRole { return NSAccessibilityGroupRole; }
- (NSArray *)accessibilityChildren { return self.elements; }
@end

/// Fixed schema metadata never includes pointers, desktop identities or user content.
static NSDictionary *Metadata(ActionElement *element) {
    SEL action = @selector(accessibilityPerformShowMenu);
    SEL enabled = @selector(isAccessibilityEnabled);
    SEL misspelled = NSSelectorFromString(@"accessibilityEnabled");
    Method method = class_getInstanceMethod(element.class, action);
    NSMethodSignature *signature = [element methodSignatureForSelector:action];
    element.inspecting = YES;
    BOOL direct = ((BOOL (*)(id, SEL))objc_msgSend)(element, action);
    BOOL invocationResult = NO;
    NSInvocation *invocation = [NSInvocation invocationWithMethodSignature:signature];
    invocation.target = element; invocation.selector = action;
    [invocation invoke]; [invocation getReturnValue:&invocationResult];
    element.inspecting = NO;
    BOOL enabledResponds = [element respondsToSelector:enabled];
    BOOL wrongResponds = [element respondsToSelector:misspelled];
    return @{ @"variant": element.variant,
        @"actionEncoding": @(method_getTypeEncoding(method)),
        @"actionReturnLength": @(signature.methodReturnLength),
        @"directActionReturn": @(direct), @"invocationActionReturn": @(invocationResult),
        @"enabledResponds": @(enabledResponds), @"misspelledEnabledResponds": @(wrongResponds),
        @"enabledReturn": enabledResponds ? @(((BOOL (*)(id, SEL))objc_msgSend)(element, enabled)) : NSNull.null,
        @"misspelledEnabledReturn": wrongResponds ? @(((BOOL (*)(id, SEL))objc_msgSend)(element, misspelled)) : NSNull.null,
        @"allowAction": @([element isAccessibilitySelectorAllowed:action]),
        @"allowEnabled": @([element isAccessibilitySelectorAllowed:enabled]) };
}

/// All JSON destinations are synthetic paths supplied by the repository-bounded owner script.
static BOOL WriteJSON(NSString *directory, NSString *name, NSDictionary *value) {
    NSData *data = [NSJSONSerialization dataWithJSONObject:value options:NSJSONWritingPrettyPrinted error:nil];
    return data && [data writeToFile:[directory stringByAppendingPathComponent:name] atomically:YES];
}

/// Reject every foreign or unidentifiable AX object before copying attributes or invoking actions.
static BOOL Owned(AXUIElementRef element, pid_t pid) {
    pid_t actual = 0;
    return AXUIElementGetPid(element, &actual) == kAXErrorSuccess && actual == pid &&
        AXUIElementSetMessagingTimeout(element, 0.5) == kAXErrorSuccess;
}
static id Attribute(AXUIElementRef element, pid_t pid, NSString *name, AXError *error) {
    if (!Owned(element, pid)) { *error = kAXErrorInvalidUIElement; return nil; }
    CFTypeRef value = NULL;
    *error = AXUIElementCopyAttributeValue(element, (__bridge CFStringRef)name, &value);
    return CFBridgingRelease(value);
}
/// Count-before-copy and exact ownership checks prevent truncated or foreign arrays becoming evidence.
static NSArray *Children(AXUIElementRef element, pid_t pid, NSString *attribute, CFIndex limit) {
    if (!Owned(element, pid)) return nil;
    CFIndex count = 0;
    if (AXUIElementGetAttributeValueCount(element, (__bridge CFStringRef)attribute, &count) != kAXErrorSuccess ||
        count < 0 || count > limit) return nil;
    if (count == 0) return @[];
    CFArrayRef raw = NULL;
    if (AXUIElementCopyAttributeValues(element, (__bridge CFStringRef)attribute, 0, count, &raw) != kAXErrorSuccess) return nil;
    NSArray *values = CFBridgingRelease(raw);
    if (values.count != (NSUInteger)count) return nil;
    for (id item in values) {
        if (CFGetTypeID((__bridge CFTypeRef)item) != AXUIElementGetTypeID() || !Owned((__bridge AXUIElementRef)item, pid)) return nil;
    }
    return values;
}

/// Bounded exact-PID traversal discovers the fixed identifiers, never the system-wide desktop tree.
static NSDictionary *FindElements(AXUIElementRef app, pid_t pid, NSArray *names) {
    NSArray *windows = Children(app, pid, @"AXWindows", 1);
    if (windows.count != 1) return nil;
    NSMutableArray *queue = [windows mutableCopy];
    NSMutableDictionary *found = [NSMutableDictionary dictionary];
    NSUInteger cursor = 0;
    while (cursor < queue.count && cursor < 32) {
        AXUIElementRef node = (__bridge AXUIElementRef)queue[cursor++];
        AXError error;
        id identifier = Attribute(node, pid, @"AXIdentifier", &error);
        if (error == kAXErrorSuccess && [identifier isKindOfClass:NSString.class] && [names containsObject:identifier]) {
            if (found[identifier]) return nil;
            found[identifier] = (__bridge id)node;
            continue;
        }
        NSArray *children = Children(node, pid, @"AXChildren", 16);
        if (queue.count + children.count > 32) return nil;
        if (children) [queue addObjectsFromArray:children];
    }
    return found.count == names.count ? found : nil;
}

/// Performs one advertised action per synthetic node and observes the actual menu delegate lifecycle.
static int Client(pid_t pid, NSString *directory, NSArray *names) {
    BOOL trusted = AXIsProcessTrusted();
    if (!trusted) {
        WriteJSON(directory, @"client.json", @{ @"status": @"external-accessibility-unavailable", @"trusted": @NO });
        return 0;
    }
    AXUIElementRef app = AXUIElementCreateApplication(pid);
    double deadline = NSProcessInfo.processInfo.systemUptime + 20;
    NSDictionary *elements = nil;
    for (int attempt = 0; attempt < 20 && NSProcessInfo.processInfo.systemUptime < deadline; attempt++) {
        elements = FindElements(app, pid, names);
        if (elements) break;
        [NSThread sleepForTimeInterval:0.1];
    }
    NSMutableArray *observations = [NSMutableArray array];
    BOOL discovered = elements != nil;
    BOOL completed = discovered;
    for (NSString *name in names) {
        if (!discovered || NSProcessInfo.processInfo.systemUptime >= deadline) { completed = NO; break; }
        AXUIElementRef node = (__bridge AXUIElementRef)elements[name];
        CFArrayRef raw = NULL;
        AXError namesError = Owned(node, pid) ? AXUIElementCopyActionNames(node, &raw) : kAXErrorInvalidUIElement;
        NSArray *actions = CFBridgingRelease(raw);
        BOOL validNames = namesError == kAXErrorSuccess && actions.count <= 16;
        BOOL advertised = validNames && [actions containsObject:NSAccessibilityShowMenuAction];
        AXError enabledError;
        id enabled = Attribute(node, pid, @"AXEnabled", &enabledError);
        AXError result = advertised && Owned(node, pid) ? AXUIElementPerformAction(node, kAXShowMenuAction) : kAXErrorActionUnsupported;
        NSString *help = nil;
        int polls = 0;
        while (polls < 20 && NSProcessInfo.processInfo.systemUptime < deadline) {
            polls++;
            AXError error;
            id candidate = Attribute(node, pid, @"AXHelp", &error);
            if (error == kAXErrorSuccess && [candidate isKindOfClass:NSString.class]) help = candidate;
            if ([help containsString:@"opens=1 closes=1"]) break;
            [NSThread sleepForTimeInterval:0.1];
        }
        // Never preserve arbitrary app text: retain only equality to fixed expected counter states.
        BOOL lifecycle = [help containsString:@"requests=1 opens=1 closes=1"];
        [observations addObject:@{ @"variant": name, @"actionNamesError": @(namesError),
            @"actionNamesCount": validNames ? @(actions.count) : NSNull.null,
            @"advertised": @(advertised), @"actionError": @(result),
            @"enabledError": @(enabledError), @"enabledIsBoolean": @([enabled isKindOfClass:NSNumber.class]),
            @"enabled": [enabled isKindOfClass:NSNumber.class] ? @([enabled boolValue]) : NSNull.null,
            @"oneMenuOpenedAndClosed": @(lifecycle), @"polls": @(polls) }];
        completed &= advertised && lifecycle;
    }
    CFRelease(app);
    BOOL written = WriteJSON(directory, @"client.json", @{ @"status": completed ? @"control-completed" : @"control-failed",
        @"trusted": @YES, @"targetPID": @(pid), @"clientPID": @(getpid()), @"observations": observations });
    return completed && written ? 0 : 1;
}

/// One owner app and one exact-PID child; neither sends global input or modifies TCC.
static int Server(NSString *executable, NSString *directory, NSArray *names) {
    [NSApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
    const char *native = method_getTypeEncoding(class_getInstanceMethod(CompiledModern.class, @selector(accessibilityPerformShowMenu)));
    Class classes[] = {CompiledModern.class,
        RuntimeClass(@"MoteAXControlNative", native, NO, NO),
        RuntimeClass(@"MoteAXControlBool", "B@:", NO, NO),
        RuntimeClass(@"MoteAXControlChar", "c@:", NO, NO),
        RuntimeClass(@"MoteAXControlWrongEnabled", native, YES, NO),
        CompiledLegacyBridge.class,
        RuntimeClass(@"MoteAXControlLegacy", native, NO, YES)};
    NSWindow *window = [[NSWindow alloc] initWithContentRect:NSMakeRect(100, 100, 480, 300)
        styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable backing:NSBackingStoreBuffered defer:NO];
    window.releasedWhenClosed = NO;
    window.title = @"mote synthetic AX action contract control";
    ControlView *view = [[ControlView alloc] initWithFrame:NSMakeRect(0, 0, 480, 300)];
    window.contentView = view;
    NSMutableArray *elements = [NSMutableArray array];
    NSMutableArray *metadata = [NSMutableArray array];
    for (NSUInteger index = 0; index < names.count; index++) {
        if (!classes[index]) return 1;
        ActionElement *element = [[classes[index] alloc] init];
        element.ownerView = view; element.variant = names[index];
        element.accessibilityFrame = NSMakeRect(110, 110 + index * 30, 100, 25);
        [elements addObject:element]; [metadata addObject:Metadata(element)];
    }
    view.elements = elements;
    [window orderFront:nil]; // Do not activate the app or replace another app's global focus.
    NSTask *task = [[NSTask alloc] init];
    task.executableURL = [NSURL fileURLWithPath:executable];
    task.arguments = @[@"--client", [NSString stringWithFormat:@"%d", getpid()], directory];
    __block int childExit = -1;
    task.terminationHandler = ^(NSTask *child) {
        dispatch_async(dispatch_get_main_queue(), ^{
            childExit = child.terminationStatus;
            [NSApp stop:nil];
            // Wake the application event loop without synthesizing input or a global event.
            [NSApp postEvent:[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint
                modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0] atStart:NO];
        });
    };
    NSError *error = nil;
    if (![task launchAndReturnError:&error]) { [window close]; return 1; }
    [NSApp run];
    [NSObject cancelPreviousPerformRequestsWithTarget:view];
    for (ActionElement *element in elements) {
        [NSObject cancelPreviousPerformRequestsWithTarget:element];
        [element.ownedMenu cancelTracking];
    }
    [window close];
    NSMutableArray *effects = [NSMutableArray array];
    for (ActionElement *element in elements) [effects addObject:element.facts];
    BOOL written = WriteJSON(directory, @"server.json", @{ @"metadata": metadata,
        @"effects": effects, @"childExit": @(childExit), @"normalShutdown": @YES });
    return childExit == 0 && written ? 0 : 1;
}

/// Modes are fixed; the owner script is responsible for repository-local output and the 45-second watchdog.
int main(int argc, const char *argv[]) {
    @autoreleasepool {
        NSArray *names = @[@"compiled-modern", @"runtime-native", @"runtime-B", @"runtime-c",
            @"runtime-wrong-enabled", @"compiled-legacy-bridge", @"runtime-legacy-bridge"];
        if (argc == 4 && strcmp(argv[1], "--client") == 0) {
            char *end = NULL; long parsed = strtol(argv[2], &end, 10);
            if (!end || *end || parsed <= 0 || parsed > INT_MAX || parsed == getpid()) return 2;
            return Client((pid_t)parsed, @(argv[3]), names);
        }
        if (argc != 3 || strcmp(argv[1], "--server") != 0) return 2;
        return Server([NSString stringWithUTF8String:argv[0]], @(argv[2]), names);
    }
}
