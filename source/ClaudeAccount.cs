using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
// Read-only access to the Claude Code sign-in. ClaudeMeter never writes or refreshes
// the credentials file: Claude Code owns it and renews the token itself.
public sealed class ClaudeLogin {
    public string AccessToken="",Plan="";public long ExpiresAtMs;
    public bool Expired { get { return ExpiresAtMs>0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()>=ExpiresAtMs; } }
}
public sealed class ClaudeAuthException : Exception { public ClaudeAuthException(string message):base(message){} }
public static class ClaudeAccount {
    public const string UsageUrl="https://api.anthropic.com/api/oauth/usage";
    public const string SetupUrl="https://code.claude.com/docs/en/setup";
    public const string NotFound="Вход в Claude Code не найден. Установите Claude Code и войдите в аккаунт Claude.";
    public const string Expired="Вход в Claude Code устарел. Запустите Claude Code, чтобы обновить его.";
    static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
    public static List<string> Candidates(string data) {
        var candidates=new List<string>();
        try { string preference=Path.Combine(data,"credentials-path.txt");if(File.Exists(preference))candidates.Add(File.ReadAllText(preference).Trim()); } catch {}
        string custom=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if(!string.IsNullOrWhiteSpace(custom))candidates.Add(Path.Combine(custom.Trim().Trim('"'),".credentials.json"));
        candidates.Add(Path.Combine(Home,".claude",".credentials.json"));
        return candidates;
    }
    public static string Find(string data) {
        return Candidates(data).FirstOrDefault(p=>{try{return !string.IsNullOrWhiteSpace(p)&&Path.IsPathRooted(p)&&File.Exists(p);}catch{return false;}});
    }
    public static string Require(string data) {
        string path=Find(data);
        if(path==null)throw new ClaudeAuthException(NotFound);
        return path;
    }
    public static ClaudeLogin Parse(string json) {
        Dictionary<string,object> root;
        try { root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json); } catch { root=null; }
        object value=null;if(root!=null)root.TryGetValue("claudeAiOauth",out value);
        var oauth=value as Dictionary<string,object>;
        if(oauth==null)throw new ClaudeAuthException("В этом файле нет входа в аккаунт Claude. Войдите в Claude Code через аккаунт Claude.");
        var login=new ClaudeLogin{AccessToken=Text(oauth,"accessToken"),Plan=Text(oauth,"subscriptionType")};
        object expires;if(oauth.TryGetValue("expiresAt",out expires)&&expires!=null){try{login.ExpiresAtMs=Convert.ToInt64(expires,System.Globalization.CultureInfo.InvariantCulture);}catch{}}
        if(login.AccessToken.Length==0)throw new ClaudeAuthException("В этом файле нет входа в аккаунт Claude. Войдите в Claude Code через аккаунт Claude.");
        return login;
    }
    public static ClaudeLogin Read(string path) { return Parse(File.ReadAllText(path,Encoding.UTF8)); }
    static string Text(Dictionary<string,object> map,string key){object value;return map.TryGetValue(key,out value)&&value!=null?Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture):"";}
    // Best effort: Claude Code keeps the account e-mail in ~/.claude.json. Display only.
    public static string Email() {
        try {
            string path=Path.Combine(Home,".claude.json");if(!File.Exists(path)||new FileInfo(path).Length>16*1024*1024)return "";
            var root=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));
            object value;if(root==null||!root.TryGetValue("oauthAccount",out value))return "";
            var account=value as Dictionary<string,object>;return account==null?"":Text(account,"emailAddress");
        } catch { return ""; }
    }
    public static string CliPath() {
        var candidates=new List<string>{Path.Combine(Home,".local","bin","claude.exe")};
        foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
            try { candidates.Add(Path.Combine(dir.Trim('"'),"claude.exe")); } catch {}
        }
        return candidates.FirstOrDefault(p=>{try{return Path.IsPathRooted(p)&&File.Exists(p);}catch{return false;}});
    }
    public static bool NearExpiry(ClaudeLogin login) { return login.ExpiresAtMs>0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()>=login.ExpiresAtMs-10*60*1000; }
    // Asks Claude Code itself to renew the sign-in (it owns the file; we only read it afterwards).
    // Step 1: "auth status" (no model request). Step 2, only if still not renewed: one tiny "-p" request on the smallest model.
    public static bool Renew(string path) {
        string cli=CliPath();if(cli==null)return false;
        long before=0;try{before=Read(path).ExpiresAtMs;}catch{}
        foreach(string args in new[]{"auth status","-p ok --model haiku --no-session-persistence"}) {
            try {
                var info=new System.Diagnostics.ProcessStartInfo(cli,args){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,WorkingDirectory=Path.GetTempPath()};
                using(var p=System.Diagnostics.Process.Start(info)) {
                    p.StandardInput.Close();p.BeginOutputReadLine();p.BeginErrorReadLine();
                    if(!p.WaitForExit(90000)){try{p.Kill();}catch{}}
                }
                var now=Read(path);if(now.ExpiresAtMs>before||!now.Expired&&!NearExpiry(now))return true;
            } catch {}
        }
        return false;
    }
    // One GET to Anthropic with the token Claude Code already stored. Nothing else leaves the machine.
    public static string Usage(string token,string version) {
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(UsageUrl);
        request.Method="GET";request.Timeout=25000;request.ReadWriteTimeout=25000;request.AllowAutoRedirect=false;
        request.Accept="application/json";request.UserAgent="ClaudeMeter/"+version;
        request.Headers["Authorization"]="Bearer "+token;request.Headers["anthropic-beta"]="oauth-2025-04-20";
        try {
            using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return reader.ReadToEnd();
        } catch(WebException e) {
            var response=e.Response as HttpWebResponse;
            if(response!=null){int code=(int)response.StatusCode;response.Close();if(code==401||code==403)throw new ClaudeAuthException(Expired);if(code==429)throw new Exception("Claude временно ограничил запросы. Повторите позже.");throw new Exception("Claude ответил ошибкой "+code);}
            throw new Exception("Нет связи с Claude");
        }
    }
}
