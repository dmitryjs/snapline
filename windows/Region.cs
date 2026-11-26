using System;
using System.Drawing;
using System.Windows.Forms;
namespace Navodka {
class Region : Form {
    Point anchor; Rectangle selection; bool dragging;
    static Rectangle Box(Point a,Point b){return Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));}
    Region(Rectangle bounds){
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=bounds;
        Opacity=.72;TransparencyKey=Color.Magenta;TopMost=true;ShowInTaskbar=false;BackColor=Color.Black;DoubleBuffered=true;Cursor=Cursors.Cross;KeyPreview=true;
        KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape)DialogResult=DialogResult.Cancel;};
        MouseDown+=(s,e)=>{dragging=true;anchor=e.Location;Capture=true;};
        MouseMove+=(s,e)=>{if(dragging){selection=Rectangle.Intersect(ClientRectangle,Box(anchor,e.Location));Invalidate();}};
        MouseUp+=(s,e)=>{dragging=false;Capture=false;selection=Rectangle.Intersect(ClientRectangle,Box(anchor,e.Location));if(selection.Width>=16&&selection.Height>=16)DialogResult=DialogResult.OK;};
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        if(selection.Width>0 && selection.Height>0){
            e.Graphics.FillRectangle(Brushes.Magenta,selection);
            using(var pen=new Pen(Color.FromArgb(83,222,241),2)){
                pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawRectangle(pen,selection);
            }
            string size=selection.Width+" × "+selection.Height;
            var label=new Rectangle(selection.Left,Math.Max(0,selection.Top-22),100,22);
            e.Graphics.FillRectangle(Brushes.Black,label);TextRenderer.DrawText(e.Graphics,size,Font,label,Color.White);
        }
        e.Graphics.DrawString(Language.Pick("Выделите область мышью · Esc для отмены", "Drag to select an area · Esc to cancel"),Font,Brushes.White,18,18);
    }
    public static Rectangle? Pick(Rectangle bounds){using(var f=new Region(bounds)){if(f.ShowDialog()!=DialogResult.OK)return null;var r=f.selection;r.Offset(bounds.Location);return r;}}
}
}
