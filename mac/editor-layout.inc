    NSSize available=NSScreen.mainScreen.visibleFrame.size;
    CGFloat width=MIN(1140,available.width-28), height=MIN(700,available.height-62);
    window=[[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,width,height) styleMask:NSWindowStyleMaskClosable|NSWindowStyleMaskTitled|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];
    window.minSize=NSMakeSize(MIN(800,width),MIN(510,height)); window.releasedWhenClosed=NO;window.delegate=self;
    window.title=[NSString stringWithFormat:@"Snapline · %@",source.lastPathComponent];
    window.appearance=[NSAppearance appearanceNamed:NSAppearanceNameDarkAqua];window.backgroundColor=StudioColor(0x191a20);
    NSView *root=window.contentView;
    StudioPanel *sidebar=Panel(root,NSMakeRect(0,127,210,height-127),0x23252c);
    sidebar.autoresizingMask=NSViewHeightSizable;
    CGFloat top=sidebar.bounds.size.height;
    StudioLabel(sidebar,@"Оформление",NSMakeRect(15,top-41,178,26),17,YES);
    StudioLabel(sidebar,@"Формат кадра",NSMakeRect(15,top-82,178,20),12,NO);
    format=[[NSPopUpButton alloc] initWithFrame:NSMakeRect(10,top-121,187,34) pullsDown:NO];
    [format addItemsWithTitles:@[@"Исходный формат",@"16:9",@"9:16",@"1:1",@"4:3"]];format.target=self;format.action=@selector(changeFormat);[sidebar addSubview:format];
    StudioLabel(sidebar,@"Отступ",NSMakeRect(15,top-157,178,20),12,NO);
    padding=[NSSlider sliderWithValue:0 minValue:0 maxValue:38 target:self action:@selector(changeFormat)];
    padding.frame=NSMakeRect(12,top-185,182,25);padding.continuous=NO;[sidebar addSubview:padding];
    StudioLabel(sidebar,@"Цвет фона",NSMakeRect(15,top-223,178,20),12,NO);
    background=[[NSPopUpButton alloc] initWithFrame:NSMakeRect(10,top-264,187,34) pullsDown:NO];
    [background addItemsWithTitles:@[@"Графит",@"Хвоя",@"Песочный",@"Белый",@"Индиго · градиент",@"Лагуна · градиент"]];background.target=self;background.action=@selector(changeFormat);[sidebar addSubview:background];
    StudioLabel(sidebar,@"Приближение · 1,5×",NSMakeRect(15,top-299,188,21),12,NO);
    NSButton *zoom=[NSButton buttonWithTitle:@"Зум здесь" target:self action:@selector(addZoom)];
    NSButton *clearZoom=[NSButton buttonWithTitle:@"Убрать" target:self action:@selector(removeZoom)];
    zoom.frame=NSMakeRect(9,top-335,99,32);clearZoom.frame=NSMakeRect(109,top-335,87,32);
    [sidebar addSubview:zoom];[sidebar addSubview:clearZoom];
    for(NSView *child in sidebar.subviews)child.autoresizingMask=NSViewMinYMargin;
    view=[[AVPlayerView alloc] initWithFrame:NSMakeRect(224,140,width-240,height-202)];
    view.autoresizingMask=NSViewWidthSizable|NSViewHeightSizable;
    view.player=[AVPlayer playerWithPlayerItem:[AVPlayerItem playerItemWithAsset:asset]];
    view.controlsStyle=AVPlayerViewControlsStyleInline;[root addSubview:view];
    NSTextField *name=StudioLabel(root,source.lastPathComponent,NSMakeRect(226,height-42,width-422,22),14,YES);
    name.autoresizingMask=NSViewWidthSizable|NSViewMinYMargin;
    NSButton *save=[self button:@"Экспорт MP4…" action:@selector(save) x:0 width:146];
    save.frame=NSMakeRect(width-159,height-49,147,35);save.autoresizingMask=NSViewMinXMargin|NSViewMinYMargin;save.bezelColor=StudioColor(0x316cff);
    track=[[ClipTrack alloc] initWithFrame:NSMakeRect(8,13,width-16,65)];
    track.duration=CMTimeGetSeconds(asset.duration);track.autoresizingMask=NSViewWidthSizable;
    __weak ClipEditor *weakSelf=self;
    track.edit=^(NSInteger handle,double seconds){[weakSelf editTrack:handle seconds:seconds];};
    track.toolTip=@"Нажми для перемотки. Потяни зелёный край для обрезки.";
    [root addSubview:track];
    rangeLabel=StudioLabel(root,@"",NSMakeRect(20,88,width-428,22),13,YES);rangeLabel.autoresizingMask=NSViewWidthSizable;
    NSButton *inButton=[self button:@"Начало здесь" action:@selector(markIn) x:0 width:115];
    NSButton *outButton=[self button:@"Конец здесь" action:@selector(markOut) x:0 width:115];
    NSButton *reset=[self button:@"Сбросить" action:@selector(resetRange) x:0 width:97];
    CGFloat x=width-365; for(NSButton *button in @[inButton,outButton,reset]){
        [button setFrameOrigin:NSMakePoint(x,81)];button.autoresizingMask=NSViewMinXMargin;x+=button.frame.size.width+5;
    }
    maskMenu=[[NSPopUpButton alloc] initWithFrame:NSMakeRect(10,top-408,187,32) pullsDown:NO];
    [maskMenu addItemsWithTitles:@[@"Маска на 3 секунды…",@"Spotlight",@"Размытие",@"Лупа",@"Callout",@"Удалить последнюю"]];
    maskMenu.target=self;maskMenu.action=@selector(pickMask);[sidebar addSubview:maskMenu];
    TrackDivider *divider=[[TrackDivider alloc] initWithFrame:NSMakeRect(218,129,width-226,6)];
    divider.autoresizingMask=NSViewWidthSizable;
    divider.resizeBy=^(CGFloat delta){
        ClipEditor *editor=weakSelf;if(!editor)return;
        NSRect preview=editor->view.frame,timeline=editor->track.frame;
        CGFloat shift=MAX(65-timeline.size.height,MIN(preview.size.height-170,delta));
        timeline.size.height+=shift;preview.origin.y+=shift;preview.size.height-=shift;
        editor->view.frame=preview;editor->track.frame=timeline;
        for(NSView *control in @[editor->rangeLabel,inButton,outButton,reset]){
            NSPoint pt=control.frame.origin;pt.y+=shift;[control setFrameOrigin:pt];
        }
        NSView *line=[editor->window.contentView viewWithTag:991];NSPoint pt=line.frame.origin;pt.y+=shift;[line setFrameOrigin:pt];
    };
    [root addSubview:divider];
    autoZoom=[NSButton checkboxWithTitle:@"Автозум по кликам" target:self action:@selector(changeFormat)];
    autoZoom.frame=NSMakeRect(12,top-372,187,28);
    autoZoom.state=clickEvents.count?NSControlStateValueOn:NSControlStateValueOff;autoZoom.enabled=clickEvents.count>0;
    [sidebar addSubview:autoZoom]; [self changeFormat];
    controls=@[maskMenu,format,padding,background,save,inButton,outButton,reset,track,zoom,clearZoom];
    [self displayRange];[window center];[window makeKeyAndOrderFront:nil];
    id clock=[view.player addPeriodicTimeObserverForInterval:CMTimeMake(1,12) queue:dispatch_get_main_queue() usingBlock:^(CMTime t){
        [weakSelf updatePlayhead:t];
    }];
    id key=[NSEvent addLocalMonitorForEventsMatchingMask:NSEventMaskKeyDown handler:^NSEvent *(NSEvent *e){
        if(e.window==self->window && e.keyCode==49 && !self->exporting){
            if(!e.isARepeat){if(self->view.player.rate)[self->view.player pause];else [self->view.player play];}
            return nil;
        }return e;
    }];
    [NSApp runModalForWindow:window];[NSEvent removeMonitor:key];[view.player removeTimeObserver:clock];
    [view.player pause];view.player=nil;[window orderOut:nil];
