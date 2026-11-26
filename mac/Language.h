#import <Foundation/Foundation.h>

static inline BOOL SLEnglish(void) {
    static BOOL english;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSString *code = [[NSUserDefaults standardUserDefaults] stringForKey:@"SnaplineLanguage"];
        if (!code) code = NSLocale.preferredLanguages.firstObject;
        english = ![[code componentsSeparatedByCharactersInSet:
            [NSCharacterSet characterSetWithCharactersInString:@"-_"]].firstObject isEqualToString:@"ru"];
    });
    return english;
}

static inline NSString *SLText(NSString *russian, NSString *english) {
    return SLEnglish() ? english : russian;
}
