using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Navodka {
sealed class MovieInfo {
    public const string Containers="mov,asf,matroska,avi";
    public const string MaxPixels="33554432";
    public double Seconds;
    public int Width,Height;public bool HasAudio;
    public static async Task<MovieInfo> Read(string path){
        if(!File.Exists(path))throw new FileNotFoundException(InterfaceLanguage.Pick("Видео отсутствует в папке записи.", "No video in the recording folder."),path);
        using(var probe=new Worker("ffmpeg","-hide_banner","-nostdin","-progress","pipe:2","-protocol_whitelist","file","-format_whitelist",Containers,"-max_pixels",MaxPixels,"-i",path,"-map","0:v:0","-frames:v","1","-vf","scale=2:2","-pix_fmt","bgra","-threads:v","1","-c:v","rawvideo","-f","rawvideo","-y","NUL")){
            int code=await probe.Wait(30000);
            string log=probe.Log;
            var duration=Regex.Match(log,@"(?m)^  Duration:\s*(\d+):(\d+):([\d.]+)");
            var size=Regex.Match(log,@"(?m)^  Stream #0:[^\r\n]*?Video:[^\r\n]*?\b(\d{2,5})x(\d{2,5})\b");
            if(code!=0||!size.Success||!duration.Success||!Regex.IsMatch(log,@"(?m)^frame=[1-9]"))
                throw new InvalidDataException(InterfaceLanguage.Pick("Не удалось прочитать видеокадр или длительность. Файл сохранён по адресу ", "Could not read the video frame or duration. File saved at ")+path);
            double seconds=int.Parse(duration.Groups[1].Value)*3600+int.Parse(duration.Groups[2].Value)*60+double.Parse(duration.Groups[3].Value,CultureInfo.InvariantCulture);
            if(seconds<=0)throw new InvalidDataException(InterfaceLanguage.Pick("Длительность видео равна нулю. ", "Video duration is zero. ")+path);
            int w=int.Parse(size.Groups[1].Value),h=int.Parse(size.Groups[2].Value);
            if((long)w*h>33554432 || seconds>int.MaxValue/1000.0)
                throw new InvalidDataException(InterfaceLanguage.Pick("Размер или длительность записи превышают поддерживаемый предел.", "Recording size or duration exceeds the supported limit."));
            return new MovieInfo{Width=w,Height=h,Seconds=seconds,HasAudio=Regex.IsMatch(log,@"(?m)^  Stream #0:[^\r\n]*?Audio:")};
        }
    }
}
// The decoder owns one bounded frame buffer. A new seek invalidates outstanding UI callbacks.
sealed class MoviePreview : IDisposable {
    readonly string path;
    readonly Dispatcher ui;
    readonly MovieInfo info;
    readonly Action<BitmapSource,double> frame;
    readonly Action<string> ended;
    Process active;
    int generation;
    public MoviePreview(string path,MovieInfo info,Dispatcher ui,Action<BitmapSource,double> frame,Action<string> ended){
        this.path=path;this.info=info;this.ui=ui;this.frame=frame;this.ended=ended;
    }
    public void Dispose(){
        generation++;
        if(active!=null){try{active.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}active=null;}
    }
    public async void Show(double seconds,bool play){
        Dispose();int token=generation;
        double factor=Math.Min(1,Math.Min((play?1920.0:3840.0)/info.Width,(play?1080.0:2160.0)/info.Height));
        int w=Math.Max(1,(int)(info.Width*factor)),h=Math.Max(1,(int)(info.Height*factor));
        double offset=Math.Max(0,Math.Min(seconds,info.Seconds-1.0/24));
        var args=new[]{"-hide_banner","-loglevel","error","-nostdin","-ss",offset.ToString("0.######",CultureInfo.InvariantCulture),"-protocol_whitelist","file","-format_whitelist",MovieInfo.Containers,"-max_pixels",MovieInfo.MaxPixels,"-i",path,"-map","0:v:0","-an","-vf","fps=24:round=up,scale="+w+":"+h,"-pix_fmt","bgra","-c:v","rawvideo","-threads:v","1","-frames:v",play?"2147483647":"1","-f","rawvideo","pipe:1"};
        var proc=new Process();
        try{
            proc.StartInfo=VerifiedTools.StartInfo("ffmpeg",args);
            proc.StartInfo.RedirectStandardOutput=true;
            proc.Start();active=proc;
            int count=await Task.Run(()=>{
                WriteableBitmap bitmap=null;var bytes=new byte[w*h*4];var output=proc.StandardOutput.BaseStream;
                int index=0;var clock=new Stopwatch();
                while(token==System.Threading.Volatile.Read(ref generation)){
                    int n=0;while(n<bytes.Length){int got=output.Read(bytes,n,bytes.Length-n);if(got==0)break;n+=got;}
                    if(n!=bytes.Length)break;
                    if(index==0)clock.Start();
                    int delay=(int)(index*1000.0/24-clock.ElapsedMilliseconds);if(delay>0)System.Threading.Thread.Sleep(delay);
                    double t=Math.Min(info.Seconds,offset+index/24.0);
                    ui.Invoke(new Action(()=>{if(token==generation){
                        if(bitmap==null)bitmap=new WriteableBitmap(w,h,96,96,PixelFormats.Bgra32,null);
                        bitmap.WritePixels(new Int32Rect(0,0,w,h),bytes,w*4,0);
                        frame(bitmap,t);
                    }}));
                    index++;
                }
                proc.WaitForExit();return index;
            });
            if(token==generation && (play||count==0))ended(count==0||proc.ExitCode!=0?InterfaceLanguage.Pick("Не удалось декодировать кадр.", "Could not decode frame."):null);
        }catch(Exception ex){if(token==generation)ended(ex.Message);}
        finally{if(active==proc)active=null;proc.Dispose();}
    }
}
}
