using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brush=System.Windows.Media.Brushes;
using Color=System.Windows.Media.Color;

namespace Navodka {
sealed class CountdownOverlay:Window {
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int cx,int cy,uint flags);
    readonly TextBlock number;
    public CountdownOverlay(Rectangle bounds,Action cancel){
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Topmost=true;ShowInTaskbar=false;
        Background=new SolidColorBrush(Color.FromArgb(142,0,0,0));FontFamily=new System.Windows.Media.FontFamily("Segoe UI");
        var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        number=new TextBlock{FontSize=85,FontWeight=FontWeights.Bold,Foreground=Brush.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        stack.Children.Add(new Border{Width=205,Height=205,CornerRadius=new CornerRadius(103),Background=new SolidColorBrush(Color.FromRgb(8,8,8)),Child=number});
        var abort=new Button{Content=InterfaceLanguage.Pick("Отмена", "Cancel"),Width=120,Height=34,Margin=new Thickness(0,13,0,0)};abort.Click+=(s,e)=>cancel();stack.Children.Add(abort);
        stack.Children.Add(new Border{CornerRadius=new CornerRadius(7),Margin=new Thickness(0,16,0,0),Padding=new Thickness(14,9,14,9),
            Background=new SolidColorBrush(Color.FromRgb(22,23,27)),Child=new TextBlock{Text=InterfaceLanguage.Pick("Движения и клики мыши создают приближения.\nДля остановки откройте «Snapline» на панели задач.", "Mouse movement and clicks create zooms.\nOpen Snapline on the taskbar to stop recording."),Foreground=Brush.White}});
        Content=stack;
        SourceInitialized+=(s,e)=>SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0);
        KeyDown+=(s,e)=>{if(e.Key==Key.Escape || (e.Key==Key.System && e.SystemKey==Key.F4)){e.Handled=true;cancel();}};
    }
    public void SetNumber(int n){number.Text=n.ToString();}
}
}
