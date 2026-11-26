using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Navodka {
class Shell {
    protected Window win;
    protected T Get<T>(string name) where T:class { return (T)win.FindName(name); }
    protected void Bind(string name, Action act){ Get<Button>(name).Click+=(s,e)=>act(); }
    protected static void LoadArt(Image target,string name){
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){
            var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();
            image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource=stream;image.EndInit();image.Freeze();target.Source=image;
        }
    }
    protected Shell(string view){
        var assembly=Assembly.GetExecutingAssembly();
        if(!Application.Current.Resources.Contains("ExportFill")){
            using(var stream=assembly.GetManifestResourceStream("Theme.xaml"))
                Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        using(var stream=assembly.GetManifestResourceStream(view))win=(Window)Language.LoadView(stream);
        LoadArt(Get<Image>("Brand"),"brand.png");
        var chrome=new System.Windows.Shell.WindowChrome{CaptionHeight=0,GlassFrameThickness=new Thickness(0),
            CornerRadius=new CornerRadius(0),ResizeBorderThickness=new Thickness(view=="Editor.xaml"?5:0)};
        System.Windows.Shell.WindowChrome.SetWindowChrome(win,chrome);
        Action maximize=()=>win.WindowState=win.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        Bind("CloseWindow",win.Close);Bind("Minimize",()=>win.WindowState=WindowState.Minimized);
        if(Get<Button>("Maximize")!=null)Bind("Maximize",maximize);
        Get<Grid>("TitleBar").MouseLeftButtonDown+=(s,e)=>{
            if(e.ChangedButton!=System.Windows.Input.MouseButton.Left)return;
            if(e.ClickCount==2 && win.ResizeMode==ResizeMode.CanResize)maximize();
            else if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)win.DragMove();
        };
        var work=SystemParameters.WorkArea;
        if(view=="Recorder.xaml" && work.Width<win.Width){win.Height*=work.Width/win.Width;win.Width=work.Width;}
        else if(view=="Editor.xaml"){win.Width=Math.Min(win.Width,work.Width);win.Height=Math.Min(win.Height,work.Height);}
    }
    public Window Window {get{return win;}}
    protected static string N(double x){return x.ToString("0.######",CultureInfo.InvariantCulture);}
    protected static void Alert(string s){MessageBox.Show(s,"Snapline",MessageBoxButton.OK,MessageBoxImage.Information);}
}
class Worker : IDisposable {
    public readonly Process Child=new Process();
    readonly StringBuilder log=new StringBuilder();
    public string Log {get{lock(log)return log.ToString();}}
    internal static string Quote(string arg){
        return "\""+Regex.Replace(Regex.Replace(arg,@"(\\*)""",m=>new string('\\',m.Groups[1].Length*2+1)+"\""),@"\\+$",m=>m.Value+m.Value)+"\"";
    }
    public long FirstFrameMs {get;private set;}
    public Worker(string tool,params string[] args):this(tool,args,null){}
    public Worker(string tool,string[] args,string workingDirectory){
        Child.StartInfo=VerifiedTools.StartInfo(tool,args,workingDirectory);
        Child.StartInfo.RedirectStandardError=true;Child.StartInfo.StandardErrorEncoding=Encoding.UTF8;
        Child.StartInfo.RedirectStandardInput=true;
        Child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(log){int marker=e.Data.IndexOf("NAVODKA_FIRST_MS=",StringComparison.Ordinal);
            long first;
            if(marker>=0 && long.TryParse(e.Data.Substring(marker+"NAVODKA_FIRST_MS=".Length),out first))FirstFrameMs=first;
            log.AppendLine(e.Data);if(log.Length>48000)log.Remove(0,8000);}};
        Child.Start(); Child.BeginErrorReadLine();
    }
    public Task<int> Wait(int timeoutMs=-1){return Task.Run(()=>{
        if(!Child.WaitForExit(timeoutMs)){
            try{Child.Kill();}catch(InvalidOperationException){}
            Child.WaitForExit();throw new IOException(Language.Pick("Файл не удалось прочитать за отведённое время.", "Reading the file timed out."));
        }
        Child.WaitForExit();return Child.ExitCode;
    });}
    public void Stop(){try{Child.StandardInput.WriteLine("q");Child.StandardInput.Flush();}catch(InvalidOperationException){}catch(IOException){}}
    public void Dispose(){Child.Dispose();}
}
}
