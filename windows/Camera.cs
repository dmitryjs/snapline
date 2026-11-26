using System;
using System.Collections.Generic;
using System.IO;

namespace Navodka {
struct MotionPoint {
    public int Ms;
    public ushort X,Y;
    public bool Click,Release;
}
struct CameraPose {
    public double Scale,X,Y;
    public CameraPose(double scale,double x,double y){Scale=scale;X=x;Y=y;}
}
class CameraTrack {
    const int Magic=0x314B544E,CleanMagic=0x324B544E;
    public bool CleanCursor {get;private set;}
    const double MaxScale=1.50, Lead=.48, Settle=.32, Exit=.76, LegacyHold=.55;
    struct Focus {public double T,RawX,RawY;}
    struct Interval {public double Start,End;public int First,Last;}
    sealed class ActionPoint {public double Down,Up,X,Y,EndX,EndY;}
    readonly List<ActionPoint> actions=new List<ActionPoint>();
    const int EventsMagic=0x334B544E,CleanEventsMagic=0x344B544E;
    readonly List<Focus> focus=new List<Focus>();
    readonly List<Focus> clicks=new List<Focus>();
    public bool HasPointer {get{return focus.Count>0;}}
    readonly List<Interval> sections=new List<Interval>();
    public int Sections {get{return sections.Count;}}
    public IEnumerable<Tuple<double,double>> Intervals(){
        foreach(var interval in sections)yield return Tuple.Create(interval.Start,interval.End);
    }
    public static string Sidecar(string movie){return Path.ChangeExtension(movie,".navcursor");}
    public static void Store(string movie,int offset,List<MotionPoint> points,bool clean=false){
        string path=Sidecar(movie),tmp=path+"."+Guid.NewGuid().ToString("N")+".partial";
        try{
            using(var w=new BinaryWriter(new FileStream(tmp,FileMode.CreateNew,FileAccess.Write))){
                w.Write(clean?CleanEventsMagic:EventsMagic);w.Write(offset);
                foreach(var e in points){w.Write(e.Ms);w.Write(e.X);w.Write(e.Y);w.Write((byte)(e.Click?1:e.Release?2:0));}
            }
            File.Move(tmp,path);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public static CameraTrack Load(string movie){
        var track=new CameraTrack();string path=Sidecar(movie);
        if(!File.Exists(path))return track;
        using(var r=new BinaryReader(File.OpenRead(path))){
            long len=r.BaseStream.Length;
            if(len<8 || (len-8)%9!=0 || len>32*1024*1024 )throw new InvalidDataException();
            int magic=r.ReadInt32();bool events=magic==EventsMagic||magic==CleanEventsMagic;
            if(!events && magic!=Magic && magic!=CleanMagic)throw new InvalidDataException();
            track.CleanCursor=magic==CleanMagic||magic==CleanEventsMagic;
            ActionPoint held=null;
            int offset=r.ReadInt32(),last=-1;

            while(r.BaseStream.Position<len){
                int ms=r.ReadInt32();if(ms<last || ms<0)throw new InvalidDataException();last=ms;
                double t=(ms-(double)offset)/1000, x=r.ReadUInt16()/65535.0,y=r.ReadUInt16()/65535.0;
                byte kind=r.ReadByte();if(kind>(events?2:1))throw new InvalidDataException();
                var point=new Focus{T=t,RawX=x,RawY=y};track.focus.Add(point);
                if(kind==1 && t>=0){
                    track.clicks.Add(point);
                    held=new ActionPoint{Down=t,Up=t+LegacyHold,X=x,Y=y,EndX=x,EndY=y};
                    track.actions.Add(held);
                }else if(kind==2 && held!=null){
                    held.Up=Math.Max(held.Down,t);held.EndX=x;held.EndY=y;held=null;
                }
            }
        }
        track.BuildSections();return track;
    }
    static int LastAt(List<Focus> samples,double t){
        int lo=0,hi=samples.Count;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(samples[mid].T<=t)lo=mid+1;else hi=mid;}
        return lo-1;
    }
    public IEnumerable<double> ClickTimes(){foreach(var c in clicks)yield return c.T;}
    public bool DisplayPointer(double t,double smooth,bool hide,out double x,out double y,out double age){
        bool found=PointerAt(t,false,out x,out y,out age);if(!found || (hide && age>1.4))return false;
        if(smooth<=0)return true;
        double blend=0,px=0,py=0;
        for(int i=0;i<5;i++){
            double sx,sy,elapsed;double weight=5-i;
            if(!PointerAt(t-i*.022*smooth,false,out sx,out sy,out elapsed))continue;
            px+=weight*sx;py+=weight*sy;blend+=weight;
        }
        if(blend>0){x=px/blend;y=py/blend;}return true;
    }
    public bool PointerAt(double t,bool click,out double x,out double y,out double age){
        var samples=click?clicks:focus;int index=LastAt(samples,t);
        x=y=age=0;if(index<0)return false;
        var p=samples[index];x=p.RawX;y=p.RawY;age=t-p.T;
        return !click||age<.40;
    }
    void BuildSections(){
        for(int i=0;i<actions.Count;i++){
            var a=actions[i];double begin=Math.Max(0,a.Down-Lead),finish=a.Up+Settle+Exit;
            int last=sections.Count-1;
            if(last>=0 && begin<sections[last].End){
                var region=sections[last];region.End=Math.Max(region.End,finish);region.Last=i;sections[last]=region;
            }else sections.Add(new Interval{Start=begin,End=finish,First=i,Last=i});
        }
    }
    static double Unit(double x){return Math.Max(0,Math.Min(1,x));}
    static double Ease(double x){x=Unit(x);return x*x*x*(x*(x*6-15)+10);}
    static void Target(ActionPoint a,bool follow,double t,out double x,out double y){
        // For a selection, track its midpoint with one continuous move over the drag.
        double progress=follow && a.Up>a.Down?Ease((t-a.Down)/(a.Up-a.Down))*.5:0;
        x=a.X+(a.EndX-a.X)*progress;y=a.Y+(a.EndY-a.Y)*progress;
    }
    public CameraPose At(double t){return Sample(t,true);}
    public CameraPose Sample(double t,bool follow){
        int lo=0,hi=sections.Count;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(sections[mid].Start<=t)lo=mid+1;else hi=mid;}
        if(lo==0 || t>=sections[lo-1].End)return new CameraPose(1,.5,.5);
        var region=sections[lo-1];var first=actions[region.First];
        double attack=Math.Max(.001,first.Down-region.Start);
        double gain=Ease((t-region.Start)/attack)*Ease((region.End-t)/Exit);
        lo=region.First;hi=region.Last+1;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(actions[mid].Down<=t)lo=mid+1;else hi=mid;}
        int idx=Math.Max(region.First,lo-1);var a=actions[idx];
        double x,y;Target(a,follow,t,out x,out y);
        if(idx<region.Last){
            var next=actions[idx+1];double panStart=Math.Max(a.Down,next.Down-.56);
            double mix=Ease((t-panStart)/Math.Max(.001,next.Down-panStart));
            x+=(next.X-x)*mix;y+=(next.Y-y)*mix;
        }
        // Source coordinates stay unclipped until the editor has applied padding and cropping.
        return new CameraPose(1+(MaxScale-1)*gain,x,y);
    }
    public static CameraPose Compose(CameraPose auto,double t,double manual,double duration,double mx,double my,double fitX,double fitY){
        double length=Math.Min(2,duration-manual), ramp=Math.Min(.35,length/3);
        double gain=manual<0 || ramp<=0?0:Ease(Math.Min((t-manual)/ramp,(manual+length-t)/ramp));
        double a=auto.Scale-1, m=.5*gain, weight=a+m;
        if(weight<=0)return new CameraPose(1,.5,.5);
        return new CameraPose(1+Math.Max(a,m),(.5+(auto.X-.5)*fitX)*a/weight+mx*m/weight,
            (.5+(auto.Y-.5)*fitY)*a/weight+my*m/weight);
    }
    public static void WriteFrames(string path,double start,double end,Func<double,CameraPose> sample){
        using(var w=new BinaryWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write))){
            long frames=(long)Math.Ceiling((end-start)*30);
            for(long i=0;i<frames;i++){
                var p=sample(start+i/30.0);
                w.Write((ushort)Math.Round(Unit(p.Scale-1)*65535));
                w.Write((ushort)Math.Round(Unit((p.X+2)/5)*65535));w.Write((ushort)Math.Round(Unit((p.Y+2)/5)*65535));
            }
        }
    }
}
}
