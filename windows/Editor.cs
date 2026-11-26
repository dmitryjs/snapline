using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Navodka {
partial class Editor:Shell {
    readonly string path;
    FileStream sourceLock;
    readonly MediaElement player;
    MoviePreview decoded;
    MovieInfo info;
    bool closed,switching;
    readonly DispatcherTimer opening=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};
    readonly DispatcherTimer seek=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
    readonly CameraTrack camera;
    readonly CheckBox automatic;
    readonly ScaleTransform cameraTransform=new ScaleTransform();
    double manualX=.5,manualY=.5;
    readonly Timeline position;
    readonly Slider padding;
    int format,background;
    readonly Grid stage,shot;
    readonly Border surface;
    readonly DispatcherTimer timer;
    readonly string[] colors={"212526","163A34","CBB99B","F8F8F5"};
    double duration,start,end,zoom=-1;
    bool ready,playing,changing,exporting;
    int width,height;
    Worker render;
    public Editor(string file):base("Editor.xaml"){
        path=Path.GetFullPath(file);
        player=Get<MediaElement>("Player");padding=Get<Slider>("PaddingSlider");
        stage=Get<Grid>("Stage");shot=Get<Grid>("Shot");surface=Get<Border>("Surface");
        automatic=Get<CheckBox>("AutoCamera");
        bool damaged=false;
        try{camera=CameraTrack.Load(path);}catch(IOException){camera=new CameraTrack();damaged=true;}
        catch(InvalidDataException){camera=new CameraTrack();damaged=true;}
        catch(UnauthorizedAccessException){camera=new CameraTrack();damaged=true;}
        position=new Timeline(camera);Get<Grid>("TimelineHost").Children.Add(position);
        InitStyle();
        InitTimelineEditing();
        automatic.IsChecked=automatic.IsEnabled=camera.Sections>0;
        Get<TextBlock>("CameraInfo").Text=damaged?InterfaceLanguage.Pick("Файл движения курсора не читается.", "Could not read cursor movement file."):camera.Sections>0?
            InterfaceLanguage.Pick("Эпизодов по кликам: ", "Click zooms: ")+camera.Sections:File.Exists(CameraTrack.Sidecar(path))?
            InterfaceLanguage.Pick("Без кликов кадр остаётся общим.", "The full frame stays visible between clicks."):InterfaceLanguage.Pick("Для этого видео доступен ручной зум. Нажми на точку в кадре.", "Manual zoom is available for this video. Click a point in the frame.");
        automatic.Click+=(s,e)=>{Range();Paint(position.Value);};
        shot.RenderTransform=cameraTransform;
        shot.MouseLeftButtonDown+=(s,e)=>{
            if(tab!=1||!ready||shot.ActualWidth<=0||shot.ActualHeight<=0)return;
            var pt=e.GetPosition(shot);
            if(position.Selected>=0){
                var z=position.Spans[position.Selected];Remember();z.TargetX=Unit(pt.X/shot.ActualWidth);z.TargetY=Unit(pt.Y/shot.ActualHeight);
                Paint(position.Value);return;
            }
            // Store the point in original canvas coordinates before applying the new zoom.
            Remember();manualX=Math.Max(0,Math.Min(1,pt.X/shot.ActualWidth));manualY=Math.Max(0,Math.Min(1,pt.Y/shot.ActualHeight));
            MarkZoom();
        };
        Get<TextBlock>("Filename").Text=Path.GetFileName(path);
        for(int i=0;i<4;i++){
            int index=i;
            Bind("Format"+i,()=>{format=index;Choices();Layout();});

        }
        Bind("Format4",()=>{format=4;Choices();Layout();});
        Bind("CanvasTab",()=>ShowTab(0));Bind("CursorTab",()=>ShowTab(1));ShowTab(0);Choices();
        padding.ValueChanged+=(s,e)=>{Layout();};

        stage.SizeChanged+=(s,e)=>{if(framing==null)Layout();else FitPreview();};
        position.ValueChanged+=(s,e)=>{if(!changing&&ready){if(decoded==null)player.Position=TimeSpan.FromSeconds(position.Value);else{decoded.Dispose();seek.Stop();seek.Start();}Paint(position.Value);}};
        var fileMenu=new ContextMenu();
        foreach(var pair in new[]{Tuple.Create(InterfaceLanguage.Pick("Показать исходный файл", "Show source file"),"Reveal"),Tuple.Create(InterfaceLanguage.Pick("Экспортировать MP4…", "Export MP4…"),"Export")}){
            var item=new MenuItem{Header=pair.Item1};string id=pair.Item2;item.Click+=(s,e)=>Get<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));fileMenu.Items.Add(item);
        }
        Bind("FileMenu",()=>{fileMenu.PlacementTarget=Get<Button>("FileMenu");fileMenu.IsOpen=true;});
        var trimMenu=new ContextMenu();
        foreach(var pair in new[]{Tuple.Create(InterfaceLanguage.Pick("Удалить до указателя", "Delete before playhead"),"MarkIn"),Tuple.Create(InterfaceLanguage.Pick("Удалить после указателя", "Delete after playhead"),"MarkOut"),Tuple.Create(InterfaceLanguage.Pick("Восстановить длину", "Restore full length"),"Reset")}){
            string id=pair.Item2;var item=new MenuItem{Header=pair.Item1};item.Click+=(s,e)=>Get<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));trimMenu.Items.Add(item);
        }
        Bind("TrimMenu",()=>{if(!ready)return;trimMenu.PlacementTarget=Get<Button>("TrimMenu");trimMenu.IsOpen=true;});
        Bind("MarkIn",()=>{if(ready&&position.Value<end-.033){Remember();start=position.Value;}Range();});
        Bind("MarkOut",()=>{if(ready&&position.Value>start+.033){Remember();end=position.Value;}Range();});
        Bind("Reset",()=>{Remember();start=0;end=duration;Range();});
        Bind("Zoom",()=>{Remember();manualX=manualY=.5;MarkZoom();});
        Bind("Unzoom",()=>{Remember();zoom=-1;Range();Paint(position.Value);});
        Bind("Play",()=>{
            if(!ready)return;
            if(playing){Pause();return;}
            double t=position.Value>=end-.035||position.Value<start?start:position.Value;
            playing=true;PlayIcon(true);
            if(decoded!=null){seek.Stop();decoded.Show(t,true);}else{player.Position=TimeSpan.FromSeconds(t);player.Play();}
        });
        Bind("Export",Save);Get<Button>("Export").IsEnabled=false;
        Bind("Reveal",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),"/select,"+Worker.Quote(path)){UseShellExecute=false});}catch(Exception ex){Alert(ex.Message);}});
        player.MediaOpened+=(s,e)=>{
            if(closed||switching||decoded!=null)return;
            if(!player.NaturalDuration.HasTimeSpan||player.NaturalDuration.TimeSpan.TotalSeconds<=0||player.NaturalVideoWidth<=0||player.NaturalVideoHeight<=0){Fallback();return;}
            info=new MovieInfo{HasAudio=info!=null?info.HasAudio:player.HasAudio,Width=player.NaturalVideoWidth,Height=player.NaturalVideoHeight,Seconds=player.NaturalDuration.TimeSpan.TotalSeconds};
            opening.Stop();Pause();Opened();
        };
        player.MediaFailed+=(s,e)=>Fallback();
        player.MediaEnded+=(s,e)=>{if(decoded==null)Pause();};
        opening.Tick+=(s,e)=>Fallback();
        seek.Tick+=(s,e)=>{seek.Stop();if(decoded!=null)decoded.Show(position.Value,playing);};
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};
        timer.Tick+=(s,e)=>{if(ready&&playing&&decoded==null&&!position.IsMouseCaptureWithin)SetPosition(player.Position.TotalSeconds);};
        win.Loaded+=async(s,e)=>{
            try{
                if(!File.Exists(path)){Failure(InterfaceLanguage.Pick("Видео отсутствует в папке записи. ", "No video in the recording folder. ")+path);return;}
                sourceLock=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                try{info=await MovieInfo.Read(path);}catch(Exception ex){if(!closed)Failure(ex.Message);return;}
                if(closed)return;
                timer.Start();opening.Start();player.Source=new Uri(path,UriKind.Absolute);player.Play();
            }catch(Exception ex){if(info!=null)Fallback();else Failure(ex.Message);}
        };
        win.Closing+=(s,e)=>{if(exporting){e.Cancel=true;Get<TextBlock>("Note").Text=InterfaceLanguage.Pick("Подожди завершения экспорта", "Wait for export to finish");}};
        win.Closed+=(s,e)=>{closed=true;timer.Stop();opening.Stop();seek.Stop();if(decoded!=null)decoded.Dispose();player.Close();if(sourceLock!=null)sourceLock.Dispose();};
    }
    void SetPosition(double t){
        if(playing&&t>=end){Pause();t=end;if(decoded==null)player.Position=TimeSpan.FromSeconds(Math.Max(start,end-1.0/30));}
        changing=true;position.Value=t;changing=false;Paint(t);
    }
    void Failure(string message){
        ready=false;playing=false;PlayIcon(false);Get<Button>("Export").IsEnabled=false;
        var label=Get<TextBlock>("Loading");label.Text=message;label.Visibility=Visibility.Visible;
    }
    void Opened(){
        if(!ready){duration=info.Seconds;end=duration;
            position.Spans.RemoveAll(z=>z.Start>=duration);
            foreach(var z in position.Spans){z.End=Math.Min(duration,z.End);z.SourceLength=z.End-z.Start;}position.Maximum=duration;ready=true;Layout();Range();}
        Get<TextBlock>("Loading").Visibility=Visibility.Collapsed;Get<Button>("Export").IsEnabled=true;Paint(position.Value);
    }
    async void Fallback(){
        opening.Stop();if(closed||switching||decoded!=null)return;
        switching=true;
        try{if(info==null)info=await MovieInfo.Read(path);}catch(Exception ex){if(!closed)Failure(ex.Message);return;}
        if(closed)return;
        var image=Get<Image>("Decoded");
        decoded=new MoviePreview(path,info,win.Dispatcher,(bitmap,t)=>{
            image.Source=bitmap;if(!ready)Opened();SetPosition(t);
        },error=>{
            playing=false;PlayIcon(false);
            if(error!=null)Failure(error);else SetPosition(end);
        });
        player.Close();player.Visibility=Visibility.Collapsed;image.Visibility=Visibility.Visible;
        decoded.Show(position.Value,playing);
    }
    void Choices(){
        for(int i=0;i<5;i++){var b=Get<Button>("Format"+i);b.Tag=i==format?"chosen":null;b.Background=i==format?new SolidColorBrush(Ink("1768EA")):Brushes.Transparent;}
        for(int i=0;i<swatches.Count;i++)swatches[i].Tag=i==background?"swatch":null;
        Get<Button>("CustomSize").Tag=format==5?"chosen":null;
        Get<Button>("Picture").Tag=background==7?"chosen":null;
    }
    void PlayIcon(bool active){
        Get<System.Windows.Shapes.Path>("PlayIcon").Data=Geometry.Parse(active?"M8,4 L8,20 M16,4 L16,20":"M7,4 L19,12 7,20 Z");
        Get<Button>("Play").ToolTip=active?InterfaceLanguage.Pick("Пауза", "Pause"):InterfaceLanguage.Pick("Воспроизвести", "Play");
    }
    void Pause(){seek.Stop();if(decoded==null)player.Pause();else decoded.Dispose();playing=false;PlayIcon(false);}
    void Range(){
        position.UpdateClip(duration,start,end,zoom,automatic.IsChecked==true,annotations,autoScale);
        Get<TextBlock>("Note").Text=string.Format(InterfaceLanguage.Pick("Фрагмент   {0:0.00} — {1:0.00} с    ·    {2:0.00} с", "Clip   {0:0.00} — {1:0.00} s    ·    {2:0.00} s"),start,end,end-start)
            +(decoded==null?"":InterfaceLanguage.Pick("     Резервный просмотр без звука", "     Fallback preview without audio"))
            +(zoom>=0?string.Format(InterfaceLanguage.Pick("     Зум   {0:0.00} — {1:0.00} с", "     Zoom   {0:0.00} — {1:0.00} s"),zoom,Math.Min(duration,zoom+2)):"");
    }
    void Layout(){
        if(exporting||!ready||stage.ActualWidth<1||stage.ActualHeight<1)return;
        int i=format;
        width=i==0?Math.Max(2,info.Width/2*2):i==1?1920:i==4?1440:1080;
        height=i==0?Math.Max(2,info.Height/2*2):i==2?1920:1080;
        if(format==5){width=customWidth;height=customHeight;}
        if(format>0 && format<5 && Math.Max(info.Width,info.Height)>1920){
            double scale=Math.Min(3840,Math.Max(info.Width,info.Height))/(double)Math.Max(width,height);
            width=(int)Math.Round(width*scale/2)*2;height=(int)Math.Round(height*scale/2)*2;
        }
        FitPreview();ArrangeScene();Paint(position.Value);
    }
    void FitPreview(){
        if(exporting||width<=0||height<=0||stage.ActualWidth<=0||stage.ActualHeight<=0)return;
        double fit=Math.Min(stage.ActualWidth/width,stage.ActualHeight/height);
        surface.Width=fit*width;surface.Height=fit*height;
        Canvas.SetLeft(surface,(stage.ActualWidth-surface.Width)/2);Canvas.SetTop(surface,(stage.ActualHeight-surface.Height)/2);
    }
    void MarkZoom(){
        if(!ready)return;
        zoom=position.Value;Range();Paint(position.Value);
    }
    void Paint(double t){
        if(!ready||framing==null)return;
        StyleFrame(t,false);
        var pose=Pose(t,automatic.IsChecked==true);
        shot.RenderTransformOrigin=new Point(pose.X,pose.Y);
        cameraTransform.ScaleX=cameraTransform.ScaleY=pose.Scale;
        Get<TextBlock>("Time").Text=Clock(t)+" / "+Clock(duration);
    }
    static string Clock(double seconds){
        return ((int)seconds/60).ToString("00")+":"+((int)seconds%60).ToString("00")+"."+((int)(seconds*100)%100).ToString("00");
    }
    async void Save(){
        if(!ready||exporting||end<=start)return;
        Pause();
        var choice=ExportDialog.Choose(win,path,width,height);if(choice==null)return;
        string target=choice.File;
        string tmp=Path.Combine(Path.GetDirectoryName(target),"Snapline-"+Guid.NewGuid().ToString("N")+".partial.mp4");
        string workDir=null;
        try{
            exporting=true;((UIElement)win.Content).IsEnabled=false;
            Get<TextBlock>("Note").Text=InterfaceLanguage.Pick("Экспортируем фрагмент…", "Exporting clip…");
            bool auto=automatic.IsChecked==true,motion=auto||zoom>=0;
            workDir=Path.Combine(Path.GetTempPath(),"Snapline-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            width=choice.Width;height=choice.Height;ArrangeScene();
            bool nativeEncoder=await VideoEncoder.Available(new System.Drawing.Size(width,height));
            var args=RenderArgs(tmp,workDir,motion,nativeEncoder,choice.Quality);
            await System.Threading.Tasks.Task.Run(()=>{
                if(motion)CameraTrack.WriteFrames(Path.Combine(workDir,"navodka-camera.bin"),start,end,t=>Pose(t,auto));
                if(cursorStyle!=0||rings)WritePointer(Path.Combine(workDir,"snap-pointer.bin"),start,end);
            });
            render=new Worker("ffmpeg",args.ToArray(),workDir);
            if(await render.Wait()!=0)throw new IOException(render.Log);
            await MovieInfo.Read(tmp);
            if(File.Exists(target))File.Replace(tmp,target,null);else File.Move(tmp,target);
            Get<TextBlock>("Note").Text=InterfaceLanguage.Pick("Сохранено: ", "Saved: ")+target;
        }catch(Exception ex){Alert(InterfaceLanguage.Pick("Ошибка экспорта. ", "Export error. ")+ex.Message);Range();}
        finally{exporting=false;Layout();((UIElement)win.Content).IsEnabled=true;if(render!=null)render.Dispose();render=null;try{if(workDir!=null && Directory.Exists(workDir))Directory.Delete(workDir,true);if(File.Exists(tmp))File.Delete(tmp);}catch(IOException){}}
    }
}
}
