using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace Navodka {
// Draw a non-activating ring on the selected desktop area for display captures.
class ClickIndicator : Form {
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int button);
    readonly Timer timer=new Timer{Interval=16}; readonly Rectangle area;
    readonly Func<Point,bool> exclude; bool pressed; DateTime until;
    protected override bool ShowWithoutActivation { get {return true;} }
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000020|0x00000080;return p;}}
    public ClickIndicator(Rectangle bounds,Func<Point,bool> exclude){
        this.exclude=exclude;
        area=bounds;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;
        BackColor=Color.Magenta;TransparencyKey=Color.Magenta;ClientSize=new Size(54,54);StartPosition=FormStartPosition.Manual;
        pressed=GetAsyncKeyState(1)<0;timer.Tick+=(s,e)=>Tick();timer.Start();
    }
    void Tick(){
        bool down=GetAsyncKeyState(1)<0;
        var point=Cursor.Position;
        if(down&&!pressed&&area.Contains(point)&&!exclude(point)){Location=new Point(point.X-27,point.Y-27);until=DateTime.UtcNow.AddMilliseconds(300);Show();}
        else if(Visible&&DateTime.UtcNow>=until)Hide();
        pressed=down;
    }
    protected override void OnPaint(PaintEventArgs e){using(var p=new Pen(Color.Gold,3))e.Graphics.DrawEllipse(p,4,4,45,45);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
}
