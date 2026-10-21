using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MokoLauncher {
    public sealed class LauncherError : Exception { public LauncherError(string message) : base(message) {} }
    public sealed class Engine {
        public const string InstanceId = "friends-village";
        public readonly string Root, Data, Game, Instance, PrismData;
        readonly Action<string> progress;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength=1048576, RecursionLimit=32 }; } }
        public static object Parse(string text) { return Json.DeserializeObject(text); }
        public static string Serialize(object value) { return Json.Serialize(value); }
        public static Dictionary<string,object> Obj(object value) { var o=value as Dictionary<string,object>; if(o==null) throw new LauncherError("패치 정보 형식이 올바르지 않아요."); return o; }
        public static object Get(object value,string key) { object v; if(!Obj(value).TryGetValue(key,out v)) throw new LauncherError("필수 패치 정보가 빠져 있어요: "+key); return v; }
        public static string Str(object value,string key) { var v=Get(value,key) as string; if(v==null) throw new LauncherError("패치 문자열이 올바르지 않아요: "+key); return v; }
        public static long Num(object value,string key) { object v=Get(value,key); if(!(v is int) && !(v is long)) throw new LauncherError("패치 숫자가 올바르지 않아요."); return Convert.ToInt64(v); }
        public static IEnumerable<object> Arr(object value) { var a=value as object[]; if(a==null) throw new LauncherError("패치 목록이 올바르지 않아요."); return a; }
        public static byte[] Resource(string name) { using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) { if(s==null) throw new LauncherError("접속기 내부 파일이 빠져 있어요."); using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();} } }
        public static string Hash(byte[] bytes) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        public static string FileHash(string file) { using(var s=File.OpenRead(file)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
        public static void Require(bool condition,string message) { if(!condition) throw new LauncherError(message); }
        public static bool IsHash(string s) { return Regex.IsMatch(s ?? "",@"\A[a-f0-9]{64}\z"); }
        static bool Version(string s) { return Regex.IsMatch(s ?? "",@"\A\d{1,3}\.\d{1,3}\.\d{1,3}\z"); }
        public static void NoLinks(string path) {
            string current=Path.GetFullPath(path);
            while(!String.IsNullOrEmpty(current)) {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new LauncherError("설치 경로에 연결 폴더가 있어요. 일반 폴더에 압축을 풀어 주세요.");
                current=Path.GetDirectoryName(current);
            }
        }
        public static string Child(string root,string relative) {
            Require(!String.IsNullOrEmpty(relative)&&!Path.IsPathRooted(relative)&&relative.IndexOf(':')<0,"잘못된 파일 경로예요.");
            var parts=relative.Replace('\\','/').Split('/');
            Require(parts.All(p=>p.Length>0&&p!="."&&p!=".."&&!p.EndsWith(".")&&!p.EndsWith(" ")&&p.IndexOfAny(Path.GetInvalidFileNameChars())<0&&!Regex.IsMatch(p,@"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)),"허용되지 않는 파일 이름이에요.");
            string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            Require(full.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase),"설치 폴더를 벗어난 경로예요.");
            NoLinks(full);return full;
        }
        public static string Host(string value) {
            value=(value??"").Trim().TrimEnd('.');
            Require(value.Length>0&&!Regex.IsMatch(value,@"[\s:/\\@?#""'`$]"),"도메인 이름이나 IP 주소만 입력하세요.");
            string name;try{name=new IdnMapping{UseStd3AsciiRules=true}.GetAscii(value).ToLowerInvariant();}catch{throw new LauncherError("도메인 철자를 확인해 주세요.");}
            Require(name.Length<=253&&Regex.IsMatch(name,@"\A[a-z0-9](?:[a-z0-9.\-]*[a-z0-9])?\z")&&name.Split('.').All(p=>p.Length>0&&p.Length<=63&&!p.StartsWith("-")&&!p.EndsWith("-")),"도메인 철자를 확인해 주세요.");return name;
        }
        public static string ServerAddress(string value) {
            var m=Regex.Match((value??"").Trim(),@"\A([^:/\s]+)(?::([0-9]{1,5}))?\z");Require(m.Success,"서버 주소를 확인하세요. 예: 마을.example.com:25565");
            string host=Host(m.Groups[1].Value);if(m.Groups[2].Success){int p=Int32.Parse(m.Groups[2].Value);Require(p>=1&&p<=65535,"포트는 1~65535 사이여야 해요.");host+=":"+p;}return host;
        }
        public static string PatchAddress(string value) {
            var m=Regex.Match((value??"").Trim(),@"\A(https?)://([^:/\s?#]+)(?::([0-9]{1,5}))?(/[A-Za-z0-9_./-]*)?\z",RegexOptions.IgnoreCase);
            Require(m.Success,"패치 주소를 확인해 주세요.");string host=Host(m.Groups[2].Value);string port="";
            if(m.Groups[3].Success){int p=Int32.Parse(m.Groups[3].Value);Require(p>=1&&p<=65535,"포트는 1~65535 사이여야 해요.");port=":"+p;}
            Require(!m.Groups[4].Value.Split('/').Contains(".."),"패치 주소 경로를 확인해 주세요.");
            return m.Groups[1].Value.ToLowerInvariant()+"://"+host+port+m.Groups[4].Value.TrimEnd('/');
        }
        public static bool ModPath(string path) { return Regex.IsMatch(path??"",@"\A(mods/[A-Za-z0-9._+\-]+\.jar|shaderpacks/[A-Za-z0-9._+\-]+\.zip)\z"); }
        static void Blob(object b,long maximum) { Require(IsHash(Str(b,"sha256"))&&Num(b,"bytes")>0&&Num(b,"bytes")<=maximum,"파일 크기나 해시 정보가 올바르지 않아요."); }
        public static object VerifyManifest(byte[] bytes,string signature) {
            Require(bytes.Length<=131072,"패치 목록이 너무 커요.");
            try { using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(Utf8.GetString(Resource("publisher-public.xml")));Require(rsa.VerifyData(bytes,"SHA256",Convert.FromBase64String(signature.Trim())),"패치 목록 서명이 올바르지 않아요. 설치를 중단했어요.");} }
            catch(FormatException){throw new LauncherError("패치 서명 형식이 올바르지 않아요.");}
            object m=Parse(Utf8.GetString(bytes));ValidateManifest(m);return m;
        }
        public static void ValidateManifest(object m) {
            Require((Num(m,"schema")==1||Num(m,"schema")==2)&&Str(m,"instanceId")==InstanceId&&Regex.IsMatch(Str(m,"minecraftVersion"),@"\A1\.21\.\d{1,3}\z"),"지원하지 않는 게임 패치예요.");
            Require(Str(m,"revision").Length<200,"패치 이름이 너무 길어요.");
            object pack=Get(m,"pack");Blob(pack,104857600);Require(Str(pack,"path")=="resourcepack.zip","리소스팩 경로가 올바르지 않아요.");
            object c=Get(m,"client");Require(Version(Str(c,"fabricLoader")),"모드 로더 버전이 올바르지 않아요.");
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;
            foreach(var f in Arr(Get(c,"files"))){
                string p=Str(f,"path");Require(ModPath(p)&&paths.Add(p)&&++count<=40,"모드 파일 경로가 중복되거나 올바르지 않아요.");Blob(f,104857600);
                Uri u;Require(Uri.TryCreate(Str(f,"url"),UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="cdn.modrinth.com"&&u.IsDefaultPort&&String.IsNullOrEmpty(u.UserInfo)&&u.AbsolutePath.StartsWith("/data/")&&String.IsNullOrEmpty(u.Fragment),"공식 모드 배포 주소가 아니에요.");
            }
            Require(count>0,"클라이언트 모드가 없어요.");
            object keys=Get(c,"villageKeys");Blob(keys,1048576);Require(Version(Str(keys,"version")),"단축키 모드 버전이 올바르지 않아요.");
            if(Num(m,"schema")==1){byte[] keyBytes;try{keyBytes=Convert.FromBase64String(Str(keys,"payload"));}catch{throw new LauncherError("단축키 파일 형식이 올바르지 않아요.");}Check(keyBytes,keys);}else{GitHubDistribution.CheckAsset(keys,1048576);GitHubDistribution.CheckAsset(pack,104857600);}
            object ui=Get(c,"ui");Blob(ui,10485760);Require(Str(ui,"path")=="ui.zip","한글 UI 경로가 올바르지 않아요.");
            Require(paths.Contains("shaderpacks/"+Str(c,"shaderPack")),"셰이더 파일 정보가 빠져 있어요.");
            if(Num(m,"schema")==1)Require(IsHash(Str(c,"shaderLanguageSha256"))&&Hash(Utf8.GetBytes(Str(c,"shaderLanguage")))==Str(c,"shaderLanguageSha256"),"셰이더 번역 파일 검증에 실패했어요.");else{GitHubDistribution.CheckAsset(Get(c,"shaderLanguageFile"),1048576);GitHubDistribution.CheckAsset(ui,10485760);}
            Require(Str(c,"shaderPreset").Length<100&&Str(c,"shaderOptions").Length<65536,"셰이더 설정이 너무 커요.");
            foreach(var f in Arr(Get(c,"retiredFiles")))Require(ModPath(Str(f,"path"))&&IsHash(Str(f,"sha256")),"이전 모드 정리 정보가 올바르지 않아요.");
        }
        public static void Check(byte[] bytes,object descriptor) { Require(bytes.LongLength==Num(descriptor,"bytes")&&Hash(bytes)==Str(descriptor,"sha256"),"다운로드 파일의 크기나 해시가 달라요. 적용하지 않았어요."); }
        public Engine(string root,string data,Action<string> progress) {
            Root=Path.GetFullPath(root);Data=Path.GetFullPath(data);NoLinks(Data);Directory.CreateDirectory(Data);this.progress=progress??delegate{};
            PrismData=Child(Data,"prism-data");Instance=Child(PrismData,"instances/"+InstanceId);Game=Child(Instance,".minecraft");
        }
        static bool AllowedRedirect(Uri original,Uri next) {
            if(next.Scheme!="https"||!next.IsDefaultPort||next.UserInfo.Length!=0)return false;
            if(original.Host=="github.com")return next.Host=="github.com"||next.Host=="release-assets.githubusercontent.com"||next.Host=="objects.githubusercontent.com";
            return original.Scheme=="https"&&next.Host==original.Host;
        }
        public static byte[] Fetch(string url,long maximum) {
            Uri original=new Uri(url), current=original;
            for(int redirects=0;redirects<6;redirects++) {
                var req=(HttpWebRequest)WebRequest.Create(current);req.AllowAutoRedirect=false;req.Timeout=60000;req.ReadWriteTimeout=60000;req.UserAgent="MokoVillageLauncher/3.0";
                using(var response=(HttpWebResponse)req.GetResponse()) {
                    if((int)response.StatusCode>=300&&(int)response.StatusCode<400){var next=new Uri(current,response.Headers["Location"]);Require(AllowedRedirect(original,next),"허용되지 않는 다운로드 이동 주소예요.");current=next;continue;}
                    Require(response.StatusCode==HttpStatusCode.OK,"다운로드 서버가 파일을 보내지 못했어요.");Require(response.ContentLength<=maximum,"다운로드 파일이 허용 크기를 넘었어요.");
                    using(var stream=response.GetResponseStream())using(var ms=new MemoryStream()) {var buffer=new byte[65536];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){Require(ms.Length+n<=maximum,"다운로드 파일이 허용 크기를 넘었어요.");ms.Write(buffer,0,n);}return ms.ToArray();}
                }
            }throw new LauncherError("다운로드 이동 횟수가 너무 많아요.");
        }
        public static void AtomicWrite(string path,byte[] bytes) {
            NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path));
            if(File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(bytes))return;
            string temp=Path.Combine(Path.GetDirectoryName(path),".moko-"+Guid.NewGuid().ToString("N")+".tmp");
            try {File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public byte[] Cached(string url,object descriptor,string ext,byte[] bundled=null) {
            string path=Child(Data,"cache/"+Str(descriptor,"sha256")+ext);
            if(File.Exists(path)){var existing=File.ReadAllBytes(path);if(existing.LongLength==Num(descriptor,"bytes")&&Hash(existing)==Str(descriptor,"sha256"))return existing;}
            var bytes=bundled!=null&&bundled.LongLength==Num(descriptor,"bytes")&&Hash(bundled)==Str(descriptor,"sha256")?bundled:Fetch(url,Num(descriptor,"bytes"));
            Check(bytes,descriptor);AtomicWrite(path,bytes);return bytes;
        }
        public static void Extract(byte[] bytes,string target) {
            NoLinks(target);Directory.CreateDirectory(target);
            using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read)) {
                Require(zip.Entries.Count<5000,"압축 파일 항목이 너무 많아요.");long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries) {
                    string n=entry.FullName.Replace('\\','/').TrimEnd('/');if(n.Length==0)continue;
                    string dest=Child(target,n);Require(names.Add(n),"압축 파일에 중복 경로가 있어요.");
                    Require(((entry.ExternalAttributes>>16)&0xF000)!=0xA000,"압축 파일에 연결 파일이 있어요.");
                    total+=entry.Length;Require(total<=536870912,"압축 해제 크기가 너무 커요.");
                    if(entry.Name.Length==0){Directory.CreateDirectory(dest);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));using(var input=entry.Open())using(var output=new FileStream(dest,FileMode.CreateNew)){input.CopyTo(output);}
                }
            }
        }
        string ShaderLanguage(object c){if(Obj(c).ContainsKey("shaderLanguage"))return Str(c,"shaderLanguage");object f=Get(c,"shaderLanguageFile");return Utf8.GetString(Cached(Str(f,"url"),f,".lang"));}
        string PrepareRuntime() {
            object pin=Parse(Utf8.GetString(Resource("runtime-pinned.json")));
            progress("공식 Prism Launcher 파일을 확인하고 있어요…");
            byte[] bytes=Cached(Str(pin,"url"),pin,".zip");string runtime=Child(Data,"Prism-"+Str(pin,"version"));
            // Verify the official installed binaries before every invocation, including DLLs.
            bool valid=Directory.Exists(runtime);var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))foreach(var entry in zip.Entries){if(entry.Name.Length==0)continue;string p=Child(runtime,entry.FullName);expected.Add(p);if(!File.Exists(p)||new FileInfo(p).Length!=entry.Length){valid=false;continue;}using(var src=entry.Open())using(var h=SHA256.Create()){if(FileHash(p)!=BitConverter.ToString(h.ComputeHash(src)).Replace("-","").ToLowerInvariant())valid=false;}}
            if(valid) foreach(var file in Directory.EnumerateFiles(runtime,"*",SearchOption.AllDirectories)) {NoLinks(file);if(new[]{".dll",".exe",".jar"}.Contains(Path.GetExtension(file).ToLowerInvariant())&&!expected.Contains(file))valid=false;}
            if(!valid){
                Require(!RuntimeRunning(runtime),"Prism이 열려 있어 파일을 갱신할 수 없어요. 해당 Prism 창을 닫아 주세요.");
                string stage=Child(Data,"runtime-staging-"+Guid.NewGuid().ToString("N"));Extract(bytes,stage);
                if(Directory.Exists(runtime)){string old=Child(Data,"backups/runtime-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.GetDirectoryName(old));NoLinks(runtime);Directory.Move(runtime,old);}
                Directory.Move(stage,runtime);
            }
            return Child(runtime,"prismlauncher.exe");
        }
        public static bool RuntimeRunning(string runtime) {foreach(var p in Process.GetProcessesByName("prismlauncher")){using(p){try{if(p.MainModule.FileName.StartsWith(Path.GetFullPath(runtime)+"\\",StringComparison.OrdinalIgnoreCase))return true;}catch(System.ComponentModel.Win32Exception){throw new LauncherError("Prism 실행 상태를 확인할 수 없어요. 실행 중인 Prism을 닫고 다시 시도해 주세요.");}catch(InvalidOperationException){}}}return false;}
        public bool GameRunning() {
            try {using(var query=new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='javaw.exe' OR Name='java.exe'"))using(var items=query.Get())foreach(ManagementObject item in items){using(item){string command=item["CommandLine"] as string;if(command!=null&&(command.IndexOf(Instance,StringComparison.OrdinalIgnoreCase)>=0||command.IndexOf(Instance.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)>=0))return true;}}return false;}
            catch(ManagementException){throw new LauncherError("게임 실행 상태를 확인할 수 없어요. Minecraft를 닫고 다시 시도해 주세요.");}
        }
        static byte[] TranslateShader(byte[] bytes,string language) {
            using(var ms=new MemoryStream()){ms.Write(bytes,0,bytes.Length);using(var zip=new ZipArchive(ms,ZipArchiveMode.Update,true)){foreach(string name in new[]{"shaders/lang/ko_KR.lang","shaders/lang/ko_kr.lang"}){var old=zip.GetEntry(name);if(old!=null)old.Delete();var entry=zip.CreateEntry(name);entry.LastWriteTime=new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero);using(var s=entry.Open()){var b=Utf8.GetBytes(language);s.Write(b,0,b.Length);}}}return ms.ToArray();}
        }
        public static string Ini(string text,IDictionary<string,string> settings) {
            if(String.IsNullOrEmpty(text))text="[General]\n";
            foreach(var kv in settings){string pattern="(?m)^"+Regex.Escape(kv.Key)+"=.*$",line=kv.Key+"="+kv.Value;if(Regex.IsMatch(text,pattern))text=Regex.Replace(text,pattern,m=>line);else text=text.TrimEnd()+"\n"+line+"\n";}return text;
        }
        static byte[] ServerList(string address) {using(var m=new MemoryStream()){Action<string> str=s=>{byte[] b=Utf8.GetBytes(s);m.WriteByte((byte)(b.Length>>8));m.WriteByte((byte)b.Length);m.Write(b,0,b.Length);};m.WriteByte(10);str("");m.WriteByte(9);str("servers");m.WriteByte(10);m.Write(new byte[]{0,0,0,1},0,4);m.WriteByte(8);str("name");str("뫀코코 마을");m.WriteByte(8);str("ip");str(address);m.WriteByte(1);str("acceptTextures");m.WriteByte(1);m.WriteByte(0);m.WriteByte(0);return m.ToArray();}}
        public static bool ManagedTarget(string path) {
            if(new[]{"verified-manifest.json","verified-manifest.sig","installed-patch.json","prism-data/prismlauncher.cfg","prism-data/translations/mmc_ko.qm","prism-data/instances/"+InstanceId+"/instance.cfg","prism-data/instances/"+InstanceId+"/mmc-pack.json"}.Contains(path))return true;
            string prefix="prism-data/instances/"+InstanceId+"/.minecraft/";
            if(!path.StartsWith(prefix,StringComparison.Ordinal))return false;
            string gamePath=path.Substring(prefix.Length);
            return ModPath(gamePath)||Regex.IsMatch(gamePath,@"\Ashaderpacks/[A-Za-z0-9._+\-]+\.zip\.txt\z")||new[]{"managed-mods.json","resourcepacks/moko-ui.zip","config/iris.properties","config/imblocker.json","options.txt","servers.dat"}.Contains(gamePath);
        }
        public static void Recover(string data) {
            string journal=Child(data,"pending-patch.json");if(!File.Exists(journal))return;
            var rows=Arr(Parse(File.ReadAllText(journal,Utf8))).ToArray();
            var restore=new List<KeyValuePair<string,byte[]>>();
            foreach(var row in rows){string relative=Str(row,"path");Require(ManagedTarget(relative),"복구 기록 경로가 올바르지 않아요.");
                string target=Child(data,relative);bool existed=(bool)Get(row,"existed");byte[] old=null;
                if(existed){string source=Str(row,"backup");Require(source.StartsWith("backups/patch-",StringComparison.Ordinal),"복구 파일 경로가 올바르지 않아요.");old=File.ReadAllBytes(Child(data,source));Require(Hash(old)==Str(row,"sha256"),"복구 파일 검증에 실패했어요. 운영자에게 알려 주세요.");}
                restore.Add(new KeyValuePair<string,byte[]>(target,old));
            }
            foreach(var item in restore){if(item.Value==null){NoLinks(item.Key);if(File.Exists(item.Key))File.Delete(item.Key);}else AtomicWrite(item.Key,item.Value);}
            File.Delete(journal);
        }
        public static void Apply(string data,Dictionary<string,byte[]> changes) {
            Recover(data);
            string backup=Child(data,"backups/patch-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));
            var journalRows=new List<object>();var pending=new Dictionary<string,byte[]>();
            foreach(var pair in changes){string path=Child(data,pair.Key);Require(ManagedTarget(pair.Key),"허용되지 않는 패치 대상이에요.");byte[] old=File.Exists(path)?File.ReadAllBytes(path):null;if(old==null&&pair.Value==null||old!=null&&pair.Value!=null&&old.SequenceEqual(pair.Value))continue;
                string saved=old==null?"":Child(backup,Hash(Utf8.GetBytes(pair.Key))+".bak");if(old!=null)AtomicWrite(saved,old);
                journalRows.Add(new {path=pair.Key,existed=old!=null,backup=old==null?"":saved.Substring(Path.GetFullPath(data).TrimEnd('\\').Length+1).Replace('\\','/'),sha256=old==null?"":Hash(old)});pending[path]=pair.Value;
            }
            if(pending.Count==0)return;
            string journal=Child(data,"pending-patch.json");AtomicWrite(journal,Utf8.GetBytes(Serialize(journalRows)));
            try {foreach(var pair in pending){if(pair.Value==null){NoLinks(pair.Key);if(File.Exists(pair.Key))File.Delete(pair.Key);}else AtomicWrite(pair.Key,pair.Value);}File.Delete(journal);}
            catch {Recover(data);throw;}
        }
        public string Prepare(string server,string patch,bool requireLive) {
            server=ServerAddress(server);patch=PatchAddress(patch);
            using(var operationLock=new FileStream(Child(Data,"launcher.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                Require(!GameRunning(),"Minecraft를 먼저 종료한 뒤 패치해 주세요. 실행 중인 게임 파일은 바꾸지 않았어요.");
                Recover(Data);
                progress("서버 패치의 서명과 버전을 확인하고 있어요…");
                byte[] bytes;string signature;object m;bool live=true;
                try {bytes=Fetch(patch+"/manifest.json",131072);signature=Utf8.GetString(Fetch(patch+"/manifest.sig",8192));}
                catch(WebException){if(requireLive)throw new LauncherError("서버 패치 주소에 연결할 수 없어요. 서버 상태와 접속 설정을 확인해 주세요.");live=false;
                    string cached=Child(Data,"verified-manifest.json"),sig=Child(Data,"verified-manifest.sig");
                    bytes=File.Exists(cached)&&File.Exists(sig)?File.ReadAllBytes(cached):Resource("bundled-manifest.json");signature=File.Exists(cached)&&File.Exists(sig)?File.ReadAllText(sig,Utf8):Utf8.GetString(Resource("bundled-manifest.sig"));progress("서버에 연결되지 않아 이전에 서명된 설치 정보를 사용해요.");}
                m=VerifyManifest(bytes,signature);object c=Get(m,"client");string exe=PrepareRuntime();
                string packPath=Child(Instance,"mmc-pack.json");object old=File.Exists(packPath)?Parse(File.ReadAllText(packPath,Utf8)):null;
                var componentList=new List<object>();
                componentList.Add(new Dictionary<string,object>{{"uid","net.minecraft"},{"version",Str(m,"minecraftVersion")},{"important",true}});
                componentList.Add(new Dictionary<string,object>{{"uid","net.fabricmc.fabric-loader"},{"version",Str(c,"fabricLoader")}});
                if(old!=null) {var prior=Arr(Get(old,"components")).ToArray();bool versionsChanged=prior.Where(x=>new[]{"net.minecraft","net.fabricmc.fabric-loader"}.Contains(Str(x,"uid"))).Any(x=>Str(x,"version")!=(Str(x,"uid")=="net.minecraft"?Str(m,"minecraftVersion"):Str(c,"fabricLoader")));
                    Require(!versionsChanged||!RuntimeRunning(Path.GetDirectoryName(exe)),"게임 버전이 바뀌었어요. 이 접속기의 Prism 창을 닫고 다시 패치해 주세요.");
                    componentList.AddRange(prior.Where(x=>!new[]{"net.minecraft","net.fabricmc.fabric-loader"}.Contains(Str(x,"uid"))));}
                var changes=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);string gameRel="prism-data/instances/"+InstanceId+"/.minecraft/";
                Action<string,byte[]> gameFile=(name,content)=>{Child(Game,name);changes[gameRel+name]=content;};
                Action<string,string> gameText=(name,content)=>gameFile(name,Utf8.GetBytes(content));
                Func<string,string> existing=name=>{string path=Child(Game,name);return File.Exists(path)?File.ReadAllText(path,Utf8):"";};
                var managed=new List<object>();
                foreach(var f in Arr(Get(c,"files"))){string path=Str(f,"path");progress("파일 확인: "+Path.GetFileName(path));byte[] payload=Cached(Str(f,"url"),f,Path.GetExtension(path));if(path=="shaderpacks/"+Str(c,"shaderPack"))payload=TranslateShader(payload,ShaderLanguage(c));gameFile(path,payload);managed.Add(new Dictionary<string,object>{{"path",path},{"sha256",Str(f,"sha256")},{"installedSha256",Hash(payload)}});}
                object keys=Get(c,"villageKeys");gameFile("mods/MokoKeys.jar",Num(m,"schema")==1?Convert.FromBase64String(Str(keys,"payload")):Cached(Str(keys,"url"),keys,".jar"));
                object ui=Get(c,"ui");gameFile("resourcepacks/moko-ui.zip",Cached(Num(m,"schema")==1?patch+"/ui.zip":Str(ui,"url"),ui,".zip"));
                // Retire only publisher-declared old files with their original hash; back up every removal.
                var currentPaths=new HashSet<string>(Arr(Get(c,"files")).Select(f=>Str(f,"path")),StringComparer.OrdinalIgnoreCase);
                string previousManifest=Child(Data,"verified-manifest.json"),previousSignature=Child(Data,"verified-manifest.sig");
                if(File.Exists(previousManifest)&&File.Exists(previousSignature)){
                    object previousClient=Get(VerifyManifest(File.ReadAllBytes(previousManifest),File.ReadAllText(previousSignature,Utf8)),"client");
                    foreach(var previous in Arr(Get(previousClient,"files"))){string name=Str(previous,"path"),file=Child(Game,name);if(currentPaths.Contains(name)||!File.Exists(file))continue;
                        string expected=Str(previous,"sha256");
                        if(name=="shaderpacks/"+Str(previousClient,"shaderPack")) {string original=Child(Data,"cache/"+expected+".zip");if(File.Exists(original)){var originalBytes=File.ReadAllBytes(original);Check(originalBytes,previous);expected=Hash(TranslateShader(originalBytes,ShaderLanguage(previousClient)));}}
                        Require(FileHash(file)==expected,"이전 모드가 직접 수정되어 자동 정리를 멈췄어요: "+Path.GetFileName(name));gameFile(name,null);
                    }
                }
                foreach(var retired in Arr(Get(c,"retiredFiles"))){string name=Str(retired,"path"),file=Child(Game,name);if(!currentPaths.Contains(name)&&File.Exists(file)&&FileHash(file)==Str(retired,"sha256"))gameFile(name,null);}
                gameText("managed-mods.json",Serialize(managed));
                string shader=Str(c,"shaderPack"),shaderSettings="shaderpacks/"+shader+".txt";
                if(!File.Exists(Child(Game,"config/iris.properties")))gameText("config/iris.properties","enableShaders=true\nshaderPack="+shader+"\n");
                if(!File.Exists(Child(Game,shaderSettings)))gameText(shaderSettings,Str(c,"shaderOptions"));
                if(!File.Exists(Child(Game,"config/imblocker.json")))gameText("config/imblocker.json","{\"basicSettings\":{\"englishStateImpl\":\"CONVERSION_STATUS\",\"primaryEnglishState\":\"CJK\"},\"advanceSettings\":{\"commandPrefixRegex\":\"(?!)\"}}");
                string options=existing("options.txt");if(options.Length==0)options="lang:ko_kr\nrenderDistance:10\nsimulationDistance:6\ntutorialStep:none\n";
                var match=Regex.Match(options,@"(?m)^resourcePacks:(.*)$");var chosen=match.Success?Arr(Parse(match.Groups[1].Value)).Select(x=>x as string).ToList():new List<string>{"vanilla"};
                Require(chosen.All(x=>x!=null),"게임 리소스팩 설정을 읽을 수 없어요. options.txt를 확인해 주세요.");
                if(!chosen.Contains("file/moko-ui.zip")){chosen.Add("file/moko-ui.zip");string line="resourcePacks:"+Serialize(chosen);options=match.Success?Regex.Replace(options,@"(?m)^resourcePacks:.*$",x=>line):options.TrimEnd()+"\n"+line+"\n";}gameText("options.txt",options);
                if(!File.Exists(Child(Game,"servers.dat")))gameFile("servers.dat",ServerList(server));
                string cfg=Child(PrismData,"prismlauncher.cfg");if(!File.Exists(cfg))changes["prism-data/prismlauncher.cfg"]=Utf8.GetBytes(Ini("",new Dictionary<string,string>{{"Language","ko"},{"ApplicationTheme","dark"},{"IconTheme","pe_colored"},{"AutomaticJavaSwitch","true"},{"AutomaticJavaDownload","true"},{"UserAskedAboutAutomaticJavaDownload","true"},{"MinMemAlloc","1024"},{"MaxMemAlloc","4096"},{"SelectedInstance",InstanceId}}));
                changes["prism-data/translations/mmc_ko.qm"]=Resource("mmc_ko.qm");
                string instanceCfg=Child(Instance,"instance.cfg");string config=File.Exists(instanceCfg)?File.ReadAllText(instanceCfg,Utf8):"";
                var defaults=new Dictionary<string,string>{{"InstanceType","OneSix"},{"name","뫀코코 마을"},{"iconKey","default"}};
                if(config.Length==0){defaults["OverrideMemory"]="true";defaults["MinMemAlloc"]="1024";defaults["MaxMemAlloc"]="4096";defaults["OverrideJavaLocation"]="false";defaults["OverrideCommands"]="false";}
                changes["prism-data/instances/"+InstanceId+"/instance.cfg"]=Utf8.GetBytes(Ini(config,defaults));
                changes["prism-data/instances/"+InstanceId+"/mmc-pack.json"]=Utf8.GetBytes(Serialize(new Dictionary<string,object>{{"formatVersion",1},{"components",componentList}}));
                if(live)Cached(Num(m,"schema")==1?patch+"/resourcepack.zip":Str(Get(m,"pack"),"url"),Get(m,"pack"),".zip");
                changes["verified-manifest.json"]=bytes;changes["verified-manifest.sig"]=Utf8.GetBytes(signature);changes["installed-patch.json"]=bytes;
                Require(!GameRunning(),"파일을 받는 동안 Minecraft가 실행됐어요. 게임을 종료한 뒤 다시 패치해 주세요.");
                progress("검증한 패치를 적용하고 있어요…");Apply(Data,changes);
                AtomicWrite(Child(Data,"last-check.json"),Utf8.GetBytes(Serialize(new{status="verified",revision=Str(m,"revision"),live=live,utc=DateTime.UtcNow.ToString("o")})));
                progress("패치 점검을 마쳤어요.");return exe;
            }
        }
        public static string Quote(string s) { Require(s.IndexOf('"')<0&&s.IndexOf('\n')<0&&s.IndexOf('\r')<0,"실행 경로가 올바르지 않아요.");return "\""+s.TrimEnd('\\')+"\""; }
        public void OpenPrism(string exe,string mode,string server) {
            Require(!GameRunning(),"이 접속기로 실행한 Minecraft가 이미 열려 있어요.");
            string args="--dir "+Quote(PrismData)+" --show-window";
            if(mode=="Play")args+=" --launch "+InstanceId+" --server "+ServerAddress(server);
            // Only the verified official launcher is invoked. Credentials remain entirely in Prism.
            var psi=new ProcessStartInfo(exe,args){WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=false,WindowStyle=ProcessWindowStyle.Normal};
            using(var p=Process.Start(psi))Require(p!=null,"Prism을 열지 못했어요.");
        }
    }
}
