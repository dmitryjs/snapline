#import "Language.h"
#import <Cocoa/Cocoa.h>
#ifdef NAVODKA_INTEL_COMPAT
#import <AVFoundation/AVFoundation.h>
#import <dlfcn.h>
#import "region.h"
#import "studio.h"
#import <CoreImage/CoreImage.h>
#import "editor.h"
#else
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#endif
#import <math.h>
#import <unistd.h>

typedef NS_ENUM(NSInteger, Phase) { Idle, Countdown, Starting, Recording, Finishing };

@interface Recorder : NSObject <NSApplicationDelegate, NSWindowDelegate, 
#ifdef NAVODKA_INTEL_COMPAT
    AVCaptureFileOutputRecordingDelegate>
#else
    SCStreamDelegate, SCRecordingOutputDelegate>
#endif
@end

@implementation Recorder {
    NSWindow *window;
    NSPopUpButton *sources;
    NSTextField *status, *destination;
    NSButton *action, *refresh, *folderButton, *preview, *audio;
    NSURL *folder, *pending, *last;
#ifdef NAVODKA_INTEL_COMPAT
    NSButton *microphone, *areaButton, *fullButton, *importButton;
    NSPopUpButton *micList, *resolution;
    NSMutableArray *clicks;
    id clickMonitor;
    NSRect captureFrame;
    NSPanel *recordHUD;
    NSButton *pauseButton;
    NSRect region;
    NSNumber *regionDisplay;
    NSSize regionScreenSize;
    BOOL editing;
    AVCaptureSession *capture;
    AVCaptureMovieFileOutput *movie;
#else
    SCStream *stream;
    SCRecordingOutput *output;
#endif
    Phase phase;
    NSUInteger generation;
    BOOL quitting, loading;
}

- (NSTextField *)label:(NSString *)text frame:(NSRect)frame {
    NSTextField *label = [NSTextField labelWithString:text];
    label.frame = frame;
    label.lineBreakMode = NSLineBreakByTruncatingMiddle;
    [window.contentView addSubview:label];
    return label;
}
- (NSButton *)button:(NSString *)title selector:(SEL)selector frame:(NSRect)frame {
    NSButton *button = [NSButton buttonWithTitle:title target:self action:selector];
    button.frame = frame;
    [window.contentView addSubview:button];
    return button;
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    window = [[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,510,264)
        styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable
        backing:NSBackingStoreBuffered defer:NO];
    window.title = @"Snapline";
    window.delegate = self;
    window.releasedWhenClosed = NO;
#ifdef NAVODKA_INTEL_COMPAT
#include "record-layout.inc"
#else
    [self label:SLText(@"Источник записи", @"Recording source") frame:NSMakeRect(20,224,200,20)];
    sources = [[NSPopUpButton alloc] initWithFrame:NSMakeRect(16,185,359,32) pullsDown:NO];
    [window.contentView addSubview:sources];
    refresh = [self button:SLText(@"Обновить", @"Refresh") selector:@selector(loadSources) frame:NSMakeRect(379,185,115,32)];
    NSString *optionTitle =
#ifdef NAVODKA_INTEL_COMPAT
        SLText(@"Подсвечивать клики мыши", @"Highlight mouse clicks");
#else
        SLText(@"Записать звук приложений", @"Record application audio");
#endif
    audio = [NSButton checkboxWithTitle:optionTitle target:nil action:NULL];
    audio.frame = NSMakeRect(20,151,365,24);
    [window.contentView addSubview:audio];
    folderButton = [self button:SLText(@"Папка…", @"Folder…") selector:@selector(chooseFolder) frame:NSMakeRect(15,107,109,32)];
#endif
    NSString *saved = [NSUserDefaults.standardUserDefaults stringForKey:@"saveFolder"];
    folder = saved.length ? [NSURL fileURLWithPath:saved isDirectory:YES] :
        [[NSFileManager.defaultManager URLsForDirectory:NSMoviesDirectory inDomains:NSUserDomainMask].firstObject URLByAppendingPathComponent:@"Snapline" isDirectory:YES];
#ifdef NAVODKA_INTEL_COMPAT
    destination.stringValue=folder.path;
#else
    destination = [self label:folder.path frame:NSMakeRect(132,112,351,22)];
    action = [self button:SLText(@"Записать", @"Record") selector:@selector(toggle) frame:NSMakeRect(15,60,217,36)];
    preview = [self button:SLText(@"Открыть последнюю запись", @"Open last recording") selector:@selector(openLast) frame:NSMakeRect(234,60,261,36)];
    status = [self label:@"" frame:NSMakeRect(20,12,469,40)];
#endif
    status.maximumNumberOfLines = 2;
    [window center];
    [window makeKeyAndOrderFront:nil];
    [NSApp activateIgnoringOtherApps:YES];
    [self loadSources];
}
- (void)update {
    BOOL idle = phase == Idle;
#ifdef NAVODKA_INTEL_COMPAT
    microphone.enabled = micList.enabled = areaButton.enabled = importButton.enabled = idle && !loading;
    resolution.enabled=idle && !loading;
    fullButton.enabled = idle && !loading;
    ((CaptureTile *)fullButton).selectedMode = regionDisplay == nil;
    ((CaptureTile *)areaButton).selectedMode = regionDisplay != nil;
    fullButton.needsDisplay = areaButton.needsDisplay = YES;
#endif
    sources.enabled = folderButton.enabled = audio.enabled = idle && !loading;
    refresh.enabled = idle && !loading;
    preview.enabled = idle && last != nil;
    action.enabled = (idle && !loading && sources.selectedItem != nil) || phase == Countdown || phase == Recording;
    action.title = phase == Countdown ? SLText(@"Отменить", @"Undo") : phase == Recording ? SLText(@"Остановить и сохранить", @"Stop and save") : SLText(@"Записать", @"Record");
}
#ifdef NAVODKA_INTEL_COMPAT
#include "compat-recording.inc"
#else
- (void)loadSources {
    loading = YES;
    [sources removeAllItems];
    status.stringValue = SLText(@"Получаем список экранов и окон…", @"Finding displays and windows…");
    [self update];
    [SCShareableContent getShareableContentExcludingDesktopWindows:YES onScreenWindowsOnly:YES completionHandler:^(SCShareableContent *content, NSError *error) {
        dispatch_async(dispatch_get_main_queue(), ^{
            self->loading = NO;
            for (SCDisplay *display in content.displays) {
                NSString *name = [NSString stringWithFormat:SLText(@"Экран %ld · %ld × %ld", @"Display %ld · %ld × %ld"), (long)self->sources.numberOfItems + 1, (long)display.width, (long)display.height];
                [self->sources.menu addItemWithTitle:name action:NULL keyEquivalent:@""].representedObject = display;
            }
            for (SCWindow *item in content.windows) {
                if (item.owningApplication.processID == getpid() || item.windowLayer != 0 || !item.title.length) continue;
                NSString *name = [NSString stringWithFormat:@"%@ · %@", item.owningApplication.applicationName, item.title];
                [self->sources.menu addItemWithTitle:name action:NULL keyEquivalent:@""].representedObject = item;
            }
            if (self->sources.numberOfItems) [self->sources selectItemAtIndex:0];
            self->status.stringValue = error ? SLText(@"Разреши запись экрана в настройках конфиденциальности macOS и открой «Snapline» снова.", @"Allow screen recording in macOS privacy settings, then reopen Snapline.") : self->sources.numberOfItems ? SLText(@"Готово к записи", @"Ready to record") : SLText(@"Источники не найдены. Нажми «Обновить».", @"No sources found. Click Refresh.");
            [self update];
        });
    }];
}
#endif
- (void)chooseFolder {
    NSOpenPanel *panel = NSOpenPanel.openPanel;
    panel.canChooseFiles = NO;
    panel.canChooseDirectories = YES;
    if ([panel runModal] == NSModalResponseOK) {
        folder = panel.URL;
        destination.stringValue = folder.path;
        [NSUserDefaults.standardUserDefaults setObject:folder.path forKey:@"saveFolder"];
    }
}
- (void)openLast {
#ifdef NAVODKA_INTEL_COMPAT
    if(last) [self editURL:last];
#else
    if (last && ![NSWorkspace.sharedWorkspace openURL:last]) status.stringValue = SLText(@"Не удалось открыть сохранённую запись.", @"Could not open the saved recording.");
#endif
}
- (void)reset:(NSString *)message {
#ifdef NAVODKA_INTEL_COMPAT
    [recordHUD orderOut:nil];recordHUD=nil;pauseButton=nil;
    [window makeKeyAndOrderFront:nil];
    movie = nil;
    capture = nil;
#else
    stream = nil;
    output = nil;
#endif
    pending = nil;
    phase = Idle;
    status.stringValue = message;
    [self update];
    if (quitting) [NSApp replyToApplicationShouldTerminate:YES];
}
- (void)toggle {
    if (phase == Recording) { [self stop]; return; }
    if (phase == Countdown) { ++generation; [self reset:SLText(@"Запись отменена", @"Recording cancelled")]; return; }
    if (phase != Idle) return;
    phase = Countdown;
    [self update];
    [self count:3 generation:++generation];
}
- (void)count:(NSInteger)n generation:(NSUInteger)token {
    if (generation != token || phase != Countdown) return;
    if (n == 0) { [self start]; return; }
    status.stringValue = [NSString stringWithFormat:SLText(@"До записи: %ld", @"Recording in %ld"), (long)n];
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW,NSEC_PER_SEC),dispatch_get_main_queue(),^{ [self count:n-1 generation:token]; });
}
#ifndef NAVODKA_INTEL_COMPAT
- (void)start {
    NSError *error = nil;
    if (![NSFileManager.defaultManager createDirectoryAtURL:folder withIntermediateDirectories:YES attributes:nil error:&error]) {
        [self reset:error.localizedDescription]; return;
    }
    id item = sources.selectedItem.representedObject;
    SCContentFilter *filter;
    if ([item isKindOfClass:SCDisplay.class]) {
        filter = [[SCContentFilter alloc] initWithDisplay:item excludingWindows:@[]];
    } else if ([item isKindOfClass:SCWindow.class]) {
        filter = [[SCContentFilter alloc] initWithDesktopIndependentWindow:item];
    } else { [self reset:SLText(@"Обнови список источников", @"Refresh sources")]; return; }
    SCStreamConfiguration *cfg = [SCStreamConfiguration new];
    CGFloat width = CGRectGetWidth(filter.contentRect) * filter.pointPixelScale;
    CGFloat height = CGRectGetHeight(filter.contentRect) * filter.pointPixelScale;
    if (width < 2 || height < 2) { [self reset:SLText(@"Источник больше недоступен", @"Source no longer available")]; return; }
    // Bound the long and short sides to a 1080p canvas in either orientation.
    CGFloat scale = MIN(1, MIN(1920 / MAX(width,height), 1080 / MIN(width,height)));
    cfg.width = MAX(2, (NSInteger)(width * scale) & ~1);
    cfg.height = MAX(2, (NSInteger)(height * scale) & ~1);
    cfg.minimumFrameInterval = CMTimeMake(1,60);
    cfg.showsCursor = YES;
    cfg.capturesAudio = audio.state == NSControlStateValueOn;
    cfg.excludesCurrentProcessAudio = YES;
    stream = [[SCStream alloc] initWithFilter:filter configuration:cfg delegate:self];
    pending = [folder URLByAppendingPathComponent:[NSString stringWithFormat:@"Snapline-%@.partial.mp4",NSUUID.UUID.UUIDString]];
    SCRecordingOutputConfiguration *file = [SCRecordingOutputConfiguration new];
    file.outputURL = pending;
    output = [[SCRecordingOutput alloc] initWithConfiguration:file delegate:self];
    if (![stream addRecordingOutput:output error:&error]) { [self reset:error.localizedDescription]; return; }
    phase = Starting;
    status.stringValue = SLText(@"Запускаем запись…", @"Starting recording…");
    [self update];
    SCStream *session = stream;
    [session startCaptureWithCompletionHandler:^(NSError *failure) {
        if (failure) dispatch_async(dispatch_get_main_queue(), ^{
            if (session == self->stream) [self reset:failure.localizedDescription];
        });
    }];
}
- (void)recordingOutputDidStartRecording:(SCRecordingOutput *)sender {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sender != self->output || self->phase != Starting) return;
        self->phase = Recording;
        self->status.stringValue = SLText(@"Идёт запись", @"Recording");
        [self update];
        if (self->quitting) [self stop];
    });
}
- (void)stop {
    phase = Finishing;
    status.stringValue = SLText(@"Сохраняем MP4…", @"Saving MP4…");
    [self update];
    SCStream *session = stream;
    [session stopCaptureWithCompletionHandler:^(NSError *error) {
        if (error) dispatch_async(dispatch_get_main_queue(), ^{
            if (session == self->stream) [self reset:error.localizedDescription];
        });
    }];
}
- (void)recordingOutputDidFinishRecording:(SCRecordingOutput *)sender {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sender != self->output) return;
        NSURL *base = self->pending.URLByDeletingPathExtension.URLByDeletingPathExtension;
        NSURL *finished = [base URLByAppendingPathExtension:@"mp4"];
        NSError *error = nil;
        if (![NSFileManager.defaultManager moveItemAtURL:self->pending toURL:finished error:&error]) {
            [self reset:error.localizedDescription]; return;
        }
        self->last = finished;
        [self reset:SLText(@"Запись сохранена", @"Recording saved")];
    });
}
- (void)recordingOutput:(SCRecordingOutput *)sender didFailWithError:(NSError *)error {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sender == self->output) { [self->stream stopCaptureWithCompletionHandler:nil]; [self reset:error.localizedDescription]; }
    });
}
- (void)stream:(SCStream *)sender didStopWithError:(NSError *)error {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sender == self->stream) [self reset:error.localizedDescription];
    });
}
#endif
- (BOOL)windowShouldClose:(NSWindow *)sender { [NSApp terminate:nil]; return NO; }
- (NSApplicationTerminateReply)applicationShouldTerminate:(NSApplication *)app {
#ifdef NAVODKA_INTEL_COMPAT
    if(editing){ NSBeep(); return NSTerminateCancel; }
#endif
    if (phase == Starting || phase == Recording || phase == Finishing) {
        quitting = YES;
        if (phase == Recording) [self stop];
        return NSTerminateLater;
    }
    return NSTerminateNow;
}
@end

@implementation NSApplication (SnaplineLanguageMenu)
- (void)snaplineSelectLanguage:(NSMenuItem *)sender {
    NSString *code = sender.representedObject;
    [[NSUserDefaults standardUserDefaults] setObject:code forKey:@"SnaplineLanguage"];
    for (NSMenuItem *item in sender.menu.itemArray)
        item.state = item == sender ? NSControlStateValueOn : NSControlStateValueOff;
    NSAlert *notice = [NSAlert new];
    notice.messageText = [code isEqualToString:@"en"] ?
        @"English will be used the next time you open Snapline." :
        @"Русский язык включится при следующем запуске Snapline.";
    [notice runModal];
}
@end

int main(void) {
    @autoreleasepool {
        NSApplication *app = NSApplication.sharedApplication;
        [app setActivationPolicy:NSApplicationActivationPolicyRegular];
        Recorder * __attribute__((objc_precise_lifetime)) recorder = [Recorder new];
        app.delegate = recorder;
        NSMenu *bar = [NSMenu new];
        NSMenuItem *root = [bar addItemWithTitle:@"" action:NULL keyEquivalent:@""];
        root.submenu = [NSMenu new];
        NSMenuItem *language = [root.submenu addItemWithTitle:SLText(@"Язык", @"Language") action:NULL keyEquivalent:@""];
        language.submenu = [NSMenu new];
        for (NSString *code in @[@"ru", @"en"]) {
            BOOL english = [code isEqualToString:@"en"];
            NSMenuItem *item = [language.submenu addItemWithTitle:(english ? @"English" : @"Русский")
                action:@selector(snaplineSelectLanguage:) keyEquivalent:@""];
            item.target = app;
            item.representedObject = code;
            item.state = english == SLEnglish() ? NSControlStateValueOn : NSControlStateValueOff;
        }
        [root.submenu addItemWithTitle:SLText(@"Завершить Snapline", @"Quit Snapline") action:@selector(terminate:) keyEquivalent:@"q"];
        app.mainMenu = bar;
        [app run];
    }
    return 0;
}
