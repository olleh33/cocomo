"""Fetch exact reviewed release bytes; signatures bind executable and game assets."""
from pathlib import Path
import base64,json,hashlib,urllib.request,urllib.parse,xml.etree.ElementTree as ET,zipfile,subprocess,shutil
N=Path(__file__).resolve().parents[1];OUT=N/'bin/release';OUT.mkdir(parents=True,exist_ok=True)
record=json.loads((N/'release-input.json').read_text(encoding='utf8'));public=ET.fromstring((N/'resources/publisher-public.xml').read_text())
integer=lambda name:int.from_bytes(base64.b64decode(public.findtext(name)),'big')
def verify(data,sig):
 raw=base64.b64decode(sig,validate=True);modulus=integer('Modulus');k=(modulus.bit_length()+7)//8
 assert len(raw)==k and int.from_bytes(raw,'big')<modulus
 block=pow(int.from_bytes(raw,'big'),integer('Exponent'),modulus).to_bytes(k,'big');digest=bytes.fromhex('3031300d060960864801650304020105000420')+hashlib.sha256(data).digest()
 assert block==b'\x00\x01'+b'\xff'*(k-len(digest)-3)+b'\x00'+digest,'Invalid publisher signature'
for name,source in [('manifest.json',N/'resources/bundled-manifest.json'),('manifest.sig',N/'resources/bundled-manifest.sig'),('launcher-update.json',N/'release-metadata/launcher-update.json'),('launcher-update.sig',N/'release-metadata/launcher-update.sig')]:shutil.copy2(source,OUT/name)
for name in ['manifest','launcher-update']:verify((OUT/(name+'.json')).read_bytes(),(OUT/(name+'.sig')).read_bytes())
for asset in record['assets']:
 assert asset['name']==Path(asset['name']).name and '/' not in asset['name']
 if 'local_file' in asset:
  p=(N/asset['local_file']).resolve();assert p.is_relative_to((N/'release-assets').resolve());b=p.read_bytes()
 else:
  source=urllib.parse.urlsplit(asset['source']);assert not source.username and not source.password
  host=source.hostname.encode('idna').decode()+(':'+str(source.port) if source.port else '')
  url=urllib.parse.urlunsplit((source.scheme,host,source.path,source.query,source.fragment))
  req=urllib.request.Request(url,headers={'User-Agent':'MokoLauncherRelease/3.0'})
  with urllib.request.urlopen(req,timeout=120) as response:b=response.read(asset['bytes']+1)
 assert len(b)==asset['bytes'] and hashlib.sha256(b).hexdigest()==asset['sha256'],asset['name']
 (OUT/asset['name']).write_bytes(b)
m=json.loads((OUT/'manifest.json').read_bytes())
for d in [m['pack'],m['client']['ui'],m['client']['villageKeys'],m['client']['shaderLanguageFile']]:
 p=OUT/d['url'].rsplit('/',1)[1];assert p.stat().st_size==d['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==d['sha256']
u=json.loads((OUT/'launcher-update.json').read_bytes())['package'];archive=OUT/u['url'].rsplit('/',1)[1]
assert archive.stat().st_size==u['bytes'] and hashlib.sha256(archive.read_bytes()).hexdigest()==u['sha256']
test=N/('bin/release-binary-test-'+u['sha256'][:12]);test.mkdir(exist_ok=True)
with zipfile.ZipFile(archive) as z:
 allowed={'뫀코코접속기.exe','사용방법.txt','구성-출처.txt','Prism-Translations-LICENSE.txt','assets/app.ico','assets/icon.png','assets/background.gif','assets/downloading.gif','assets/connecting.png'}
 assert set(z.namelist())==allowed and len(z.namelist())==len(allowed)
 z.extractall(test)
proof=json.loads((N/'release-metadata/verification.json').read_text());assert hashlib.sha256((test/'뫀코코접속기.exe').read_bytes()).hexdigest()==proof['exe_sha256']
shutil.copy2(N/'bin/LauncherTests.exe',test/'LauncherTests.exe');subprocess.run([str(test/'LauncherTests.exe'),str(test/'results')],check=True)
print('Exact packaged launcher passed runtime tests; all release asset hashes and publisher signatures verified.')
