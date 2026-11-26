using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace Navodka {
sealed class SceneLayout {
    public readonly int W,H,CropX,CropY,CropW,CropH,X,Y,VideoW,VideoH;
    public readonly double Radius;
    public Rect Video {get{return new Rect(X,Y,VideoW,VideoH);}}
    static int Even(double n){return Math.Max(2,(int)(n/2)*2);}
    public SceneLayout(int width,int height,int sourceW,int sourceH,double pad,double inset,double corners){
        W=width;H=height;
        CropX=(int)(sourceW*inset/200)*2;CropY=(int)(sourceH*inset/200)*2;
        CropW=Even(sourceW-2*CropX);CropH=Even(sourceH-2*CropY);
        double scale=Math.Min(W*(1-pad/100)/CropW,H*(1-pad/100)/CropH);
        VideoW=Even(CropW*scale);VideoH=Even(CropH*scale);
        X=(W-VideoW)/4*2;Y=(H-VideoH)/4*2;
        Radius=corners/100*Math.Min(VideoW,VideoH);
    }
    public Point ToCanvas(double sx,double sy,int sourceW,int sourceH){
        return new Point(X+(sx*sourceW-CropX)*VideoW/CropW,Y+(sy*sourceH-CropY)*VideoH/CropH);
    }
    public Point ToSource(Point p,int sourceW,int sourceH){
        return new Point((CropX+(p.X-X)*CropW/VideoW)/sourceW,(CropY+(p.Y-Y)*CropH/VideoH)/sourceH);
    }
    // The camera can centre an action even at a corner. Store the exposed paper
    // once, in the same limited-range planar format as the camera working frame.
    public void SaveCameraPaper(string path,Brush paper){
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(paper,null,new Rect(0,0,W,H));
        var image=new RenderTargetBitmap(W,H,96,96,PixelFormats.Pbgra32);image.Render(visual);
        var rgb=new byte[checked(W*H*4)];image.CopyPixels(rgb,W*4,0);
        int count=W*H;var data=new byte[count+count/2];
        for(int y=0;y<H;y+=2)for(int x=0;x<W;x+=2){
            int red=0,green=0,blue=0;
            for(int row=0;row<2;row++)for(int col=0;col<2;col++){
                int pixel=(y+row)*W+x+col,i=pixel*4;
                int b=rgb[i],g=rgb[i+1],r=rgb[i+2];red+=r;green+=g;blue+=b;
                data[pixel]=(byte)(16+((66*r+129*g+25*b+128)>>8));
            }
            int uv=(y/2)*(W/2)+x/2;
            data[count+uv]=(byte)(128+((-38*red-74*green+112*blue+512)>>10));
            data[count+count/4+uv]=(byte)(128+((112*red-94*green-18*blue+512)>>10));
        }
        File.WriteAllBytes(path,data);
    }
    public void SaveMatte(string path,Brush bg){
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){
            var paper=new RectangleGeometry(new Rect(0,0,W,H));
            var hole=new RectangleGeometry(Video,Radius,Radius);
            dc.DrawGeometry(bg,null,new CombinedGeometry(GeometryCombineMode.Exclude,paper,hole));
        }
        var bitmap=new RenderTargetBitmap(W,H,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        byte[] pixels=new byte[checked(W*H*4)];new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0).CopyPixels(pixels,W*4,0);File.WriteAllBytes(path,pixels);
    }
}
sealed class Annotation {
    public static readonly string[] Names={"Spotlight","Blur","Callout","Magnifier"};
    public Annotation Copy(){return (Annotation)MemberwiseClone();}
    public int Kind;public double Start,End;public Rect Bounds;
    public bool Active(double t){return Start<=t && t<End;}
    public Rect Area(SceneLayout scene,int sourceW,int sourceH){
        var r=new Rect(scene.ToCanvas(Bounds.Left,Bounds.Top,sourceW,sourceH),scene.ToCanvas(Bounds.Right,Bounds.Bottom,sourceW,sourceH));
        r.Intersect(scene.Video);
        if(Kind==3 && !r.IsEmpty){double side=Math.Min(r.Width,r.Height);r=new Rect(r.X+(r.Width-side)/2,r.Y+(r.Height-side)/2,side,side);}
        return r;
    }
    public override string ToString(){return Names[Kind]+"   "+Start.ToString("0.0")+"–"+End.ToString("0.0")+InterfaceLanguage.Pick(" с", " s");}
}
}
