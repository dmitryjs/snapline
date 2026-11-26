using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Navodka {
sealed class CursorSession : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type,MouseHook callback,IntPtr module,uint thread);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect rect,int bytes);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flag);
    readonly Rectangle bounds;
    readonly IntPtr target,recorder,hud;
    readonly bool client;
    readonly MouseHook callback;
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly long epoch=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    readonly List<MotionPoint> points=new List<MotionPoint>();
    readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(22)};
    IntPtr hook;bool pressed,stopped,activePress;
    public CursorSession(Rectangle bounds,IntPtr target,bool client,IntPtr recorder,IntPtr hud){
        this.hud=hud;this.bounds=bounds;this.target=target;this.client=client;this.recorder=recorder;
        callback=OnMouse;
        hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
        pressed=Down();timer.Tick+=(s,e)=>Tick();Tick();timer.Start();
    }
    public static Size WindowSize(IntPtr window){
        Rect r;if(DwmGetWindowAttribute(window,9,out r,16)!=0&&!GetWindowRect(window,out r))return Size.Empty;
        return new Size(r.Right-r.Left,r.Bottom-r.Top);
    }
    static bool Down(){return GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0;}
    IntPtr OnMouse(int code,IntPtr msg,IntPtr data){
        int m=msg.ToInt32();
        if(code>=0 && !stopped && (m==0x201 || m==0x204 || m==0x202 || m==0x205)){
            bool down=m==0x201||m==0x204;
            Add(new Point(Marshal.ReadInt32(data),Marshal.ReadInt32(data,4)),down,!down);
        }
        return CallNextHookEx(hook,code,msg,data);
    }
    void Tick(){
        Point p;bool down=Down();
        if(!stopped&&GetCursorPos(out p))Add(p,hook==IntPtr.Zero && down && !pressed,hook==IntPtr.Zero && pressed && !down);
        pressed=down;
    }
    void Add(Point p,bool clicked,bool release){
        if(release && !activePress)return;
        var area=bounds;
        if(target!=IntPtr.Zero){
            Rect r;
            if(client){
                Point origin=new Point();
                if(!GetClientRect(target,out r)||!ClientToScreen(target,ref origin))return;
                area=new Rectangle(origin.X,origin.Y,r.Right-r.Left,r.Bottom-r.Top);
            }else{
                if(DwmGetWindowAttribute(target,9,out r,16)!=0 && !GetWindowRect(target,out r))return;
                area=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
            }
        }
        if(!area.Contains(p)){if(release)EndPress();return;}
        var root=GetAncestor(WindowFromPoint(p),2);
        if(root==recorder || root==hud || (target!=IntPtr.Zero && root!=target)){if(release)EndPress();return;}
        ushort x=(ushort)(((long)p.X-area.Left)*65535/Math.Max(1,area.Width-1));
        ushort y=(ushort)(((long)p.Y-area.Top)*65535/Math.Max(1,area.Height-1));
        if(points.Count>0&&!clicked&&!release){var last=points[points.Count-1];if(last.X==x&&last.Y==y)return;}
        if(clicked)activePress=true;if(release)activePress=false;
        points.Add(new MotionPoint{Ms=(int)clock.ElapsedMilliseconds,X=x,Y=y,Click=clicked,Release=release});
    }
    void EndPress(){
        if(!activePress||points.Count==0)return;
        var p=points[points.Count-1];p.Click=false;p.Release=true;p.Ms=(int)clock.ElapsedMilliseconds;points.Add(p);activePress=false;
    }
    public void Append(List<MotionPoint> output,long firstMs,int baseMs,int length){
        Dispose();
        int offset=firstMs>=epoch && firstMs-epoch<=clock.ElapsedMilliseconds?(int)(firstMs-epoch):0;
        if(points.Count>0){
            int last=points.FindLastIndex(p=>p.Ms<offset);
            if(last>=0){var p=points[last];p.Ms=baseMs;p.Click=false;p.Release=false;output.Add(p);}
        }
        foreach(var raw in points){int local=raw.Ms-offset;if(local<0||local>=length)continue;var p=raw;p.Ms=checked(baseMs+local);output.Add(p);}
    }
    public void Dispose(){
        if(stopped)return;EndPress();stopped=true;timer.Stop();clock.Stop();
        if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
    }
}
}
