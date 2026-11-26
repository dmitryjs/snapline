using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
namespace Navodka {
static class VideoEncoder {
    static readonly System.Collections.Generic.Dictionary<Size,Task<bool>> probes=new System.Collections.Generic.Dictionary<Size,Task<bool>>();
    public static Task<bool> Available(Size size){
        Task<bool> probe;if(probes.TryGetValue(size,out probe))return probe;
        if(probes.Count>=16)probes.Clear();
        return probes[size]=Check(size);
    }
    static async Task<bool> Check(Size size){
        string stem=Path.Combine(Path.GetTempPath(),"Snapline-"+Guid.NewGuid().ToString("N"));
        string image=stem+".bmp",video=stem+".mp4";
        try{
            using(var frame=new Bitmap(320,240,PixelFormat.Format24bppRgb))frame.Save(image,ImageFormat.Bmp);
            using(var worker=new Worker("ffmpeg","-hide_banner","-loglevel","error","-f","bmp_pipe","-i",image,"-frames:v","1","-vf","scale="+size.Width+":"+size.Height+",format=nv12","-c:v","h264_mf","-b:v","2M","-n",video)){
                if(await worker.Wait(12000)!=0)return false;
            }
            await MovieInfo.Read(video);return true;
        }catch(IOException){return false;}
        finally{foreach(var file in new[]{image,video})try{File.Delete(file);}catch(IOException){}}
    }
}
}
