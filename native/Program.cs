using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly:AssemblyTitle("뫀코코의 마을 접속기")]
[assembly:AssemblyProduct("뫀코코의 마을")]
[assembly:AssemblyVersion("3.0.2.0")]
[assembly:AssemblyFileVersion("3.0.2.0")]
[assembly:AssemblyDescription("검증된 패치를 직접 적용하는 Windows 접속기")]
namespace MokoLauncher {
    public static class Program {
        public static string Friendly(Exception e) {
            if(e is LauncherError)return e.Message;
            if(e is WebException)return "파일을 내려받지 못했어요. 인터넷 연결과 서버 상태를 확인해 주세요.";
            if(e is UnauthorizedAccessException)return "이 폴더에 쓸 수 없어요. 문서나 바탕 화면의 새 폴더에 ZIP을 풀어 주세요.";
            if(e is IOException)return "파일이 사용 중이거나 저장할 수 없어요. 이 접속기의 게임을 닫고 디스크 공간을 확인해 주세요.";
            return "준비 중 오류가 발생했어요. 파일을 바꾸지 말고 운영자에게 알려 주세요. ("+e.GetType().Name+")";
        }
        [STAThread] public static int Main(string[] args) {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            if(args.Length==4&&args[0]=="--complete-launcher-update"){
                try{LauncherUpdates.Complete(args[1],args[2],Int32.Parse(args[3]));return 0;}
                catch(Exception e){MessageBox.Show(Friendly(e),"접속기 업데이트",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
            }
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string data=Path.Combine(root,"data");bool prepare=false;string server=null,patch=null;
            try {
                for(int i=0;i<args.Length;i++){
                    if(args[i]=="--prepare-only"){prepare=true;continue;}
                    if(i+1>=args.Length)throw new LauncherError("실행 옵션의 값이 빠져 있어요.");
                    switch(args[i]){case "--data-dir":data=Path.GetFullPath(args[++i]);break;case "--server":server=args[++i];break;case "--patch":patch=args[++i];break;default:throw new LauncherError("지원하지 않는 접속기 실행 옵션이에요.");}
                }
                Engine.NoLinks(data);Directory.CreateDirectory(data);
                object defaults=Engine.Parse(Encoding.UTF8.GetString(Engine.Resource("launcher-defaults.json")));
                string settingsFile=Engine.Child(data,"client-settings.json");
                object settings=File.Exists(settingsFile)?Engine.Parse(File.ReadAllText(settingsFile,Encoding.UTF8)):defaults;
                server=server??GitHubDistribution.MigrateServerAddress(Engine.Str(settings,"serverAddress"),Engine.Str(defaults,"serverAddress"));
                if(patch==null)patch=GitHubDistribution.MigratePatchAddress(Engine.Str(settings,"patchUrl"),Engine.Str(defaults,"legacyPatchUrl"));
                Engine.ServerAddress(server);Engine.PatchAddress(patch);
                using(var mutex=new Mutex(false,"Local\\MokoLauncher2-"+Engine.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToLowerInvariant())).Substring(0,24))){
                    bool acquired;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(!acquired)throw new LauncherError("같은 폴더의 접속기가 이미 열려 있어요.");
                    try {
                        if(prepare){var engine=new Engine(root,data,delegate{});engine.Prepare(server,patch,false);WriteResult(data,true,"패치 점검 완료");return 0;}
                        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                        using(var form=new MainWindow(Path.Combine(root,"assets"),data)){
                            form.Text="뫀코코의 마을 · 접속기 "+GitHubDistribution.CurrentVersion;form.ServerInput.Text=server;form.PatchInput.Text=patch;
                            bool busy=false;
                            Action<bool> enabled=on=>{foreach(var button in new[]{form.PlayButton,form.LoginButton,form.InstallButton,form.SettingsButton,form.SaveButton})button.Enabled=on;form.ServerInput.Enabled=on;form.PatchInput.Enabled=on;};
                            Action save=()=>{
                                Engine.ServerAddress(form.ServerInput.Text);Engine.PatchAddress(form.PatchInput.Text);
                                Engine.AtomicWrite(settingsFile,Encoding.UTF8.GetBytes(Engine.Serialize(new{serverAddress=form.ServerInput.Text.Trim(),patchUrl=form.PatchInput.Text.Trim().TrimEnd('/')})));
                            };
                            Action<string> run=async mode=>{
                                if(busy)return;
                                try {
                                    save();string address=form.ServerInput.Text,patchUrl=form.PatchInput.Text;
                                    busy=true;enabled(false);form.SettingsOpen=false;form.SetMode("Patching","마을에 갈 준비를 하고 있어요","패치 서명과 필요한 파일을 확인합니다.");
                                    var engine=new Engine(root,data,msg=>{if(!form.IsDisposed&&form.IsHandleCreated)form.BeginInvoke((MethodInvoker)(()=>{if(!form.IsDisposed)form.SetMessage(msg);}));});
                                    string launcherStage=await Task.Run(()=>LauncherUpdates.Stage(root,msg=>{if(!form.IsDisposed&&form.IsHandleCreated)form.BeginInvoke((MethodInvoker)(()=>form.SetMessage(msg)));}));
                                    if(launcherStage!=null){LauncherUpdates.Handoff(root,launcherStage);busy=false;form.Close();return;}
                                    string exe=await Task.Run(()=>engine.Prepare(address,patchUrl,mode=="Play"));
                                    if(mode!="Install")engine.OpenPrism(exe,mode,address);
                                    if(mode=="Play")form.SetMode("Launching","게임 실행을 요청했어요","첫 실행은 Prism에서 계정을 연결해 주세요. 이후에는 게임 시작을 누르면 돼요.");
                                    else if(mode=="Login")form.SetMode("Idle","공식 Prism을 열었어요","Prism 오른쪽 위에서 Microsoft 계정을 연결한 뒤 게임 시작을 눌러 주세요.");
                                    else form.SetMode("Complete","패치 점검을 마쳤어요","처음이라면 계정 연결부터, 준비됐다면 게임 시작을 눌러 주세요.");
                                }catch(Exception e){form.SetMode("Error","잠깐, 확인이 필요해요",Friendly(e));}
                                finally{busy=false;enabled(true);}
                            };
                            form.PlayButton.Click+=(s,e)=>run("Play");form.LoginButton.Click+=(s,e)=>run("Login");form.InstallButton.Click+=(s,e)=>run("Install");
                            form.SettingsButton.Click+=(s,e)=>form.SettingsOpen=!form.SettingsOpen;
                            form.SaveButton.Click+=(s,e)=>{try{save();form.SettingsOpen=false;form.SetMode("Idle","주소를 저장했어요","게임 시작을 누르면 저장한 주소로 연결합니다.");}catch(Exception ex){form.SetMode("Error","주소를 확인해 주세요",Friendly(ex));}};
                            form.FormClosing+=(s,e)=>{if(busy){e.Cancel=true;form.SetMessage("파일 준비가 끝나면 접속기를 닫을 수 있어요.");}};
                            Application.Run(form);
                        }
                    }finally{mutex.ReleaseMutex();}
                }return 0;
            }catch(Exception e){
                if(prepare){try{WriteResult(data,false,Friendly(e));}catch{}return 1;}
                MessageBox.Show(Friendly(e),"뫀코코의 마을 접속기",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;
            }
        }
        static void WriteResult(string data,bool ok,string message){Engine.AtomicWrite(Engine.Child(data,"prepare-result.json"),Encoding.UTF8.GetBytes(Engine.Serialize(new{success=ok,message=message,utc=DateTime.UtcNow.ToString("o")})));}
    }
}
