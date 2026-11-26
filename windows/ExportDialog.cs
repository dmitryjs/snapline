using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Forms=System.Windows.Forms;
namespace Navodka {
sealed class ExportChoice {
    public string File;public int Width,Height,Quality;
}
static class ExportDialog {
    public static ExportChoice Choose(Window owner,string source,int width,int height){
        Window dialog;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ExportDialog.xaml"))dialog=(Window)Language.LoadView(stream);
        dialog.Owner=owner;
        var resolution=(ComboBox)dialog.FindName("Resolution");
        var quality=(ComboBox)dialog.FindName("Quality");
        VideoQuality.Populate(quality,VideoQuality.Levels,1);
        resolution.Items.Add(Language.Pick("Холст · ", "Canvas · ")+width+" × "+height);
        for(int i=0;i<VideoQuality.Sizes.Length;i++){
            var size=VideoQuality.Fit(width,height,i,true);
            resolution.Items.Add(VideoQuality.Sizes[i]+" · "+size.Width+" × "+size.Height);
        }
        resolution.SelectedIndex=0;
        var name=(TextBox)dialog.FindName("FileNameBox");var dest=(TextBox)dialog.FindName("Destination");var error=(TextBlock)dialog.FindName("Error");
        name.Text=Path.GetFileNameWithoutExtension(source)+"-edit";dest.Text=Path.GetDirectoryName(source);

        ((Button)dialog.FindName("Browse")).Click+=(s,e)=>{
            using(var b=new Forms.FolderBrowserDialog()){b.SelectedPath=dest.Text;if(b.ShowDialog()==Forms.DialogResult.OK)dest.Text=b.SelectedPath;}
        };
        ExportChoice result=null;
        ((Button)dialog.FindName("Confirm")).Click+=(s,e)=>{
            try{
                var file=name.Text.Trim();
                if(file.Length==0 || file.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || file.EndsWith("."))throw new IOException(Language.Pick("Укажите имя файла.", "Enter a file name."));
                if(!file.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase))file+=".mp4";
                if(!Directory.Exists(dest.Text))throw new IOException(Language.Pick("Выберите существующую папку.", "Choose an existing folder."));
                string path=Path.GetFullPath(Path.Combine(dest.Text,file));
                if(string.Equals(source,path,StringComparison.OrdinalIgnoreCase))throw new IOException(Language.Pick("Укажите другое имя, чтобы сохранить оригинал.", "Choose another name to keep the original."));
                if(File.Exists(path)&&MessageBox.Show(dialog,Language.Pick("Заменить файл «", "Replace file «")+file+"»?",Language.Pick("Экспорт видео", "Export video"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                var size=resolution.SelectedIndex==0?new System.Drawing.Size(width,height):VideoQuality.Fit(width,height,resolution.SelectedIndex-1,true);
                result=new ExportChoice{File=path,Width=size.Width,Height=size.Height,Quality=quality.SelectedIndex};dialog.DialogResult=true;
            }catch(Exception ex){error.Text=ex.Message;error.Visibility=Visibility.Visible;}
        };
        dialog.Loaded+=(s,e)=>{name.Focus();name.SelectAll();};
        return dialog.ShowDialog()==true?result:null;
    }
}
}
