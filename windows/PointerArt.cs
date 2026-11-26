using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Navodka {
sealed class PointerArt {
    public const int Side=144, Phases=12;
    public const double Extent=3;
    public int Style,Effect;public Color Accent;public BitmapSource[] Tiles;
    static readonly Geometry Arrow=Geometry.Parse("M0,0 L0,1 .25,.76 .47,1.23 .65,1.14 .43,.70 .84,.66 Z");
    static readonly Geometry Narrow=Geometry.Parse("M0,0 L.20,1.23 .42,.82 .87,.72 Z");
    static readonly Geometry Hand=Geometry.Parse("M.2,1.08 L-.19,.55 Q-.27,.36 -.12,.39 L.12,.60 .10,.04 Q.12,-.1 .24,.02 L.30,.43 Q.38,.27 .48,.41 Q.59,.30 .67,.45 Q.83,.34 .89,.54 L.86,.94 .68,1.19 H.34 Z");
    static SolidColorBrush ColorBrush(Color c){return new SolidColorBrush(c);}
    static void Line(DrawingContext d,Pen p,double a,double b,double c,double e){d.DrawLine(p,new Point(a,b),new Point(c,e));}
    public static void DrawPointer(DrawingContext d,int style,Color accent){
        if(style==0)return;
        if(style==1){d.DrawEllipse(ColorBrush(Color.FromArgb(92,accent.R,accent.G,accent.B)),null,new Point(),.85,.85);return;}
        Brush fill=style==3?Brushes.Black:style==4?ColorBrush(accent):style==5?Brushes.MediumPurple:style==6?Brushes.LightSkyBlue:Brushes.White;
        d.DrawGeometry(fill,new Pen(style==3?Brushes.White:Brushes.Black,.075),style==7?Hand:style==6?Narrow:Arrow);
    }
    public static void DrawClick(DrawingContext d,int effect,double a,Color accent){
        if(effect==0)return;
        d.PushOpacity(Math.Min(1,(1-a)*1.4));
        var brush=ColorBrush(accent);var p=new Pen(brush,.12);
        double r=.42+a*1.45;
        if(effect<=4){
            if(effect==1)d.DrawEllipse(brush,null,new Point(),.28*(1-a)+.14,.28*(1-a)+.14);
            if(effect==2||effect==3){d.DrawEllipse(null,p,new Point(),r,r);if(effect==2)d.DrawEllipse(null,p,new Point(),r*.62,r*.62);}
            if(effect==4){d.PushOpacity(.55);d.DrawEllipse(brush,null,new Point(),r,r);d.Pop();}
        }else if(effect==5){
            var radial=new RadialGradientBrush(Color.FromArgb(155,accent.R,accent.G,accent.B),Colors.Transparent);
            d.DrawEllipse(radial,null,new Point(),r+.45,r+.45);
        }else{
            int count=effect==6?6:effect==7?12:8;
            for(int i=0;i<count;i++){
                double angle=i*2*Math.PI/count+.23,x=Math.Cos(angle)*r,y=Math.Sin(angle)*r;
                Brush c=i%3==0?Brushes.White:i%3==1?brush:effect==8?Brushes.LightGreen:Brushes.DeepSkyBlue;
                if(effect==6){Line(d,new Pen(c,.10),x-.16,y,x+.16,y);Line(d,new Pen(c,.10),x,y-.16,x,y+.16);}
                else if(effect==7)Line(d,new Pen(c,.10),x*.72,y*.72,x,y);
                else d.DrawEllipse(c,null,new Point(x,y),.13,.13);
            }
        }
        d.Pop();
    }
    public static BitmapSource Tile(int style,int effect,int phase,Color accent){
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
            dc.PushTransform(new TranslateTransform(Side/2.0,Side/2.0));dc.PushTransform(new ScaleTransform(Side/(Extent*2),Side/(Extent*2)));
            if(phase<0)DrawPointer(dc,style,accent);else DrawClick(dc,effect,(phase+.25)/Phases,accent);
            dc.Pop();dc.Pop();
        }
        var img=new RenderTargetBitmap(Side,Side,96,96,PixelFormats.Pbgra32);img.Render(visual);img.Freeze();return img;
    }
    public static PointerArt Create(int style,int effect,Color color){
        var art=new PointerArt{Style=style,Effect=effect,Accent=color,Tiles=new BitmapSource[Phases+1]};
        for(int i=0;i<=Phases;i++)art.Tiles[i]=Tile(style,effect,i-1,color);return art;
    }
    public void Save(string path){
        var bytes=new byte[Side*Side*4];using(var f=File.Create(path))foreach(var tile in Tiles){tile.CopyPixels(bytes,Side*4,0);f.Write(bytes,0,bytes.Length);}
    }
}
partial class Editor {
    int chosenPointer=2,clickEffect;
    double angle,direction,smooth,lastSound=-1;bool hideIdle,clickSound;
    readonly MemoryStream clickSample=new MemoryStream();
    System.Media.SoundPlayer speaker;
    void PreviewClick(double t){
        if(!playing || !clickSound){lastSound=t;return;}
        double x,y,age;
        if(camera.PointerAt(t,true,out x,out y,out age) && t-age>lastSound+1e-6 && age<.15){
            try{speaker.Play();}catch(InvalidOperationException){}catch(System.Runtime.InteropServices.ExternalException){}
        }
        lastSound=t;
    }
    PointerArt art;
    static readonly string[] PointerNames={"",Language.Pick("Ореол", "Halo"),Language.Pick("Светлый", "Light"),Language.Pick("Тёмный", "Dark"),Language.Pick("Акцент", "Accent"),Language.Pick("Сирень", "Lilac"),Language.Pick("Стрела", "Arrow"),Language.Pick("Рука", "Hand")};
    static readonly string[] EffectNames={"None","Default","Ripple","Ring","Diffusion","Spotlight","Sparkle","Firework","Christmas"};
    void InitPointer(){
        var show=Get<CheckBox>("ShowPointer");
        show.IsChecked=camera.CleanCursor&&camera.HasPointer;
        show.ToolTip=camera.CleanCursor?Language.Pick("Видимость указателя", "Pointer visibility"):Language.Pick("Системный курсор уже может быть частью записи. Переключатель управляет дополнительным указателем.", "The system cursor may already be in the recording. This controls the additional pointer.");
        cursorStyle=show.IsChecked==true?chosenPointer:0;show.Click+=(s,e)=>{cursorStyle=show.IsChecked==true?chosenPointer:0;RefreshPointer();};
        for(int i=1;i<PointerNames.Length;i++)AddPointerCard(false,i);
        for(int i=0;i<EffectNames.Length;i++)AddPointerCard(true,i);
        Get<StackPanel>("CursorPanel").IsEnabled=true;
        foreach(string id in new[]{"ShowPointer","PointerSize","CursorGallery","ClickGallery","Angle","DynamicAngle"})Get<UIElement>(id).IsEnabled=camera.HasPointer;
        foreach(string id in new[]{"PointerSize","Angle","DynamicAngle"}){string name=id;Bind("Reset"+name,()=>Get<Slider>(name).Value=name=="PointerSize"?1:0);}
        Get<Slider>("PointerSize").ValueChanged+=(s,e)=>{markerSize=e.NewValue;Paint(position.Value);};
        Get<Slider>("Angle").ValueChanged+=(s,e)=>{angle=e.NewValue;Paint(position.Value);};
        Get<Slider>("DynamicAngle").ValueChanged+=(s,e)=>{direction=e.NewValue;Paint(position.Value);};
        ClickAudio.Write(clickSample,0,.04,new[]{0.0});clickSample.Position=0;speaker=new System.Media.SoundPlayer(clickSample);
        win.Closed+=(s,e)=>{speaker.Dispose();clickSample.Dispose();};
        foreach(string id in new[]{"ClickAudio","SmoothPath","HideIdle","Smoothness"})Get<UIElement>(id).IsEnabled=camera.HasPointer;
        Get<CheckBox>("ClickAudio").Click+=(s,e)=>clickSound=Get<CheckBox>("ClickAudio").IsChecked==true;
        Action sync=()=>{hideIdle=Get<CheckBox>("HideIdle").IsChecked==true;bool on=Get<CheckBox>("SmoothPath").IsChecked==true;
            Get<Slider>("Smoothness").IsEnabled=on&&camera.HasPointer;smooth=on?Get<Slider>("Smoothness").Value/100:0;Paint(position.Value);};
        Get<CheckBox>("HideIdle").Click+=(s,e)=>sync();Get<CheckBox>("SmoothPath").Click+=(s,e)=>sync();Get<Slider>("Smoothness").ValueChanged+=(s,e)=>sync();
        sync();RefreshPointer();
    }
    static readonly string[] ClickIcons={
        "M18,10 A8,8 0 1 1 2,10 A8,8 0 1 1 18,10 M4.4,4.4 L15.6,15.6",
        "M5,2 L16,10.5 11,11.1 9,16.5 Z M12,13 L15,17",
        "M18,10 A8,8 0 1 1 2,10 A8,8 0 1 1 18,10 M14,10 A4,4 0 1 1 6,10 A4,4 0 1 1 14,10",
        "M17.5,10 A7.5,7.5 0 1 1 2.5,10 A7.5,7.5 0 1 1 17.5,10",
        "M4,6 Q10,0 16,6 M2,10 Q10,1 18,10 M5,13 Q10,7 15,13 M8,16 Q10,13 12,16",
        "M3,16 L7,4 14,4 17,16 M4,13 Q10,9 16,13 M4,16 Q10,13 16,16",
        "M10,2 Q10.5,9 17,10 Q10.5,11 10,18 Q9.5,11 3,10 Q9.5,9 10,2 Z",
        "M10,2 V6 M10,14 V18 M2,10 H6 M14,10 H18 M4,4 L7,7 M13,13 L16,16 M4,16 L7,13 M13,7 L16,4",
        "M10,2 L15,8 H13 L17,14 H3 L7,8 H5 Z M10,14 V18"
    };
    void AddPointerCard(bool click,int index){
        var list=Get<UniformGrid>(click?"ClickGallery":"CursorGallery");var label=click?EffectNames[index]:PointerNames[index];
        var visual=new DrawingVisual();using(var d=visual.RenderOpen()){
            d.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,48,28));
            if(click){
                var stroke=new Pen(new SolidColorBrush(Ink("D6DCE7")),1.45){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
                d.PushTransform(new TranslateTransform(14,4));
                d.DrawGeometry(null,stroke,Geometry.Parse(ClickIcons[index]));
                d.Pop();
            }else{
                d.PushTransform(new TranslateTransform(index==1?24:19,index==1?14:6));d.PushTransform(new ScaleTransform(13,13));
                PointerArt.DrawPointer(d,index,accents[0]);d.Pop();d.Pop();
            }
        }
        var drawing=new DrawingImage(visual.Drawing);drawing.Freeze();
        var stack=new StackPanel{Margin=new Thickness(3,3,3,5)};var b=new Button{Content=new Image{Source=drawing,Width=46,Height=28},Height=38,Margin=new Thickness(0),Padding=new Thickness(0),ToolTip=label};
        stack.Children.Add(b);if(click)stack.Children.Add(new TextBlock{Text=label,FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,4,0,0)});
        list.Children.Add(stack);
        b.Click+=(s,e)=>{
            if(click){clickEffect=index;rings=index!=0;}else{chosenPointer=index;Get<CheckBox>("ShowPointer").IsChecked=true;cursorStyle=index;}
            RefreshPointer();
        };
    }
    void RefreshPointer(){
        foreach(bool click in new[]{false,true}){
            var list=Get<UniformGrid>(click?"ClickGallery":"CursorGallery");
            for(int i=0;i<list.Children.Count;i++){
                var b=(Button)((StackPanel)list.Children[i]).Children[0];bool selected=click?i==clickEffect:i+1==cursorStyle;
                b.Background=selected?new SolidColorBrush(Ink("315688")):new SolidColorBrush(Ink("2C2E38"));b.Tag=selected?"chosen":null;
            }
        }
        art=PointerArt.Create(cursorStyle,clickEffect,accents[accent]);Paint(position.Value);
    }
    double PointerAngle(double t,double x,double y){
        double prevX,prevY,age;
        if(direction<=0 || !camera.PointerAt(t-.09,false,out prevX,out prevY,out age))return angle;
        return angle+direction*Math.Max(-1,Math.Min(1,(x-prevX)*14));
    }
}
sealed class PointerLayer:FrameworkElement {
    readonly CameraTrack track;public PointerLayer(CameraTrack t){track=t;IsHitTestVisible=false;}
    PointerArt art;SceneLayout layout;int w,h;double time,size,angle,smooth;bool hide;
    public void Update(SceneLayout scene,int w,int h,double time,PointerArt art,double size,double angle,double smooth,bool hide){
        if(this.art==art && art!=null && art.Style==0 && art.Effect==0)return;
        this.art=art;layout=scene;this.w=w;this.h=h;this.time=time;this.size=size;this.angle=angle;this.smooth=smooth;this.hide=hide;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext d){
        if(art==null||layout==null)return;
        double u=Math.Max(1,Math.Min(layout.W,layout.H)/48.0)*size*PointerArt.Extent;
        for(int i=0;i<2;i++){
            if(i==0?art.Style==0:art.Effect==0)continue;
            double x,y,age;bool visible=i==1?track.PointerAt(time,true,out x,out y,out age):track.DisplayPointer(time,smooth,hide,out x,out y,out age);if(!visible)continue;
            var p=layout.ToCanvas(x,y,w,h);if(!layout.Video.Contains(p))continue;
            d.PushTransform(new RotateTransform(i==0?angle:0,p.X,p.Y));
            d.DrawImage(art.Tiles[i==0?0:1+Math.Min(11,(int)(age/.4*12))],new Rect(p.X-u,p.Y-u,u*2,u*2));d.Pop();
        }
    }
}
}
