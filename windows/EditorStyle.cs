using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shape=System.Windows.Shapes.Rectangle;
namespace Navodka {
partial class Editor {
    readonly List<Annotation> annotations=new List<Annotation>();
    ImageBrush picture;
    SceneLayout framing;
    PointerLayer pointer;
    int tool=-1, tab, cursorStyle;
    readonly Color[] accents={Color.FromRgb(255,199,63),Color.FromRgb(69,196,250),Color.FromRgb(242,138,175)};
    int accent;
    double markerSize=1,autoScale=1.5;
    bool edgeFollow=true;
    bool rings,dragging,editingLength;
    Point first;
    Rect selection=Rect.Empty;
    readonly List<Annotation> visible=new List<Annotation>();
    void InitStyle(){
        Get<ComboBox>("AutoScale").IsEnabled=Get<CheckBox>("EdgeFollow").IsEnabled=camera.Sections>0;
        Get<ComboBox>("AutoScale").SelectionChanged+=(s,e)=>{autoScale=new[]{1.25,1.50,1.75}[Get<ComboBox>("AutoScale").SelectedIndex];Range();Paint(position.Value);};
        Get<CheckBox>("EdgeFollow").Click+=(s,e)=>{edgeFollow=Get<CheckBox>("EdgeFollow").IsChecked==true;Paint(position.Value);};
        Bind("FxTab",()=>ShowTab(2));
        InitCanvas();
        Bind("Picture",()=>{
            var dlg=new Microsoft.Win32.OpenFileDialog{Filter=Language.Pick("Изображения|*.jpg;*.jpeg;*.png;*.bmp", "Images|*.jpg;*.jpeg;*.png;*.bmp")};
            if(dlg.ShowDialog(win)!=true)return;
            try{var bitmap=LocalPicture.Read(dlg.FileName);
                picture=new ImageBrush(bitmap){Stretch=Stretch.UniformToFill};SelectBackground(7);
            }catch(Exception ex){Alert(Language.Pick("Не удалось открыть фон. ", "Could not open the background. ")+ex.Message);}
        });
        pointer=new PointerLayer(camera);Get<Canvas>("SceneBase").Children.Add(pointer);
        InitPointer();
        for(int i=0;i<accents.Length;i++){
            int index=i;var button=Get<Button>("Accent"+i);button.IsEnabled=camera.HasPointer;button.Tag=i==accent?"chosen":null;
            Bind("Accent"+i,()=>{accent=index;for(int n=0;n<3;n++)Get<Button>("Accent"+n).Tag=n==index?"chosen":null;RefreshPointer();});
        }
        string[] ids={"Spot","Blur","Lens","Magnifier"};
        for(int i=0;i<ids.Length;i++){
            int n=i;Bind(ids[i],()=>{if(!ready)return;Pause();Get<ListBox>("FxList").SelectedIndex=-1;tool=n;MaskButtons();shot.Cursor=Cursors.Cross;
                Get<TextBlock>("FxHint").Text=Language.Pick("Протяни мышью рамку по видео.", "Drag to draw a rectangle on the video.");});
        }
        var list=Get<ListBox>("FxList");
        list.SelectionChanged+=(s,e)=>{var a=list.SelectedItem as Annotation;HistoryButtons();if(a==null)return;position.Selected=-1;position.ManualSelected=false;Pause();position.Value=a.Start;
            editingLength=true;double length=a.End-a.Start;
            Get<ComboBox>("FxDuration").SelectedIndex=Math.Abs(length-1)<.02?0:Math.Abs(length-3)<.02?1:Math.Abs(length-5)<.02?2:3;editingLength=false;};
        Get<ComboBox>("FxDuration").SelectionChanged+=(s,e)=>{
            var a=list.SelectedItem as Annotation;if(editingLength||a==null)return;
            Remember();a.End=Math.Min(duration,a.Start+new[]{1.0,3.0,5.0,duration}[Get<ComboBox>("FxDuration").SelectedIndex]);
            list.Items.Refresh();Range();StyleFrame(position.Value,true);
        };
        Bind("RemoveFx",()=>{var a=list.SelectedItem as Annotation;if(a==null)return;Remember();annotations.Remove(a);list.Items.Remove(a);StyleFrame(position.Value,true);Range();});
        shot.MouseLeftButtonDown+=(s,e)=>{
            if(tab!=2 || tool<0 || framing==null)return;
            Point p=e.GetPosition(Get<Grid>("Scene"));if(!framing.Video.Contains(p))return;
            dragging=true;first=p;selection=Rect.Empty;shot.CaptureMouse();e.Handled=true;
        };
        shot.MouseMove+=(s,e)=>{if(!dragging)return;selection=new Rect(first,Clamp(e.GetPosition(Get<Grid>("Scene"))));StyleFrame(position.Value,true);};
        shot.MouseLeftButtonUp+=(s,e)=>{
            if(!dragging)return;
            var last=Clamp(e.GetPosition(Get<Grid>("Scene")));dragging=false;shot.ReleaseMouseCapture();
            var r=new Rect(first,last);selection=Rect.Empty;
            if(r.Width>=4 && r.Height>=4){
                double at=position.Value,len=new[]{1.0,3.0,5.0,duration}[Get<ComboBox>("FxDuration").SelectedIndex];
                if(at<duration-.025){
                    var a=new Annotation{Start=at,End=Math.Min(duration,at+len),Kind=tool,
                        Bounds=new Rect(framing.ToSource(first,info.Width,info.Height),framing.ToSource(last,info.Width,info.Height))};
                    Remember();annotations.Add(a);list.Items.Add(a);list.SelectedItem=a;tool=-1;MaskButtons();shot.Cursor=null;Range();
                    Get<TextBlock>("FxHint").Text=Language.Pick("Выбери следующий эффект или запись в списке.", "Select the next effect or recording in the list.");
                }
            }
            StyleFrame(position.Value,true);e.Handled=true;
        };
    }
    void MaskButtons(){
        var ids=new[]{"Spot","Blur","Lens","Magnifier"};
        for(int i=0;i<ids.Length;i++)Get<Button>(ids[i]).Background=i==tool?Brushes.RoyalBlue:new SolidColorBrush(Color.FromRgb(43,45,52));
    }
    Point Clamp(Point p){return new Point(Math.Max(framing.X,Math.Min(framing.Video.Right,p.X)),Math.Max(framing.Y,Math.Min(framing.Video.Bottom,p.Y)));}
    void SelectBackground(int index){
        background=index;if(padding.Value==0)padding.Value=8;Choices();Layout();
    }
    void ShowTab(int next){
        tab=next;tool=-1;MaskButtons();dragging=false;selection=Rect.Empty;if(shot.IsMouseCaptured)shot.ReleaseMouseCapture();shot.Cursor=null;
        string[] panels={"Canvas","Cursor","Fx"};for(int i=0;i<3;i++){
            Get<StackPanel>(panels[i]+"Panel").Visibility=i==tab?Visibility.Visible:Visibility.Collapsed;
            var b=Get<Button>(panels[i]+"Tab");b.Background=i==tab?new SolidColorBrush(Ink("383B49")):Brushes.Transparent;b.Tag=i==tab?"chosen":null;
        }
        var scroll=Get<ScrollViewer>("InspectorScroll");scroll.ScrollToHome();
        if(ready)StyleFrame(position.Value,true);
    }
    void ArrangeScene(){
        framing=new SceneLayout(width,height,info.Width,info.Height,padding.Value,Get<Slider>("Inset").Value,Get<Slider>("Corners").Value);
        var scene=Get<Grid>("Scene");scene.Width=width;scene.Height=height;
        var paper=MakePaper();surface.Background=paper;
        Get<Canvas>("SceneBase").Background=LockedFrame?Brushes.Transparent:paper;
        Get<Grid>("VideoViewport").Clip=LockedFrame?new RectangleGeometry(framing.Video,framing.Radius,framing.Radius):null;
        var clip=Get<Canvas>("ClipVideo");clip.Width=framing.VideoW;clip.Height=framing.VideoH;
        Canvas.SetLeft(clip,framing.X);Canvas.SetTop(clip,framing.Y);
        clip.Clip=LockedFrame?null:new RectangleGeometry(new Rect(0,0,clip.Width,clip.Height),framing.Radius,framing.Radius);
        double sx=clip.Width/framing.CropW,sy=clip.Height/framing.CropH;
        foreach(var view in new FrameworkElement[]{player,Get<Image>("Decoded")}){
            view.Width=info.Width*sx;view.Height=info.Height*sy;
            Canvas.SetLeft(view,-framing.CropX*sx);Canvas.SetTop(view,-framing.CropY*sy);
        }
        pointer.Width=width;pointer.Height=height;StyleFrame(position.Value,true);
    }
    void StyleFrame(double t,bool force){
        if(framing==null)return;
        double x,y,age;camera.DisplayPointer(t,smooth,hideIdle,out x,out y,out age);
        pointer.Update(framing,info.Width,info.Height,t,art,markerSize,PointerAngle(t,x,y),smooth,hideIdle);PreviewClick(t);
        var active=annotations.Where(a=>a.Active(t)).ToList();
        if(!force && active.SequenceEqual(visible))return;
        visible.Clear();visible.AddRange(active);
        var layer=Get<Canvas>("FxLayer");layer.Children.Clear();
        foreach(var a in active){
            Rect r=a.Area(framing,info.Width,info.Height);if(r.IsEmpty || r.Width<2||r.Height<2)continue;
            if(a.Kind==0){
                var mask=new System.Windows.Shapes.Path{Fill=new SolidColorBrush(Color.FromArgb(160,0,0,0)),
                    Data=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(0,0,width,height)),new RectangleGeometry(r))};
                layer.Children.Add(mask);continue;
            }
            var from=r;
            if(a.Kind>=2)from=new Rect(r.X+r.Width/4,r.Y+r.Height/4,r.Width/2,r.Height/2);
            var sample=new VisualBrush(Get<Canvas>("SceneBase")){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=from,Stretch=Stretch.Fill};
            var box=new Grid{Width=r.Width,Height=r.Height,ClipToBounds=true};
            var image=new Shape{Fill=sample};
            if(a.Kind==1)image.Effect=new BlurEffect{Radius=Math.Min(30,Math.Min(r.Width,r.Height)/5),RenderingBias=RenderingBias.Performance};
            box.Children.Add(image);
            if(a.Kind==3){
                box.Clip=new EllipseGeometry(new Rect(0,0,r.Width,r.Height));
                box.Children.Add(new System.Windows.Shapes.Ellipse{Stroke=Brushes.White,StrokeThickness=2});
            }
            if(a.Kind==2)box.Children.Add(new Shape{Stroke=Brushes.White,StrokeThickness=2});
            Canvas.SetLeft(box,r.X);Canvas.SetTop(box,r.Y);layer.Children.Add(box);
        }
        if(dragging&&!selection.IsEmpty){
            var box=new Shape{Width=selection.Width,Height=selection.Height,Stroke=Brushes.DeepSkyBlue,StrokeThickness=2};
            Canvas.SetLeft(box,selection.X);Canvas.SetTop(box,selection.Y);layer.Children.Add(box);
        }
    }
}
}
