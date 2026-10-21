using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MokoLauncher;
class EngineTests {
    static List<string> passed=new List<string>();
    static void Test(string name,Action body){body();passed.Add(name);}
    static void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
    static void Reject(Action body){try{body();}catch(LauncherError){return;}throw new Exception("Expected rejection");}
    static byte[] B(string s){return Encoding.UTF8.GetBytes(s);}
    static byte[] Zip(params string[] paths){using(var ms=new MemoryStream()){using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true)){foreach(var p in paths){using(var s=zip.CreateEntry(p).Open()){var b=B("fixture");s.Write(b,0,b.Length);}}}return ms.ToArray();}}
    static int Main(string[] args){string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);try {
        string m=Encoding.UTF8.GetString(Engine.Resource("bundled-manifest.json")),sig=Encoding.UTF8.GetString(Engine.Resource("bundled-manifest.sig"));
        Test("embedded_manifest_signature",()=>Engine.VerifyManifest(B(m),sig));
        Test("modified_manifest_rejected",()=>Reject(()=>Engine.VerifyManifest(B(m+" "),sig)));
        Test("invalid_signature_rejected",()=>Reject(()=>Engine.VerifyManifest(B(m),"not-base64")));
        Test("oversized_manifest_rejected",()=>Reject(()=>Engine.VerifyManifest(new byte[131073],sig)));
        Test("korean_domain_idna",()=>Assert(Engine.ServerAddress("뫀코코.kro.kr:25565").StartsWith("xn--")));
        string currentServer=GitHubDistribution.ServerDomain+":25565";
        Test("new_domain_exact_idna",()=>Assert(Engine.ServerAddress(currentServer)=="xn--v52b219aa.xn--yq5b.xn--3e0b707e:25565"));
        Test("old_server_unicode_migrates",()=>Assert(GitHubDistribution.MigrateServerAddress("뫀코코.kro.kr:25565",currentServer)==currentServer));
        Test("old_server_ascii_migrates",()=>Assert(GitHubDistribution.MigrateServerAddress("xn--k62bi09aa.kro.kr:25565",currentServer)==currentServer));
        Test("old_server_implicit_port_migrates",()=>Assert(GitHubDistribution.MigrateServerAddress("뫀코코.kro.kr",currentServer)==currentServer));
        Test("custom_server_is_preserved",()=>Assert(GitHubDistribution.MigrateServerAddress("localhost:25565",currentServer)=="localhost:25565"));
        Test("custom_old_domain_port_is_preserved",()=>Assert(GitHubDistribution.MigrateServerAddress("뫀코코.kro.kr:25576",currentServer)=="뫀코코.kro.kr:25576"));
        Test("new_server_is_idempotent",()=>Assert(GitHubDistribution.MigrateServerAddress(currentServer,currentServer)==currentServer));
        Test("patch_http_signature_transport",()=>Assert(Engine.PatchAddress("http://localhost:8163/")=="http://localhost:8163"));
        Test("old_default_patch_address_migrates_to_github",()=>Assert(GitHubDistribution.MigratePatchAddress("http://example.com:8163/","http://example.com:8163")==GitHubDistribution.Latest));
        Test("custom_patch_address_is_preserved",()=>Assert(GitHubDistribution.MigratePatchAddress("http://localhost:8163","http://example.com:8163")=="http://localhost:8163"));
        Test("github_patch_address_is_idempotent",()=>Assert(GitHubDistribution.MigratePatchAddress(GitHubDistribution.Latest,"http://example.com:8163")==GitHubDistribution.Latest));
        foreach(string bad in new[]{"host:0","host:65536","host --help","-host","host/path","host\"","host`x"}){string s=bad;Test("server_reject_"+passed.Count,()=>Reject(()=>Engine.ServerAddress(s)));}
        foreach(string bad in new[]{"../outside","C:/outside","a/../../outside","a:ads","a/CON.txt","a/b.","a/b ","a//b"}){string s=bad;Test("path_reject_"+passed.Count,()=>Reject(()=>Engine.Child(root,s)));}
        Test("valid_path",()=>Assert(Engine.Child(root,"mods/normal.jar").StartsWith(root)));
        foreach(string bad in new[]{"../bad.jar","mods/../../bad.jar","mods/a.exe"}){string s=bad;Test("manifest_path_reject_"+passed.Count,()=>{var o=Engine.Parse(m);var c=Engine.Get(o,"client");var f=Engine.Arr(Engine.Get(c,"files")).First();Engine.Obj(f)["path"]=s;Reject(()=>Engine.ValidateManifest(o));});}
        Test("manifest_untrusted_host",()=>{var o=Engine.Parse(m);Engine.Obj(Engine.Arr(Engine.Get(Engine.Get(o,"client"),"files")).First())["url"]="https://cdn.modrinth.com.evil.invalid/data/a.jar";Reject(()=>Engine.ValidateManifest(o));});
        Test("manifest_duplicate_path",()=>{var o=Engine.Parse(m);var fs=Engine.Arr(Engine.Get(Engine.Get(o,"client"),"files")).ToArray();Engine.Obj(fs[1])["path"]=Engine.Str(fs[0],"path");Reject(()=>Engine.ValidateManifest(o));});
        Test("manifest_bad_payload_hash",()=>{var o=Engine.Parse(m);Engine.Obj(Engine.Get(Engine.Get(o,"client"),"villageKeys"))["sha256"]="invalid";Reject(()=>Engine.ValidateManifest(o));});
        Test("manifest_bad_translation_hash",()=>{var o=Engine.Parse(m);Engine.Obj(Engine.Get(Engine.Get(o,"client"),"shaderLanguageFile"))["sha256"]="invalid";Reject(()=>Engine.ValidateManifest(o));});
        Test("external_manifest_has_no_embedded_mod_or_translation",()=>{var c=Engine.Get(Engine.Parse(m),"client");Assert(!Engine.Obj(Engine.Get(c,"villageKeys")).ContainsKey("payload")&&!Engine.Obj(c).ContainsKey("shaderLanguage")&&B(m).Length<8192);});
        Test("larger_mod_does_not_bloat_manifest",()=>{var o=Engine.Parse(m);Engine.Obj(Engine.Get(Engine.Get(o,"client"),"villageKeys"))["bytes"]=1000000;Engine.ValidateManifest(o);Assert(B(Engine.Serialize(o)).Length<8192);});
        foreach(string url in new[]{"https://github.com.evil.invalid/olleh33/cocomo/releases/download/moko-v3.0.0/a.jar","https://github.com/other/cocomo/releases/download/moko-v3.0.0/a.jar","https://github.com/olleh33/cocomo/releases/download/moko-v3.0.0/a.jar?token=bad","https://user@github.com/olleh33/cocomo/releases/download/moko-v3.0.0/a.jar","http://github.com/olleh33/cocomo/releases/download/moko-v3.0.0/a.jar","https://github.com/olleh33/cocomo/releases/download/moko-v3.0.0/a%2fb.jar"}){string u=url;Test("github_asset_rejected_"+passed.Count,()=>Assert(!GitHubDistribution.TrustedAsset(u)));}
        Test("pinned_release_asset_accepted",()=>Assert(GitHubDistribution.TrustedAsset("https://github.com/olleh33/cocomo/releases/download/moko-v3.0.0/MokoKeys-1.3.1.jar")));
        Test("missing_mod_version_rejected",()=>{var o=Engine.Parse(m);Engine.Obj(Engine.Get(Engine.Get(o,"client"),"villageKeys")).Remove("version");Reject(()=>Engine.ValidateManifest(o));});
        if(args.Length>1)Test("legacy_v2_signed_manifest_remains_readable",()=>Engine.VerifyManifest(File.ReadAllBytes(args[1]),File.ReadAllText(args[2])));
        Test("zip_slip_rejected",()=>Reject(()=>Engine.Extract(Zip("../escape.txt"),Path.Combine(root,"zip-slip"))));
        Test("zip_duplicate_case_rejected",()=>Reject(()=>Engine.Extract(Zip("one.txt","ONE.txt"),Path.Combine(root,"zip-duplicate"))));
        Test("zip_valid",()=>{Engine.Extract(Zip("inner/file.txt"),Path.Combine(root,"zip-valid"));Assert(File.ReadAllText(Path.Combine(root,"zip-valid/inner/file.txt"))=="fixture");});
        Test("ini_keeps_unrelated_settings",()=>{var value=Engine.Ini("[General]\nName=old\nCustom=user\n",new Dictionary<string,string>{{"Name","new"}});Assert(value.Contains("Name=new")&&value.Contains("Custom=user"));});
        Test("accounts_never_managed",()=>{Assert(!Engine.ManagedTarget("prism-data/accounts.json"));Reject(()=>Engine.Apply(root,new Dictionary<string,byte[]>{{"prism-data/accounts.json",B("bad")}}));});
        Test("arbitrary_personal_file_never_managed",()=>Assert(!Engine.ManagedTarget("prism-data/instances/friends-village/.minecraft/saves/world/level.dat")));
        string tx=Path.Combine(root,"transaction"),p="prism-data/instances/friends-village/.minecraft/options.txt",q="prism-data/instances/friends-village/.minecraft/config/iris.properties";
        Directory.CreateDirectory(tx);Engine.AtomicWrite(Engine.Child(tx,p),B("old"));
        Test("transaction_backup_and_apply",()=>{Engine.Apply(tx,new Dictionary<string,byte[]>{{p,B("new")},{q,B("first")}});Assert(File.ReadAllText(Engine.Child(tx,p))=="new");Assert(Directory.GetFiles(Path.Combine(tx,"backups"),"*.bak",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f)=="old"));Assert(!File.Exists(Engine.Child(tx,"pending-patch.json")));});
        Test("unchanged_patch_no_backup",()=>{int n=Directory.GetDirectories(Path.Combine(tx,"backups")).Length;Engine.Apply(tx,new Dictionary<string,byte[]>{{p,B("new")}});Assert(Directory.GetDirectories(Path.Combine(tx,"backups")).Length==n);});
        Test("failed_transaction_rolls_back",()=>{using(var hold=new FileStream(Engine.Child(tx,q),FileMode.Open,FileAccess.Read,FileShare.Read)){bool failed=false;try{Engine.Apply(tx,new Dictionary<string,byte[]>{{p,B("bad")},{q,B("bad")}});}catch(IOException){failed=true;}Assert(failed);Assert(File.ReadAllText(Engine.Child(tx,p))=="new");Assert(File.ReadAllText(Engine.Child(tx,q))=="first");}});
        Test("interrupted_transaction_recovers",()=>{string backup="backups/patch-fixture/original";Engine.AtomicWrite(Engine.Child(tx,backup),B("restored"));Engine.AtomicWrite(Engine.Child(tx,"pending-patch.json"),B(Engine.Serialize(new[]{new{path=p,existed=true,backup=backup,sha256=Engine.Hash(B("restored"))},new{path=q,existed=false,backup="",sha256=""}})));Engine.Recover(tx);Assert(File.ReadAllText(Engine.Child(tx,p))=="restored"&&!File.Exists(Engine.Child(tx,q)));});
        Test("corrupt_recovery_backup_rejected",()=>{string backup="backups/patch-corrupt/original";Engine.AtomicWrite(Engine.Child(tx,backup),B("wrong"));Engine.AtomicWrite(Engine.Child(tx,"pending-patch.json"),B(Engine.Serialize(new[]{new{path=p,existed=true,backup=backup,sha256=Engine.Hash(B("right"))}})));Reject(()=>Engine.Recover(tx));Assert(File.ReadAllText(Engine.Child(tx,p))=="restored");});
        string[] packageNames={"뫀코코접속기.exe","사용방법.txt","구성-출처.txt","Prism-Translations-LICENSE.txt","assets/app.ico","assets/icon.png","assets/background.gif","assets/downloading.gif","assets/connecting.png"};
        Test("launcher_package_disallows_account_paths",()=>Reject(()=>LauncherUpdates.ReadPackage(Zip(packageNames.Concat(new[]{"data/prism-data/accounts.json"}).ToArray()))));
        Test("launcher_package_disallows_traversal",()=>Reject(()=>LauncherUpdates.ReadPackage(Zip(packageNames.Concat(new[]{"../outside"}).ToArray()))));
        Test("launcher_package_disallows_duplicates",()=>Reject(()=>LauncherUpdates.ReadPackage(Zip(packageNames.Concat(new[]{"assets/app.ico"}).ToArray()))));
        Test("launcher_package_requires_all_files",()=>Reject(()=>LauncherUpdates.ReadPackage(Zip("뫀코코접속기.exe"))));
        string app=Path.Combine(root,"app");Engine.AtomicWrite(Engine.Child(app,"data/prism-data/accounts.json"),B("fake-account-fixture"));Engine.AtomicWrite(Engine.Child(app,"data/keys.txt"),B("keep-hotkeys"));Engine.AtomicWrite(Engine.Child(app,"뫀코코접속기.exe"),B("old-launcher-fixture"));
        Test("launcher_upgrade_keeps_accounts_and_hotkeys",()=>{LauncherUpdates.ApplyPackage(app,Zip(packageNames));Assert(File.ReadAllText(Engine.Child(app,"data/prism-data/accounts.json"))=="fake-account-fixture"&&File.ReadAllText(Engine.Child(app,"data/keys.txt"))=="keep-hotkeys");Assert(Directory.EnumerateFiles(Engine.Child(app,".launcher-backup"),"*.exe",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f)=="old-launcher-fixture"));});
        Test("launcher_upgrade_rolls_back_on_locked_file",()=>{Engine.AtomicWrite(Engine.Child(app,"뫀코코접속기.exe"),B("restore-this"));Engine.AtomicWrite(Engine.Child(app,"assets/icon.png"),B("locked-old-icon"));using(var held=new FileStream(Engine.Child(app,"assets/icon.png"),FileMode.Open,FileAccess.Read,FileShare.Read)){bool failed=false;try{LauncherUpdates.ApplyPackage(app,Zip(packageNames));}catch(IOException){failed=true;}Assert(failed&&File.ReadAllText(Engine.Child(app,"뫀코코접속기.exe"))=="restore-this");}});
        File.WriteAllText(Path.Combine(root,"result.json"),Engine.Serialize(new{success=true,count=passed.Count,tests=passed}),Encoding.UTF8);return 0;
    }catch(Exception e){File.WriteAllText(Path.Combine(root,"result.json"),Engine.Serialize(new{success=false,count=passed.Count,tests=passed,error=e.ToString()}),Encoding.UTF8);return 1;}}
}
