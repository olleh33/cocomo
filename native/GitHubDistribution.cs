using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MokoLauncher {
    public static class GitHubDistribution {
        public const string Repository = "olleh33/cocomo";
        public const string Latest = "https://github.com/"+Repository+"/releases/latest/download";
        public const string CurrentVersion = "3.0.2";
        public const string ServerDomain = "모코코.웹.한국";
        public static string MigrateServerAddress(string saved,string current) {
            string address=Engine.ServerAddress(saved);
            string previous=Engine.Host("뫀코코.kro.kr");
            return address.Equals(previous,StringComparison.OrdinalIgnoreCase)||address.Equals(previous+":25565",StringComparison.OrdinalIgnoreCase)?current:saved;
        }
        public static string MigratePatchAddress(string saved,string legacy) {
            return Engine.PatchAddress(saved).Equals(Engine.PatchAddress(legacy),StringComparison.OrdinalIgnoreCase)?Latest:saved;
        }
        public static bool TrustedAsset(string value) {
            Uri u;
            return Uri.TryCreate(value,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host=="github.com"&&u.IsDefaultPort&&u.UserInfo.Length==0&&u.Query.Length==0&&u.Fragment.Length==0
                &&Regex.IsMatch(u.AbsolutePath,"\\A/"+Regex.Escape(Repository)+"/releases/download/moko-v[0-9]+\\.[0-9]+\\.[0-9]+/[A-Za-z0-9._-]+\\z");
        }
        public static void CheckAsset(object item,long maximum) {
            Engine.Require(TrustedAsset(Engine.Str(item,"url")),"우리 서버의 GitHub 배포 주소가 아니에요.");
            Engine.Require(Engine.IsHash(Engine.Str(item,"sha256"))&&Engine.Num(item,"bytes")>0&&Engine.Num(item,"bytes")<=maximum,"GitHub 배포 파일의 크기나 해시가 올바르지 않아요.");
        }
        public static object VerifySigned(byte[] bytes,string signature,int maximum) {
            Engine.Require(bytes.Length<=maximum,"업데이트 안내 파일이 너무 커요.");
            try {using(var rsa=new RSACryptoServiceProvider()) {rsa.PersistKeyInCsp=false;rsa.FromXmlString(Encoding.UTF8.GetString(Engine.Resource("publisher-public.xml")));
                Engine.Require(rsa.VerifyData(bytes,"SHA256",Convert.FromBase64String(signature.Trim())),"업데이트 서명 확인에 실패했어요. 기존 파일을 유지해요.");}}
            catch(FormatException){throw new LauncherError("업데이트 서명 형식이 올바르지 않아요.");}
            return Engine.Parse(Encoding.UTF8.GetString(bytes));
        }
        public static object ValidateUpdate(byte[] bytes,string signature) {
            var value=VerifySigned(bytes,signature,16384);
            Engine.Require(Engine.Num(value,"schema")==1,"지원하지 않는 접속기 업데이트예요.");
            Version version;Engine.Require(Regex.IsMatch(Engine.Str(value,"version"),@"\A\d{1,3}\.\d{1,3}\.\d{1,3}\z")&&Version.TryParse(Engine.Str(value,"version"),out version),"접속기 버전 정보가 올바르지 않아요.");
            CheckAsset(Engine.Get(value,"package"),20971520);return value;
        }
    }
}
