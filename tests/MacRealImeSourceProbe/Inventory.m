/**
 * Read-only inventory of macOS text-input capabilities on a disposable runner.
 *
 * This intentionally does not select or enable any source, send a key, inspect
 * document text, or request a privacy grant. It cannot establish a real IME pass.
 */
#import <ApplicationServices/ApplicationServices.h>
#import <Carbon/Carbon.h>
#import <Foundation/Foundation.h>

/** Return a borrowed TIS Boolean property as a JSON-safe NSNumber. */
static NSNumber *boolProperty(TISInputSourceRef source, CFStringRef key) {
    CFBooleanRef value = (CFBooleanRef)TISGetInputSourceProperty(source, key);
    return @(value != NULL && CFBooleanGetValue(value));
}

/** Return a borrowed TIS string property without taking ownership of it. */
static NSString *stringProperty(TISInputSourceRef source, CFStringRef key) {
    CFStringRef value = (CFStringRef)TISGetInputSourceProperty(source, key);
    return value == NULL ? @"" : (__bridge NSString *)value;
}

/** Print only system input-source IDs and permission preflights as one JSON object. */
int main(void) {
    @autoreleasepool {
        TISInputSourceRef current = TISCopyCurrentKeyboardInputSource();
        NSString *currentID = current == NULL ? @"" : stringProperty(current, kTISPropertyInputSourceID);
        CFArrayRef all = TISCreateInputSourceList(NULL, true);
        NSMutableArray *chinese = [NSMutableArray array];
        CFIndex count = all == NULL ? 0 : CFArrayGetCount(all);
        for (CFIndex index = 0; index < count; ++index) {
            TISInputSourceRef source = (TISInputSourceRef)CFArrayGetValueAtIndex(all, index);
            NSString *sourceID = stringProperty(source, kTISPropertyInputSourceID);
            NSString *name = stringProperty(source, kTISPropertyLocalizedName);
            NSString *modeID = stringProperty(source, kTISPropertyInputModeID);
            BOOL relevant = [sourceID containsString:@"SCIM"] ||
                [sourceID localizedCaseInsensitiveContainsString:@"pinyin"] ||
                [name localizedCaseInsensitiveContainsString:@"pinyin"] ||
                [name containsString:@"拼音"];
            if (!relevant) continue;
            [chinese addObject:@{
                @"id": sourceID,
                @"mode_id": modeID,
                @"enabled": boolProperty(source, kTISPropertyInputSourceIsEnabled),
                @"enable_capable": boolProperty(source, kTISPropertyInputSourceIsEnableCapable),
                @"select_capable": boolProperty(source, kTISPropertyInputSourceIsSelectCapable),
                @"selected": boolProperty(source, kTISPropertyInputSourceIsSelected)
            }];
        }
        NSDictionary *report = @{
            @"schema": @"mote.mac-real-ime-source-inventory.v2",
            @"os_version": [NSProcessInfo processInfo].operatingSystemVersionString,
            @"current_source_id": currentID,
            @"installed_source_count": @(count),
            @"pinyin_sources": chinese,
            @"ax_trusted": @(AXIsProcessTrusted()),
            @"screen_capture_preflight": @(CGPreflightScreenCaptureAccess()),
            @"real_ime_tested": @NO
        };
        NSError *error = nil;
        NSData *json = [NSJSONSerialization dataWithJSONObject:report options:NSJSONWritingSortedKeys error:&error];
        if (all != NULL) CFRelease(all);
        if (current != NULL) CFRelease(current);
        if (json == nil) {
            fprintf(stderr, "IME inventory serialization failed: %s\n", error.localizedDescription.UTF8String);
            return 2;
        }
        fwrite(json.bytes, 1, json.length, stdout);
        fputc('\n', stdout);
        return 0;
    }
}
