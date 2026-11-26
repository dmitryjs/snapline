using System;
using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Navodka {
sealed class ZoomSpan {
    public double Source,SourceLength,Start,End;
    public double TargetX=double.NaN,TargetY=double.NaN;
    public ZoomSpan Copy(){return (ZoomSpan)MemberwiseClone();}
}
sealed class Timeline : Slider {
    public readonly List<ZoomSpan> Spans=new List<ZoomSpan>();
    public Action BeginEdit,EndEdit;
    public Action<double,double> Trim;
    public Action<double> MoveManual;
    public int Selected=-1;
    public bool ManualSelected;
    enum Drag { None,Seek,Left,Right,Auto,AutoLeft,AutoRight,Manual }
    Drag drag;
    double down,originalStart,originalEnd;
    const double Grip=7;

    static readonly Brush Purple=Gradient("#723ADD","#913FF0"),Blue=Gradient("#1466F5","#168DF2");
    static readonly Brush TextBrush=Brush("#DDDFE8"), Gold=Brush("#F4BC40");
    static readonly Typeface Typeface=new Typeface("Segoe UI");

    DrawingGroup artwork;
    Size artworkSize;
    double duration,trimIn,trimOut,manual;
    bool auto;double multiplier;
    System.Collections.Generic.List<Annotation> annotations;
    static SolidColorBrush Brush(string hex){var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
    static LinearGradientBrush Gradient(string top,string bottom){
        var b=new LinearGradientBrush(Brush(top).Color,Brush(bottom).Color,90);b.Freeze();return b;
    }
    public Timeline(CameraTrack track){
        foreach(var t in track.Intervals())Spans.Add(new ZoomSpan{Source=t.Item1,SourceLength=t.Item2-t.Item1,Start=t.Item1,End=t.Item2});
        Focusable=true;ClipToBounds=true;SmallChange=1.0/30;LargeChange=1;
        Template=new ControlTemplate(typeof(Slider));Cursor=Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(this,InterfaceLanguage.Pick("Позиция на таймлайне", "Timeline position"));
    }
    public void UpdateClip(double length,double from,double to,double zoom,bool follow,System.Collections.Generic.List<Annotation> effects,double scale){
        multiplier=scale;
        annotations=effects;
        duration=length;trimIn=from;trimOut=to;manual=zoom;auto=follow;ArrangeMasks();artwork=null;InvalidateVisual();
    }
    double X(double t){return 6+Math.Max(0,ActualWidth-12)*Math.Max(0,Math.Min(1,t/duration));}
    double TimeAt(Point p){return duration*Math.Max(0,Math.Min(1,(p.X-6)/Math.Max(1,ActualWidth-12)));}
    Drag Hit(Point p,out int index){
        index=-1;
        if(p.Y>=27 && p.Y<=59){
            if(Math.Abs(p.X-X(trimIn))<=Grip)return Drag.Left;
            if(Math.Abs(p.X-X(trimOut))<=Grip)return Drag.Right;
        }
        if(manual>=0 && p.Y>=(auto?89:65) && p.Y<=(auto?105:85) && p.X>=X(manual)-2 && p.X<=X(manual+2)+2)return Drag.Manual;
        if(auto && p.Y>=64 && p.Y<=88){
            for(int i=0;i<Spans.Count;i++){
                var z=Spans[i];
                if(p.X<X(z.Start)-2 || p.X>X(z.End)+2)continue;
                index=i;
                if(Math.Abs(p.X-X(z.Start))<=Grip)return Drag.AutoLeft;
                if(Math.Abs(p.X-X(z.End))<=Grip)return Drag.AutoRight;
                return Drag.Auto;
            }
        }
        return Drag.Seek;
    }
    static Cursor Pointer(Drag d){
        if(d==Drag.Auto||d==Drag.Manual)return Cursors.SizeAll;
        return d==Drag.Seek?Cursors.Hand:Cursors.SizeWE;
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e){
        if(duration<=0)return;
        Focus();Point p=e.GetPosition(this);int i;drag=Hit(p,out i);down=TimeAt(p);
        Selected=i;ManualSelected=drag==Drag.Manual;artwork=null;InvalidateVisual();
        if(i>=0){originalStart=Spans[i].Start;originalEnd=Spans[i].End;}else originalStart=manual;
        if(drag==Drag.Seek)Value=down;else if(SelectionChanged!=null)SelectionChanged();
        started=false;
        Cursor=Pointer(drag);CaptureMouse();e.Handled=true;
    }
    static double Limit(double v,double lo,double hi){return Math.Max(lo,Math.Min(hi,v));}
    void ApplyDrag(Point p){
        double t=TimeAt(p),delta=t-down,frame=Math.Min(duration,1.0/30);
        if(drag==Drag.Seek){Value=t;return;}
        if(!started){if(Math.Abs(t-down)<duration/Math.Max(1,ActualWidth)*.25)return;started=true;if(BeginEdit!=null)BeginEdit();}
        if(drag==Drag.Left){double v=Limit(t,0,trimOut-frame);Trim(v,trimOut);Value=v;return;}
        if(drag==Drag.Right){double v=Limit(t,trimIn+frame,duration);Trim(trimIn,v);Value=Math.Max(trimIn,v-frame);return;}
        if(drag==Drag.Manual){MoveManual(Limit(originalStart+delta,0,Math.Max(0,duration-2)));return;}
        if(Selected<0 || Selected>=Spans.Count)return;
        var z=Spans[Selected];double left=Selected==0?0:Spans[Selected-1].End;
        double right=Selected+1==Spans.Count?duration:Spans[Selected+1].Start;
        double min=Math.Min(frame,originalEnd-originalStart);
        if(drag==Drag.AutoLeft)z.Start=Limit(originalStart+delta,left,z.End-min);
        else if(drag==Drag.AutoRight)z.End=Limit(originalEnd+delta,z.Start+min,right);
        else{double length=originalEnd-originalStart;z.Start=Limit(originalStart+delta,left,right-length);z.End=z.Start+length;}
        artwork=null;InvalidateVisual();if(Changed!=null)Changed();
    }
    public Action Changed,SelectionChanged;
    bool started;
    protected override void OnPreviewMouseMove(MouseEventArgs e){
        Point p=e.GetPosition(this);
        if(IsMouseCaptured){ApplyDrag(p);e.Handled=true;}else{int i;Cursor=Pointer(Hit(p,out i));}
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e){
        if(!IsMouseCaptured)return;ApplyDrag(e.GetPosition(this));ReleaseMouseCapture();e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e){
        base.OnLostMouseCapture(e);if(drag!=Drag.None && drag!=Drag.Seek && EndEdit!=null)EndEdit();drag=Drag.None;
    }
    public ZoomSpan SpanAt(double time){
        int lo=0,hi=Spans.Count;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(Spans[mid].Start<=time)lo=mid+1;else hi=mid;}
        return lo==0 || time>=Spans[lo-1].End?null:Spans[lo-1];
    }
    public CameraPose Sample(CameraTrack track,double time,bool follow){
        var z=SpanAt(time);if(z==null)return new CameraPose(1,.5,.5);
        return track.Sample(z.Source+(time-z.Start)*z.SourceLength/(z.End-z.Start),follow);
    }
    protected override void OnValueChanged(double oldValue,double newValue){base.OnValueChanged(oldValue,newValue);InvalidateVisual();}
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo){base.OnRenderSizeChanged(sizeInfo);artwork=null;InvalidateVisual();}
    void Label(DrawingContext dc,string text,double x,double y,double size,Brush color){
        var t=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,Typeface,size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(t,new Point(x,y));
    }
    void Block(DrawingContext dc,double a,double b,double y,double h,Brush fill,string label,bool selected=false){
        double x=X(a),w=X(b)-x;if(w<=0)return;
        dc.DrawRoundedRectangle(fill,null,new Rect(x,y,w,h),5,5);
        if(selected && w>18){
            var grip=new Pen(Brushes.White,2);
            foreach(double side in new[]{x+4,x+w-4})dc.DrawLine(grip,new Point(side,y+h*.3),new Point(side,y+h*.7));
        }
        if(w>78)Label(dc,label,x+8,y+(h-15)/2,11,Brushes.White);
    }
    readonly List<int> lanes=new List<int>();
    void ArrangeMasks(){
        lanes.Clear();if(annotations==null)return;
        for(int i=0;i<annotations.Count;i++)lanes.Add(0);
        var indices=new List<int>();for(int i=0;i<annotations.Count;i++)indices.Add(i);
        indices.Sort((a,b)=>annotations[a].Start.CompareTo(annotations[b].Start));
        var ends=new List<double>();
        foreach(int index in indices){
            var mask=annotations[index];int lane=ends.FindIndex(t=>t<=mask.Start);
            if(lane<0){lane=ends.Count;ends.Add(mask.End);}else ends[lane]=mask.End;
            lanes[index]=lane;
        }
        MinHeight=ends.Count==0?(manual>=0||auto?106:60):112+23*ends.Count;
    }
    void Build(){
        var group=new DrawingGroup();
        using(var dc=group.Open()){
            dc.DrawRectangle(Brush("#202127"),null,new Rect(RenderSize));
            dc.DrawRectangle(Brush("#191A1F"),null,new Rect(0,0,ActualWidth,23));
            if(duration>0){
                double raw=duration*62/Math.Max(1,ActualWidth-12),power=Math.Pow(10,Math.Floor(Math.Log10(raw)));
                double n=raw/power,step=(n<=1?1:n<=2?2:n<=5?5:10)*power;
                var pen=new Pen(Brush("#6A6D79"),1);
                for(double t=0;t<duration;t+=step/2){
                    double x=X(t);bool major=Math.Abs(t/step-Math.Round(t/step))<.01;
                    dc.DrawLine(pen,new Point(x,0),new Point(x,major?5:3));
                    if(major && x<ActualWidth-23)Label(dc,t.ToString("0.##")+InterfaceLanguage.Pick(" с", " s"),x+3,6,10,TextBrush);
                }
                Block(dc,trimIn,trimOut,27,31,Purple,InterfaceLanguage.Pick("Клип   ", "Clip   ")+(trimOut-trimIn).ToString("0.#")+InterfaceLanguage.Pick(" с   1.0×", " s   1.0×"),Selected<0&&!ManualSelected);
                if(auto)for(int i=0;i<Spans.Count;i++)Block(dc,Spans[i].Start,Spans[i].End,65,22,Blue,InterfaceLanguage.Pick("Автозум   ", "Auto zoom   ")+multiplier.ToString("0.##")+"×",Selected==i);
                if(annotations!=null)for(int i=0;i<annotations.Count;i++){var a=annotations[i];Block(dc,a.Start,a.End,109+23*lanes[i],19,Brush("#388063"),Annotation.Names[a.Kind]);}
                if(manual>=0)Block(dc,manual,Math.Min(duration,manual+2),auto?89:65,17,Blue,InterfaceLanguage.Pick("Зум   1,5×", "Zoom   1.5×"),ManualSelected);
            }
        }
        group.Freeze();artwork=group;artworkSize=RenderSize;
    }
    protected override void OnRender(DrawingContext dc){
        if(artwork==null || artworkSize!=RenderSize)Build();dc.DrawDrawing(artwork);
        if(duration<=0)return;
        double x=X(Value);dc.DrawLine(new Pen(Gold,1.4),new Point(x,0),new Point(x,ActualHeight));
        var cap=new StreamGeometry();using(var c=cap.Open()){c.BeginFigure(new Point(x-4,0),true,true);c.LineTo(new Point(x+4,0),true,false);c.LineTo(new Point(x,7),true,false);}cap.Freeze();dc.DrawGeometry(Gold,null,cap);
    }
}
}
