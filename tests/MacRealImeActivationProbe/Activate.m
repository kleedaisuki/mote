/**
 * Reversibly activate Apple Pinyin only inside a disposable GitHub-hosted Mac.
 *
 * This feasibility probe sends no keys, opens no editor, and cannot prove real
 * composition. It must never run on a personal or self-hosted machine.
 */
#import <Carbon/Carbon.h>
#import <Foundation/Foundation.h>
#include <stdlib.h>
#include <string.h>

static NSString *const parentID = @"com.apple.inputmethod.SCIM";
static NSString *const modeID = @"com.apple.inputmethod.SCIM.ITABC";

/** Read one borrowed TIS Boolean property. */
static BOOL flag(TISInputSourceRef source, CFStringRef key) {
    if (source == NULL) return NO;
    CFBooleanRef value = (CFBooleanRef)TISGetInputSourceProperty(source, key);
    return value != NULL && CFBooleanGetValue(value);
}

/** Read one borrowed TIS string property. */
static NSString *stringProperty(TISInputSourceRef source, CFStringRef key) {
    if (source == NULL) return @"";
    CFStringRef value = (CFStringRef)TISGetInputSourceProperty(source, key);
    return value == NULL ? @"" : (__bridge NSString *)value;
}

/** Return a retained source by stable ID, including disabled installed sources. */
static TISInputSourceRef copySource(NSString *identifier) {
    CFArrayRef list = TISCreateInputSourceList(NULL, true);
    if (list == NULL) return NULL;
    TISInputSourceRef found = NULL;
    for (CFIndex index = 0; index < CFArrayGetCount(list); ++index) {
        TISInputSourceRef source = (TISInputSourceRef)CFArrayGetValueAtIndex(list, index);
        if (![stringProperty(source, kTISPropertyInputSourceID) isEqualToString:identifier]) continue;
        found = (TISInputSourceRef)CFRetain(source);
        break;
    }
    CFRelease(list);
    return found;
}

/** Re-query a source after a TIS transition; retained references can be stale. */
static NSDictionary *state(NSString *identifier) {
    TISInputSourceRef source = copySource(identifier);
    NSDictionary *value = @{
        @"present": @(source != NULL),
        @"enabled": @(flag(source, kTISPropertyInputSourceIsEnabled)),
        @"enable_capable": @(flag(source, kTISPropertyInputSourceIsEnableCapable)),
        @"select_capable": @(flag(source, kTISPropertyInputSourceIsSelectCapable)),
        @"selected": @(flag(source, kTISPropertyInputSourceIsSelected))
    };
    if (source != NULL) CFRelease(source);
    return value;
}

/** Return the globally selected keyboard source ID without logging user text. */
static NSString *currentID(void) {
    TISInputSourceRef source = TISCopyCurrentKeyboardInputSource();
    NSString *identifier = [stringProperty(source, kTISPropertyInputSourceID) copy];
    if (source != NULL) CFRelease(source);
    return identifier;
}

/** Poll a desired enabled/selected state to allow asynchronous TIS propagation. */
static BOOL waitFor(NSString *identifier, NSString *property, BOOL desired) {
    for (int attempt = 0; attempt < 20; ++attempt) {
        if ([state(identifier)[property] boolValue] == desired) return YES;
        [NSThread sleepForTimeInterval:0.1];
    }
    return [state(identifier)[property] boolValue] == desired;
}

/** Record a monotonic, content-free snapshot at a restoration boundary. */
static NSDictionary *sample(NSString *phase, NSTimeInterval started) {
    return @{
        @"phase": phase,
        @"elapsed_ms": @((long long)(([NSProcessInfo processInfo].systemUptime - started) * 1000)),
        @"current_source_id": currentID(),
        @"parent": state(parentID),
        @"mode": state(modeID)
    };
}

/** Return the same binary's read-only TIS view from a fresh process, or an error. */
static NSDictionary *observeSeparately(NSTimeInterval started) {
    NSTask *task = [[NSTask alloc] init];
    task.executableURL = [NSURL fileURLWithPath:[NSBundle mainBundle].executablePath];
    task.arguments = @[@"--observe"];
    NSPipe *pipe = [NSPipe pipe];
    task.standardOutput = pipe;
    task.standardError = [NSPipe pipe];
    NSError *launchError = nil;
    if (![task launchAndReturnError:&launchError]) return @{@"error": @"observer_launch_failed"};
    NSTimeInterval deadline = [NSProcessInfo processInfo].systemUptime + 1.5;
    while (task.isRunning && [NSProcessInfo processInfo].systemUptime < deadline)
        [NSThread sleepForTimeInterval:0.02];
    if (task.isRunning) {
        [task terminate];
        return @{@"error": @"observer_timeout"};
    }
    if (task.terminationStatus != 0) return @{@"error": @"observer_exit_failed"};
    NSData *data = [pipe.fileHandleForReading readDataToEndOfFile];
    id value = [NSJSONSerialization JSONObjectWithData:data options:0 error:NULL];
    if (![value isKindOfClass:[NSDictionary class]]) return @{@"error": @"observer_json_invalid"};
    NSMutableDictionary *result = [value mutableCopy];
    result[@"elapsed_ms"] = @((long long)(([NSProcessInfo processInfo].systemUptime - started) * 1000));
    return result;
}

/** Verify the external TIS view matches the original identity and source flags. */
static BOOL observerRestored(NSDictionary *view, NSString *originalID,
                             NSDictionary *original, NSDictionary *parent, NSDictionary *mode) {
    if (![view[@"current_source_id"] isEqualToString:originalID]) return NO;
    for (NSString *key in @[@"present", @"enabled", @"selected"]) {
        if (![view[@"original"][key] isEqual:original[key]] ||
            ![view[@"parent"][key] isEqual:parent[key]] ||
            ![view[@"mode"][key] isEqual:mode[key]]) return NO;
    }
    return YES;
}

/** Require a fresh-process observation to agree with the adjacent local sample. */
static BOOL observerAgrees(NSDictionary *view, NSDictionary *local) {
    if (![view[@"current_source_id"] isEqualToString:local[@"current_source_id"]]) return NO;
    for (NSString *source in @[@"parent", @"mode"]) {
        for (NSString *key in @[@"present", @"enabled", @"selected"])
            if (![view[source][key] isEqual:local[source][key]]) return NO;
    }
    return YES;
}

/** Sample five seconds of propagation without making any additional mutations. */
static BOOL traceWindow(NSString *phase, NSTimeInterval started,
                        NSMutableArray *trace, NSMutableArray *observations) {
    NSTimeInterval windowStart = [NSProcessInfo processInfo].systemUptime;
    BOOL complete = YES;
    for (int second = 0; second <= 5; ++second) {
        NSTimeInterval target = windowStart + second;
        NSTimeInterval remaining = target - [NSProcessInfo processInfo].systemUptime;
        if (remaining > 0) [NSThread sleepForTimeInterval:remaining];
        NSDictionary *local = sample([NSString stringWithFormat:@"%@_%d", phase, second], started);
        [trace addObject:local];
        if (second != 0 && second != 3 && second != 5) continue;
        NSDictionary *view = observeSeparately(started);
        [observations addObject:@{@"phase": local[@"phase"], @"view": view,
                                  @"agrees_with_adjacent_sample": @(observerAgrees(view, local))}];
        if (!observerAgrees(view, local)) complete = NO;
    }
    return complete;
}

/** Emit one privacy-safe JSON report and return nonzero on activation/restoration failure. */
int main(int argc, const char *argv[]) {
    @autoreleasepool {
        if (argc == 2 && strcmp(argv[1], "--observe") == 0) {
            NSString *identifier = currentID();
            NSDictionary *view = @{
                @"current_source_id": identifier,
                @"original": state(identifier),
                @"parent": state(parentID),
                @"mode": state(modeID)
            };
            NSData *json = [NSJSONSerialization dataWithJSONObject:view options:0 error:NULL];
            if (json == nil) return 4;
            fwrite(json.bytes, 1, json.length, stdout);
            return 0;
        }
        const char *actions = getenv("GITHUB_ACTIONS");
        const char *environment = getenv("RUNNER_ENVIRONMENT");
        const char *os = getenv("RUNNER_OS");
        const char *architecture = getenv("RUNNER_ARCH");
        if (actions == NULL || strcmp(actions, "true") != 0 ||
            environment == NULL || strcmp(environment, "github-hosted") != 0 ||
            os == NULL || strcmp(os, "macOS") != 0 ||
            architecture == NULL || (strcmp(architecture, "X64") != 0 &&
                                     strcmp(architecture, "ARM64") != 0)) {
            fprintf(stderr, "Refusing input-source mutation outside GitHub-hosted macOS Actions.\n");
            return 3;
        }
        NSString *originalID = currentID();
        TISInputSourceRef originalSource = copySource(originalID);
        BOOL originalRestorable = originalSource != NULL &&
            flag(originalSource, kTISPropertyInputSourceIsSelectCapable);
        if (originalSource != NULL) CFRelease(originalSource);
        NSDictionary *originalBefore = state(originalID);
        NSDictionary *parentBefore = state(parentID);
        NSDictionary *modeBefore = state(modeID);
        NSTimeInterval started = [NSProcessInfo processInfo].systemUptime;
        NSDictionary *observerBefore = observeSeparately(started);
        NSMutableArray *restoreTrace = [NSMutableArray array];
        NSMutableDictionary *report = [@{
            @"schema": @"mote.mac-real-ime-activation.v1",
            @"real_ime_tested": @NO,
            @"os_version": [NSProcessInfo processInfo].operatingSystemVersionString,
            @"original_source_id": originalID,
            @"original_source_before": originalBefore,
            @"original_source_restorable": @(originalRestorable),
            @"parent_before": parentBefore,
            @"mode_before": modeBefore,
            @"observer_before": observerBefore
        } mutableCopy];
        BOOL originalParentEnabled = [parentBefore[@"enabled"] boolValue];
        BOOL originalModeEnabled = [modeBefore[@"enabled"] boolValue];
        BOOL originalParentSelected = [parentBefore[@"selected"] boolValue];
        BOOL originalModeSelected = [modeBefore[@"selected"] boolValue];
        NSString *expectedOriginalID = strcmp(architecture, "X64") == 0 ?
            @"com.apple.keylayout.ABC" : @"com.apple.keylayout.US";
        BOOL baselinePassed = [originalID isEqualToString:expectedOriginalID] &&
            ![originalID isEqualToString:modeID] &&
            [originalBefore[@"enabled"] boolValue] &&
            [originalBefore[@"selected"] boolValue] &&
            !originalParentEnabled && !originalModeEnabled &&
            !originalParentSelected && !originalModeSelected;
        report[@"baseline_passed"] = @(baselinePassed);
        BOOL activationPassed = NO;
        BOOL mutationStarted = NO;
        BOOL parentEnableAttempted = NO, modeEnableAttempted = NO, selectAttempted = NO;
        BOOL traceComplete = YES;
        BOOL modeWindowRecorded = NO, parentWindowRecorded = NO;
        BOOL modeDisableAttempted = NO, parentDisableAttempted = NO;
        NSMutableArray *observerSeries = [NSMutableArray array];
        NSString *error = @"";
        OSStatus parentEnableStatus = -1, modeEnableStatus = -1;
        OSStatus selectStatus = -1, restoreSelectStatus = -1;
        OSStatus modeDisableStatus = -1, parentDisableStatus = -1;
        NSString *restoreError = @"";

        @try {
            if (originalID.length == 0 || !originalRestorable ||
                ![parentBefore[@"present"] boolValue] ||
                ![modeBefore[@"present"] boolValue]) {
                error = @"Original source missing/not select-capable, or Pinyin source missing";
            } else if (!baselinePassed) {
                error = @"Hosted source state differs from known disabled SCIM/ITABC baseline";
            } else if (!observerRestored(observerBefore, originalID, originalBefore,
                                         parentBefore, modeBefore)) {
                error = @"Independent observer did not agree with original TIS state";
            } else if (![parentBefore[@"enable_capable"] boolValue] ||
                       ![modeBefore[@"enable_capable"] boolValue] ||
                       ![modeBefore[@"select_capable"] boolValue]) {
                error = @"Parent/mode not enable-capable and mode not select-capable";
            } else {
                if (!originalParentEnabled) {
                    TISInputSourceRef parent = copySource(parentID);
                    parentEnableAttempted = parent != NULL;
                    mutationStarted |= parentEnableAttempted;
                    parentEnableStatus = parent == NULL ? paramErr : TISEnableInputSource(parent);
                    if (parent != NULL) CFRelease(parent);
                    if (parentEnableStatus != noErr || !waitFor(parentID, @"enabled", YES))
                        error = @"Failed to enable SCIM parent";
                }
                if (error.length == 0 && ![state(modeID)[@"enabled"] boolValue]) {
                    TISInputSourceRef mode = copySource(modeID);
                    modeEnableAttempted = mode != NULL;
                    mutationStarted |= modeEnableAttempted;
                    modeEnableStatus = mode == NULL ? paramErr : TISEnableInputSource(mode);
                    if (mode != NULL) CFRelease(mode);
                    if (modeEnableStatus != noErr || !waitFor(modeID, @"enabled", YES))
                        error = @"Failed to enable ITABC mode";
                }
                if (error.length == 0) {
                    TISInputSourceRef mode = copySource(modeID);
                    selectAttempted = mode != NULL;
                    mutationStarted |= selectAttempted;
                    selectStatus = mode == NULL ? paramErr : TISSelectInputSource(mode);
                    if (mode != NULL) CFRelease(mode);
                    for (int attempt = 0; attempt < 20 && ![currentID() isEqualToString:modeID]; ++attempt)
                        [NSThread sleepForTimeInterval:0.1];
                    activationPassed = selectStatus == noErr && [currentID() isEqualToString:modeID] &&
                        [state(modeID)[@"enabled"] boolValue] && [state(modeID)[@"selected"] boolValue];
                    if (!activationPassed) error = @"TIS selection did not reach ITABC";
                }
            }
            report[@"selected_during_probe"] = currentID();
            report[@"parent_during_probe"] = state(parentID);
            report[@"mode_during_probe"] = state(modeID);
        } @catch (NSException *exception) {
            error = [NSString stringWithFormat:@"Activation exception: %@", exception.name];
        } @finally {
            if (!mutationStarted) {
                // A failed preflight must not issue any TIS mutation, including restore.
                traceComplete = NO;
            } else {
            // Restore selection before removing either source from the UI.
            BOOL originalSelected = NO;
            @try {
                NSString *selectedNow = currentID();
                if (![selectedNow isEqualToString:originalID] &&
                    !(selectAttempted && [selectedNow isEqualToString:modeID])) {
                    restoreError = @"Source changed outside probe selection; skipped source restore";
                } else if (originalRestorable && ![selectedNow isEqualToString:originalID]) {
                    TISInputSourceRef original = copySource(originalID);
                    restoreSelectStatus = original == NULL ? paramErr : TISSelectInputSource(original);
                    if (original != NULL) CFRelease(original);
                    if (restoreSelectStatus != noErr)
                        restoreError = @"Original-source TISSelectInputSource returned nonzero OSStatus";
                    for (int attempt = 0; attempt < 20 && ![currentID() isEqualToString:originalID]; ++attempt)
                        [NSThread sleepForTimeInterval:0.1];
                }
                originalSelected = originalRestorable && [currentID() isEqualToString:originalID];
            } @catch (NSException *exception) {
                NSString *detail = [NSString stringWithFormat:@"Source-restore exception: %@", exception.name];
                restoreError = restoreError.length == 0 ? detail :
                    [restoreError stringByAppendingFormat:@"; %@", detail];
            }
            if (!originalSelected) {
                // Never disable the active Pinyin source; discard this runner.
                if (restoreError.length == 0)
                    restoreError = @"Original source could not be reselected; skipped both disables";
            } else {
                // Disable only source states moved from disabled to enabled.
                @try {
                    [restoreTrace addObject:sample(@"before_mode_disable", started)];
                    if ((modeEnableAttempted || parentEnableAttempted) &&
                        !originalModeEnabled && [state(modeID)[@"enabled"] boolValue]) {
                        modeDisableAttempted = YES;
                        TISInputSourceRef mode = copySource(modeID);
                        modeDisableStatus = mode == NULL ? paramErr : TISDisableInputSource(mode);
                        if (mode != NULL) CFRelease(mode);
                        [restoreTrace addObject:sample(@"after_mode_disable_call", started)];
                        if (modeDisableStatus != noErr) {
                            NSString *detail = @"ITABC TISDisableInputSource returned nonzero OSStatus";
                            restoreError = restoreError.length == 0 ? detail :
                                [restoreError stringByAppendingFormat:@"; %@", detail];
                        }
                        BOOL reachedDisabled = waitFor(modeID, @"enabled", NO);
                        report[@"mode_disable_wait_reached"] = @(reachedDisabled);
                        [restoreTrace addObject:sample(@"after_mode_disable_wait", started)];
                    }
                    if (modeDisableAttempted) {
                        traceComplete &= traceWindow(@"after_mode", started, restoreTrace, observerSeries);
                        modeWindowRecorded = YES;
                        report[@"observer_after_mode_disable"] =
                            [observerSeries.firstObject objectForKey:@"view"];
                    }
                } @catch (NSException *exception) {
                    traceComplete = NO;
                    NSString *detail = [NSString stringWithFormat:@"Mode-disable exception: %@", exception.name];
                    restoreError = restoreError.length == 0 ? detail :
                        [restoreError stringByAppendingFormat:@"; %@", detail];
                }
                @try {
                    [restoreTrace addObject:sample(@"before_parent_disable", started)];
                    if (parentEnableAttempted && !originalParentEnabled &&
                        [state(parentID)[@"enabled"] boolValue]) {
                        parentDisableAttempted = YES;
                        TISInputSourceRef parent = copySource(parentID);
                        parentDisableStatus = parent == NULL ? paramErr : TISDisableInputSource(parent);
                        if (parent != NULL) CFRelease(parent);
                        [restoreTrace addObject:sample(@"after_parent_disable_call", started)];
                        if (parentDisableStatus != noErr) {
                            NSString *detail = @"SCIM TISDisableInputSource returned nonzero OSStatus";
                            restoreError = restoreError.length == 0 ? detail :
                                [restoreError stringByAppendingFormat:@"; %@", detail];
                        }
                        BOOL reachedDisabled = waitFor(parentID, @"enabled", NO);
                        report[@"parent_disable_wait_reached"] = @(reachedDisabled);
                        [restoreTrace addObject:sample(@"after_parent_disable_wait", started)];
                    }
                    if (parentDisableAttempted) {
                        traceComplete &= traceWindow(@"after_parent", started, restoreTrace, observerSeries);
                        parentWindowRecorded = YES;
                    }
                } @catch (NSException *exception) {
                    traceComplete = NO;
                    NSString *detail = [NSString stringWithFormat:@"Parent-disable exception: %@", exception.name];
                    restoreError = restoreError.length == 0 ? detail :
                        [restoreError stringByAppendingFormat:@"; %@", detail];
                }
            }
            }
        }

        traceComplete = traceComplete && modeWindowRecorded && parentWindowRecorded;
        [restoreTrace addObject:sample(@"final", started)];
        NSDictionary *observerAfter = observeSeparately(started);
        NSDictionary *originalAfter = state(originalID);
        NSDictionary *parentAfter = state(parentID);
        NSDictionary *modeAfter = state(modeID);
        NSString *sourceAfter = currentID();
        BOOL restorationPassed = [sourceAfter isEqualToString:originalID] &&
            [originalAfter[@"present"] boolValue] == [originalBefore[@"present"] boolValue] &&
            [originalAfter[@"enabled"] boolValue] == [originalBefore[@"enabled"] boolValue] &&
            [originalAfter[@"selected"] boolValue] == [originalBefore[@"selected"] boolValue] &&
            [parentAfter[@"present"] boolValue] == [parentBefore[@"present"] boolValue] &&
            [parentAfter[@"enabled"] boolValue] == originalParentEnabled &&
            [modeAfter[@"present"] boolValue] == [modeBefore[@"present"] boolValue] &&
            [modeAfter[@"enabled"] boolValue] == originalModeEnabled &&
            [parentAfter[@"selected"] boolValue] == originalParentSelected &&
            [modeAfter[@"selected"] boolValue] == originalModeSelected &&
            restoreError.length == 0 &&
            observerRestored(observerAfter, originalID, originalBefore, parentBefore, modeBefore);
        report[@"activation_passed"] = @(activationPassed);
        report[@"mutation_started"] = @(mutationStarted);
        report[@"restoration_passed"] = @(restorationPassed);
        report[@"error"] = error;
        report[@"restore_error"] = restoreError;
        report[@"trace_complete"] = @(traceComplete);
        report[@"restore_trace"] = restoreTrace;
        report[@"observer_series"] = observerSeries;
        report[@"observer_after"] = observerAfter;
        report[@"parent_enable_osstatus"] = @(parentEnableStatus);
        report[@"mode_enable_osstatus"] = @(modeEnableStatus);
        report[@"select_osstatus"] = @(selectStatus);
        report[@"restore_select_osstatus"] = @(restoreSelectStatus);
        report[@"mode_disable_osstatus"] = @(modeDisableStatus);
        report[@"parent_disable_osstatus"] = @(parentDisableStatus);
        report[@"source_after"] = sourceAfter;
        report[@"original_source_after"] = originalAfter;
        report[@"parent_after"] = parentAfter;
        report[@"mode_after"] = modeAfter;
        NSError *jsonError = nil;
        NSData *json = [NSJSONSerialization dataWithJSONObject:report options:NSJSONWritingSortedKeys error:&jsonError];
        if (json == nil) {
            fprintf(stderr, "Activation report serialization failed: %s\n", jsonError.localizedDescription.UTF8String);
            return 4;
        }
        fwrite(json.bytes, 1, json.length, stdout);
        fputc('\n', stdout);
        return activationPassed && restorationPassed && traceComplete ? 0 : 2;
    }
}
