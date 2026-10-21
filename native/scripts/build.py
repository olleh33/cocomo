"""Reproducible local/Windows CI source build. No account files are read."""
from pathlib import Path
import os,sys,json,hashlib,subprocess,shutil
N=Path(__file__).resolve().parents[1];out=N/'bin';out.mkdir(exist_ok=True)
dotnet=shutil.which('dotnet') or 'C:/Program Files/dotnet/dotnet.exe'
sdks=subprocess.check_output([dotnet,'--list-sdks'],text=True).splitlines()
chosen=next((x for x in sdks if x.startswith('10.0.401 ')),None)
assert chosen,'Build requires .NET SDK 10.0.401 for reproducibility'
sdk=Path(chosen.split('[',1)[1].rstrip(']'))/'10.0.401'
compiler=sdk/'Roslyn/bincore/csc.dll';framework=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319'
refs=['mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem','System.Management','System.Xml']
sources=[N/x for x in ['Engine.cs','Program.cs','Launcher.Visuals.cs','GitHubDistribution.cs','LauncherUpdates.cs']]
common=[dotnet,str(compiler),'/nologo','/platform:x64','/deterministic+','/optimize+','/debug-','/nostdlib+','/utf8output','/langversion:latest','/pathmap:'+str(N)+'=/_/native']
common+=['/reference:'+str(framework/(name+'.dll')) for name in refs]
resources=['/resource:'+str(p)+','+p.name for p in sorted((N/'resources').iterdir()) if p.is_file()]
cmd=common+['/target:winexe','/win32manifest:'+str(N/'app.manifest'),'/win32icon:'+str(N/'assets/app.ico')]+resources+[str(p) for p in sources]
exe=out/'뫀코코접속기.exe';subprocess.run(cmd+['/out:'+str(exe)],check=True)
check=out/'repro/뫀코코접속기.exe';check.parent.mkdir(exist_ok=True);subprocess.run(cmd+['/out:'+str(check)],check=True)
assert exe.read_bytes()==check.read_bytes()
test=out/'LauncherTests.exe'
subprocess.run(common+['/target:exe','/out:'+str(test),'/reference:'+str(exe),str(N/'EngineTests.cs')],check=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
report={'source_build':True,'reproducible':True,'sha256':sha(exe),'bytes':exe.stat().st_size,'source_hashes':{p.name:sha(p) for p in sources},'compiler_sha256':sha(compiler),'authenticode_signed':False,'defender':'not_run_local_user_previously_skipped_defender'}
(out/'build-review.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({'source_build':True,'reproducible':True,'bytes':exe.stat().st_size,'sha256':sha(exe)}))
