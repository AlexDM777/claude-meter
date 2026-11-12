using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
namespace ClaudeMeter {
public static class CustomSkins {
    public static string Folder(string data){return Path.Combine(data,"UserSkins");}
    static string Required(Dictionary<string,object> map,string key){string text=Json.Str(map,key);if(string.IsNullOrWhiteSpace(text))throw new Exception("Отсутствует поле: "+key);return text;}
    static string Color(Dictionary<string,object> map,string key){string value=Required(map,key);if(!Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$"))throw new Exception("Ожидается цвет #RRGGBB: "+key);return value;}
    static Skin Read(string descriptor){
        if(new FileInfo(descriptor).Length>65536)throw new Exception("Файл skin.json слишком большой.");
        var map=Json.Read(File.ReadAllText(descriptor,Encoding.UTF8));
        if(Json.Str(map,"format")!="claudemeter-skin-v1"&&Json.Str(map,"format")!="codexmeter-skin-v1")throw new Exception("Неподдерживаемый формат скина.");
        string id=Required(map,"id"),name=Required(map,"name");
        if(!Regex.IsMatch(id,"^[a-z0-9][a-z0-9-]{0,47}$"))throw new Exception("ID: только строчные латинские буквы, цифры и дефис, до 48 символов.");
        if(name.Length>60||name.Any(char.IsControl))throw new Exception("Название скина должно содержать от 1 до 60 обычных символов.");
        string filename=Json.Str(map,"background");if(filename!="background.png"&&filename!="background.webp")throw new Exception("Фон: background.webp или background.png рядом с skin.json.");
        string image=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(descriptor)),filename);
        if(!File.Exists(image)||new FileInfo(image).Length>20*1024*1024)throw new Exception("Размер фона не более 20 МБ.");
        var frame=SkinImage.Load(image);if(frame.PixelWidth!=frame.PixelHeight||frame.PixelWidth<512||frame.PixelWidth>2048)throw new Exception("Нужен квадратный фон от 512 до 2048 пикселей.");
        string font=Json.Str(map,"font");if(string.IsNullOrEmpty(font))font="digital";
        if(!new[]{"digital","sans","serif","mono"}.Contains(font))throw new Exception("Шрифт: digital, sans, serif или mono.");
        if(!(Json.Get(map,"dark") is bool))throw new Exception("Поле dark должно быть true или false.");
        int number=0;object stored=Json.Get(map,"number");if(stored!=null&&(!int.TryParse(Convert.ToString(stored),out number)||number<17))throw new Exception("Номер пользовательского скина должен быть не меньше 17.");
        return new Skin{Id="user."+id,Name=name,Kind="space",Background=image,Ink=Color(map,"ink"),Fill=Color(map,"fill"),Accent=Color(map,"accent"),Number=number,IsCustom=true,IsDark=(bool)Json.Get(map,"dark"),Font=font};
    }
    static object Metadata(Skin skin,int number){return new{format="claudemeter-skin-v1",id=skin.Id.Substring(5),name=skin.Name,background=Path.GetFileName(skin.Background),ink=skin.Ink,fill=skin.Fill,accent=skin.Accent,dark=skin.IsDark,font=skin.Font,number=number};}
    public static List<Skin> Load(string data,out List<string> errors){
        var result=new List<Skin>();errors=new List<string>();string folder=Folder(data);if(!Directory.Exists(folder))return result;
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string directory in Directory.GetDirectories(folder).Where(p=>!Path.GetFileName(p).StartsWith(".")).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)){
            try{var skin=Read(Path.Combine(directory,"skin.json"));if(!ids.Add(skin.Id))throw new Exception("Повторяющийся ID скина.");result.Add(skin);}catch(Exception e){errors.Add(Path.GetFileName(directory)+": "+e.Message);}
        }
        var used=new HashSet<int>();int next=Math.Max(16,result.Select(s=>s.Number).DefaultIfEmpty(16).Max())+1;
        foreach(var skin in result){
            if(skin.Number<17||!used.Add(skin.Number)){
                while(used.Contains(next))next++;skin.Number=next++;used.Add(skin.Number);
                // Persist assigned numbers for folders added manually; no image or user label is changed.
                try{File.WriteAllText(Path.Combine(Path.GetDirectoryName(skin.Background),"skin.json"),Json.Write(Metadata(skin,skin.Number)),Encoding.UTF8);}catch(Exception e){errors.Add(skin.Name+": "+e.Message);}
            }
        }
        return result.OrderBy(s=>s.Number).ToList();
    }
    public static Skin Import(string descriptor,string data){
        var skin=Read(Path.GetFullPath(descriptor));List<string> errors;var installed=Load(data,out errors);
        if(installed.Any(s=>s.Id==skin.Id))throw new Exception("Скин с таким ID уже есть. Используйте другой ID.");
        int number=Math.Max(16,installed.Select(s=>s.Number).DefaultIfEmpty(16).Max())+1;
        string folder=Folder(data);Directory.CreateDirectory(folder);string target=Path.Combine(folder,skin.Id.Substring(5));
        if(Directory.Exists(target))throw new Exception("Папка с таким ID уже существует. Используйте другой ID.");
        string stage=Path.Combine(folder,".import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
            // Decode/re-encode the validated image rather than carrying external metadata into the library.
            SkinImage.SaveWebP(SkinImage.Load(skin.Background),Path.Combine(stage,"background.webp"));
            skin.Background=Path.Combine(stage,"background.webp");
            File.WriteAllText(Path.Combine(stage,"skin.json"),Json.Write(Metadata(skin,number)),Encoding.UTF8);
            Read(Path.Combine(stage,"skin.json"));Directory.Move(stage,target);return Read(Path.Combine(target,"skin.json"));
        }finally{if(Directory.Exists(stage)){foreach(string file in new[]{"background.webp","background.png","skin.json"}){string path=Path.Combine(stage,file);if(File.Exists(path))File.Delete(path);}if(!Directory.EnumerateFileSystemEntries(stage).Any())Directory.Delete(stage);}}
    }
    public static string Guide(){using(var stream=typeof(CustomSkins).Assembly.GetManifestResourceStream("ClaudeMeter.SkinGuide.md"))using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}
    public static void SelfTest(string root){
        string task=Path.Combine(Path.GetTempPath(),"ClaudeMeter-custom-test-"+Guid.NewGuid().ToString("N")),source=Path.Combine(task,"source"),data=Path.Combine(task,"data");Directory.CreateDirectory(source);
        var fixture=SkinImage.Load(Path.Combine(root,"skins","beskar.webp"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(fixture));using(var output=File.Create(Path.Combine(source,"background.png")))encoder.Save(output);
        var map=Json.Read("{\"format\":\"claudemeter-skin-v1\",\"id\":\"sample\",\"name\":\"Sample\",\"background\":\"background.png\",\"ink\":\"#E6F4FF\",\"fill\":\"#14202A\",\"accent\":\"#69CAFF\",\"dark\":true,\"font\":\"digital\"}");
        string file=Path.Combine(source,"skin.json");File.WriteAllText(file,Json.Write(map));var imported=Import(file,data);List<string> errors;var list=Load(data,out errors);
        if(errors.Count!=0||list.Count!=1||imported.Number!=17||list[0].Id!="user.sample"||!list[0].IsCustom)throw new Exception("Custom import test failed");
        bool rejected=false;try{Import(file,data);}catch{rejected=true;}if(!rejected)throw new Exception("Duplicate import overwrote an existing skin");
        foreach(var change in new[]{new[]{"id","../outside"},new[]{"background","../background.png"},new[]{"accent","not-a-color"},new[]{"font","script"}}){var bad=new Dictionary<string,object>(map);bad[change[0]]=change[1];File.WriteAllText(file,Json.Write(bad));rejected=false;try{Import(file,data);}catch{rejected=true;}if(!rejected)throw new Exception("Invalid custom skin accepted: "+change[0]);}
        File.WriteAllText(file,Json.Write(map));File.WriteAllText(Path.Combine(source,"background.png"),"not a PNG");rejected=false;try{Import(file,data);}catch{rejected=true;}if(!rejected)throw new Exception("Invalid PNG accepted");
        string broken=Path.Combine(Folder(data),"broken");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"skin.json"),"{}");list=Load(data,out errors);if(list.Count!=1||errors.Count!=1)throw new Exception("Invalid manual skin disabled valid library");
        if(!Guide().Contains("claudemeter-skin-v1"))throw new Exception("Embedded skin guide missing");
        // Test artifacts stay isolated in the temporary test directory.
    }
}
}
