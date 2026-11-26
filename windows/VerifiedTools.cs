using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Navodka {
static class VerifiedTools {
    // Keep the verified files open without write/delete sharing for this process's lifetime.
    static readonly Dictionary<string,FileStream> handles=new Dictionary<string,FileStream>(StringComparer.Ordinal);
    internal static ProcessStartInfo StartInfo(string name,string[] args,string directory=null){
        if(name!="ffmpeg" && name!="capture")throw new ArgumentException(Language.Pick("Неизвестный компонент Snapline.", "Unknown Snapline component."));
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools");
        string filename=Path.Combine(folder,name+".exe");
        lock(handles){
            if(!handles.ContainsKey(name)){
                if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0 || (File.GetAttributes(filename)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException(Language.Pick("Компоненты Snapline должны находиться в распакованной папке приложения.", "Snapline components must be in the extracted application folder."));
                var input=new FileStream(filename,FileMode.Open,FileAccess.Read,FileShare.Read);
                try{
                    string hash;
                    using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
                    using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("ToolHashes.txt"))){
                        if(!reader.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Contains(hash+"  "+name+".exe"))
                            throw new InvalidDataException(Language.Pick("Проверка компонента ", "Verification of ")+name+Language.Pick(".exe не пройдена. Распакуйте оригинальный архив Snapline в новую папку.", ".exe failed verification. Extract the original Snapline archive to a new folder."));
                    }
                    handles.Add(name,input);
                }catch{input.Dispose();throw;}
            }
        }
        var info=new ProcessStartInfo(filename,string.Join(" ",args.Select(Worker.Quote))){
            UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory??folder
        };
        info.EnvironmentVariables["PATH"]=Environment.SystemDirectory;
        info.EnvironmentVariables.Remove("FFREPORT");
        return info;
    }
}
}
