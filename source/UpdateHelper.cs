using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Web.Script.Serialization;
namespace ClaudeMeter {
static class UpdateHelper {
    static Process StartApp(UpdateJob job){return Process.Start(new ProcessStartInfo(Path.Combine(job.target,"ClaudeMeter.exe"),"--data-dir \""+job.profile+"\""){UseShellExecute=true,WorkingDirectory=job.target});}
    [STAThread] static int Main(string[] args){UpdateJob job=null;try{if(args.Length!=1)throw new Exception("Missing update job");job=new JavaScriptSerializer().Deserialize<UpdateJob>(File.ReadAllText(args[0]));UpdatePackage.Validate(job.stage,job.version);try{using(var parent=Process.GetProcessById(job.processId)){if(!parent.WaitForExit(60000))throw new Exception("Close ClaudeMeter to finish updating");}}catch(ArgumentException){}UpdatePackage.Apply(job.stage,job.target,job.version);StartApp(job);File.WriteAllText(Path.Combine(Path.GetDirectoryName(job.stage),"result.txt"),"PASS "+job.version);return 0;}catch(Exception e){if(job!=null){File.WriteAllText(Path.Combine(Path.GetDirectoryName(job.stage),"result.txt"),"ERROR "+e.Message);try{StartApp(job);}catch{}}MessageBox.Show("Не удалось обновить ClaudeMeter. Предыдущая версия сохранена.\n"+e.Message,"ClaudeMeter",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}}
}
}
