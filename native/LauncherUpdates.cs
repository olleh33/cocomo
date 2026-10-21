using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace MokoLauncher {
    public static class LauncherUpdates {
        static readonly string[] Allowed={"뫀코코접속기.exe","사용방법.txt","구성-출처.txt","Prism-Translations-LICENSE.txt","assets/app.ico","assets/icon.png","assets/background.gif","assets/downloading.gif","assets/connecting.png"};
        public static Dictionary<string,byte[]> ReadPackage(byte[] bytes) {
            var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);long total=0;
            using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))foreach(var entry in zip.Entries) {
                string name=entry.FullName;
                Engine.Require(Allowed.Contains(name)&&!files.ContainsKey(name)&&((entry.ExternalAttributes>>16)&0xF000)!=0xA000,"접속기 업데이트에 허용하지 않은 파일이 있어요.");
                total+=entry.Length;Engine.Require(entry.Length<=10485760&&total<=20971520,"업데이트 압축 해제 크기가 너무 커요.");
                using(var source=entry.Open())using(var output=new MemoryStream()){source.CopyTo(output);files.Add(name,output.ToArray());}
            }
            Engine.Require(Allowed.All(files.ContainsKey),"접속기 업데이트 파일이 빠져 있어요.");return files;
        }
        public static string Stage(string root,Action<string> progress) {
            byte[] info;string signature;
            try {info=Engine.Fetch(GitHubDistribution.Latest+"/launcher-update.json",16384);signature=Encoding.UTF8.GetString(Engine.Fetch(GitHubDistribution.Latest+"/launcher-update.sig",8192));}
            catch(WebException e){if(e.Response is HttpWebResponse&&((HttpWebResponse)e.Response).StatusCode==HttpStatusCode.NotFound)return null;throw;}
            var update=GitHubDistribution.ValidateUpdate(info,signature);
            if(new Version(Engine.Str(update,"version"))<=new Version(GitHubDistribution.CurrentVersion))return null;
            progress("GitHub에서 새 접속기를 준비하고 있어요…");var descriptor=Engine.Get(update,"package");var bytes=Engine.Fetch(Engine.Str(descriptor,"url"),Engine.Num(descriptor,"bytes"));Engine.Check(bytes,descriptor);ReadPackage(bytes);
            string stage=Engine.Child(root,".launcher-update/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            Engine.AtomicWrite(Engine.Child(stage,"package.zip"),bytes);Engine.AtomicWrite(Engine.Child(stage,"update.json"),info);Engine.AtomicWrite(Engine.Child(stage,"update.sig"),Encoding.UTF8.GetBytes(signature));return stage;
        }
        public static void ApplyPackage(string root,byte[] bytes) {
            var files=ReadPackage(bytes);var backup=new Dictionary<string,byte[]>();var written=new List<string>();
            foreach(var pair in files){string target=Engine.Child(root,pair.Key);backup[pair.Key]=File.Exists(target)?File.ReadAllBytes(target):null;}
            string backupDir=Engine.Child(root,".launcher-backup/"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));
            foreach(var pair in backup)if(pair.Value!=null)Engine.AtomicWrite(Engine.Child(backupDir,pair.Key),pair.Value);
            try {foreach(var pair in files){Engine.AtomicWrite(Engine.Child(root,pair.Key),pair.Value);written.Add(pair.Key);}}
            catch {foreach(var name in written.AsEnumerable().Reverse()){var old=backup[name];string path=Engine.Child(root,name);if(old==null)File.Delete(path);else Engine.AtomicWrite(path,old);}throw;}
        }
        public static void Handoff(string root,string stage) {
            string helper=Engine.Child(stage,"update-helper.exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper,false);
            var info=new ProcessStartInfo(helper,"--complete-launcher-update "+Engine.Quote(root)+" "+Engine.Quote(stage)+" "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=stage};
            using(var p=Process.Start(info))Engine.Require(p!=null,"접속기 업데이트를 시작하지 못했어요.");
        }
        public static void Complete(string root,string stage,int oldPid) {
            root=Path.GetFullPath(root);stage=Path.GetFullPath(stage);Engine.NoLinks(root);Engine.NoLinks(stage);
            Engine.Require(stage.StartsWith(Engine.Child(root,".launcher-update")+"\\",StringComparison.OrdinalIgnoreCase),"업데이트 작업 폴더가 올바르지 않아요.");
            var update=GitHubDistribution.ValidateUpdate(File.ReadAllBytes(Engine.Child(stage,"update.json")),File.ReadAllText(Engine.Child(stage,"update.sig")));
            var bytes=File.ReadAllBytes(Engine.Child(stage,"package.zip"));Engine.Check(bytes,Engine.Get(update,"package"));ReadPackage(bytes);
            try {using(var old=Process.GetProcessById(oldPid)){Engine.Require(old.MainModule.FileName.Equals(Engine.Child(root,"뫀코코접속기.exe"),StringComparison.OrdinalIgnoreCase),"종료를 기다릴 접속기가 일치하지 않아요.");Engine.Require(old.WaitForExit(45000),"이전 접속기가 아직 열려 있어요. 다시 실행해 주세요.");}}
            catch(ArgumentException){}
            ApplyPackage(root,bytes);
            Process.Start(new ProcessStartInfo(Engine.Child(root,"뫀코코접속기.exe")){UseShellExecute=false,WorkingDirectory=root,WindowStyle=ProcessWindowStyle.Normal});
        }
    }
}
