using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Navodka {
sealed class RecordingHud : Form {
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd,uint affinity);
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd,out uint affinity);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref VersionInfo version);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct VersionInfo {
        public int size,major,minor,build,platform;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string servicePack;
    }
    readonly Label time=new Label();
    readonly Button pause=new Button(),stop=new Button();
    readonly Stopwatch elapsed=new Stopwatch();
    readonly Timer timer=new Timer{Interval=200};
    readonly NotifyIcon tray=new NotifyIcon();
    bool pauseEnabled=true;
    readonly Action finish,toggle;
    bool hotkey;
    Point drag;
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x08000000|0x80;return cp;}}
    public RecordingHud(Rectangle bounds,Action toggle,Action finish){
        this.toggle=toggle;this.finish=finish;
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;
        TopMost=true;BackColor=Color.FromArgb(25,26,31);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        ClientSize=new Size(246,44);var area=Screen.FromRectangle(bounds).WorkingArea;
        Location=new Point(area.Left+(area.Width-Width)/2,area.Bottom-Height-14);
        time.SetBounds(6,4,79,36);time.TextAlign=ContentAlignment.MiddleCenter;
        time.ForeColor=Color.LightSalmon;time.Text="● 00:00";Controls.Add(time);
        time.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){drag=e.Location;time.Capture=true;}};
        time.MouseMove+=(s,e)=>{if(e.Button==MouseButtons.Left && time.Capture)Location=new Point(Left+e.X-drag.X,Top+e.Y-drag.Y);};
        time.MouseUp+=(s,e)=>time.Capture=false;
        Configure(pause,89,81,Language.Pick("Пауза", "Pause"),toggle);Configure(stop,174,65,Language.Pick("Стоп", "Stop"),finish);
        tray.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)finish();else if(e.Button==MouseButtons.Left&&pauseEnabled)toggle();};
        tray.Icon=SystemIcons.Application;tray.Text=Language.Pick("Snapline · слева пауза, справа стоп", "Snapline · Left-click to pause, right-click to stop");
        timer.Tick+=(s,e)=>time.Text=(elapsed.IsRunning?"● ":"Ⅱ ")+(int)elapsed.Elapsed.TotalMinutes+":"+elapsed.Elapsed.Seconds.ToString("00");
    }
    void Configure(Button b,int x,int w,string label,Action action){
        b.SetBounds(x,6,w,32);b.Text=label;b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;
        b.BackColor=Color.FromArgb(52,54,63);b.ForeColor=Color.White;b.TabStop=false;b.Click+=(s,e)=>action();Controls.Add(b);
    }
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    Bitmap Sample(){
        var bitmap=new Bitmap(ClientSize.Width,ClientSize.Height);
        using(var source=Graphics.FromHwnd(IntPtr.Zero))using(var dest=Graphics.FromImage(bitmap)){
            var src=source.GetHdc();var dst=dest.GetHdc();
            try{if(!BitBlt(dst,0,0,bitmap.Width,bitmap.Height,src,Left,Top,0x40CC0020)){
                throw new System.ComponentModel.Win32Exception();
            }}catch{dest.ReleaseHdc(dst);source.ReleaseHdc(src);bitmap.Dispose();throw;}
            dest.ReleaseHdc(dst);source.ReleaseHdc(src);
        }
        return bitmap;
    }
    bool CheckDesktop(){
        using(var before=Sample()){
            Show();Refresh();DwmFlush();
            using(var after=Sample()){
                int match=0,samples=0,nonBlack=0;
                for(int y=3;y<before.Height;y+=6)for(int x=3;x<before.Width;x+=8){
                    var a=before.GetPixel(x,y);var b=after.GetPixel(x,y);
                    if(a.ToArgb()==b.ToArgb())match++;
                    if(b.R+b.G+b.B>36)nonBlack++;samples++;
                }
                return match>=samples*.96 && nonBlack>samples/8;
            }
        }
    }
    public void Begin(bool desktopCheck){
        var v=new VersionInfo{size=Marshal.SizeOf(typeof(VersionInfo))};uint actual;
        // Earlier Windows versions implement 0x11 as a black capture rectangle.
        bool modern=RtlGetVersion(ref v)==0 && v.build>=19041;
        if(modern && SetWindowDisplayAffinity(Handle,0x11) && GetWindowDisplayAffinity(Handle,out actual) && actual==0x11){
            if(desktopCheck){
                bool safe=false;try{safe=CheckDesktop();}catch(System.ComponentModel.Win32Exception){}catch(ExternalException){}
                if(!safe){Hide();DwmFlush();}
            }else Show();
        }
        hotkey=RegisterHotKey(Handle,1,0x4006,(uint)Keys.F10);
        tray.Visible=true;elapsed.Start();timer.Start();
    }
    public void State(bool paused,bool enabled){
        if(paused)elapsed.Stop();else elapsed.Start();
        pause.Text=paused?Language.Pick("Продолжить", "Resume"):Language.Pick("Пауза", "Pause");pause.Enabled=enabled;
        pauseEnabled=enabled;tray.Text=paused?Language.Pick("Snapline · слева продолжить, справа стоп", "Snapline · Left-click to resume, right-click to stop"):Language.Pick("Snapline · слева пауза, справа стоп", "Snapline · Left-click to pause, right-click to stop");
    }
    protected override void WndProc(ref Message m){if(m.Msg==0x312 && m.WParam.ToInt32()==1){finish();return;}base.WndProc(ref m);}
    protected override void Dispose(bool disposing){
        if(disposing){if(hotkey)UnregisterHotKey(Handle,1);timer.Dispose();tray.Visible=false;tray.Dispose();}
        base.Dispose(disposing);
    }
}
}
