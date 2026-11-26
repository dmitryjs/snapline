using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Navodka {
static class LocalPicture {
    public static BitmapImage Read(string path){
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
            if(input.Length>128L*1024*1024)throw new InvalidDataException(InterfaceLanguage.Pick("Фон превышает 128 МБ.", "Background exceeds 128 MB."));
            var signature=new byte[8];
            if(input.Read(signature,0,8)!=8)throw new InvalidDataException(InterfaceLanguage.Pick("Файл изображения неполный.", "Incomplete image file."));
            input.Position=0;
            var options=BitmapCreateOptions.DelayCreation;
            BitmapDecoder decoder;
            if(signature[0]==137 && signature[1]==80 && signature[2]==78 && signature[3]==71)
                decoder=new PngBitmapDecoder(input,options,BitmapCacheOption.None);
            else if(signature[0]==255 && signature[1]==216)
                decoder=new JpegBitmapDecoder(input,options,BitmapCacheOption.None);
            else if(signature[0]==66 && signature[1]==77)
                decoder=new BmpBitmapDecoder(input,options,BitmapCacheOption.None);
            else throw new InvalidDataException(InterfaceLanguage.Pick("Выберите файл PNG, JPEG или BMP.", "Choose a PNG, JPEG or BMP file."));
            int w=decoder.Frames[0].PixelWidth,h=decoder.Frames[0].PixelHeight;
            if(w<=0||h<=0||w>32768||h>32768||(long)w*h>100000000)
                throw new InvalidDataException(InterfaceLanguage.Pick("Слишком большое исходное изображение для фона.", "Background image is too large."));
            input.Position=0;
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
            if(w>=h && w>2560)image.DecodePixelWidth=2560;
            if(h>w && h>2560)image.DecodePixelHeight=2560;
            image.StreamSource=input;image.EndInit();image.Freeze();return image;
        }
    }
}
}
