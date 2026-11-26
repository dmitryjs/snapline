#import <AVKit/AVKit.h>

@interface ClipEditor : NSObject <NSWindowDelegate>
- (void)show:(NSURL *)source;
@end
@implementation ClipEditor {
    NSWindow *window;
    AVPlayerView *view;
    AVAsset *asset;
    NSURL *original;
    NSTextField *rangeLabel;
    NSPopUpButton *format, *background;
    NSSlider *padding;
    ClipTrack *track;
    NSArray<NSControl *> *controls;
    CMTime begin, end, zoomStart, zoomEnd;
    NSArray *clickEvents;
    NSMutableArray *masks;
    NSPopUpButton *maskMenu;
    MaskPicker *picker;
    NSButton *autoZoom;
    BOOL hasZoom;
    BOOL exporting;
}
- (NSButton *)button:(NSString *)title action:(SEL)selector x:(CGFloat)x width:(CGFloat)width {
    NSButton *button = [NSButton buttonWithTitle:title target:self action:selector];
    button.frame = NSMakeRect(x,24,width,34);
    [window.contentView addSubview:button];
    return button;
}
- (void)show:(NSURL *)source {
    masks=[NSMutableArray array];original = source;
    NSURL *side=[source.URLByDeletingPathExtension URLByAppendingPathExtension:@"snapclicks"];
    NSNumber *size=nil;[side getResourceValue:&size forKey:NSURLFileSizeKey error:nil];
    NSMutableArray *points=[NSMutableArray array];double last=-1;
    if(size && size.unsignedLongLongValue<32*1024*1024){
        for(id row in [NSArray arrayWithContentsOfURL:side]){
            if(![row isKindOfClass:NSArray.class] || [row count]!=3)continue;
            BOOL valid=YES;for(id v in row)if(![v isKindOfClass:NSNumber.class] || !isfinite([v doubleValue]))valid=NO;
            if(!valid || [row[0] doubleValue]<last || [row[0] doubleValue]<0)continue;
            last=[row[0] doubleValue];[points addObject:row];
        }
    }
    clickEvents=[points copy];
    asset = [AVURLAsset URLAssetWithURL:source options:@{AVURLAssetPreferPreciseDurationAndTimingKey:@YES}];
    begin = kCMTimeZero; end = asset.duration;
    if (!isfinite(CMTimeGetSeconds(end)) || CMTimeGetSeconds(end)<=0 || ![asset tracksWithMediaType:AVMediaTypeVideo].count) {
        NSAlert *alert=[NSAlert new]; alert.messageText=@"Не удалось прочитать видеозапись"; [alert runModal]; return;
    }
#include "editor-layout.inc"
}
- (void)updatePlayhead:(CMTime)t {
    if(CMTIME_IS_NUMERIC(t)){track.playTime=CMTimeGetSeconds(t);track.needsDisplay=YES;}
}
- (void)editTrack:(NSInteger)handle seconds:(double)seconds {
    CMTime t=CMTimeMinimum(asset.duration,CMTimeMakeWithSeconds(seconds,600));
    CMTime step=CMTimeMinimum(asset.duration,CMTimeMake(1,30));
    if(handle==3 && hasZoom){
        double length=CMTimeGetSeconds(CMTimeSubtract(zoomEnd,zoomStart));
        zoomStart=CMTimeMakeWithSeconds(MAX(0,MIN(track.duration-length,seconds)),600);
        zoomEnd=CMTimeAdd(zoomStart,CMTimeMakeWithSeconds(length,600));track.zoomIn=CMTimeGetSeconds(zoomStart);track.zoomOut=CMTimeGetSeconds(zoomEnd);
        [self changeFormat];
    }
    if(handle==1) begin=t=CMTimeMaximum(kCMTimeZero,CMTimeMinimum(t,CMTimeSubtract(end,step)));
    if(handle==2) end=t=CMTimeMinimum(asset.duration,CMTimeMaximum(t,CMTimeAdd(begin,step)));
    [view.player pause];
    [view.player seekToTime:t toleranceBefore:kCMTimeZero toleranceAfter:kCMTimeZero];
    [self updatePlayhead:t];[self displayRange];
}
- (void)displayRange {
    track.startTime=CMTimeGetSeconds(begin);track.endTime=CMTimeGetSeconds(end);track.needsDisplay=YES;
    rangeLabel.stringValue=[NSString stringWithFormat:@"Фрагмент   %.2f — %.2f с     ·     %.2f с",CMTimeGetSeconds(begin),CMTimeGetSeconds(end),CMTimeGetSeconds(CMTimeSubtract(end,begin))];
}
- (void)markIn {
    CMTime t=view.player.currentTime;
    if (CMTIME_IS_NUMERIC(t) && CMTimeCompare(t,kCMTimeZero)>=0 && CMTimeCompare(t,end)<0) {begin=t; [self displayRange];}
    else NSBeep();
}
- (void)markOut {
    CMTime t=view.player.currentTime;
    if (CMTIME_IS_NUMERIC(t) && CMTimeCompare(t,begin)>0) {end=CMTimeMinimum(t,asset.duration); [self displayRange];}
    else NSBeep();
}
- (void)resetRange { begin=kCMTimeZero; end=asset.duration; [self displayRange]; }
- (void)addZoom {
    CMTime t=view.player.currentTime;
    if(!CMTIME_IS_NUMERIC(t))return;
    zoomStart=CMTimeMaximum(kCMTimeZero,CMTimeMinimum(t,CMTimeSubtract(asset.duration,CMTimeMake(2,1))));
    zoomEnd=CMTimeMinimum(asset.duration,CMTimeAdd(zoomStart,CMTimeMake(2,1)));
    if(CMTimeGetSeconds(CMTimeSubtract(zoomEnd,zoomStart))<0.05)return;
    hasZoom=YES; track.zoomIn=CMTimeGetSeconds(zoomStart); track.zoomOut=CMTimeGetSeconds(zoomEnd);
    track.needsDisplay=YES; [self changeFormat];
}
- (void)removeZoom {hasZoom=NO;track.zoomOut=0;track.needsDisplay=YES;[self changeFormat];}
- (void)pickMask {
    [picker removeFromSuperview];
    NSInteger kind=maskMenu.indexOfSelectedItem;[maskMenu selectItemAtIndex:0];
    if(kind==5){if(masks.count)[masks removeLastObject];[self changeFormat];return;}
    if(kind==0 || exporting)return;
    [view.player pause];
    NSRect rect=view.videoBounds;if(NSIsEmptyRect(rect))return;
    picker=[[MaskPicker alloc] initWithFrame:rect];
    double start=CMTimeGetSeconds(view.player.currentTime);
    __weak ClipEditor *weakSelf=self;
    picker.finish=^(NSRect rect){
        ClipEditor *editor=weakSelf;if(!editor)return;
        [editor->masks addObject:@[@(kind),@(start),[NSValue valueWithRect:rect]]];
        [editor changeFormat];
    };
    [view addSubview:picker];
}
- (void)changeFormat {
    AVPlayerItem *item=view.player.currentItem;
    NSInteger index=format.indexOfSelectedItem;
    NSArray *effects=[masks copy];
    NSArray *events=autoZoom.state==NSControlStateValueOn?clickEvents:@[];
    if(!index && !padding.doubleValue && !hasZoom && !events.count && !effects.count){item.videoComposition=nil;return;}
    AVAssetTrack *video=[asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    CGRect src=CGRectStandardize(CGRectApplyAffineTransform((CGRect){CGPointZero,video.naturalSize},video.preferredTransform));
    CGSize sizes[]={src.size,{1920,1080},{1080,1920},{1080,1080},{1440,1080}};
    CGSize canvas=sizes[index];CGRect bounds=(CGRect){CGPointZero,canvas};
    double pad=1-padding.doubleValue/100, manual=hasZoom?CMTimeGetSeconds(zoomStart):-10, stop=CMTimeGetSeconds(zoomEnd);
    NSInteger color=background.indexOfSelectedItem;
    const unsigned colors[]={0x212526,0x163a34,0xcbb99b,0xf8f8f5,0x5340bd,0x156df2};
    CIColor *ink=[CIColor colorWithCGColor:StudioColor(colors[color]).CGColor];
    CIImage *paper=[CIImage imageWithColor:ink];
    if(color>=4){
        CIColor *other=[CIColor colorWithCGColor:StudioColor(color==4?0xeda1f4:0x4de1cf).CGColor];
        paper=[CIFilter filterWithName:@"CILinearGradient" keysAndValues:@"inputPoint0",[CIVector vectorWithX:0 Y:0],
            @"inputPoint1",[CIVector vectorWithX:canvas.width Y:canvas.height],@"inputColor0",ink,@"inputColor1",other,nil].outputImage;
    }
    paper=[paper imageByCroppingToRect:bounds];
    AVMutableVideoComposition *composition=[AVMutableVideoComposition videoCompositionWithAsset:asset applyingCIFiltersWithHandler:^(AVAsynchronousCIImageFilteringRequest *request){
        CIImage *image=request.sourceImage;CGRect rect=image.extent;
        double fit=MIN(canvas.width/rect.size.width,canvas.height/rect.size.height)*pad;
        double w=rect.size.width*fit,h=rect.size.height*fit;
        CGRect aperture=CGRectMake((canvas.width-w)/2,(canvas.height-h)/2,w,h);
        double t=CMTimeGetSeconds(request.compositionTime),gain=0,x=.5,y=.5;
        NSUInteger lo=0,hi=events.count;
        while(lo<hi){NSUInteger mid=lo+(hi-lo)/2;if([events[mid][0] doubleValue]<=t+.48)lo=mid+1;else hi=mid;}
        if(lo){
            NSArray *pt=events[lo-1];double age=t-[pt[0] doubleValue];
            gain=MIN(1,MAX(0,MIN((age+.48)/.48,(1.55-age)/.76)));
            x=[pt[1] doubleValue];y=[pt[2] doubleValue];
            if(lo>1 && age<0){
                NSArray *prev=events[lo-2];double gap=[pt[0] doubleValue]-[prev[0] doubleValue];
                if(gap<=1.07){double blend=gain*gain*(3-2*gain);x=[prev[1] doubleValue]*(1-blend)+x*blend;y=[prev[2] doubleValue]*(1-blend)+y*blend;gain=1;}
            }
        }
        if(manual>=0 && t>=manual && t<stop){gain=MAX(gain,MIN(1,MIN((t-manual)/.35,(stop-t)/.35)));if(!events.count){x=.5;y=.5;}}
        gain=gain*gain*gain*(gain*(gain*6-15)+10);
        double z=1+.5*gain,px=MAX(0,MIN(1,3*x-1)),py=MAX(0,MIN(1,3*y-1));
        CGAffineTransform tr=CGAffineTransformMake(fit*z,0,0,fit*z,aperture.origin.x+(1-z)*w*px-rect.origin.x*fit*z,aperture.origin.y+(1-z)*h*py-rect.origin.y*fit*z);
        CIImage *shot=[[image imageByApplyingTransform:tr] imageByCroppingToRect:aperture];
        for(NSArray *mask in effects){
            double age=t-[mask[1] doubleValue];if(age<0 || age>=3)continue;
            CGRect r=[mask[2] rectValue];r=CGRectMake(r.origin.x*canvas.width,r.origin.y*canvas.height,r.size.width*canvas.width,r.size.height*canvas.height);
            r=CGRectIntersection(r,aperture);if(CGRectIsEmpty(r))continue;
            NSInteger kind=[mask[0] integerValue];
            if(kind==1){
                CIImage *dim=[[CIImage imageWithColor:[CIColor colorWithRed:0 green:0 blue:0 alpha:.62]] imageByCroppingToRect:aperture];
                shot=[[shot imageByCroppingToRect:r] imageByCompositingOverImage:[dim imageByCompositingOverImage:shot]];
            }else if(kind==2){
                CIImage *blur=[[shot imageByClampingToExtent] imageByApplyingFilter:@"CIGaussianBlur" withInputParameters:@{kCIInputRadiusKey:@12}];
                shot=[[blur imageByCroppingToRect:r] imageByCompositingOverImage:shot];
            }else{
                CGFloat cx=CGRectGetMidX(r),cy=CGRectGetMidY(r);
                CIImage *lens=[[shot imageByApplyingTransform:CGAffineTransformMake(2,0,0,2,-cx,-cy)] imageByCroppingToRect:r];
                if(kind==3){
                    CGFloat size=MIN(r.size.width,r.size.height)/2;
                    CIImage *circle=[CIFilter filterWithName:@"CIRadialGradient" keysAndValues:kCIInputCenterKey,[CIVector vectorWithX:cx Y:cy],
                        @"inputRadius0",@(MAX(0,size-1)),@"inputRadius1",@(size),@"inputColor0",[CIColor colorWithRed:1 green:1 blue:1],@"inputColor1",[CIColor colorWithRed:0 green:0 blue:0],nil].outputImage;
                    shot=[lens imageByApplyingFilter:@"CIBlendWithMask" withInputParameters:@{kCIInputBackgroundImageKey:shot,kCIInputMaskImageKey:[circle imageByCroppingToRect:aperture]}];
                }else{
                    CIImage *rim=[[CIImage imageWithColor:[CIColor colorWithRed:1 green:1 blue:1]] imageByCroppingToRect:r];
                    shot=[[lens imageByCroppingToRect:CGRectInset(r,1,1)] imageByCompositingOverImage:[rim imageByCompositingOverImage:shot]];
                }
            }
        }
        [request finishWithImage:[shot imageByCompositingOverImage:paper] context:nil];
    }];
    composition.renderSize=canvas;composition.frameDuration=CMTimeMake(1,30);
    item.videoComposition=composition;
}
- (void)save {
    [view.player pause];
    NSSavePanel *panel=NSSavePanel.savePanel;
    panel.allowedFileTypes=@[@"mp4"];panel.directoryURL=original.URLByDeletingLastPathComponent;
    panel.nameFieldStringValue=[original.lastPathComponent.stringByDeletingPathExtension stringByAppendingString:@"-edit.mp4"];
    if ([panel runModal]!=NSModalResponseOK) return;
    NSURL *target=panel.URL;
    if ([target.URLByResolvingSymlinksInPath.path caseInsensitiveCompare:original.URLByResolvingSymlinksInPath.path]==NSOrderedSame) {rangeLabel.stringValue=@"Выбери новое имя, чтобы сохранить оригинал";return;}
    NSURL *temp=[target.URLByDeletingLastPathComponent URLByAppendingPathComponent:[NSUUID.UUID.UUIDString stringByAppendingString:@".mp4"]];
    AVAssetExportSession *job=[[AVAssetExportSession alloc] initWithAsset:asset presetName:AVAssetExportPresetHighestQuality];
    if (![job.supportedFileTypes containsObject:AVFileTypeMPEG4]) {rangeLabel.stringValue=@"Экспорт MP4 недоступен для этого файла";return;}
    job.timeRange=CMTimeRangeFromTimeToTime(begin,end); job.outputURL=temp; job.outputFileType=AVFileTypeMPEG4;
    job.videoComposition=view.player.currentItem.videoComposition;
    exporting=YES; for(NSControl *c in controls)c.enabled=NO;
    rangeLabel.stringValue=@"Экспортируем фрагмент…";
    [job exportAsynchronouslyWithCompletionHandler:^{ dispatch_async(dispatch_get_main_queue(), ^{
        NSError *err=job.error;
        BOOL ok=job.status==AVAssetExportSessionStatusCompleted;
        NSFileManager *fm=NSFileManager.defaultManager;
        if(ok){
            if([fm fileExistsAtPath:target.path]) ok=[fm replaceItemAtURL:target withItemAtURL:temp backupItemName:nil options:0 resultingItemURL:nil error:&err];
            else ok=[fm moveItemAtURL:temp toURL:target error:&err];
        }
        if(!ok)[fm removeItemAtURL:temp error:nil];
        self->exporting=NO; for(NSControl *c in self->controls)c.enabled=YES;
        self->rangeLabel.stringValue=ok ? [NSString stringWithFormat:@"Сохранено · %@",target.lastPathComponent] : err.localizedDescription ?: @"Экспорт прерван";
        if(ok)[NSWorkspace.sharedWorkspace activateFileViewerSelectingURLs:@[target]];
    }); }];
}
- (BOOL)windowShouldClose:(NSWindow *)sender {
    if(exporting){NSBeep();return NO;} [NSApp stopModal];return NO;
}
@end
