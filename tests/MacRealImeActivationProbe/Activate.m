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

/** Emit one privacy-safe JSON report and return nonzero on activation/restoration failure. */
int main(void) {
    @autoreleasepool {
        const char *actions = getenv("GITHUB_ACTIONS");
        const char *environment = getenv("RUNNER_ENVIRONMENT");
        const char *os = getenv("RUNNER_OS");
        if (actions == NULL || strcmp(actions, "true") != 0 ||
            environment == NULL || strcmp(environment, "github-hosted") != 0 ||
            os == NULL || strcmp(os, "macOS") != 0) {
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
        NSMutableDictionary *report = [@{
            @"schema": @"mote.mac-real-ime-activation.v1",
            @"real_ime_tested": @NO,
            @"os_version": [NSProcessInfo processInfo].operatingSystemVersionString,
            @"original_source_id": originalID,
            @"original_source_before": originalBefore,
            @"original_source_restorable": @(originalRestorable),
            @"parent_before": parentBefore,
            @"mode_before": modeBefore
        } mutableCopy];
        BOOL originalParentEnabled = [parentBefore[@"enabled"] boolValue];
        BOOL originalModeEnabled = [modeBefore[@"enabled"] boolValue];
        BOOL originalParentSelected = [parentBefore[@"selected"] boolValue];
        BOOL originalModeSelected = [modeBefore[@"selected"] boolValue];
        BOOL activationPassed = NO;
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
            } else if (![parentBefore[@"enable_capable"] boolValue] ||
                       ![modeBefore[@"enable_capable"] boolValue] ||
                       ![modeBefore[@"select_capable"] boolValue]) {
                error = @"Parent/mode not enable-capable and mode not select-capable";
            } else {
                if (!originalParentEnabled) {
                    TISInputSourceRef parent = copySource(parentID);
                    parentEnableStatus = parent == NULL ? paramErr : TISEnableInputSource(parent);
                    if (parent != NULL) CFRelease(parent);
                    if (parentEnableStatus != noErr || !waitFor(parentID, @"enabled", YES))
                        error = @"Failed to enable SCIM parent";
                }
                if (error.length == 0 && ![state(modeID)[@"enabled"] boolValue]) {
                    TISInputSourceRef mode = copySource(modeID);
                    modeEnableStatus = mode == NULL ? paramErr : TISEnableInputSource(mode);
                    if (mode != NULL) CFRelease(mode);
                    if (modeEnableStatus != noErr || !waitFor(modeID, @"enabled", YES))
                        error = @"Failed to enable ITABC mode";
                }
                if (error.length == 0) {
                    TISInputSourceRef mode = copySource(modeID);
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
            // Restore selection before removing either source from the UI.
            BOOL originalSelected = NO;
            @try {
                if (originalRestorable && ![currentID() isEqualToString:originalID]) {
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
                    if (!originalModeEnabled && [state(modeID)[@"enabled"] boolValue]) {
                        TISInputSourceRef mode = copySource(modeID);
                        modeDisableStatus = mode == NULL ? paramErr : TISDisableInputSource(mode);
                        if (mode != NULL) CFRelease(mode);
                        if (modeDisableStatus != noErr) {
                            NSString *detail = @"ITABC TISDisableInputSource returned nonzero OSStatus";
                            restoreError = restoreError.length == 0 ? detail :
                                [restoreError stringByAppendingFormat:@"; %@", detail];
                        }
                        (void)waitFor(modeID, @"enabled", NO);
                    }
                } @catch (NSException *exception) {
                    NSString *detail = [NSString stringWithFormat:@"Mode-disable exception: %@", exception.name];
                    restoreError = restoreError.length == 0 ? detail :
                        [restoreError stringByAppendingFormat:@"; %@", detail];
                }
                @try {
                    if (!originalParentEnabled && [state(parentID)[@"enabled"] boolValue]) {
                        TISInputSourceRef parent = copySource(parentID);
                        parentDisableStatus = parent == NULL ? paramErr : TISDisableInputSource(parent);
                        if (parent != NULL) CFRelease(parent);
                        if (parentDisableStatus != noErr) {
                            NSString *detail = @"SCIM TISDisableInputSource returned nonzero OSStatus";
                            restoreError = restoreError.length == 0 ? detail :
                                [restoreError stringByAppendingFormat:@"; %@", detail];
                        }
                        (void)waitFor(parentID, @"enabled", NO);
                    }
                } @catch (NSException *exception) {
                    NSString *detail = [NSString stringWithFormat:@"Parent-disable exception: %@", exception.name];
                    restoreError = restoreError.length == 0 ? detail :
                        [restoreError stringByAppendingFormat:@"; %@", detail];
                }
            }
        }

        NSDictionary *originalAfter = state(originalID);
        NSDictionary *parentAfter = state(parentID);
        NSDictionary *modeAfter = state(modeID);
        NSString *sourceAfter = currentID();
        BOOL restorationPassed = [sourceAfter isEqualToString:originalID] &&
            [originalAfter[@"enabled"] boolValue] == [originalBefore[@"enabled"] boolValue] &&
            [originalAfter[@"selected"] boolValue] == [originalBefore[@"selected"] boolValue] &&
            [parentAfter[@"enabled"] boolValue] == originalParentEnabled &&
            [modeAfter[@"enabled"] boolValue] == originalModeEnabled &&
            [parentAfter[@"selected"] boolValue] == originalParentSelected &&
            [modeAfter[@"selected"] boolValue] == originalModeSelected &&
            restoreError.length == 0;
        report[@"activation_passed"] = @(activationPassed);
        report[@"restoration_passed"] = @(restorationPassed);
        report[@"error"] = error;
        report[@"restore_error"] = restoreError;
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
        return activationPassed && restorationPassed ? 0 : 2;
    }
}
