#import "Language.h"
// Older Intel systems use AVFoundation's native display input.
- (void)editURL:(NSURL *)url {
    editing=YES;
    [[ClipEditor new] show:url];
    editing=NO;
}
- (void)importVideo {
    NSOpenPanel *panel=NSOpenPanel.openPanel;
    panel.allowedFileTypes=@[@"mp4",@"mov",@"m4v"]; panel.directoryURL=folder;
    if ([panel runModal]==NSModalResponseOK) [self editURL:panel.URL];
}
- (void)clearArea {
    regionDisplay=nil; region=NSZeroRect;
    status.stringValue=SLText(@"Запись всего экрана", @"Full-screen recording"); [self update];
}
- (void)selectArea {
    NSNumber *display=sources.selectedItem.representedObject;
    for (NSScreen *screen in NSScreen.screens) {
        if (![screen.deviceDescription[@"NSScreenNumber"] isEqual:display]) continue;
        [window orderOut:nil];
        NSRect chosen=PickRegion(screen);
        [window makeKeyAndOrderFront:nil];
        if (!NSIsEmptyRect(chosen)) {
            region=chosen; regionDisplay=display; regionScreenSize=screen.frame.size;
            status.stringValue=[NSString stringWithFormat:SLText(@"Выбрана область %.0f × %.0f", @"Selected area %.0f × %.0f"),chosen.size.width,chosen.size.height];
        }
        [self update]; return;
    }
    status.stringValue=SLText(@"Экран недоступен. Нажми «Обновить».", @"Display unavailable. Click Refresh.");
}
- (void)loadSources {
    regionDisplay=nil;
    NSString *selectedID=micList.selectedItem.representedObject;
    [micList removeAllItems];[micList addItemWithTitle:SLText(@"Системный по умолчанию", @"System default")];
    AVCaptureDeviceDiscoverySession *inputs = [AVCaptureDeviceDiscoverySession
        discoverySessionWithDeviceTypes:@[AVCaptureDeviceTypeBuiltInMicrophone,AVCaptureDeviceTypeExternalUnknown]
        mediaType:AVMediaTypeAudio position:AVCaptureDevicePositionUnspecified];
    for(AVCaptureDevice *device in inputs.devices){
        NSMenuItem *item=[micList.menu addItemWithTitle:device.localizedName action:NULL keyEquivalent:@""];
        item.representedObject=device.uniqueID;
        if ([selectedID isEqual:device.uniqueID]) [micList selectItem:item];
    }
    [sources removeAllItems];
    NSArray<NSScreen *> *screens = NSScreen.screens;
    for (NSScreen *screen in screens) {
        NSMenuItem *item = [sources.menu addItemWithTitle:screen.localizedName action:NULL keyEquivalent:@""];
        item.representedObject = screen.deviceDescription[@"NSScreenNumber"];
    }
    if (screens.count) [sources selectItemAtIndex:0];
    status.stringValue = screens.count ? SLText(@"Запись экрана · до 30 кадров/с", @"Screen recording · up to 30 fps") : SLText(@"Экран не найден", @"Display not found");
    [self update];
}
- (void)start {
    typedef bool (*AccessCheck)(void);
    AccessCheck preflight = (AccessCheck)dlsym(RTLD_DEFAULT, "CGPreflightScreenCaptureAccess");
    AccessCheck request = (AccessCheck)dlsym(RTLD_DEFAULT, "CGRequestScreenCaptureAccess");
    if (preflight && request && !preflight() && !request()) {
        [self reset:SLText(@"Разреши запись экрана в настройках конфиденциальности macOS и открой приложение снова.", @"Allow screen recording in macOS privacy settings, then reopen the app.")];
        return;
    }
    BOOL withMic = microphone.state == NSControlStateValueOn;
    if (withMic) {
        AVAuthorizationStatus permission = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio];
        if (permission == AVAuthorizationStatusNotDetermined) {
            phase = Starting;
            status.stringValue = SLText(@"Разреши доступ к микрофону для записи голоса", @"Allow microphone access to record your voice");
            [self update];
            [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:^(BOOL granted) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (self->quitting || !granted) {
                        [self reset:SLText(@"Доступ к микрофону не выдан. Можно снять галочку и записать экран.", @"Microphone access was denied. Turn off the microphone to record the screen.")];
                    } else {
                        [self start];
                    }
                });
            }];
            return;
        }
        if (permission != AVAuthorizationStatusAuthorized) {
            [self reset:SLText(@"Разреши доступ к микрофону в настройках macOS или сними галочку.", @"Allow microphone access in macOS settings or turn off the microphone.")];
            return;
        }
    }
    NSError *error = nil;
    if (![NSFileManager.defaultManager createDirectoryAtURL:folder withIntermediateDirectories:YES attributes:nil error:&error]) {
        [self reset:error.localizedDescription]; return;
    }
    CGDirectDisplayID display = [sources.selectedItem.representedObject unsignedIntValue];
    if (!CGDisplayIsOnline(display)) { [self reset:SLText(@"Экран отключён. Нажми «Обновить».", @"Display disconnected. Click Refresh.")]; return; }
    AVCaptureScreenInput *input = [[AVCaptureScreenInput alloc] initWithDisplayID:display];
    CGRect bounds = CGDisplayBounds(display);
    if(regionDisplay) {
        NSScreen *screen=nil;
        for(NSScreen *candidate in NSScreen.screens)
            if([candidate.deviceDescription[@"NSScreenNumber"] isEqual:regionDisplay]) screen=candidate;
        if(!screen || !NSEqualSizes(screen.frame.size,regionScreenSize) || display!=regionDisplay.unsignedIntValue) {
            [self reset:SLText(@"Параметры экрана изменились. Выбери область заново.", @"Display settings changed. Select the area again.")];return;
        }
        CGRect base=input.cropRect;
        if(CGRectIsEmpty(base)) base=(CGRect){CGPointZero,bounds.size};
        CGFloat x=base.size.width/regionScreenSize.width, y=base.size.height/regionScreenSize.height;
        bounds=CGRectIntegral(CGRectMake(base.origin.x+region.origin.x*x,base.origin.y+region.origin.y*y,region.size.width*x,region.size.height*y));
        input.cropRect=bounds;
    }
    input.minFrameDuration = CMTimeMake(1,30);
    NSScreen *selected=nil;
    for(NSScreen *screen in NSScreen.screens)
        if([screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue]==display)selected=screen;
    if(!selected){[self reset:SLText(@"Экран отключён", @"Display disconnected")];return;}
    captureFrame=regionDisplay?NSOffsetRect(region,selected.frame.origin.x,selected.frame.origin.y):selected.frame;
    CGFloat pixels=MAX(1,selected.backingScaleFactor);
    const CGFloat limits[]={720,1080,1440,2160};
    CGFloat limit=limits[resolution.indexOfSelectedItem];
    input.scaleFactor=MIN(1,MIN(limit/MIN(captureFrame.size.width*pixels,captureFrame.size.height*pixels),
                                (limit*16/9)/MAX(captureFrame.size.width*pixels,captureFrame.size.height*pixels)));
    input.capturesCursor = YES;
    input.capturesMouseClicks = audio.state == NSControlStateValueOn;
    capture = [AVCaptureSession new];
    movie = [AVCaptureMovieFileOutput new];
    if (!input || ![capture canAddInput:input]) { [self reset:SLText(@"Системе не удалось открыть экран", @"Could not open display")]; return; }
    [capture addInput:input];
    if (withMic) {
        NSString *selectedID = micList.selectedItem.representedObject;
        AVCaptureDevice *device = selectedID ? [AVCaptureDevice deviceWithUniqueID:selectedID] : [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeAudio];
        AVCaptureDeviceInput *voice = device ? [AVCaptureDeviceInput deviceInputWithDevice:device error:&error] : nil;
        if (!voice || ![capture canAddInput:voice]) {
            [self reset:error.localizedDescription ?: SLText(@"Микрофон недоступен. Проверь вход звука в настройках macOS.", @"Microphone unavailable. Check Sound Input in macOS settings.")];
            return;
        }
        [capture addInput:voice];
    }
    if (![capture canAddOutput:movie]) { [self reset:SLText(@"Видеозапись недоступна", @"Recording unavailable")]; return; }
    [capture addOutput:movie];
    if (withMic) {
        AVCaptureConnection *voiceTrack = [movie connectionWithMediaType:AVMediaTypeAudio];
        if (!voiceTrack) { [self reset:SLText(@"Не удалось подключить звуковую дорожку", @"Could not attach audio track")]; return; }
        [movie setOutputSettings:@{AVFormatIDKey:@(kAudioFormatMPEG4AAC), AVSampleRateKey:@48000,
                                   AVNumberOfChannelsKey:@1, AVEncoderBitRateKey:@96000} forConnection:voiceTrack];
    }
    AVCaptureConnection *connection = [movie connectionWithMediaType:AVMediaTypeVideo];
    if (connection)
        [movie setOutputSettings:@{AVVideoCodecKey:AVVideoCodecTypeH264} forConnection:connection];
    pending = [folder URLByAppendingPathComponent:[NSString stringWithFormat:@"Snapline-%@.mov", NSUUID.UUID.UUIDString]];
    phase = Starting;
    status.stringValue = SLText(@"Открываем экран…", @"Opening display…");
    [self update];
    AVCaptureSession *session = capture;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
        [session startRunning];
        dispatch_async(dispatch_get_main_queue(), ^{
            if (session != self->capture) return;
            if (!session.running) { [self reset:SLText(@"Не удалось запустить запись экрана", @"Could not start screen recording")]; return; }
            [self->movie startRecordingToOutputFileURL:self->pending recordingDelegate:self];
        });
    });
}
- (void)captureOutput:(AVCaptureFileOutput *)sender didStartRecordingToOutputFileAtURL:(NSURL *)url fromConnections:(NSArray *)connections {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sender != self->movie) return;
        self->phase = Recording;
        self->clicks=[NSMutableArray array];
        self->clickMonitor=[NSEvent addGlobalMonitorForEventsMatchingMask:NSEventMaskLeftMouseDown|NSEventMaskRightMouseDown handler:^(NSEvent *event){
            NSPoint point=NSEvent.mouseLocation;NSRect frame=self->captureFrame;
            double t=CMTimeGetSeconds(self->movie.recordedDuration);
            if(self->phase==Recording && !self->movie.recordingPaused && isfinite(t) && t>=0 && NSPointInRect(point,frame) && self->clicks.count<100000)
                [self->clicks addObject:@[@(t),@((point.x-frame.origin.x)/frame.size.width),@((point.y-frame.origin.y)/frame.size.height)]];
        }];
        self->status.stringValue = self->microphone.state == NSControlStateValueOn ? SLText(@"Идёт запись экрана и микрофона", @"Recording screen and microphone") : SLText(@"Идёт запись экрана", @"Recording screen");
        [self update];
        [self->window orderOut:nil];
        self->recordHUD=[[NSPanel alloc] initWithContentRect:NSMakeRect(NSMinX(self->captureFrame)+16,NSMinY(self->captureFrame)+18,235,44)
            styleMask:NSWindowStyleMaskBorderless|NSWindowStyleMaskNonactivatingPanel backing:NSBackingStoreBuffered defer:NO];
        self->recordHUD.level=NSFloatingWindowLevel;self->recordHUD.sharingType=NSWindowSharingNone;
        self->recordHUD.movableByWindowBackground=YES;self->recordHUD.hidesOnDeactivate=NO;
        self->pauseButton=[NSButton buttonWithTitle:SLText(@"Пауза", @"Pause") target:self action:@selector(togglePause)];
        self->pauseButton.frame=NSMakeRect(18,7,90,30);
        NSButton *stop=[NSButton buttonWithTitle:SLText(@"Остановить", @"Stop") target:self action:@selector(stop)];
        stop.frame=NSMakeRect(114,7,115,30);
        [self->recordHUD.contentView addSubview:self->pauseButton];[self->recordHUD.contentView addSubview:stop];
        [self->recordHUD orderFrontRegardless];
        if (self->quitting) [self stop];
    });
}
- (void)togglePause {
    if(phase!=Recording)return;
    if(movie.recordingPaused){[movie resumeRecording];pauseButton.title=SLText(@"Пауза", @"Pause");}
    else{[movie pauseRecording];pauseButton.title=SLText(@"Продолжить", @"Resume");}
}
- (void)stop {
    if(phase!=Recording)return;
    [recordHUD orderOut:nil];
    phase = Finishing;
    status.stringValue = SLText(@"Сохраняем запись…", @"Saving recording…");
    [self update];
    [movie stopRecording];
}
- (void)captureOutput:(AVCaptureFileOutput *)sender didFinishRecordingToOutputFileAtURL:(NSURL *)url fromConnections:(NSArray *)connections error:(NSError *)error {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (sender != self->movie) return;
        if(self->clickMonitor){[NSEvent removeMonitor:self->clickMonitor];self->clickMonitor=nil;}
        if(self->clicks.count)[self->clicks writeToURL:[url.URLByDeletingPathExtension URLByAppendingPathExtension:@"snapclicks"] atomically:YES];
        self->clicks=nil;
        self->phase = Finishing;
        [self update];
        AVCaptureSession *session = self->capture;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY,0), ^{ [session stopRunning]; });
        if (error && ![error.userInfo[AVErrorRecordingSuccessfullyFinishedKey] boolValue]) {
            [self reset:error.localizedDescription]; return;
        }
        self->last = url;
        AVAssetExportSession *export = [[AVAssetExportSession alloc] initWithAsset:[AVURLAsset URLAssetWithURL:url options:nil] presetName:AVAssetExportPresetPassthrough];
        if (![export.supportedFileTypes containsObject:AVFileTypeMPEG4]) {
            [self reset:SLText(@"Запись сохранена в MOV", @"Saved as MOV")]; if(!self->quitting)[self performSelector:@selector(openLast) withObject:nil afterDelay:0]; return;
        }
        NSURL *mp4 = [url.URLByDeletingPathExtension URLByAppendingPathExtension:@"mp4"];
        export.outputURL = mp4;
        export.outputFileType = AVFileTypeMPEG4;
        [export exportAsynchronouslyWithCompletionHandler:^{
            dispatch_async(dispatch_get_main_queue(), ^{
                if (export.status == AVAssetExportSessionStatusCompleted) {
                    self->last = mp4;
                    [NSFileManager.defaultManager removeItemAtURL:url error:nil];
                    [self reset:SLText(@"Запись сохранена в MP4", @"Saved as MP4")];
                } else {
                    [self reset:SLText(@"Не удалось создать MP4. Запись сохранена в MOV.", @"Could not create MP4. Recording saved as MOV.")];
                }
                if (!self->quitting) [self performSelector:@selector(openLast) withObject:nil afterDelay:0];
            });
        }];
    });
}
