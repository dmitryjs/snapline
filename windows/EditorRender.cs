using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
namespace Navodka {
partial class Editor {
    static double Unit(double v){return Math.Max(0,Math.Min(1,v));}
    CameraPose Pose(double t,bool follow){
        var p=follow?position.Sample(camera,t,edgeFollow):new CameraPose(1,.5,.5);
        if(p.Scale>1){
            // Project the actual action into the final canvas before constraining the viewport.
            var center=framing.ToCanvas(p.X,p.Y,info.Width,info.Height);
            p.X=(Unit(center.X/width)*autoScale-.5)/(autoScale-1);p.Y=(Unit(center.Y/height)*autoScale-.5)/(autoScale-1);
            var z=position.SpanAt(t);
            if(z!=null&&!double.IsNaN(z.TargetX)){p.X=(z.TargetX*autoScale-.5)/(autoScale-1);p.Y=(z.TargetY*autoScale-.5)/(autoScale-1);}
            p.Scale=1+(p.Scale-1)*(autoScale-1)/.5;
        }
        var pose=CameraTrack.Compose(p,t,zoom,duration,manualX,manualY,1,1);
        if(LockedFrame){
            // A pivot inside the footage keeps the entire stationary aperture covered at every zoom level.
            pose.X=Math.Max(framing.X/(double)width,Math.Min(framing.Video.Right/width,pose.X));
            pose.Y=Math.Max(framing.Y/(double)height,Math.Min(framing.Video.Bottom/height,pose.Y));
        }
        return pose;
    }
    void WritePointer(string file,double begin,double finish){
        using(var output=new BinaryWriter(File.Create(file))){
            int frames=checked((int)Math.Ceiling((finish-begin)*30));
            output.Write(accents[accent].R);output.Write(accents[accent].G);output.Write(accents[accent].B);output.Write((ushort)(markerSize*100));
            for(int i=0;i<frames;i++){
                var t=begin+i/30.0;double x,y,age;
                bool has=camera.DisplayPointer(t,smooth,hideIdle,out x,out y,out age);var p=framing.ToCanvas(x,y,info.Width,info.Height);
                short degrees=(short)Math.Round(PointerAngle(t,x,y)*64);
                bool clicked=camera.PointerAt(t,true,out x,out y,out age);var c=framing.ToCanvas(x,y,info.Width,info.Height);
                output.Write((byte)((has&&framing.Video.Contains(p)?1:0)));
                output.Write(Pixel(p.X));output.Write(Pixel(p.Y));output.Write(Pixel(c.X));output.Write(Pixel(c.Y));
                output.Write(rings&&clicked&&framing.Video.Contains(c)?(ushort)Math.Min(65534,age/.4*65535):ushort.MaxValue);output.Write(degrees);
            }
        }
    }
    static ushort Pixel(double v){return (ushort)Math.Max(0,Math.Min(65535,Math.Round(v)));}
    static int EvenDown(double v){return (int)(v/2)*2;}
    static string Box(int x,int y,int w,int h,string when){
        return w>0 && h>0?",drawbox=x="+x+":y="+y+":w="+w+":h="+h+":color=black@0.63:t=fill"+when:"";
    }
    List<string> RenderArgs(string tmp,string dir,bool motion,bool native,int quality){
        var f=framing;
        var args=new List<string>{"-hide_banner","-loglevel","error","-filter_complex_threads","2","-ss",N(start),"-protocol_whitelist","file","-format_whitelist",MovieInfo.Containers,"-max_pixels",MovieInfo.MaxPixels,"-i",path};
        if(motion)f.SaveCameraPaper(Path.Combine(dir,"camera-paper.yuv"),surface.Background);
        bool matte=LockedFrame || background>=4 || f.Radius>0 || Get<System.Windows.Controls.Slider>("Shadow").Value>0;
        if(matte){
            string file=Path.Combine(dir,"background.bgra");f.SaveMatte(file,surface.Background);
            args.AddRange(new[]{"-f","rawvideo","-pixel_format","bgra","-video_size",width+"x"+height,"-framerate","30","-i",file});
        }
        string graph="[0:v:0]setpts=PTS-STARTPTS,fps=30,crop="+f.CropW+":"+f.CropH+":"+f.CropX+":"+f.CropY+
            ",scale="+f.VideoW+":"+f.VideoH+":flags=lanczos,setsar=1,pad="+width+":"+height+":"+f.X+":"+f.Y+":color=0x"+colors[Math.Min(3,background)];
        if(matte && !LockedFrame)graph+="[paper];[paper][1:v]overlay=x=0:y=0:repeatlast=1";
        if(cursorStyle!=0||rings){art.Save(Path.Combine(dir,"pointer-atlas.bgra"));graph+=",snapcursor";}
        var active=annotations.Where(a=>a.Start<end && a.End>start).ToList();
        // Blur and lens sample the clean scene so overlapping annotations cannot feed back into a lens.
        var rectangles=active.Select(a=>a.Area(f,info.Width,info.Height)).ToArray();
        var samples=Enumerable.Range(0,active.Count).Where(i=>active[i].Kind!=0&&!rectangles[i].IsEmpty&&rectangles[i].Width>=4&&rectangles[i].Height>=4).ToList();
        if(samples.Count>0)graph+=",split="+(samples.Count+1)+"[base]"+string.Concat(samples.Select(i=>"[src"+i+"]"))+";[base]null";
        int nextInput=matte?2:1;
        for(int i=0;i<active.Count;i++){
            var a=active[i];Rect r=rectangles[i];if(r.IsEmpty||r.Width<4||r.Height<4)continue;
            int x=EvenDown(r.Left),y=EvenDown(r.Top),w=EvenDown(r.Width),h=EvenDown(r.Height);
            string when=":enable='gte(t,"+N(Math.Max(0,a.Start-start))+")*lt(t,"+N(a.End-start)+")'";
            if(a.Kind==0){
                graph+=Box(0,0,width,y,when)+Box(0,y+h,width,height-y-h,when)+Box(0,y,x,h,when)+Box(x+w,y,width-x-w,h,when);
            }else{
                graph+="[under"+i+"];[src"+i+"]";
                if(a.Kind==1){
                    int radius=Math.Max(1,Math.Min(12,Math.Min(w,h)/8));
                    graph+="crop="+w+":"+h+":"+x+":"+y+",format=yuv444p,gblur=sigma="+radius+":steps=2";
                }else{
                    int cw=Math.Max(2,EvenDown(w/2.0)),ch=Math.Max(2,EvenDown(h/2.0));
                    graph+="crop="+cw+":"+ch+":"+EvenDown(x+(w-cw)/2.0)+":"+EvenDown(y+(h-ch)/2.0)+",scale="+w+":"+h;
                    if(a.Kind==2)graph+=",drawbox=color=white:t=2";
                    else{
                        string alpha=Path.Combine(dir,"lens-"+i+".gray");
                        var pixels=new byte[checked(w*h)];var border=new byte[checked(w*h*4)];
                        for(int row=0;row<h;row++)for(int col=0;col<w;col++){
                            double dx=col+.5-w/2.0,dy=row+.5-h/2.0;
                            double distance=w/2.0-Math.Sqrt(dx*dx+dy*dy);
                            int p=row*w+col;pixels[p]=(byte)(255*Unit(distance+.5));border[4*p]=border[4*p+1]=border[4*p+2]=255;
                            border[4*p+3]=(byte)(255*Unit(2.5-distance));
                        }
                        File.WriteAllBytes(alpha,pixels);
                        args.AddRange(new[]{"-f","rawvideo","-pixel_format","gray","-video_size",w+"x"+h,"-framerate","30","-i",alpha});
                        int alphaIndex=nextInput++;
                        string rim=Path.Combine(dir,"rim-"+i+".bgra");File.WriteAllBytes(rim,border);
                        args.AddRange(new[]{"-f","rawvideo","-pixel_format","bgra","-video_size",w+"x"+h,"-framerate","30","-i",rim});
                        graph+="[lens"+i+"];[lens"+i+"]["+(nextInput++)+":v]overlay[rim"+i+"];[rim"+i+"]["+alphaIndex+":v]alphamerge";
                    }
                }
                graph+="[patch"+i+"];[under"+i+"][patch"+i+"]overlay="+x+":"+y+when;
            }
        }
        if(motion)graph+=",format=yuv420p,zoompan=track=1:d=1:s="+width+"x"+height+":fps=30";
        if(LockedFrame)graph+="[moving];[moving][1:v]overlay=repeatlast=1";
        graph+=(native?",format=nv12":",format=yuv420p")+"[out]";
        string audio="0:a:0?";
        if(clickSound){
            string wave=Path.Combine(dir,"clicks.wav");using(var stream=File.Create(wave))ClickAudio.Write(stream,start,end,camera.ClickTimes());
            args.AddRange(new[]{"-f","wav","-i",wave});audio=nextInput+":a";
            if(info.HasAudio){graph+=";[0:a:0]asetpts=PTS-STARTPTS[original];[original]["+audio+"]amix=inputs=2:duration=longest:normalize=0[audio]";audio="[audio]";}
        }
        File.WriteAllText(Path.Combine(dir,"scene.txt"),graph,new UTF8Encoding(false));
        args.AddRange(new[]{"-filter_complex_script","scene.txt","-t",N(end-start),"-map","[out]","-map",audio});args.AddRange(VideoQuality.Encoding(native,width,height,quality));args.AddRange(new[]{"-c:a","aac","-b:a","128k","-movflags","+faststart","-n",tmp});return args;
    }
}
}
