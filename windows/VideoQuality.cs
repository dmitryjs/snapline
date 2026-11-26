using System;
using System.Drawing;
using System.Windows.Controls;
namespace Navodka {
static class VideoQuality {
    public static readonly string[] Sizes={"720p","1080p","1440p","2160p · 4K"};
    public static readonly string[] Levels={Language.Pick("Стандартное", "Standard"),Language.Pick("Высокое", "High"),Language.Pick("Максимальное", "Maximum")};
    static readonly int[] LongSide={1280,1920,2560,3840},ShortSide={720,1080,1440,2160};
    public static void Populate(ComboBox combo,string[] options,int index){
        foreach(var option in options)combo.Items.Add(option);combo.SelectedIndex=index;
    }
    public static Size Fit(int w,int h,int index,bool upscale){
        double factor=Math.Min((double)LongSide[index]/Math.Max(w,h),(double)ShortSide[index]/Math.Min(w,h));
        if(!upscale)factor=Math.Min(1,factor);
        return new Size(Math.Max(2,(int)Math.Round(w*factor/2)*2),Math.Max(2,(int)Math.Round(h*factor/2)*2));
    }
    public static int Bitrate(int w,int h,int level,int fps){
        double rate=(double)w*h*Math.Sqrt(fps/30.0)*new[]{4.8,8.5,13.0}[level];
        return 1000000*(int)Math.Max(2,Math.Min(160,Math.Ceiling(rate/1000000)));
    }
    public static string[] Encoding(bool native,int w,int h,int quality){
        return native?new[]{"-c:v","h264_mf","-b:v",Bitrate(w,h,quality,30).ToString()}:
            new[]{"-c:v","mpeg4","-q:v",new[]{"5","3","2"}[quality]};
    }
    public static string CaptureScale(int index){
        string a=LongSide[index].ToString(),b=ShortSide[index].ToString();
        return "scale='min(iw,if(gte(iw,ih),"+a+","+b+"))':'min(ih,if(gte(iw,ih),"+b+","+a+"))':force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos";
    }
}
}
