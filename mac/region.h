#import "Language.h"
// AppKit and AVCaptureScreenInput both use a lower-left origin.
@interface RegionView : NSView
@property NSPoint anchor;
@property NSRect selection;
@end
@implementation RegionView
- (BOOL)acceptsFirstResponder { return YES; }
- (void)resetCursorRects { [self addCursorRect:self.bounds cursor:NSCursor.crosshairCursor]; }
- (void)drawRect:(NSRect)dirty {
    [[NSColor colorWithWhite:0 alpha:0.42] setFill]; NSRectFill(self.bounds);
    [[NSColor colorWithWhite:1 alpha:0.25] setFill]; NSRectFill(self.selection);
    [NSColor.whiteColor setStroke]; [NSBezierPath strokeRect:self.selection];
    [SLText(@"Выдели область мышью · Esc для отмены", @"Drag to select an area · Esc to cancel") drawAtPoint:NSMakePoint(28,28)
        withAttributes:@{NSFontAttributeName:[NSFont boldSystemFontOfSize:19], NSForegroundColorAttributeName:NSColor.whiteColor}];
}
- (void)mouseDown:(NSEvent *)e { self.anchor = [self convertPoint:e.locationInWindow fromView:nil]; }
- (void)mouseDragged:(NSEvent *)e {
    NSPoint b = [self convertPoint:e.locationInWindow fromView:nil], a = self.anchor;
    self.selection = NSIntegralRect(NSIntersectionRect(self.bounds, NSMakeRect(MIN(a.x,b.x),MIN(a.y,b.y),fabs(a.x-b.x),fabs(a.y-b.y))));
    self.needsDisplay = YES;
}
- (void)mouseUp:(NSEvent *)e {
    [self mouseDragged:e];
    if (self.selection.size.width >= 16 && self.selection.size.height >= 16) [NSApp stopModalWithCode:NSModalResponseOK];
}
- (void)cancelOperation:(id)sender { [NSApp stopModalWithCode:NSModalResponseCancel]; }
@end

@interface RegionPanel : NSPanel
@end
@implementation RegionPanel
- (BOOL)canBecomeKeyWindow { return YES; }
@end

static NSRect PickRegion(NSScreen *screen) {
    NSPanel *panel = [[RegionPanel alloc] initWithContentRect:screen.frame styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];
    panel.level = NSScreenSaverWindowLevel;
    panel.opaque = NO; panel.backgroundColor = NSColor.clearColor;
    panel.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary;
    RegionView *view = [[RegionView alloc] initWithFrame:NSMakeRect(0,0,screen.frame.size.width,screen.frame.size.height)];
    panel.contentView = view;
    [panel makeFirstResponder:view]; [panel makeKeyAndOrderFront:nil];
    NSModalResponse answer = [NSApp runModalForWindow:panel];
    [panel orderOut:nil];
    return answer == NSModalResponseOK ? view.selection : NSZeroRect;
}
