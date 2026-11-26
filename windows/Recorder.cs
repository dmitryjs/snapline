using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Forms=System.Windows.Forms;
using Brush=System.Windows.Media.Brushes;

namespace Navodka {
class TargetItem {
    public string Name; public IntPtr Handle; public Rectangle Bounds;
    public override string ToString(){return Name;}
}
class Recorder:Shell {
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(System.Drawing.Point pt,uint flags);
    readonly ComboBox targets,inputs;
    bool microphone;
    readonly ContextMenu fileMenu=new ContextMenu();
    MenuItem folderMenu;
    readonly MenuItem systemCursor=new MenuItem{Header=Language.Pick("Записывать системный курсор", "Record system cursor"),IsCheckable=true,IsChecked=false};
    string mode="monitor",folder;
    readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Navodka","folder.txt");
    Rectangle? region;
    Worker job;
    RecordingHud hud;
    TaskCompletionSource<bool> resume;
    bool pausing;
    CursorSession cursor;
    ClickIndicator indicator;
    CancellationTokenSource countdown;
    bool busy,closing,stopSent;
    int refreshVersion;
    Button RecordBtn {get{return Get<Button>("RecordBtn");}}
    void Status(string s){Get<TextBlock>("Status").Text=s;}
    public Recorder():base("Recorder.xaml"){
        targets=Get<ComboBox>("Target"); inputs=Get<ComboBox>("Inputs");LoadArt(Get<System.Windows.Controls.Image>("ScreenArt"),"screen.png");
        LoadArt(Get<System.Windows.Controls.Image>("AreaArt"),"area.png");LoadArt(Get<System.Windows.Controls.Image>("WindowArt"),"window.png");
        VideoQuality.Populate(Get<ComboBox>("CaptureSize"),VideoQuality.Sizes,3);
        VideoQuality.Populate(Get<ComboBox>("CaptureLevel"),VideoQuality.Levels,1);
        Bind("MicButton",()=>{microphone=!microphone;Update();});
        Bind("FileBtn",()=>{fileMenu.PlacementTarget=Get<Button>("FileBtn");fileMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;fileMenu.IsOpen=true;});
        folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"Snapline");
        try{if(File.Exists(settings)&&Directory.Exists(File.ReadAllText(settings)))folder=File.ReadAllText(settings);}catch(IOException){}

        Bind("ScreenBtn",()=>SetMode("monitor"));Bind("WindowBtn",()=>SetMode("window"));Bind("AreaBtn",PickArea);
        AddMenu(Language.Pick("Обновить устройства", "Refresh devices"),()=>{RefreshTargets();RefreshMics();});
        folderMenu=AddMenu(Language.Pick("Папка для записей…", "Recording folder…"),()=>{using(var p=new Forms.FolderBrowserDialog()){p.SelectedPath=folder;if(p.ShowDialog()==Forms.DialogResult.OK){folder=p.SelectedPath;try{Directory.CreateDirectory(Path.GetDirectoryName(settings));File.WriteAllText(settings,folder);}catch(IOException){} }}});
        AddMenu(Language.Pick("Открыть видео…", "Open video…"),()=>{var p=new Microsoft.Win32.OpenFileDialog{Filter=Language.Pick("Видео|*.mp4;*.mov;*.m4v;*.wmv", "Video|*.mp4;*.mov;*.m4v;*.wmv")};if(p.ShowDialog(win)==true)Open(p.FileName);});
        fileMenu.Items.Add(systemCursor);
        var languages = new MenuItem { Header = Language.Pick("Язык", "Language") };
        foreach (string code in new[] { "ru", "en" }) {
            var item = new MenuItem { Header = code == "ru" ? "Русский" : "English",
                IsChecked = (code == "en") == Language.English };
            item.Click += (sender, args) => {
                Language.Save(code);
                foreach (MenuItem option in languages.Items) option.IsChecked = option == item;
                MessageBox.Show(win, code == "en" ? "English will be used the next time you open Snapline." :
                    "Русский язык включится при следующем запуске Snapline.", "Snapline");
            };
            languages.Items.Add(item);
        }
        fileMenu.Items.Add(languages);
        Bind("RecordBtn",()=>{if(busy)Stop();else Start();});
        targets.SelectionChanged+=(s,e)=>{region=null;if(mode=="area")mode="monitor";Update();};
        win.Closing+=(s,e)=>{if(busy){e.Cancel=true;closing=true;Stop();}};
        SetMode("monitor");Status("");win.Loaded+=(s,e)=>RefreshMics();
    }
    MenuItem AddMenu(string title,Action invoke){
        var item=new MenuItem{Header=title};item.Click+=(s,e)=>invoke();fileMenu.Items.Add(item);return item;
    }
    void SetMode(string value){mode=value;region=null;RefreshTargets();Update();}
    void RefreshTargets(){
        targets.Items.Clear();
        if(mode=="window"){
            foreach(var p in Process.GetProcesses()){using(p){try{if(p.MainWindowHandle!=IntPtr.Zero&&p.Id!=Process.GetCurrentProcess().Id&&!string.IsNullOrWhiteSpace(p.MainWindowTitle))targets.Items.Add(new TargetItem{Name=p.MainWindowTitle,Handle=p.MainWindowHandle});}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}}
        }else foreach(var s in Forms.Screen.AllScreens)targets.Items.Add(new TargetItem{Name=Language.Pick("Экран ", "Display ")+(targets.Items.Count+1)+" · "+s.Bounds.Width+" × "+s.Bounds.Height,Bounds=s.Bounds,Handle=MonitorFromPoint(new System.Drawing.Point(s.Bounds.Left+1,s.Bounds.Top+1),2)});
        if(targets.Items.Count>0)targets.SelectedIndex=0;
    }
    async void RefreshMics(){
        int version=++refreshVersion;
        try{using(var p=new Worker("ffmpeg","-hide_banner","-list_devices","true","-f","dshow","-i","dummy")){
            await p.Wait();if(version!=refreshVersion||busy)return;
            string old=inputs.SelectedItem as string;inputs.Items.Clear();
            foreach(Match m in Regex.Matches(p.Log,"\"([^\r\n]+)\" \\(audio\\)"))inputs.Items.Add(m.Groups[1].Value);
            if(inputs.Items.Contains(old??""))inputs.SelectedItem=old;else if(inputs.Items.Count>0)inputs.SelectedIndex=0;
            Update();
        }}catch(Exception e){Status(e.Message);}
    }
    void PickArea(){
        if(mode=="window")SetMode("monitor");
        var t=targets.SelectedItem as TargetItem;if(t==null)return;
        win.Hide();try{var box=Region.Pick(t.Bounds);if(box.HasValue){region=box;mode="area";}}finally{win.Show();win.Activate();Update();}
    }
    void Update(){
        foreach(var n in new[]{"ScreenBtn","AreaBtn","WindowBtn","FileBtn","MicButton","Target"})Get<Control>(n).IsEnabled=!busy;
        Get<CheckBox>("Clicks").IsEnabled=!busy&&mode!="window";
        inputs.IsEnabled=!busy&&microphone;
        Get<Button>("MicButton").Tag=microphone?"chosen":null;
        Get<Button>("MicButton").Opacity=microphone?1:.65;
        Get<Button>("MicButton").ToolTip=microphone?Language.Pick("Выключить микрофон", "Disable microphone"):Language.Pick("Включить микрофон", "Enable microphone");
        Get<TextBlock>("MicOff").Visibility=microphone?Visibility.Collapsed:Visibility.Visible;
        foreach(var n in new[]{"ScreenBtn","AreaBtn","WindowBtn"})Get<Button>(n).Tag="card";
        Get<Button>(mode=="window"?"WindowBtn":region.HasValue?"AreaBtn":"ScreenBtn").Tag="chosenCard";
        targets.ToolTip=region.HasValue?Language.Pick("Область ", "Area ")+region.Value.Width+" × "+region.Value.Height:Convert.ToString(targets.SelectedItem);
        folderMenu.ToolTip=folder;
        RecordBtn.IsEnabled=busy?!stopSent:targets.SelectedItem!=null;
    }
    void TogglePause(){
        if(stopSent||hud==null)return;
        if(resume!=null){resume.TrySetResult(true);return;}
        if(job==null||pausing)return;
        pausing=true;hud.State(true,false);cursor.Dispose();job.Stop();
    }
    void Stop(){
        if(stopSent)return;stopSent=true;RecordBtn.IsEnabled=false;
        if(countdown!=null)countdown.Cancel();
        if(cursor!=null)cursor.Dispose();
        if(resume!=null)resume.TrySetResult(false);
        if(hud!=null)hud.Hide();
        if(job!=null)job.Stop();
        Status(Language.Pick("Завершаем запись…", "Finishing recording…"));
    }
    bool UseNativeCapture(TargetItem t,string voice,bool nativeEncoder,System.Drawing.Size source){
        if(!nativeEncoder||region.HasValue||voice!=null||source.Width<=0||source.Height<=0)return false;
        var fitted=VideoQuality.Fit(source.Width,source.Height,Get<ComboBox>("CaptureSize").SelectedIndex,false);
        return Math.Abs(source.Width-fitted.Width)<=1&&Math.Abs(source.Height-fitted.Height)<=1;
    }
    Worker Capture(TargetItem t,string voice,string tmp,bool nativeEncoder,bool nativeCapture,System.Drawing.Size source){
            int size=Get<ComboBox>("CaptureSize").SelectedIndex,level=Get<ComboBox>("CaptureLevel").SelectedIndex;
            var target=VideoQuality.Fit(Math.Max(2,source.Width),Math.Max(2,source.Height),size,false);
            if(nativeCapture)return new Worker("capture",mode=="window"?"window":"monitor",t.Handle.ToInt64().ToString(),tmp,systemCursor.IsChecked?"system":"clean",VideoQuality.Bitrate(source.Width,source.Height,level,60).ToString());
            else{
                var a=new List<string>{"-hide_banner","-loglevel","warning","-thread_queue_size","16","-f","gdigrab","-framerate","30","-draw_mouse",systemCursor.IsChecked?"1":"0"};
                if(mode=="window")a.AddRange(new[]{"-i","hwnd="+t.Handle.ToInt64()});
                else{var b=region??t.Bounds;a.AddRange(new[]{"-offset_x",b.X.ToString(),"-offset_y",b.Y.ToString(),"-video_size",b.Width+"x"+b.Height,"-i","desktop"});}
                if(voice!=null)a.AddRange(new[]{"-thread_queue_size","128","-f","dshow","-i","audio="+voice,"-map","0:v:0","-map","1:a:0","-c:a","aac","-b:a","128k"});
                a.AddRange(new[]{"-vf",VideoQuality.CaptureScale(size)+",format="+(nativeEncoder?"nv12":"yuv420p")});
                a.AddRange(VideoQuality.Encoding(nativeEncoder,target.Width,target.Height,level));
                a.AddRange(new[]{"-movflags","+faststart","-n",tmp});
                return new Worker("ffmpeg",a.ToArray());
            }
    }
    async void Start(){
        var t=targets.SelectedItem as TargetItem;if(t==null)return;
        string voice=microphone?inputs.SelectedItem as string:null;
        if(microphone&&voice==null){Alert(Language.Pick("Выбери доступный микрофон или отключи запись микрофона.", "Choose an available microphone or turn off microphone recording."));return;}
        string result=null,parts=null;
        busy=true;stopSent=false;pausing=false;countdown=new CancellationTokenSource();Update();Get<TextBlock>("RecordLabel").Text=Language.Pick("Отмена", "Cancel");
        CountdownOverlay overlay=null;
        try{
            Directory.CreateDirectory(folder);
            var bounds=mode=="window"?Forms.Screen.FromHandle(t.Handle).Bounds:t.Bounds;
            overlay=new CountdownOverlay(bounds,Stop);
            win.Hide();overlay.SetNumber(3);overlay.Show();
            var source=mode=="window"?CursorSession.WindowSize(t.Handle):(region??t.Bounds).Size;
            var captureSize=VideoQuality.Fit(Math.Max(2,source.Width),Math.Max(2,source.Height),Get<ComboBox>("CaptureSize").SelectedIndex,false);
            var encoderProbe=VideoEncoder.Available(captureSize);
            for(int n=3;n>0;n--){overlay.SetNumber(n);await Task.Delay(1000,countdown.Token);}
            bool nativeEncoder=await encoderProbe;countdown.Token.ThrowIfCancellationRequested();
            overlay.Close();overlay=null;
            // Keep the launcher off the captured desktop until the last frame is flushed.
            countdown.Dispose();countdown=null;
            string stem=Path.Combine(folder,"Snapline-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
            parts=stem+".parts";Directory.CreateDirectory(parts);
            var clips=new List<string>();var motion=new List<MotionPoint>();int totalMs=0;
            bool nativeCapture=UseNativeCapture(t,voice,nativeEncoder,source);
            hud=new RecordingHud(bounds,TogglePause,Stop);
            while(true){
                string clip=Path.Combine(parts,clips.Count.ToString("D5")+".mp4");
                cursor=new CursorSession(region??t.Bounds,mode=="window"?t.Handle:IntPtr.Zero,!nativeCapture,
                    new System.Windows.Interop.WindowInteropHelper(win).Handle,hud.Handle);
                if(clips.Count==0)hud.Begin(!nativeCapture);else hud.State(false,true);
                job=Capture(t,voice,clip,nativeEncoder,nativeCapture,source);
                if(mode!="window" && Get<CheckBox>("Clicks").IsChecked==true)
                    indicator=new ClickIndicator(region??t.Bounds,p=>hud!=null && hud.Visible && hud.Bounds.Contains(p));
                Get<TextBlock>("RecordLabel").Text=Language.Pick("Остановить запись", "Stop recording");Status(Language.Pick("Идёт запись экрана", "Recording screen"));
                int code=await job.Wait();long first=job.FirstFrameMs;string log=job.Log;
                cursor.Dispose();if(indicator!=null){indicator.Dispose();indicator=null;}
                job.Dispose();job=null;
                if(code!=0)throw new IOException(Language.Pick("Не удалось завершить захват. ", "Could not finish capture. ")+log.Trim());
                var meta=await MovieInfo.Read(clip);
                int length=checked((int)Math.Round(meta.Seconds*1000));
                cursor.Append(motion,first,totalMs,length);totalMs=checked(totalMs+length);
                cursor=null;clips.Add(clip);
                if(!pausing||stopSent)break;
                resume=new TaskCompletionSource<bool>();hud.State(true,true);Status(Language.Pick("Запись на паузе", "Recording paused"));
                bool again=await resume.Task;resume=null;
                if(!again||stopSent)break;
                pausing=false;
            }
            hud.Dispose();hud=null;
            string path=stem+".mp4";
            if(clips.Count==1)File.Move(clips[0],path);
            else{
                string manifest=Path.Combine(parts,"clips.txt"),merged=Path.Combine(parts,"merged.mp4");
                File.WriteAllLines(manifest,clips.Select(c=>"file '"+Path.GetFileName(c)+"'"),new System.Text.UTF8Encoding(false));
                using(var mux=new Worker("ffmpeg","-hide_banner","-loglevel","error","-f","concat","-auto_convert","0","-safe","1","-i",manifest,"-map","0","-c","copy","-movflags","+faststart","-n",merged)){
                    if(await mux.Wait()!=0)throw new IOException(Language.Pick("Не удалось соединить фрагменты. ", "Could not join clips. ")+mux.Log);
                }
                await MovieInfo.Read(merged);File.Move(merged,path);
            }
            result=path;
            try{CameraTrack.Store(path,0,motion,!systemCursor.IsChecked);}catch(IOException e){Alert(Language.Pick("Видео сохранено. Не удалось сохранить движение курсора. ", "Video saved. Could not save cursor movement. ")+e.Message);}
            try{foreach(var clip in clips)File.Delete(clip);File.Delete(Path.Combine(parts,"clips.txt"));Directory.Delete(parts);}catch(IOException){}
            Status(Language.Pick("Сохранено: ", "Saved: ")+Path.GetFileName(path));
        }catch(OperationCanceledException){Status(Language.Pick("Запись отменена", "Recording cancelled"));}
        catch(Exception e){Status(Language.Pick("Ошибка записи", "Recording error"));Alert(e.Message+(parts!=null?Language.Pick("\nФайлы захвата: ", "\nCapture files: ")+parts:""));}
        finally{if(job!=null)job.Stop();if(hud!=null)hud.Dispose();hud=null;resume=null;if(overlay!=null)overlay.Close();win.WindowState=WindowState.Normal;win.Show();if(cursor!=null)cursor.Dispose();cursor=null;if(indicator!=null)indicator.Dispose();indicator=null;if(job!=null)job.Dispose();job=null;if(countdown!=null)countdown.Dispose();countdown=null;busy=false;stopSent=false;Get<TextBlock>("RecordLabel").Text=Language.Pick("Записать", "Record");Update();}
        if(closing)win.Close();else if(result!=null)Open(result);
    }
    void Open(string path){var editor=new Editor(path);editor.Window.Owner=win;editor.Window.ShowDialog();}
}
static class Program{
    [STAThread] static void Main(){var app=new Application();try{app.Run(new Recorder().Window);}catch(Exception e){MessageBox.Show(e.Message,"Snapline");}}
}
}
