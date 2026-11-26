using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace Navodka {
static class ClickAudio {
    const int Rate=16000;
    static readonly short[] pulse=MakePulse();
    static short[] MakePulse(){
        var samples=new short[Rate/32];
        for(int i=0;i<samples.Length;i++){
            double t=(double)i/Rate;
            samples[i]=(short)(8000*Math.Exp(-t*180)*(Math.Sin(t*11000)+.35*Math.Sin(t*26000)));
        }return samples;
    }
    public static void Write(Stream stream,double start,double end,IEnumerable<double> clicks){
        long count=(long)Math.Ceiling((end-start)*Rate);
        if(count<=0 || count>(int.MaxValue-44)/2)throw new IOException(InterfaceLanguage.Pick("Фрагмент слишком длинный для дорожки щелчков.", "Clip is too long for the click sound track."));
        var w=new BinaryWriter(stream,Encoding.ASCII,true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write((int)(36+count*2));w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(Rate);w.Write(Rate*2);w.Write((short)2);w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));w.Write((int)(count*2));stream.SetLength(44+2*count);
        foreach(var t in clicks){
            long first=(long)Math.Round((t-start)*Rate);
            if(first>=count)break;if(first+pulse.Length<=0)continue;
            stream.Position=44+2*Math.Max(0,first);
            for(int i=(int)Math.Max(0,-first);i<pulse.Length && first+i<count;i++)w.Write(pulse[i]);
        }
        w.Flush();
    }
}
}
