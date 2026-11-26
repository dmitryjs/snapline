// Native regression for the production editor composition. Runs on a Mac without recording its desktop.
#import <Cocoa/Cocoa.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreImage/CoreImage.h>
#import "studio.h"
#import "editor.h"

static void require(BOOL ok,NSString *message){
    if(!ok){NSLog(@"FAILED: %@",message);exit(1);}
}
static void writeVideo(NSURL *url){
    NSError *err=nil;
    AVAssetWriter *writer=[[AVAssetWriter alloc] initWithURL:url fileType:AVFileTypeMPEG4 error:&err];
    require(writer!=nil,err.description);
    NSDictionary *settings=@{AVVideoCodecKey:AVVideoCodecTypeH264,AVVideoWidthKey:@320,AVVideoHeightKey:@180};
    AVAssetWriterInput *input=[AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeVideo outputSettings:settings];
    [writer addInput:input];
    AVAssetWriterInputPixelBufferAdaptor *adapter=[AVAssetWriterInputPixelBufferAdaptor assetWriterInputPixelBufferAdaptorWithAssetWriterInput:input sourcePixelBufferAttributes:@{(id)kCVPixelBufferPixelFormatTypeKey:@(kCVPixelFormatType_32BGRA)}];
    require([writer startWriting],writer.error.description);
    [writer startSessionAtSourceTime:kCMTimeZero];
    CVPixelBufferRef buffer=NULL;
    require(CVPixelBufferPoolCreatePixelBuffer(NULL,adapter.pixelBufferPool,&buffer)==kCVReturnSuccess,@"buffer");
    CVPixelBufferLockBaseAddress(buffer,0);
    for(int y=0;y<180;y++){
        uint8_t *row=CVPixelBufferGetBaseAddress(buffer)+y*CVPixelBufferGetBytesPerRow(buffer);
        for(int x=0;x<320;x++){int v=((x/12+y/12)%2)?215:40;row[4*x]=row[4*x+1]=row[4*x+2]=v;row[4*x+3]=255;}
    }
    CVPixelBufferUnlockBaseAddress(buffer,0);
    for(int n=0;n<100;n++){
        NSDate *until=[NSDate dateWithTimeIntervalSinceNow:8];
        while(!input.readyForMoreMediaData && until.timeIntervalSinceNow>0)[NSThread sleepForTimeInterval:.005];
        require(input.readyForMoreMediaData && [adapter appendPixelBuffer:buffer withPresentationTime:CMTimeMake(n,30)],writer.error.description);
    }
    CVPixelBufferRelease(buffer);[input markAsFinished];
    dispatch_semaphore_t finished=dispatch_semaphore_create(0);
    [writer finishWritingWithCompletionHandler:^{dispatch_semaphore_signal(finished);}];
    require(!dispatch_semaphore_wait(finished,dispatch_time(DISPATCH_TIME_NOW,20*NSEC_PER_SEC)) && writer.status==AVAssetWriterStatusCompleted,writer.error.description);
}
static NSData *frame(AVAsset *asset,double sec){
    AVAssetImageGenerator *generator=[[AVAssetImageGenerator alloc] initWithAsset:asset];
    generator.requestedTimeToleranceBefore=generator.requestedTimeToleranceAfter=kCMTimeZero;
    NSError *err=nil;CGImageRef image=[generator copyCGImageAtTime:CMTimeMakeWithSeconds(sec,600) actualTime:nil error:&err];
    require(image && CGImageGetWidth(image)==320 && CGImageGetHeight(image)==180,err.description);
    NSMutableData *data=[NSMutableData dataWithLength:320*180*4];
    CGColorSpaceRef rgb=CGColorSpaceCreateDeviceRGB();
    CGContextRef dc=CGBitmapContextCreate(data.mutableBytes,320,180,8,1280,rgb,kCGImageAlphaPremultipliedLast);
    CGContextDrawImage(dc,CGRectMake(0,0,320,180),image);
    CGContextRelease(dc);CGColorSpaceRelease(rgb);CGImageRelease(image);return data;
}
int main(void){@autoreleasepool{
    [NSApplication sharedApplication];
    NSURL *dir=[NSURL fileURLWithPath:[NSTemporaryDirectory() stringByAppendingPathComponent:NSUUID.UUID.UUIDString] isDirectory:YES];
    [NSFileManager.defaultManager createDirectoryAtURL:dir withIntermediateDirectories:YES attributes:nil error:nil];
    NSURL *source=[dir URLByAppendingPathComponent:@"source.mp4"];writeVideo(source);
    ClipEditor *localized = [ClipEditor new];
    NSString *code = [[NSUserDefaults standardUserDefaults] stringForKey:@"SnaplineLanguage"];
    NSString *expected = [code isEqual:@"ru"] ? @"Исходный формат" : @"Original format";
    dispatch_async(dispatch_get_main_queue(), ^{
        NSPopUpButton *control = [localized valueForKey:@"format"];
        require([control.itemArray.firstObject.title isEqual:expected], @"Incorrect editor language");
        [NSApp stopModal];
    });
    [localized show:source];
    AVURLAsset *asset=[AVURLAsset URLAssetWithURL:source options:nil];ClipEditor *editor=[ClipEditor new];
    AVPlayerView *view=[AVPlayerView new];view.player=[AVPlayer playerWithPlayerItem:[AVPlayerItem playerItemWithAsset:asset]];
    [editor setValue:view forKey:@"view"];[editor setValue:asset forKey:@"asset"];
    NSPopUpButton *format=[NSPopUpButton new];[format addItemWithTitle:@"Original"];[editor setValue:format forKey:@"format"];
    NSPopUpButton *bg=[NSPopUpButton new];[bg addItemsWithTitles:@[@"1",@"2",@"3",@"4",@"Gradient"]];[bg selectItemAtIndex:4];
    [editor setValue:bg forKey:@"background"];
    [editor setValue:[NSSlider sliderWithValue:25 minValue:0 maxValue:38 target:nil action:NULL] forKey:@"padding"];
    NSButton *autoZoom=[NSButton checkboxWithTitle:@"" target:nil action:NULL];autoZoom.state=NSControlStateValueOn;
    [editor setValue:autoZoom forKey:@"autoZoom"];[editor setValue:@[@[@1.0,@.12,@.85]] forKey:@"clickEvents"];
    [editor changeFormat];
    AVAssetExportSession *exporter=[[AVAssetExportSession alloc] initWithAsset:asset presetName:AVAssetExportPresetHighestQuality];
    exporter.videoComposition=view.player.currentItem.videoComposition;
    exporter.outputFileType=AVFileTypeMPEG4;exporter.outputURL=[dir URLByAppendingPathComponent:@"export.mp4"];
    dispatch_semaphore_t done=dispatch_semaphore_create(0);[exporter exportAsynchronouslyWithCompletionHandler:^{dispatch_semaphore_signal(done);}];
    require(!dispatch_semaphore_wait(done,dispatch_time(DISPATCH_TIME_NOW,60*NSEC_PER_SEC)) && exporter.status==AVAssetExportSessionStatusCompleted,exporter.error.description);
    AVAsset *output=[AVAsset assetWithURL:exporter.outputURL];
    NSData *a=frame(output,0),*b=frame(output,1.2);const uint8_t *x=a.bytes,*y=b.bytes;
    double outside=0,inside=0;int nout=0,nin=0;
    for(int py=0;py<180;py++)for(int px=0;px<320;px++){
        int offset=(py*320+px)*4;
        if(px<12 || px>=308){outside+=abs(x[offset]-y[offset]);nout++;}
        if(px>70 && px<250 && py>45 && py<135){inside+=abs(x[offset]-y[offset]);nin++;}
    }
    NSLog(@"mean difference in margin %.2f, in footage %.2f",outside/nout,inside/nin);
    require(outside/nout<5 && inside/nin>20,@"fixed background or click zoom regression");
    [NSFileManager.defaultManager removeItemAtURL:dir error:nil];return 0;
}}
