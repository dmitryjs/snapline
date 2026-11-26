using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
namespace Navodka {
partial class Editor {
    readonly List<Brush> papers=new List<Brush>();
    readonly List<Button> swatches=new List<Button>();
    readonly string[][] palettes={
        new[]{"5340BD EDA1F4","156DF2 4DE1CF","FA597F FFB43B","23599D 84CDFF","DF856E FAD49C","6857CA FF82CA","259DCD 5343AA","284154 429799","7134C1 ED6B65","5033CA 49E5EF","CA4C71 F6DC87","0E6984 C0F098","4063A7 DDDCFF","9B4879 FAE9C5"},
        new[]{"42465C 929FB5","31464E 79948D","6C575A BDA4A4","3F4E72 9DA7C9","536C60 A8BEAF","725B47 D9C4AB","514960 AFA0CB"},
        new[]{"A55ACF FBC6DC","B46892 F7D29E","666AC8 C2EBFC","638F96 F0EBC7","9C75BF F9C8FF","C17B6A FFF0BC","8E67C1 90DCDD"},
        new[]{"163A59 FFAC78","164D47 D2CB71","46598B F7D7CE","123660 57CED2","64395D FBB696","2F5E48 BED790","24404E ECE5C8"}};
    int customWidth=1280,customHeight=720;
    bool fixedPaper;
    bool LockedFrame {get{return fixedPaper && (framing.VideoW<width || framing.VideoH<height || framing.Radius>0);}}
    static Color Ink(string hex){return (Color)ColorConverter.ConvertFromString("#"+hex);}
    static Brush Wallpaper(string pair,int i){
        var parts=pair.Split(' ');Color a=Ink(parts[0]),b=Ink(parts[1]);
        var canvas=new DrawingGroup();
        using(var d=canvas.Open()){
            d.DrawRectangle(new LinearGradientBrush(a,b,35+i*19),null,new Rect(0,0,1,1));
            var wave=Geometry.Parse(i%2==0?"M0,.73 C.27,.84 .32,.12 .70,.29 Q.91,.37 1,.09 V1 H0 Z":"M0,.35 C.33,.18 .55,.9 1,.40 V1 H0 Z");
            d.DrawGeometry(new LinearGradientBrush(Color.FromArgb(155,b.R,b.G,b.B),Color.FromArgb(55,a.R,a.G,a.B),70),null,wave);
        }
        canvas.Freeze();var brush=new DrawingBrush(canvas){Stretch=Stretch.Fill};brush.Freeze();return brush;
    }
    Brush Backdrop{get{return background==7?picture:papers[background];}}
    void InitCanvas(){
        foreach(var c in colors)papers.Add(new SolidColorBrush(Ink(c)));
        foreach(var p in palettes[0])papers.Add(Wallpaper(p,papers.Count));
        papers.Insert(7,Brushes.Transparent); // Slot 7 holds the user's picture.
        for(int g=1;g<palettes.Length;g++)foreach(var p in palettes[g])papers.Add(Wallpaper(p,papers.Count));
        int solidStart=papers.Count;
        foreach(var c in "BCD0FF 6DE9F6 55D5E6 9CD6B6 FFE678 FFCDD9 E9B9FF 9AAACB F2F4F8 CBE7D0 CFEC98 F9EA99 FEC36E F09299".Split(' '))papers.Add(new SolidColorBrush(Ink(c)));
        for(int i=0;i<papers.Count;i++){
            int n=i;var b=new Button{Height=25,Margin=new Thickness(3,3,3,3),Background=papers[i],Padding=new Thickness(0),
                ToolTip=i<4||i>=solidStart?Language.Pick("Цвет фона", "Background color"):Language.Pick("Градиент ", "Gradient ")+i};
            b.Click+=(sender,e)=>SelectBackground(n);swatches.Add(b);
            if(i<4 || i>=solidStart)Get<System.Windows.Controls.Primitives.UniformGrid>("PaintGallery").Children.Add(b);
        }
        for(int i=0;i<4;i++){int group=i;Bind("Group"+i,()=>Gallery(group));}
        Gallery(0);
        foreach(string id in new[]{"Inset","Corners","Shadow","PaperBlur"})Get<Slider>(id).ValueChanged+=(s,e)=>Layout();
        fixedPaper=Get<CheckBox>("FixedZoom").IsChecked==true;
        Get<CheckBox>("FixedZoom").Click+=(s,e)=>{fixedPaper=Get<CheckBox>("FixedZoom").IsChecked==true;Layout();};
        Bind("CustomSize",ChooseSize);
    }
    void Gallery(int group){
        var panel=Get<System.Windows.Controls.Primitives.UniformGrid>("WallpaperGallery");panel.Children.Clear();
        int first=group==0?4:4+1+palettes[0].Length+(group-1)*7;
        for(int i=0;i<palettes[group].Length;i++)panel.Children.Add(swatches[first+i+(group==0&&i>=3?1:0)]);
        for(int i=0;i<4;i++)Get<Button>("Group"+i).Tag=i==group?"chosen":null;
    }
    void ChooseSize(){
        var dialog=new Window{Owner=win,Title=Language.Pick("Размер холста", "Canvas size"),SizeToContent=SizeToContent.WidthAndHeight,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ResizeMode=ResizeMode.NoResize,Background=win.Background,Foreground=win.Foreground};
        var panel=new StackPanel{Margin=new Thickness(18)};dialog.Content=panel;
        TextBox w=new TextBox{Text=customWidth.ToString()},h=new TextBox{Text=customHeight.ToString()};
        foreach(var item in new[]{Tuple.Create(Language.Pick("Ширина, пикс.", "Width, px"),w),Tuple.Create(Language.Pick("Высота, пикс.", "Height, px"),h)}){panel.Children.Add(new TextBlock{Text=item.Item1});panel.Children.Add(item.Item2);}
        var message=new TextBlock{Text=Language.Pick("Чётные значения от 64 до 4096", "Even values from 64 to 4096"),Margin=new Thickness(0,8,0,8)};panel.Children.Add(message);
        var ok=new Button{Content=Language.Pick("Применить", "Apply"),IsDefault=true};panel.Children.Add(ok);ok.Click+=(s,e)=>{
            int x,y;if(!int.TryParse(w.Text,out x)||!int.TryParse(h.Text,out y)||x<64||y<64||x>4096||y>4096||x%2!=0||y%2!=0){message.Foreground=Brushes.Orange;return;}
            customWidth=x;customHeight=y;format=5;Choices();Layout();dialog.DialogResult=true;
        };dialog.ShowDialog();
    }
    Brush MakePaper(){
        var backdrop=Backdrop;
        double blur=Get<Slider>("PaperBlur").Value,shadow=Get<Slider>("Shadow").Value;
        if(blur==0 && shadow==0)return backdrop;
        double scale=Math.Min(1,720.0/Math.Max(width,height));
        int w=Math.Max(2,(int)Math.Round(width*scale)),h=Math.Max(2,(int)Math.Round(height*scale));
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){
            dc.DrawRectangle(backdrop,null,new Rect(0,0,w,h));
            if(blur>0){
                var bg=new DrawingVisual{Effect=new BlurEffect{Radius=blur*.24}};
                using(var part=bg.RenderOpen())part.DrawRectangle(backdrop,null,new Rect(0,0,w,h));
                dc.DrawRectangle(new VisualBrush(bg){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,w,h)},null,new Rect(0,0,w,h));
            }
            if(shadow>0){
                var rect=new Rect(framing.X*scale,framing.Y*scale,framing.VideoW*scale,framing.VideoH*scale);
                for(int i=18;i>=1;i--){var r=rect;r.Offset(0,shadow*.055);r.Inflate(i*shadow*.009,i*shadow*.009);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(shadow*.11),0,0,0)),null,r,framing.Radius*scale+i*.4,framing.Radius*scale+i*.4);}
            }
        }
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return new ImageBrush(bitmap){Stretch=Stretch.Fill};
    }
}
}
