#import <Cocoa/Cocoa.h>

static NSColor *StudioColor(unsigned rgb) {
    return [NSColor colorWithSRGBRed:((rgb>>16)&255)/255.0 green:((rgb>>8)&255)/255.0 blue:(rgb&255)/255.0 alpha:1];
}
@interface StudioPanel : NSView
@property(strong) NSColor *fill;
@end
@implementation StudioPanel
- (void)drawRect:(NSRect)dirty { [self.fill setFill]; NSRectFill(self.bounds); }
@end
static StudioPanel *Panel(NSView *parent,NSRect frame,unsigned rgb) {
    StudioPanel *panel=[[StudioPanel alloc] initWithFrame:frame]; panel.fill=StudioColor(rgb); [parent addSubview:panel]; return panel;
}
static NSTextField *StudioLabel(NSView *parent,NSString *text,NSRect frame,CGFloat size,BOOL bold) {
    NSTextField *label=[NSTextField labelWithString:text]; label.frame=frame;
    label.font=bold ? [NSFont boldSystemFontOfSize:size] : [NSFont systemFontOfSize:size];
    label.lineBreakMode=NSLineBreakByTruncatingMiddle;
    [parent addSubview:label];return label;
}

@interface CaptureTile : NSButton
@property BOOL regionIcon, selectedMode;
@end
@implementation CaptureTile
- (void)drawRect:(NSRect)dirty {
    NSBezierPath *card=[NSBezierPath bezierPathWithRoundedRect:NSInsetRect(self.bounds,1,1) xRadius:11 yRadius:11];
    [(self.selectedMode?StudioColor(0x244640):StudioColor(0x263536)) setFill];[card fill];
    [(self.selectedMode?StudioColor(0xbdf494):StudioColor(0x455455)) setStroke];[card stroke];
    NSRect icon=NSMakeRect(16,42,36,24);
    NSBezierPath *outline=[NSBezierPath bezierPathWithRoundedRect:icon xRadius:3 yRadius:3];
    outline.lineWidth=2;
    if(self.regionIcon){CGFloat dash[]={5,3};[outline setLineDash:dash count:2 phase:0];}
    [outline stroke];
    if(!self.regionIcon)[NSBezierPath strokeLineFromPoint:NSMakePoint(25,38) toPoint:NSMakePoint(43,38)];
    [self.title drawAtPoint:NSMakePoint(15,12) withAttributes:@{NSForegroundColorAttributeName:self.enabled?NSColor.whiteColor:NSColor.disabledControlTextColor,NSFontAttributeName:[NSFont boldSystemFontOfSize:14]}];
}
@end
static CaptureTile *ModeTile(NSView *parent,NSRect rect,NSString *title,id target,SEL action,BOOL region) {
    CaptureTile *tile=[[CaptureTile alloc] initWithFrame:rect];tile.title=title;tile.regionIcon=region;
    tile.target=target;tile.action=action;tile.accessibilityLabel=title;[parent addSubview:tile];return tile;
}

@interface ClipTrack : NSControl
@property double duration, startTime, endTime, playTime, zoomIn, zoomOut;
@property NSInteger handle;
@property CGFloat zoomDragOffset;
@property(copy) void (^edit)(NSInteger handle,double time);
@end
@implementation ClipTrack
- (CGFloat)x:(double)time {return 14+(self.bounds.size.width-28)*time/MAX(0.001,self.duration);}
- (void)drawRect:(NSRect)dirty {
    CGFloat x0=[self x:0],x1=[self x:self.duration],a=[self x:self.startTime],b=[self x:self.endTime];
    [StudioColor(0x1a2728) setFill];NSRectFill(self.bounds);
    [StudioColor(0x334443) setFill];NSRectFill(NSMakeRect(x0,9,x1-x0,26));
    [StudioColor(0x447568) setFill];NSRectFill(NSMakeRect(a,9,MAX(1,b-a),26));
    [StudioColor(0xc3f794) setFill];NSRectFill(NSMakeRect(a-2,7,4,30));NSRectFill(NSMakeRect(b-2,7,4,30));
    if(self.zoomOut>self.zoomIn){
        [StudioColor(0xf4b366) setFill];
        NSRectFill(NSMakeRect([self x:self.zoomIn],1,MAX(2,[self x:self.zoomOut]-[self x:self.zoomIn]),5));
    }
    for(NSInteger i=0;i<=4;i++){
        double t=self.duration*i/4;CGFloat x=[self x:t];
        NSString *s=[NSString stringWithFormat:@"%.1f c",t];
        NSDictionary *attrs=@{NSFontAttributeName:[NSFont monospacedDigitSystemFontOfSize:10 weight:NSFontWeightRegular],NSForegroundColorAttributeName:NSColor.lightGrayColor};
        [s drawAtPoint:NSMakePoint(MIN(x,x1-[s sizeWithAttributes:attrs].width),40) withAttributes:attrs];
    }
    [NSColor.whiteColor setFill];NSRectFill(NSMakeRect([self x:self.playTime]-1,4,2,35));
}
- (void)mouseDown:(NSEvent *)ev {
    if(!self.enabled)return;
    NSPoint p=[self convertPoint:ev.locationInWindow fromView:nil];
    self.handle=(p.y<38 && fabs(p.x-[self x:self.startTime])<=7)?1:(p.y<38 && fabs(p.x-[self x:self.endTime])<=7)?2:0;
    if(p.y<=8 && self.zoomOut>self.zoomIn && p.x>=[self x:self.zoomIn] && p.x<=[self x:self.zoomOut]){
        self.handle=3;self.zoomDragOffset=(p.x-14)*self.duration/(self.bounds.size.width-28)-self.zoomIn;
    }
    [self mouseDragged:ev];
}
- (void)mouseDragged:(NSEvent *)ev {
    if(!self.enabled)return;
    CGFloat x=[self convertPoint:ev.locationInWindow fromView:nil].x;
    self.edit(self.handle,MAX(0,MIN(self.duration,(x-14)*self.duration/(self.bounds.size.width-28)-(self.handle==3?self.zoomDragOffset:0))));
}
@end

@interface MaskPicker : NSView
@property NSPoint startPoint;
@property NSRect picked;
@property(copy) void (^finish)(NSRect);
@end
@implementation MaskPicker
- (void)drawRect:(NSRect)dirty {
    [[NSColor colorWithWhite:0 alpha:.16] setFill];NSRectFillUsingOperation(self.bounds,NSCompositingOperationSourceOver);
    [NSColor.systemBlueColor setStroke];[NSBezierPath strokeRect:self.picked];
}
- (void)mouseDown:(NSEvent *)event {self.startPoint=[self convertPoint:event.locationInWindow fromView:nil];}
- (void)mouseDragged:(NSEvent *)event {
    NSPoint a=self.startPoint,b=[self convertPoint:event.locationInWindow fromView:nil];
    self.picked=NSIntersectionRect(self.bounds,NSMakeRect(MIN(a.x,b.x),MIN(a.y,b.y),fabs(a.x-b.x),fabs(a.y-b.y)));
    self.needsDisplay=YES;
}
- (void)mouseUp:(NSEvent *)event {
    [self mouseDragged:event];NSRect r=self.picked;CGFloat w=self.bounds.size.width,h=self.bounds.size.height;
    if(w>0 && h>0 && r.size.width>4 && r.size.height>4)self.finish(NSMakeRect(r.origin.x/w,r.origin.y/h,r.size.width/w,r.size.height/h));
    [self removeFromSuperview];
}
@end

@interface TrackDivider : NSView
@property CGFloat lastY;
@property(copy) void (^resizeBy)(CGFloat);
@end
@implementation TrackDivider
- (NSInteger)tag {return 991;}
- (void)resetCursorRects {[self addCursorRect:self.bounds cursor:NSCursor.resizeUpDownCursor];}
- (void)drawRect:(NSRect)dirty {[NSColor.separatorColor setFill];NSRectFill(self.bounds);}
- (void)mouseDown:(NSEvent *)event {self.lastY=event.locationInWindow.y;}
- (void)mouseDragged:(NSEvent *)event {CGFloat y=event.locationInWindow.y;self.resizeBy(y-self.lastY);self.lastY=y;}
@end
