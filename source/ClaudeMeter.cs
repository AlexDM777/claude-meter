using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Security.Cryptography;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace ClaudeMeter {
public static class Json {
    public static string Write(object value) { return new JavaScriptSerializer().Serialize(value); }
    public static Dictionary<string,object> Read(string text) { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text); }
    public static Dictionary<string,object> Map(object value) { return value as Dictionary<string,object>; }
    public static object Get(Dictionary<string,object> map,string key) { object value; return map!=null && map.TryGetValue(key,out value)?value:null; }
    public static string Str(Dictionary<string,object> map,string key) { return Convert.ToString(Get(map,key),CultureInfo.InvariantCulture); }
}
public class Quota {
    public double Remaining; public int Minutes; public long Reset;
    // Claude reports used percent ("utilization", or "percent" in the limits list) and an ISO reset time.
    public static Quota Parse(object value,int minutes) {
        var map=Json.Map(value); object raw=Json.Get(map,"utilization")??Json.Get(map,"percent"); if(raw==null) return null;
        double used; try { used=Convert.ToDouble(raw,CultureInfo.InvariantCulture); } catch { return null; }
        if(double.IsNaN(used)||double.IsInfinity(used)) return null;
        return new Quota { Remaining=Math.Max(0,Math.Min(100,100-used)), Minutes=minutes, Reset=Time(Json.Get(map,"resets_at")) };
    }
    public static long Time(object value) { DateTimeOffset time; return value!=null&&DateTimeOffset.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out time)?time.ToUnixTimeSeconds():0; }
    public string Label(string fallback) { return Minutes==300?Lang.T("5 часов"):Minutes==10080?Lang.T("Неделя"):Minutes==1440?Lang.T("Сутки"):Minutes>0?(Minutes%60==0?(Minutes/60)+" "+Lang.T("ч"):Minutes+" "+Lang.T("мин")):Lang.T(fallback); }
    public string Countdown { get { if(Reset<=0)return "—"; var left=DateTimeOffset.FromUnixTimeSeconds(Reset)-DateTimeOffset.Now; if(left.TotalSeconds<=0)return Lang.T("Обновляется"); return (left.Days>0?left.Days+Lang.T("д")+" ":"")+left.Hours+Lang.T("ч")+" "+left.Minutes+Lang.T("м"); } }
    public string ResetText { get { return Reset>0 ? DateTimeOffset.FromUnixTimeSeconds(Reset).ToLocalTime().ToString("dd.MM HH:mm") : "--.-- --:--"; } }
}
public class Limits {
    public Quota Primary,Secondary;
    // CreditBalance: extra usage spent beyond the plan. ModelName/ModelValue: the tightest per-model weekly limit.
    public string PlanType="", CreditBalance="", ModelName="", ModelValue=""; public int? ResetCount;
    public string CreditLine { get { return string.IsNullOrWhiteSpace(CreditBalance)?"":Lang.T("Доп. расходы")+": "+CreditBalance; } }
    public string ExtraLabel { get { return ModelName.Length>0?ModelName+" · "+Lang.T("Неделя"):ResetCount.HasValue?Lang.T("Доступно сбросов"):""; } }
    public string ExtraValue { get { return ModelName.Length>0?ModelValue:ResetCount.HasValue?ResetCount.Value.ToString(CultureInfo.InvariantCulture):""; } }
    public string ResetLine { get { return ExtraLabel.Length>0?ExtraLabel+": "+ExtraValue:""; } }
    public string Footer { get { return string.Join("   ·   ",new[]{ResetLine,CreditLine}.Where(v=>v.Length>0)); } }
    public string WindowTitle(Quota q) { return (q==null?Lang.T("Лимит"):q.Label("Лимит"))+(string.IsNullOrEmpty(PlanType)?"":"  ·  "+PlanLabel); }
    public List<Quota> Windows { get { return new[]{Primary,Secondary}.Where(q=>q!=null).OrderBy(q=>q.Minutes>0?q.Minutes:int.MaxValue).ToList(); } }
    // Claude plans are stored as "claude_<plan>" and use the wide two-window layout.
    // The bare "pro*" names remain only for the square layout used by previews and skin thumbnails.
    public bool Square { get { return PlanType.StartsWith("pro",StringComparison.OrdinalIgnoreCase); } }
    public string PlanLabel { get { string plan=PlanType.ToLowerInvariant(); if(plan.StartsWith("claude_"))return "CLAUDE "+plan.Substring(7).ToUpperInvariant(); switch(plan) { case "plus":return "PLUS"; case "prolite":return "PRO $100"; default:return string.IsNullOrEmpty(PlanType)?"PLAN UNKNOWN":PlanType.ToUpperInvariant(); } } }
    public static Limits Parse(Dictionary<string,object> data) {
        var result=new Limits { Primary=Quota.Parse(Json.Get(data,"five_hour"),300), Secondary=Quota.Parse(Json.Get(data,"seven_day"),10080) };
        var rows=Json.Get(data,"limits") as System.Collections.IEnumerable; double tightest=double.MaxValue;
        if(rows!=null && !(rows is string)) foreach(var row in rows) {
            var item=Json.Map(row); string kind=Json.Str(item,"kind");
            if(kind=="session") { if(result.Primary==null)result.Primary=Quota.Parse(item,300); }
            else if(kind=="weekly_all") { if(result.Secondary==null)result.Secondary=Quota.Parse(item,10080); }
            else if(kind=="weekly_scoped") {
                var quota=Quota.Parse(item,10080); string name=Json.Str(Json.Map(Json.Get(Json.Map(Json.Get(item,"scope")),"model")),"display_name");
                if(quota!=null && name.Length>0 && quota.Remaining<tightest) { tightest=quota.Remaining; result.ModelName=name; result.ModelValue=Math.Round(quota.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)+"%"; }
            }
        }
        var extra=Json.Map(Json.Get(data,"extra_usage"));
        try {
            if(extra!=null && Convert.ToBoolean(Json.Get(extra,"is_enabled")??false) && Json.Get(extra,"used_credits")!=null) {
                decimal used=Convert.ToDecimal(Json.Get(extra,"used_credits"),CultureInfo.InvariantCulture); int places=Math.Max(0,Math.Min(6,Convert.ToInt32(Json.Get(extra,"decimal_places")??2)));
                decimal divisor=1; for(int i=0;i<places;i++)divisor*=10; string currency=Json.Str(extra,"currency"), amount=(used/divisor).ToString("F"+places,CultureInfo.InvariantCulture);
                result.CreditBalance=currency=="USD"||currency.Length==0?"$"+amount:amount+" "+currency;
            }
        } catch {}
        return result;
    }
}
public class Skin {
    public string Id,Name,Kind,Background,Ink,Fill,Accent,Font;public int Number;public bool IsCustom,IsDark;
    public static List<Skin> Load(string root) {
        var results=new List<Skin>();
        var manifest=Json.Read(File.ReadAllText(Path.Combine(root,"skins","skins.json"),Encoding.UTF8));
        var rows=Json.Get(manifest,"skins") as System.Collections.IEnumerable;
        if(rows==null) throw new Exception("Некорректный файл skins/skins.json");
        foreach(var row in rows) {
            var m=Json.Map(row); string kind=Json.Str(m,"kind"); if(kind!="lcd" && kind!="analog" && kind!="glass" && kind!="space") continue;
            string filename=Json.Str(m,"background");
            if(Path.GetFileName(filename)!=filename) throw new Exception("Фон скина должен находиться в папке skins");
            results.Add(new Skin {Id=Json.Str(m,"id"),Name=Json.Str(m,"name"),Kind=kind,Background=Path.Combine(root,"skins",filename),Ink=Json.Str(m,"ink"),Fill=Json.Str(m,"fill"),Accent=Json.Str(m,"accent"),Number=Convert.ToInt32(Json.Get(m,"number")??results.Count+1)});
        }
        if(results.Count==0) throw new Exception("Скины не найдены"); return results;
    }
}
public partial class MeterFace : FrameworkElement {
    public int SelectedMinutes=0; public Quota SmallQuota {get{return SelectedMinutes==0?TrayDisplay.Selected(Limits):Limits.Windows.FirstOrDefault(q=>q.Minutes==SelectedMinutes);}}
    public Skin Skin; public Limits Limits=new Limits(); public string Status=""; public bool Stale; public double OpacityValue=1;
    private BitmapSource background;
    public void SetSkin(Skin s) { ResetNumericAnimation();Skin=s;background=SkinImage.Load(s.Background);InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc) {base.OnRender(dc);BeginNumericFrame();try{DrawFace(dc,ActualWidth,ActualHeight);}finally{EndNumericFrame();}}
    void DrawFace(DrawingContext dc,double width,double height) {
        if(Skin==null)return;
        if(Skin.Kind=="space"){DrawSpace(dc,width,height);return;}
        if(Skin.Kind=="glass"){DrawGlass(dc,width,height);return;}
        if(width<=160) {
            dc.PushTransform(new ScaleTransform(width/100,height/100));
            dc.PushClip(new RectangleGeometry(new Rect(0,0,100,100),8,8));
            BitmapSource miniBackground=Skin.Kind=="analog"?new CroppedBitmap(background,new Int32Rect(0,0,background.PixelWidth/2,background.PixelHeight)):(BitmapSource)background;
            dc.DrawImage(miniBackground,new Rect(0,0,100,100));
            Brush miniInk=WarningInk(SmallQuota,(Brush)new BrushConverter().ConvertFromString(Skin.Ink));
            var miniQuota=SmallQuota;
            string value=miniQuota==null?"--":Math.Round(miniQuota.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
            double w=value.Length==3?20:27,h=48,gap=5,total=value.Length*w+(value.Length-1)*gap,x=(100-total)/2;
            dc.PushOpacity(Stale?.48:1);foreach(char c in value){Digit(dc,c,x,26,w,h,miniInk);x+=w+gap;}dc.Pop();
            dc.Pop();dc.Pop();return;
        }
        if(width<=240) {
            dc.PushTransform(new ScaleTransform(width/200,height/200));
            dc.PushClip(new RectangleGeometry(new Rect(0,0,200,200),10,10));
            BitmapSource compactBackground=Skin.Kind=="analog"?new CroppedBitmap(background,new Int32Rect(0,0,background.PixelWidth/2,background.PixelHeight)):(BitmapSource)background;
            dc.DrawImage(compactBackground,new Rect(0,0,200,200));
            Brush compactInk=WarningInk(SmallQuota,(Brush)new BrushConverter().ConvertFromString(Skin.Ink));
            var q=SmallQuota;
            dc.PushOpacity(Stale?.48:1);
            for(int i=0;i<10;i++) {
                var rect=new Rect(20+i*16.3,25,12.7,20);
                dc.PushOpacity(.25);dc.DrawRoundedRectangle(null,new Pen(compactInk,1),rect,1.5,1.5);dc.Pop();
                double fill=q==null?0:Math.Max(0,Math.Min(1,q.Remaining/10-i));
                if(fill>0){dc.PushClip(new RectangleGeometry(new Rect(rect.X,rect.Y,rect.Width*fill,rect.Height)));dc.DrawRoundedRectangle(compactInk,null,rect,1.5,1.5);dc.Pop();}
            }
            string value=q==null?"--":Math.Round(q.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
            double w=value.Length==3?40:54,h=96,gap=10,total=value.Length*w+(value.Length-1)*gap,x=(200-total)/2;
            foreach(char c in value){Digit(dc,c,x,73,w,h,compactInk);x+=w+gap;}
            dc.Pop();dc.Pop();dc.Pop();return;
        }
        if(Limits.Square){DrawSquare(dc,width,height);return;}
        dc.PushTransform(new ScaleTransform(width/1000,height/450));
        dc.PushClip(new RectangleGeometry(new Rect(0,0,1000,450),12,12));
        if(Skin.Kind=="analog" && Limits.Windows.Count!=2) {
            var panel=new CroppedBitmap(background,new Int32Rect(0,0,background.PixelWidth/2,background.PixelHeight));
            dc.DrawImage(panel,new Rect(0,0,1000,450));
        } else dc.DrawImage(background,new Rect(0,0,1000,450));
        Brush ink=(Brush)new BrushConverter().ConvertFromString(Skin.Ink);
        dc.PushOpacity(Stale?0.48:1);
        if(Limits.Windows.Count!=2)Fit(dc,string.IsNullOrEmpty(Limits.PlanType)?"":Limits.PlanLabel,500,17,23,860,ink);
        var windows=Limits.Windows;
        if(windows.Count==0) {
            Text(dc,"—",500,125,110,ink,"Consolas",true);
            Text(dc,Lang.T("Лимиты пока недоступны"),500,282,30,ink,"Segoe UI",true);
        } else {
            if(windows.Count==2)dc.DrawLine(new Pen(ink,1.2),new Point(500,48),new Point(500,407));
            for(int i=0;i<windows.Count;i++) {
                dc.PushTransform(new TranslateTransform(windows.Count==1?250:i*500,0));
                dc.PushTransform(new TranslateTransform(0,45));
                dc.PushTransform(new ScaleTransform(1,.82));
                if(Skin.Kind=="analog")Analog(dc,0,windows[i],"LIMIT",ink);
                else Lcd(dc,0,windows[i],"LIMIT",10,ink);
                dc.Pop();dc.Pop();dc.Pop();
                double cx=windows.Count==1?500:i*500+250;
                if(windows.Count==2){Fit(dc,string.IsNullOrEmpty(Limits.PlanType)?"":Limits.PlanLabel,cx,17,23,425,ink);Fit(dc,Limits.Footer,cx,398,18,425,ink);}
                if(Skin.Kind!="analog")Text(dc,Lang.T("Осталось"),cx,315,16,ink,"Segoe UI",true);
                Text(dc,windows[i].ResetText,cx,375,16,ink,"Consolas",true);
            }
        }
        if(windows.Count!=2)Fit(dc,Limits.Footer,500,398,18,850,ink);
        dc.Pop();
        if(!string.IsNullOrEmpty(Status)) {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(224,23,34,37)),null,new Rect(20,417,960,27),5,5);
            Text(dc,Status,500,419,16,Brushes.White,"Segoe UI",true);
        }
        dc.Pop(); dc.Pop();
    }
    void DrawSquare(DrawingContext dc,double width,double height) {
        dc.PushTransform(new ScaleTransform(width/500,height/500));
        dc.PushClip(new RectangleGeometry(new Rect(0,0,500,500),14,14));
        var panel=Skin.Kind=="analog"?new CroppedBitmap(background,new Int32Rect(0,0,background.PixelWidth/2,background.PixelHeight)):(BitmapSource)background;
        dc.DrawImage(panel,new Rect(0,0,500,500));
        Brush ink=(Brush)new BrushConverter().ConvertFromString(Skin.Ink);
        dc.PushOpacity(Stale?.48:1);
        var windows=Limits.Windows;
        if(windows.Count==0)Text(dc,Lang.T("Лимиты пока недоступны"),250,225,24,ink,"Segoe UI",true);
        for(int i=0;i<windows.Count;i++) {
            bool pair=windows.Count==2;
            double scale=pair?.48:.84;
            double x=pair?10+i*250:40,y=pair?108:57;
            dc.PushTransform(new TranslateTransform(x,y));dc.PushTransform(new ScaleTransform(scale,scale));
            if(Skin.Kind=="analog")Analog(dc,0,windows[i],"LIMIT",ink);else Lcd(dc,0,windows[i],"LIMIT",10,ink);
            dc.Pop();dc.Pop();
            double cx=pair?130+i*250:250;
            Text(dc,windows[i].ResetText,cx,pair?324:416,14,ink,"Consolas",true);
        }
        Fit(dc,string.IsNullOrEmpty(Limits.PlanType)?"":Limits.PlanLabel,250,28,24,400,ink);
        Fit(dc,Limits.Footer,250,443,18,400,ink);
        dc.Pop();
        if(!string.IsNullOrEmpty(Status)) {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235,23,34,37)),null,new Rect(12,470,476,24),4,4);
            Text(dc,Status,250,474,12,Brushes.White,"Segoe UI",true);
        }
        dc.Pop();dc.Pop();
    }
    void Text(DrawingContext dc,string text,double x,double y,double size,Brush brush,string font,bool center) {
        var ft=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(font),size,brush,1.0);
        DrawNumericText(dc,text,ft,center?x-ft.Width/2:x,y,font,size,brush,FontWeights.Normal);
    }
    static void Polygon(DrawingContext dc,Brush brush,params Point[] pts) {
        var geometry=new StreamGeometry(); using(var c=geometry.Open()){c.BeginFigure(pts[0],true,true);c.PolyLineTo(pts.Skip(1).ToArray(),true,false);} geometry.Freeze(); dc.DrawGeometry(brush,null,geometry);
    }
    // Seven-segment vector digits remain crisp at any widget size.
    static readonly int[] Masks={63,6,91,79,102,109,125,7,127,111};
    void Digit(DrawingContext dc,char digit,double x,double y,double w,double h,Brush ink){AnimateNumber(dc,digit.ToString(),new Rect(x-1,y-1,w+2,h+2),false,target=>DigitRaw(target,digit,x,y,w,h,ink));}
    static void DigitRaw(DrawingContext dc,char digit,double x,double y,double w,double h,Brush ink) {
        double t=w*.14;
        Point[][] segments={
            new[]{new Point(t,0),new Point(w-t,0),new Point(w,t),new Point(w-t,2*t),new Point(t,2*t),new Point(0,t)},
            new[]{new Point(w,t),new Point(w-2*t,2*t),new Point(w-2*t,h/2-t),new Point(w-t,h/2),new Point(w,h/2-t)},
            new[]{new Point(w-t,h/2),new Point(w-2*t,h/2+t),new Point(w-2*t,h-2*t),new Point(w,h-t),new Point(w,h/2+t)},
            new[]{new Point(t,h-2*t),new Point(w-t,h-2*t),new Point(w,h-t),new Point(w-t,h),new Point(t,h),new Point(0,h-t)},
            new[]{new Point(t,h/2),new Point(0,h/2+t),new Point(0,h-t),new Point(2*t,h-2*t),new Point(2*t,h/2+t)},
            new[]{new Point(0,t),new Point(2*t,2*t),new Point(2*t,h/2-t),new Point(t,h/2),new Point(0,h/2-t)},
            new[]{new Point(t,h/2-t),new Point(w-t,h/2-t),new Point(w,h/2),new Point(w-t,h/2+t),new Point(t,h/2+t),new Point(0,h/2)}
        };
        int mask=digit>='0'&&digit<='9'?Masks[digit-'0']:64;
        dc.PushTransform(new TranslateTransform(x,y));
        for(int i=0;i<7;i++){dc.PushOpacity((mask&(1<<i))!=0?1:.06);Polygon(dc,ink,segments[i]);dc.Pop();}dc.Pop();
    }
    static Brush WarningInk(Quota q,Brush normal){return q==null?normal:q.Remaining<10?new SolidColorBrush(Color.FromRgb(177,42,30)):q.Remaining<20?new SolidColorBrush(Color.FromRgb(148,76,0)):normal;}
    public BitmapSource Capture(){int width=Math.Max(1,(int)Math.Round(ActualWidth)),height=Math.Max(1,(int)Math.Round(ActualHeight));var visual=new DrawingVisual();using(var dc=visual.RenderOpen())DrawFace(dc,width,height);var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;}
    void Number(DrawingContext dc,string value,double cx,double y,Brush ink) {
        double w=value.Length>=3?81:101,h=168,gap=16;
        var percent=new FormattedText("%",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),72,ink,1.0);
        double digitsWidth=value.Length*w+(value.Length-1)*gap;
        double x=cx-digitsWidth/2;
        foreach(char c in value){Digit(dc,c,x,y,w,h,ink);x+=w+gap;}
        dc.DrawText(percent,new Point(x,y+69));
    }
    void Lcd(DrawingContext dc,double ox,Quota q,string fallback,int count,Brush ink) {
        double rem=q==null?0:q.Remaining, bw=(434-(count-1)*11)/count;
        for(int i=0;i<count;i++) {
            var r=new Rect(ox+33+i*(bw+11),29,bw,43);
            dc.PushOpacity(.20);dc.DrawRoundedRectangle(null,new Pen(ink,2),r,3,3);dc.Pop();
            double fill=Math.Max(0,Math.Min(1,rem*count/100-i));
            if(q!=null&&fill>0) { dc.PushClip(new RectangleGeometry(new Rect(r.X,r.Y,r.Width*fill,r.Height)));dc.DrawRoundedRectangle(ink,null,r,3,3);dc.Pop(); }
        }
        Text(dc,q==null?fallback:q.Label(fallback),ox+250,91,32,ink,"Consolas",true);
        Number(dc,q==null?"--":Math.Round(q.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),ox+250,149,WarningInk(q,ink));
        Fit(dc,Lang.T("Сброс через")+" "+(q==null?"—":q.Countdown),ox+250,357,32,450,ink);
    }
    static Point ArcPoint(double percent,double cx,double cy,double radius) {
        double angle=(-145+percent*1.10)*Math.PI/180;
        return new Point(cx+Math.Cos(angle)*radius,cy+Math.Sin(angle)*radius);
    }
    static void Curve(DrawingContext dc,double start,double end,double cx,double cy,double radius,Pen pen) {
        var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(ArcPoint(start,cx,cy,radius),false,false);for(double v=start+0.5;v<=end;v+=.5)c.LineTo(ArcPoint(v,cx,cy,radius),true,false);}dc.DrawGeometry(null,pen,g);
    }
    void Analog(DrawingContext dc,double ox,Quota q,string fallback,Brush ink) {
        double cx=ox+250,cy=320;
        Brush red=new SolidColorBrush(Color.FromRgb(168,42,25));
        Fit(dc,q==null?fallback:q.Label(fallback),cx,14,26,420,ink);
        Curve(dc,0,10,cx,cy,220,new Pen(red,7));Curve(dc,10,100,cx,cy,220,new Pen(ink,3));
        for(int i=0;i<=100;i+=5){Brush b=i<10?red:ink;dc.DrawLine(new Pen(b,i%10==0?2.5:1.3),ArcPoint(i,cx,cy,221),ArcPoint(i,cx,cy,i%10==0?201:210));if(i%20==0){Point p=ArcPoint(i,cx,cy,249);Text(dc,i.ToString(),p.X,p.Y-11,21,b,"Georgia",true);}}
        if(q!=null) {
            Point end=ArcPoint(q.Remaining,cx,cy,214);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60,0,0,0)),6),new Point(cx+3,cy+3),new Point(end.X+3,end.Y+3));
            dc.DrawLine(new Pen(q.Remaining<10?red:ink,4.0),new Point(cx,cy),end);
        }
        dc.DrawEllipse(ink,null,new Point(cx,cy),10,10);
        Text(dc,q==null?"--%":Math.Round(q.Remaining,MidpointRounding.AwayFromZero)+"%",cx,272,31,WarningInk(q,ink),"Georgia",true);
        dc.DrawLine(new Pen(ink,.8),new Point(ox+35,351),new Point(ox+465,351));
        Fit(dc,Lang.T("Сброс через")+" "+(q==null?"—":q.Countdown),cx,359,30,440,ink);
    }
    public void Export(string path,int size=0) {
        int width=size>0?size:Limits.Square?500:1000;
        int height=(int)Math.Round(width*Aspect(width));
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())DrawFace(dc,width,height);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        BitmapEncoder encoder=path.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)?(BitmapEncoder)new JpegBitmapEncoder{QualityLevel=90}:new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
    }
}
public class StartupRegistration {
    const string KeyPath="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    readonly string testFile;
    public StartupRegistration(string data,bool systemSettings){if(!systemSettings)testFile=Path.Combine(data,"startup-test.json");}
    public static string Command { get {return "\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --startup";} }
    public bool Enabled {
        get {if(testFile!=null)return File.Exists(testFile);using(var key=Registry.CurrentUser.OpenSubKey(KeyPath))return key!=null && Convert.ToString(key.GetValue("ClaudeMeter"))==Command;}
    }
    public void Set(bool enabled) {
        if(testFile!=null){if(enabled)File.WriteAllText(testFile,Json.Write(new{command=Command}));else if(File.Exists(testFile))File.Delete(testFile);return;}
        using(var key=Registry.CurrentUser.CreateSubKey(KeyPath)){if(enabled)key.SetValue("ClaudeMeter",Command,RegistryValueKind.String);else key.DeleteValue("ClaudeMeter",false);}
    }
}
public class SingleInstance : IDisposable {
    readonly System.Threading.Mutex mutex; readonly System.Threading.EventWaitHandle signal; readonly System.Threading.RegisteredWaitHandle waiter;
    public bool Owner; public event Action ShowRequested;
    public SingleInstance(string data) {
        string name;using(var hash=SHA256.Create())name="Local\\ClaudeMeter."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToUpperInvariant()))).Replace("-","").Substring(0,24);
        bool created;mutex=new System.Threading.Mutex(true,name,out created);Owner=created;
        signal=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,name+".show");
        if(Owner)waiter=System.Threading.ThreadPool.RegisterWaitForSingleObject(signal,delegate(object state,bool timeout){var handler=ShowRequested;if(handler!=null)handler();},null,System.Threading.Timeout.Infinite,false);
    }
    public void ShowExisting(){signal.Set();}
    public void Dispose(){if(waiter!=null)waiter.Unregister(null);signal.Dispose();if(Owner)mutex.ReleaseMutex();mutex.Dispose();}
}
public partial class Widget : Window {
    readonly MeterFace face=new MeterFace(); readonly List<Skin> skins; readonly string data;
    readonly DispatcherTimer updateTimer=new DispatcherTimer();readonly DispatcherTimer firstUpdateTimer=new DispatcherTimer();AppUpdate appUpdate;bool autoUpdate=true;MenuItem updateMenu,autoUpdateMenu;
    readonly DispatcherTimer timer=new DispatcherTimer(); readonly DispatcherTimer ageTimer=new DispatcherTimer();
    bool busy,closed,loggedIn,demo; string accountPlan="",accountName=""; DateTime lastUpdate=DateTime.MinValue; string connectionStatus="Подключение…";
    MenuItem pin; ContextMenu menu; Forms.NotifyIcon tray; System.Drawing.Icon appIcon,trayValueIcon; TrayHover trayHover; string trayIconKey="";
    bool randomSkinOnStartup=false;Skin skinPreviewOriginal;
    bool trayEnabled=true,startHidden=false,startupDesired=true,hasStartupPreference=false,exitRequested=false;
    readonly StartupRegistration startup;List<string> customSkinErrors=new List<string>();
    public Widget(string root,string dataRoot,bool isDemo,bool systemSettings=true) {
        data=dataRoot;demo=isDemo;skins=Skin.Load(root);skins.AddRange(CustomSkins.Load(data,out customSkinErrors));startup=new StartupRegistration(data,systemSettings&&!demo);
        Title="Claude Meter 1.0 Lite";Width=500;Height=225;MinWidth=100;MinHeight=100;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;Content=face;
        Left=Math.Max(0,SystemParameters.WorkArea.Right-Width-28);Top=SystemParameters.WorkArea.Top+28;
        ShowInTaskbar=false;
        face.SetSkin(skins[0]); LoadSettings();if(randomSkinOnStartup&&!demo&&systemSettings){face.SetSkin(SkinOptions.Next(skins,face.Skin.Id,new Random()));Height=Width*face.Aspect(Width);SaveSettings();}
        appIcon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
        Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());Icon.Freeze();
        appUpdate=new AppUpdate(root,data);appUpdate.Changed+=delegate{Ui(UpdateUpdateControls);};EnsureRecovery();BuildMenu();SetupTray();
        if(systemSettings&&!demo){updateTimer.Interval=TimeSpan.FromHours(6);updateTimer.Tick+=async delegate{await CheckAppUpdate(false);};updateTimer.Start();firstUpdateTimer.Interval=TimeSpan.FromSeconds(15);firstUpdateTimer.Tick+=async delegate{firstUpdateTimer.Stop();if(!closed)await CheckAppUpdate(false);};firstUpdateTimer.Start();}
        face.ToolTip=Lang.T("Перетащите мышью • ПКМ: настройки • Колесо: размер");
        MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ClickCount==2){ToggleSize();e.Handled=true;return;}try{DragMove();SaveSettings();}catch{}};
        MouseWheel+=delegate(object sender,MouseWheelEventArgs e){SetWidth(Width+(e.Delta>0?40:-40));};
        Loaded+=async delegate {
            if(!demo){try{if(!hasStartupPreference || startupDesired&&!startup.Enabled){startup.Set(startupDesired);hasStartupPreference=true;SaveSettings();}}catch(Exception e){startupDesired=false;hasStartupPreference=true;SaveSettings();MessageBox.Show(this,"Не удалось включить автозапуск: "+e.Message,"Claude Meter");}}
            if(startHidden&&trayEnabled)Hide();
            if(demo){face.Limits=Program.DemoLimits();Height=Width*face.Aspect(Width);face.Status="ДЕМО — пример значений";face.InvalidateVisual();return;}await Refresh();
        };
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exitRequested&&trayEnabled){e.Cancel=true;HideDisplay();}};
        StateChanged+=delegate{if(WindowState==WindowState.Minimized&&trayEnabled)HideDisplay();};
        Closed+=delegate{if(resizeTimer!=null)resizeTimer.Stop();closed=true;firstUpdateTimer.Stop();updateTimer.Stop();timer.Stop();ageTimer.Stop();SaveSettings();if(tray!=null){tray.Visible=false;tray.Dispose();}if(trayHover!=null)trayHover.Dispose();if(trayValueIcon!=null)trayValueIcon.Dispose();if(appIcon!=null)appIcon.Dispose();};
        timer.Interval=TimeSpan.FromSeconds(120);timer.Tick+=async delegate{await Refresh();};timer.Start();
        ageTimer.Interval=TimeSpan.FromSeconds(10);ageTimer.Tick+=delegate{UpdateStatus();};ageTimer.Start();
    }
    void Ui(Action action){if(!closed)Dispatcher.BeginInvoke(action);}
    void EnsureRecovery(){if(!trayEnabled){ShowInTaskbar=true;startHidden=false;}}
    void SetupTray(){
        if(trayHover!=null){trayHover.Dispose();trayHover=null;}
        if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}
        if(trayValueIcon!=null){trayValueIcon.Dispose();trayValueIcon=null;}trayIconKey="";
        if(!trayEnabled)return;
        tray=new Forms.NotifyIcon{Icon=appIcon,Text="",Visible=true};trayHover=new TrayHover();trayHover.SettingsRequested+=delegate{Ui(OpenTraySettings);};trayHover.UpdateRequested+=delegate{Ui(async delegate{await InstallAppUpdate(true);});};UpdateTrayText();
        tray.MouseMove+=delegate{if(trayHover!=null&&!menu.IsOpen)trayHover.Present();};
        tray.DoubleClick+=delegate{if(trayHover!=null)trayHover.Dismiss();Ui(TrayDoubleClick);};
        tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(trayHover!=null)trayHover.Dismiss();if(e.Button==Forms.MouseButtons.Right)Ui(delegate{menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;menu.PlacementTarget=face;menu.IsOpen=true;});};
    }
    void OpenTraySettings(){
        if(closed)return;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;menu.PlacementTarget=face;menu.IsOpen=true;
        menu.UpdateLayout();var settings=menu.Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Настройки"));settings.IsSubmenuOpen=true;settings.Focus();
    }
    void TrayDoubleClick(){if(resizing)return;if(IsVisible&&Width>=799){HideDisplay();return;}ShowDisplay();if(Width<799){expanded=false;ToggleSize();}}
    public void ShowDisplay(){if(closed)return;Show();WindowState=WindowState.Normal;Activate();}
    void HideDisplay(){if(!trayEnabled)return;menu.IsOpen=false;Hide();}
    public void Quit(){exitRequested=true;Close();}
    void DisplaySkin(Skin skin){face.SetSkin(skin);Height=Width*face.Aspect(Width);UpdateTrayText();}
    void PreviewSkin(Skin skin){if(closed||menu==null||!menu.IsOpen)return;if(skinPreviewOriginal==null)skinPreviewOriginal=face.Skin;if(face.Skin.Id!=skin.Id)DisplaySkin(skin);}
    void CancelSkinPreview(){if(closed||skinPreviewOriginal==null)return;var original=skinPreviewOriginal;skinPreviewOriginal=null;if(face.Skin.Id!=original.Id)DisplaySkin(original);}
    void CommitSkin(Skin skin){skinPreviewOriginal=null;DisplaySkin(skin);SaveSettings();}
    void ReloadCustomSkins(string selected=null){
        string keep=selected??(skinPreviewOriginal??face.Skin).Id;CancelSkinPreview();var loaded=Skin.Load(AppDomain.CurrentDomain.BaseDirectory);loaded.AddRange(CustomSkins.Load(data,out customSkinErrors));skins.Clear();skins.AddRange(loaded);
        face.SetSkin(skins.FirstOrDefault(s=>s.Id==keep)??skins[0]);Height=Width*face.Aspect(Width);menu.IsOpen=false;BuildMenu();SetupTray();SaveSettings();
    }
    FrameworkElement SkinThumbnail(Skin skin){
        var preview=new MeterFace();preview.SetSkin(skin);preview.Limits=Program.DemoLimits();preview.Limits.PlanType="prolite";preview.Limits.Primary=preview.Limits.Secondary;preview.Limits.Secondary=null;preview.Limits.Primary.Remaining=67;preview.Limits.Primary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+86400;
        preview.Measure(new Size(500,500));preview.Arrange(new Rect(0,0,500,500));
        var image=new Image{Source=preview.Capture(),Width=104,Height=104,Stretch=Stretch.Uniform};
        var content=new StackPanel();content.Children.Add(image);content.Children.Add(new TextBlock{Text=skin.Number+". "+Lang.T(skin.Name),FontSize=11,Width=132,MinHeight=32,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Left,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,0)});
        var tile=new Border{Child=content,Padding=new Thickness(1)};System.Windows.Automation.AutomationProperties.SetName(tile,skin.Number+". "+Lang.T(skin.Name));return tile;
    }
    void BuildMenu() {
        CancelSkinPreview();menu=new ContextMenu();menu.Closed+=delegate{CancelSkinPreview();};MenuAppearance.Apply(menu);var themes=new MenuItem{Header="Скин"};themes.ItemsPanel=(ItemsPanelTemplate)System.Windows.Markup.XamlReader.Parse("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><UniformGrid Columns='4'/></ItemsPanelTemplate>");
        foreach(var s in skins){var item=new MenuItem{Header=SkinThumbnail(s),Tag=s.Id,ToolTip=s.Number+". "+Lang.T(s.Name),IsCheckable=true,Padding=new Thickness(5)};item.MouseEnter+=delegate{PreviewSkin(s);};item.GotKeyboardFocus+=delegate{PreviewSkin(s);};item.Click+=delegate{CommitSkin(s);};themes.Items.Add(item);}themes.SubmenuClosed+=delegate{CancelSkinPreview();};menu.Items.Add(themes);
        var sizes=new MenuItem{Header="Размер"};foreach(int size in new[]{100,200,300,500,800,1000}){int value=size;var item=new MenuItem{Header=size.ToString(),Tag=size,IsCheckable=true};item.Click+=delegate{SetWidth(value);};sizes.Items.Add(item);}menu.Items.Add(sizes);
        var quotaChoice=new MenuItem{Header="Показывать лимит"};menu.Items.Add(quotaChoice);
        var screenshot=new MenuItem{Header="Скриншот приложения в буфер"};screenshot.Click+=delegate{try{menu.IsOpen=false;Clipboard.SetImage(face.Capture());}catch(Exception e){MessageBox.Show(this,e.Message,Lang.T("Скриншот"));}};menu.Items.Add(screenshot);
        var transparency=new MenuItem{Header="Прозрачность",ToolTip="20% — едва видимое стекло; 100% — полностью видимое"};foreach(int percent in new[]{20,40,60,80,100}){int value=percent;var item=new MenuItem{Header=percent==100?Lang.T("100% (без прозрачности)"):percent+"% "+Lang.T("видимости"),Tag=percent,IsCheckable=true};item.Click+=delegate{Opacity=value/100.0;SaveSettings();};transparency.Items.Add(item);}menu.Items.Add(transparency);
        pin=new MenuItem{Header="Поверх окон",IsCheckable=true,IsChecked=Topmost};pin.Click+=delegate{Topmost=pin.IsChecked;SaveSettings();};
        var preferences=new MenuItem{Header="Настройки"};menu.Items.Add(pin);
        var skinStartup=new MenuItem{Header="Скин при запуске"};var fixedSkin=new MenuItem{Header="Зафиксировать выбранный скин",IsCheckable=true};var randomSkin=new MenuItem{Header="Каждый запуск — новый скин",IsCheckable=true};fixedSkin.Click+=delegate{randomSkinOnStartup=false;fixedSkin.IsChecked=true;randomSkin.IsChecked=false;SaveSettings();};randomSkin.Click+=delegate{randomSkinOnStartup=true;fixedSkin.IsChecked=false;randomSkin.IsChecked=true;SaveSettings();};skinStartup.Items.Add(fixedSkin);skinStartup.Items.Add(randomSkin);preferences.Items.Add(skinStartup);var custom=new MenuItem{Header="Пользовательские скины"};
        var importSkin=new MenuItem{Header="Загрузить скин…"};importSkin.Click+=delegate{using(var picker=new Forms.OpenFileDialog{Filter="ClaudeMeter skin (skin.json)|*.json",Title=Lang.T("Загрузить скин…")}){if(picker.ShowDialog()!=Forms.DialogResult.OK)return;try{var imported=CustomSkins.Import(picker.FileName,data);ReloadCustomSkins(imported.Id);}catch(Exception e){MessageBox.Show(this,Lang.T(e.Message),Lang.T("Пользовательские скины"));}}};custom.Items.Add(importSkin);
        var copyGuide=new MenuItem{Header="Скопировать MD-инструкцию"};copyGuide.Click+=delegate{try{Clipboard.SetText(CustomSkins.Guide());}catch(Exception e){MessageBox.Show(this,e.Message,Lang.T("Пользовательские скины"));}};custom.Items.Add(copyGuide);
        var skinFolder=new MenuItem{Header="Открыть папку скинов"};skinFolder.Click+=delegate{try{string folder=CustomSkins.Folder(data);Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message,Lang.T("Пользовательские скины"));}};custom.Items.Add(skinFolder);
        var reloadSkins=new MenuItem{Header="Обновить список"};reloadSkins.Click+=delegate{try{ReloadCustomSkins();}catch(Exception e){MessageBox.Show(this,e.Message,Lang.T("Пользовательские скины"));}};custom.Items.Add(reloadSkins);
        if(customSkinErrors.Count>0)custom.Items.Add(new MenuItem{Header=Lang.T("Не загружено")+": "+customSkinErrors.Count,IsEnabled=false,ToolTip=string.Join("\n",customSkinErrors)});
        preferences.Items.Add(custom);
        var planItem=new MenuItem{Header="Тариф",IsEnabled=false};preferences.Items.Add(planItem);preferences.Items.Add(new MenuItem{Header="ClaudeMeter 1.0.1 Lite",IsEnabled=false});
        var languages=new MenuItem{Header="Язык"};foreach(var language in Lang.Names){string code=language.Key;var item=new MenuItem{Header=SkinOptions.Language(code,language.Value),Tag=code,ToolTip=language.Value,IsCheckable=true,IsChecked=Lang.Code==code};item.Click+=delegate{Lang.Code=code;SaveSettings();BuildMenu();UpdateStatus();face.ToolTip=Lang.T("Перетащите мышью • ПКМ: настройки • Колесо: размер");face.InvalidateVisual();UpdateTrayText();};languages.Items.Add(item);}preferences.Items.Add(languages);
        var autorun=new MenuItem{Header="Запускать с Windows",IsCheckable=true};autorun.Click+=delegate{try{startup.Set(autorun.IsChecked);startupDesired=autorun.IsChecked;hasStartupPreference=true;SaveSettings();}catch(Exception e){autorun.IsChecked=startup.Enabled;MessageBox.Show(this,e.Message,Lang.T("Автозапуск"));}};preferences.Items.Add(autorun);
        var trayOption=new MenuItem{Header="Значок в трее",IsCheckable=true};trayOption.Click+=delegate{trayEnabled=trayOption.IsChecked;EnsureRecovery();SetupTray();if(!trayEnabled)ShowDisplay();SaveSettings();};preferences.Items.Add(trayOption);
        var taskbar=new MenuItem{Header="Показывать на панели задач",IsCheckable=true};taskbar.Click+=delegate{ShowInTaskbar=taskbar.IsChecked;SaveSettings();};preferences.Items.Add(taskbar);
        var hiddenOption=new MenuItem{Header="Запускать скрытым в трей",IsCheckable=true};hiddenOption.Click+=delegate{startHidden=hiddenOption.IsChecked;SaveSettings();};preferences.Items.Add(hiddenOption);menu.Items.Add(preferences);
        var show=new MenuItem{Header="Показать дисплей"};show.Click+=delegate{ShowDisplay();};preferences.Items.Add(show);
        var hide=new MenuItem{Header="Скрыть дисплей в трей"};hide.Click+=delegate{HideDisplay();};preferences.Items.Add(hide);
        
        updateMenu=new MenuItem{Header=Lang.T("Проверить обновления программы")};updateMenu.Click+=async delegate{if(appUpdate.Available!=null)await InstallAppUpdate(true);else await CheckAppUpdate(true);};preferences.Items.Add(updateMenu);
        autoUpdateMenu=new MenuItem{Header=Lang.T("Автоматически обновлять программу"),IsCheckable=true,IsChecked=autoUpdate};autoUpdateMenu.Click+=async delegate{autoUpdate=autoUpdateMenu.IsChecked;SaveSettings();if(autoUpdate&&appUpdate.Available!=null)await InstallAppUpdate(false);};preferences.Items.Add(autoUpdateMenu);
        var refresh=new MenuItem{Header="Обновить показатели"};refresh.Click+=async delegate{await Refresh();};preferences.Items.Add(refresh);
        var cli=new MenuItem{Header="Файл входа Claude Code…"};cli.Click+=async delegate{using(var picker=new Forms.OpenFileDialog{Filter="Claude Code (.credentials.json)|*.json",Title=Lang.T("Выберите файл .credentials.json")}){if(picker.ShowDialog()!=Forms.DialogResult.OK)return;try{ClaudeAccount.Read(picker.FileName);File.WriteAllText(Path.Combine(data,"credentials-path.txt"),picker.FileName);await Refresh();}catch(Exception e){MessageBox.Show(this,Lang.T(e.Message),"Claude Code");}}};preferences.Items.Add(cli);
        var accountItem=new MenuItem{Header="Аккаунт: не выполнен вход",IsEnabled=false};preferences.Items.Add(accountItem);
        var login=new MenuItem{Header="Открыть Claude Code…"};login.Click+=delegate{OpenClaudeCode();};preferences.Items.Add(login);

        var help=new MenuItem{Header="Как пользоваться"};help.Click+=delegate{MessageBox.Show(this,Lang.T("Тариф и версия доступны в настройках. Перетащите виджет мышью. Колесо меняет размер. Двойной клик раскрывает виджет; повторный возвращает прежний размер. Через меню можно выбрать лимит, сделать снимок и настроить автозапуск."),"ClaudeMeter 1.0.1 Lite");};preferences.Items.Add(help);
        var quit=new MenuItem{Header=Lang.T("Выход из приложения")};quit.Click+=delegate{Quit();};menu.Items.Add(new Separator());menu.Items.Add(quit);face.ContextMenu=menu;Lang.Menu(menu);
        menu.Opened+=delegate{UpdateUpdateControls();fixedSkin.IsChecked=!randomSkinOnStartup;randomSkin.IsChecked=randomSkinOnStartup;planItem.Header=Lang.T("Тариф")+": "+face.Limits.PlanLabel;
            quotaChoice.Items.Clear();
            var auto=new MenuItem{Header=Lang.T("Авто · неделя, иначе самое длительное окно"),IsCheckable=true,IsChecked=face.SelectedMinutes==0};auto.Click+=delegate{face.SelectedMinutes=0;face.InvalidateVisual();UpdateTrayText();SaveSettings();};quotaChoice.Items.Add(auto);
            var durations=face.Limits.Windows.Select(q=>q.Minutes).Where(m=>m>0).ToList();if(!durations.Contains(300))durations.Add(300);if(!durations.Contains(10080))durations.Add(10080);if(face.SelectedMinutes>0&&!durations.Contains(face.SelectedMinutes))durations.Add(face.SelectedMinutes);
            foreach(int minutes in durations.Distinct().OrderBy(m=>m)){int selected=minutes;bool available=face.Limits.Windows.Any(q=>q.Minutes==minutes);var option=new MenuItem{Header=QuotaName(minutes)+(available?"":" · "+Lang.T("нет данных")),IsCheckable=true,IsChecked=face.SelectedMinutes==minutes};option.Click+=delegate{face.SelectedMinutes=selected;face.InvalidateVisual();UpdateTrayText();SaveSettings();};quotaChoice.Items.Add(option);}
foreach(MenuItem i in themes.Items)i.IsChecked=(string)i.Tag==face.Skin.Id;foreach(MenuItem i in sizes.Items)i.IsChecked=Math.Abs(Width-(int)i.Tag)<.5;foreach(MenuItem i in transparency.Items)i.IsChecked=Math.Abs(Opacity*100-(int)i.Tag)<.5;pin.IsChecked=Topmost;autorun.IsChecked=startup.Enabled;autorun.IsEnabled=!demo;trayOption.IsChecked=trayEnabled;taskbar.IsChecked=ShowInTaskbar;taskbar.IsEnabled=trayEnabled;hiddenOption.IsChecked=startHidden;hiddenOption.IsEnabled=trayEnabled;show.IsEnabled=!IsVisible;hide.IsEnabled=IsVisible&&trayEnabled;login.IsEnabled=!demo;accountItem.Header=loggedIn?Lang.T("Аккаунт")+": "+(string.IsNullOrEmpty(accountName)?Lang.T("имя недоступно"):accountName):Lang.T("Аккаунт")+": "+Lang.T("не выполнен вход");refresh.IsEnabled=!demo&&!busy;Lang.Menu(menu);};
    }
    bool expanded=false,resizing=false;double compactWidth=200,compactLeft,compactTop;DispatcherTimer resizeTimer;
    void ToggleSize(){
        if(resizing)return;
        if(!expanded){compactWidth=Width;compactLeft=Left;compactTop=Top;}
        expanded=!expanded;
        double fromW=Width,fromH=Height,fromX=Left,fromY=Top,targetW=expanded?800:compactWidth;
        double targetH=targetW*face.Aspect(targetW);
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)(Left+Width/2),(int)(Top+Height/2)));
        var source=PresentationSource.FromVisual(this);double dpi=source==null?1:source.CompositionTarget.TransformToDevice.M11;
        var work=screen.WorkingArea;double left=work.Left/dpi,top=work.Top/dpi,right=work.Right/dpi,bottom=work.Bottom/dpi;
        double targetX=expanded?Math.Max(left,Math.Min(Left+Width-targetW,right-targetW)):compactLeft;
        double targetY=expanded?Math.Max(top,Math.Min(Top+Height-targetH,bottom-targetH)):compactTop;
        if(!SystemParameters.ClientAreaAnimation){Width=targetW;Height=targetH;Left=targetX;Top=targetY;SaveSettings();return;}
        resizing=true;var started=System.Diagnostics.Stopwatch.StartNew();resizeTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
        resizeTimer.Tick+=delegate{double t=Math.Min(1,started.Elapsed.TotalMilliseconds/300),ease=1-Math.Pow(1-t,3);Width=fromW+(targetW-fromW)*ease;Height=fromH+(targetH-fromH)*ease;Left=fromX+(targetX-fromX)*ease;Top=fromY+(targetY-fromY)*ease;if(t>=1){resizeTimer.Stop();resizing=false;SaveSettings();}};resizeTimer.Start();
    }
    void SetWidth(double width){if(resizeTimer!=null)resizeTimer.Stop();resizing=false;expanded=false;Width=Math.Max(100,Math.Min(1000,width));Height=Width*face.Aspect(Width);Left=Math.Min(Left,SystemParameters.WorkArea.Right-Width);Top=Math.Min(Top,SystemParameters.WorkArea.Bottom-Height);SaveSettings();}
    static string QuotaName(int minutes){return minutes==10080?Lang.T("Неделя"):minutes==1440?Lang.T("Сутки"):minutes%60==0?(minutes/60)+" "+Lang.T("ч"):minutes+" "+Lang.T("мин");}
    void LoadSettings(){try{var s=Json.Read(File.ReadAllText(Path.Combine(data,"settings.json")));autoUpdate=Convert.ToBoolean(Json.Get(s,"autoUpdate")??true);randomSkinOnStartup=Convert.ToBoolean(Json.Get(s,"randomSkinOnStartup")??false);face.SelectedMinutes=Convert.ToInt32(Json.Get(s,"selectedMinutes")??0);Lang.Code=Json.Str(s,"language");string id=Json.Str(s,"skin");var skin=skins.FirstOrDefault(x=>x.Id==id);if(skin!=null)face.SetSkin(skin);Width=Math.Max(100,Math.Min(1000,Convert.ToDouble(Json.Get(s,"width")??500)));Height=Width*face.Aspect(Width);Opacity=Math.Max(.2,Math.Min(1,Convert.ToDouble(Json.Get(s,"opacity")??1)));Topmost=Convert.ToBoolean(Json.Get(s,"topmost")??true);trayEnabled=Convert.ToBoolean(Json.Get(s,"trayEnabled")??true);ShowInTaskbar=Convert.ToBoolean(Json.Get(s,"showInTaskbar")??false);startHidden=Convert.ToBoolean(Json.Get(s,"startHidden")??false);hasStartupPreference=Json.Get(s,"startupEnabled")!=null;startupDesired=Convert.ToBoolean(Json.Get(s,"startupEnabled")??true);Left=Convert.ToDouble(Json.Get(s,"left")??Left);Top=Convert.ToDouble(Json.Get(s,"top")??Top);if(Left+Width<0||Left>SystemParameters.VirtualScreenWidth||Top+Height<0||Top>SystemParameters.VirtualScreenHeight){Left=30;Top=30;}}catch{}}
    void SaveSettings(){try{Directory.CreateDirectory(data);var settings=new Dictionary<string,object>{{"autoUpdate",autoUpdate},{"randomSkinOnStartup",randomSkinOnStartup},{"language",Lang.Code},{"selectedMinutes",face.SelectedMinutes},{"skin",(skinPreviewOriginal??face.Skin).Id},{"width",Width},{"left",Left},{"top",Top},{"topmost",Topmost},{"opacity",Opacity},{"trayEnabled",trayEnabled},{"showInTaskbar",ShowInTaskbar},{"startHidden",startHidden}};if(hasStartupPreference)settings["startupEnabled"]=startupDesired;File.WriteAllText(Path.Combine(data,"settings.json"),Json.Write(settings),Encoding.UTF8);}catch{}}
    const string Hint="Перетащите мышью • ПКМ: настройки • Колесо: размер";
    // Reads the Claude Code sign-in (never writes it) and asks Anthropic for the current limits.
    DateTime lastRenew=DateTime.MinValue;
    async Task Refresh() {
        if(busy||closed||demo)return;busy=true;
        try {
            string credsPath=ClaudeAccount.Require(data);var login=ClaudeAccount.Read(credsPath);
            if(ClaudeAccount.NearExpiry(login)&&DateTime.Now-lastRenew>TimeSpan.FromMinutes(15)){lastRenew=DateTime.Now;connectionStatus="Обновляю вход через Claude Code…";UpdateStatus();await Task.Run(()=>ClaudeAccount.Renew(credsPath));login=ClaudeAccount.Read(credsPath);}
            if(login.Expired)throw new ClaudeAuthException(ClaudeAccount.Expired);
            string body=await Task.Run(()=>ClaudeAccount.Usage(login.AccessToken,AppUpdate.CurrentVersion));
            if(closed)return;
            loggedIn=true;accountPlan=string.IsNullOrEmpty(login.Plan)?"":"claude_"+login.Plan;
            if(string.IsNullOrEmpty(accountName))accountName=await Task.Run(()=>ClaudeAccount.Email());
            ApplyLimits(Json.Read(body));face.ToolTip=Lang.T(Hint);
        }catch(ClaudeAuthException e){
            loggedIn=false;bool missing=e.Message!=ClaudeAccount.Expired;
            if(missing){accountName="";face.Limits=new Limits();lastUpdate=DateTime.MinValue;}
            connectionStatus=missing?"ПКМ: войти в Claude Code":"Вход устарел • ПКМ: открыть Claude Code";face.ToolTip=Lang.T(e.Message);
        }catch(Exception e){connectionStatus="Обновление не удалось • ПКМ: повторить";face.ToolTip=Lang.T(e.Message);}finally{busy=false;UpdateStatus();}
    }
    void ApplyLimits(Dictionary<string,object> value){face.Limits=Limits.Parse(value);face.Limits.PlanType=accountPlan;Height=Width*face.Aspect(Width);lastUpdate=DateTime.Now;connectionStatus=face.Limits.Primary==null&&face.Limits.Secondary==null?"Данные лимитов Claude недоступны":"";UpdateStatus();}
    void UpdateStatus(){if(demo||closed)return;if(!resizing)Height=Width*face.Aspect(Width);bool stale=lastUpdate!=DateTime.MinValue && ((DateTime.Now-lastUpdate).TotalSeconds>300 || !string.IsNullOrEmpty(connectionStatus));face.Stale=stale;
        face.Status=connectionStatus;face.Status=Lang.T(face.Status);face.InvalidateVisual();UpdateTrayText();}
    void UpdateTrayText(){
        if(tray==null)return;
        var quota=face.SmallQuota;string value=quota==null?"—":Math.Round(quota.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),key=value+"|"+(quota==null?0:quota.Remaining<10?2:quota.Remaining<20?1:0)+"|"+face.Stale+"|"+face.Skin.Id+"|"+face.Skin.Ink;
        if(key!=trayIconKey){var next=TrayDisplay.CreateIcon(value,face.Stale,face.Skin,quota==null?(double?)null:quota.Remaining);var old=trayValueIcon;tray.Icon=next;trayValueIcon=next;trayIconKey=key;if(old!=null)old.Dispose();}
        if(trayHover!=null)trayHover.UpdateData(face.Skin,face.Limits,accountName,loggedIn,face.Stale,lastUpdate,face.Status);UpdateUpdateControls();
    }

    // Signing in and renewing the sign-in is done by Claude Code itself; ClaudeMeter only opens it.
    void OpenClaudeCode(){try{string cli=ClaudeAccount.CliPath();if(cli!=null)Process.Start(new ProcessStartInfo(cli){UseShellExecute=true,WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)});else Process.Start(new ProcessStartInfo(ClaudeAccount.SetupUrl){UseShellExecute=true});}catch(Exception e){MessageBox.Show(this,e.Message,"Claude Code");}}
    public void VerifyStartup(string output){if(!loggedIn&&string.IsNullOrEmpty(face.Status))throw new Exception("Widget did not reach a signed-in or explained state");face.Export(output);}
    public void VerifyShape() { face.Limits.PlanType="prolite";UpdateStatus();if(Height!=Width)throw new Exception("Pro must be square");face.Limits.PlanType="pro";UpdateStatus();if(Height!=Width)throw new Exception("Pro must stay square");face.Limits.PlanType="plus";UpdateStatus();if(Math.Abs(Height-Width*.45)>.01)throw new Exception("Plus must be horizontal");SetWidth(100);if(Width!=100||Height!=100)throw new Exception("Mini must be 100 square");SetWidth(200);if(Width!=200||Height!=200)throw new Exception("Compact must be 200 square");SetWidth(500);face.Limits=new Limits();UpdateStatus(); }
    public async Task VerifyAnimation(){ShowDisplay();SetWidth(500);await Task.Delay(150);await face.VerifyNumericAnimation(skins,data);foreach(int size in new[]{100,200}){SetWidth(size);await Task.Delay(65);await face.VerifyNumericAnimation(skins,data);}SetWidth(500);}
    public async Task VerifyToggle(){SetWidth(200);double x=Left,y=Top;ToggleSize();await Task.Delay(450);if(Width!=800)throw new Exception("Expand failed");ToggleSize();await Task.Delay(450);if(Width!=200||Math.Abs(Left-x)>.1||Math.Abs(Top-y)>.1)throw new Exception("Restore failed");SetWidth(200);TrayDoubleClick();await Task.Delay(450);if(Width!=800||!IsVisible)throw new Exception("Tray must expand small widget");TrayDoubleClick();if(IsVisible)throw new Exception("Tray must hide large widget");TrayDoubleClick();if(!IsVisible||Width!=800)throw new Exception("Tray must show hidden large widget");SetWidth(200);face.Measure(new Size(200,200));face.Arrange(new Rect(0,0,200,200));var capture=face.Capture();if(capture.PixelWidth!=200||capture.PixelHeight!=200)throw new Exception("Screenshot dimensions");SetWidth(500);using(var hover=new TrayHover()){hover.UpdateData(face.Skin,Program.DemoLimits(),"demo@example.com",true,false,DateTime.Now,"");hover.Present();Forms.Application.DoEvents();if(!Forms.Screen.GetWorkingArea(Forms.Cursor.Position).Contains(hover.Bounds))throw new Exception("First hover outside monitor");hover.Dismiss();}}
    async Task VerifySkinPreview(MenuItem themes){
        var original=face.Skin;SaveSettings();var alternative=skins.First(x=>x.Id!=original.Id);var candidate=themes.Items.OfType<MenuItem>().First(i=>(string)i.Tag==alternative.Id);
        themes.IsSubmenuOpen=true;candidate.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
        if(face.Skin.Id!=alternative.Id)throw new Exception("Hover preview failed");
        SaveSettings();if(Json.Str(Json.Read(File.ReadAllText(Path.Combine(data,"settings.json"))),"skin")!=original.Id)throw new Exception("Hover persisted an uncommitted skin");
        themes.IsSubmenuOpen=false;await Task.Delay(100);if(face.Skin.Id!=original.Id)throw new Exception("Leaving gallery did not cancel preview");
        themes.IsSubmenuOpen=true;candidate.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});candidate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));themes.IsSubmenuOpen=false;await Task.Delay(100);
        if(face.Skin.Id!=alternative.Id||Json.Str(Json.Read(File.ReadAllText(Path.Combine(data,"settings.json"))),"skin")!=alternative.Id)throw new Exception("Click did not commit preview");
        menu.IsOpen=true;await Task.Delay(100);themes.IsSubmenuOpen=true;var oldItem=themes.Items.OfType<MenuItem>().First(i=>(string)i.Tag==original.Id);oldItem.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});menu.IsOpen=false;await Task.Delay(100);
        if(face.Skin.Id!=alternative.Id)throw new Exception("Closing context menu did not cancel preview");
        CommitSkin(original);menu.IsOpen=true;
    }
    public async Task VerifyTray() {
        HideDisplay();OpenTraySettings();await Task.Delay(100);if(!menu.IsOpen||!menu.Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Настройки")).IsSubmenuOpen)throw new Exception("Popup settings did not open while hidden");menu.IsOpen=false;
        bool originalRandom=randomSkinOnStartup;randomSkinOnStartup=true;SaveSettings();if(!Convert.ToBoolean(Json.Get(Json.Read(File.ReadAllText(Path.Combine(data,"settings.json"))),"randomSkinOnStartup")))throw new Exception("Random preference not saved");randomSkinOnStartup=originalRandom;SaveSettings();
        var previous=face.Skin;
        foreach(var skin in skins){face.SetSkin(skin);UpdateTrayText();if(!trayIconKey.Contains(skin.Id))throw new Exception("Tray did not follow skin");}
        face.SetSkin(previous);UpdateTrayText();
        if(tray==null||!tray.Visible||ShowInTaskbar)throw new Exception("Expected tray icon and no taskbar button");
        if(!startup.Enabled)throw new Exception("Expected isolated startup registration");
        startup.Set(false);if(startup.Enabled)throw new Exception("Startup disable failed");startup.Set(true);
        HideDisplay();if(IsVisible)throw new Exception("Tray hide failed");
        menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;menu.PlacementTarget=face;menu.IsOpen=true;
        if(!menu.IsOpen)throw new Exception("Tray menu could not open while hidden");menu.UpdateLayout();var menuBitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);menuBitmap.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(menuBitmap));using(var file=File.Create(Path.Combine(data,"menu-preview.png")))encoder.Save(file);var themesItem=menu.Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Скин"));if(themesItem.Items.Count!=skins.Count||themesItem.Items.OfType<MenuItem>().Any(i=>!(i.Header is Border)))throw new Exception("Skin previews missing");themesItem.IsSubmenuOpen=true;menu.UpdateLayout();var popupPart=themesItem.Template.FindName("PART_Popup",themesItem) as System.Windows.Controls.Primitives.Popup;if(popupPart==null||!popupPart.IsOpen)throw new Exception("Skin gallery closed");var gallery=popupPart.Child as FrameworkElement;gallery.UpdateLayout();var galleryBitmap=new RenderTargetBitmap((int)Math.Ceiling(gallery.ActualWidth),(int)Math.Ceiling(gallery.ActualHeight),96,96,PixelFormats.Pbgra32);galleryBitmap.Render(gallery);var galleryEncoder=new PngBitmapEncoder();galleryEncoder.Frames.Add(BitmapFrame.Create(galleryBitmap));using(var file=File.Create(Path.Combine(data,"gallery-preview.png")))galleryEncoder.Save(file);themesItem.IsSubmenuOpen=false;await VerifySkinPreview(themesItem);var preferencesItem=menu.Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Настройки"));preferencesItem.IsSubmenuOpen=true;var languagesItem=preferencesItem.Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Язык"));if(languagesItem.Items.Count!=8||languagesItem.Items.OfType<MenuItem>().Any(i=>!(i.Header is StackPanel)))throw new Exception("Language flags missing");languagesItem.IsSubmenuOpen=true;menu.UpdateLayout();var flagPopup=languagesItem.Template.FindName("PART_Popup",languagesItem) as System.Windows.Controls.Primitives.Popup;var flagVisual=flagPopup.Child as FrameworkElement;flagVisual.UpdateLayout();var flagBitmap=new RenderTargetBitmap((int)Math.Ceiling(flagVisual.ActualWidth),(int)Math.Ceiling(flagVisual.ActualHeight),96,96,PixelFormats.Pbgra32);flagBitmap.Render(flagVisual);var flagEncoder=new PngBitmapEncoder();flagEncoder.Frames.Add(BitmapFrame.Create(flagBitmap));using(var file=File.Create(Path.Combine(data,"languages-preview.png")))flagEncoder.Save(file);languagesItem.IsSubmenuOpen=false;preferencesItem.IsSubmenuOpen=false;if(!menu.Items.OfType<MenuItem>().Any(i=>Convert.ToString(i.Header)==Lang.T("Поверх окон")))throw new Exception("Topmost must be top-level");var quotaItem=menu.Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Настройки")).Items.OfType<MenuItem>().First(i=>Convert.ToString(i.Header)==Lang.T("Показывать лимит"));quotaItem.IsSubmenuOpen=true;menu.UpdateLayout();if(quotaItem.Items.Count==0)throw new Exception("No quota choices");quotaItem.IsSubmenuOpen=false;menu.IsOpen=false;
        ShowDisplay();if(!IsVisible)throw new Exception("Tray show failed");
        Close();if(closed||IsVisible)throw new Exception("Close should hide in tray");ShowDisplay();
        trayEnabled=false;EnsureRecovery();SetupTray();if(!ShowInTaskbar||startHidden||tray!=null)throw new Exception("Recovery when tray disabled failed");
        trayEnabled=true;ShowInTaskbar=false;SetupTray();SaveSettings();
    }
}
public static class Program {
    public static Limits DemoLimits(){return new Limits{PlanType="claude_pro",ModelName="Fable",ModelValue="88%",Primary=new Quota{Remaining=72,Minutes=300,Reset=1791073405},Secondary=new Quota{Remaining=75,Minutes=10080,Reset=1791620325}};}
    [STAThread] public static int Main(string[] args) {
        string root=AppDomain.CurrentDomain.BaseDirectory;
        try {
            if(args.Length==2 && args[0]=="--layout-audit"){
                Directory.CreateDirectory(args[1]);
                foreach(var skin in Skin.Load(root))foreach(string scenario in new[]{"hours","week","pair","compact","pro-hours","pro-pair"}){
                    var f=new MeterFace();f.SetSkin(skin);f.Limits=DemoLimits();
                    f.Limits.Primary.Remaining=100;f.Limits.Secondary.Remaining=9;f.Limits.CreditBalance="123.45";
                    f.Limits.Primary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+14400;
                    f.Limits.Secondary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+500000;
                    if(scenario=="hours"||scenario=="pro-hours")f.Limits.Secondary=null;
                    if(scenario=="pro-hours"||scenario=="pro-pair")f.Limits.PlanType="pro";
                    if(scenario=="compact")f.SelectedMinutes=300;
                    if(scenario=="week"){f.Limits.Primary=null;f.Limits.PlanType="prolite";}
                    f.Export(Path.Combine(args[1],skin.Number.ToString("00")+"-"+scenario+".jpg"),scenario=="compact"?200:500);
                }
                return 0;
            }
            if(args.Length>0 && args[0]=="--render"){
                RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
                string output=args[1];Directory.CreateDirectory(output);
                foreach(var skin in Skin.Load(root))foreach(string scenario in new[]{"plus-two","prolite-week","pro-two","pro-unknown","missing","low"}) {
                    var f=new MeterFace();f.SetSkin(skin);f.Limits=DemoLimits();
                    long now=DateTimeOffset.Now.ToUnixTimeSeconds();f.Limits.Primary.Reset=now+14400;f.Limits.Secondary.Reset=now+518400;
                    if(scenario=="prolite-week"){f.Limits.PlanType="prolite";f.Limits.Primary=f.Limits.Secondary;f.Limits.Primary.Remaining=66;f.Limits.Secondary=null;}
                    if(scenario=="pro-unknown"){f.Limits.PlanType="pro";f.Limits.Secondary=null;}
                    if(scenario=="pro-two")f.Limits.PlanType="pro";
                    if(scenario=="missing")f.Limits=new Limits();
                    if(scenario=="low"){f.Limits.Primary.Remaining=5;f.Limits.Secondary.Remaining=0;}
                    f.Export(Path.Combine(output,skin.Id+"-"+scenario+".png"));if(scenario=="prolite-week"){f.Export(Path.Combine(output,skin.Id+"-mini.png"),100);f.Export(Path.Combine(output,skin.Id+"-compact.png"),200);f.Limits.Primary.Remaining=19;f.Export(Path.Combine(output,skin.Id+"-amber.png"),200);f.Limits.Primary.Remaining=9;f.Export(Path.Combine(output,skin.Id+"-red.png"),200);}
                }
                return 0;
            }
            if(args.Length>0 && args[0]=="--tray-preview"){
                string output=args[1];Directory.CreateDirectory(output);
                foreach(var skin in Skin.Load(root))foreach(string language in new[]{"ru","de"}){
                    Lang.Code=language;var values=DemoLimits();values.Primary.Remaining=5;values.Secondary.Remaining=44;values.Primary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+12480;values.Secondary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+205080;
                    using(var popup=new TrayHover()){popup.UpdateData(skin,values,"demo@example.com",true,false,DateTime.Now,"");popup.SetUpdate(Lang.T("Доступно обновление")+" 4.1.0",true,false);popup.Show();Forms.Application.DoEvents();using(var bitmap=new System.Drawing.Bitmap(popup.Width,popup.Height)){popup.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,popup.Width,popup.Height));bitmap.Save(Path.Combine(output,skin.Id+"-"+language+".png"));}popup.Hide();}
                }
                Lang.Code="ru";return 0;
            }
            if(args.Length>0 && args[0]=="--space-preview"){
                string output=args[1];Directory.CreateDirectory(output);
                foreach(string language in new[]{"ru","de"}){Lang.Code=language;foreach(var skin in Skin.Load(root).Where(x=>x.Kind=="space")){
                    var f=new MeterFace();f.SetSkin(skin);f.Limits=DemoLimits();f.Limits.Primary=null;f.Limits.Secondary.Remaining=67;f.Limits.Secondary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+187200;
                    foreach(int size in new[]{100,200,300,500,800,1000})f.Export(Path.Combine(output,skin.Id+"-"+language+"-"+size+".png"),size);
                    foreach(double remaining in new[]{0,9,19,100}){f.Limits.Secondary.Remaining=remaining;f.Export(Path.Combine(output,skin.Id+"-"+language+"-quota-"+remaining+".png"),500);}
                    f.Limits.Primary=new Quota{Remaining=99,Minutes=300,Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+14400};f.Export(Path.Combine(output,skin.Id+"-"+language+"-pair.png"));
                }}Lang.Code="ru";return 0;
            }
            if(args.Length>0 && args[0]=="--glass-preview"){
                string output=args[1];Directory.CreateDirectory(output);var skins=Skin.Load(root);
                foreach(string language in Lang.Names.Keys){Lang.Code=language;foreach(var skin in skins.Where(x=>x.Kind=="glass")){var f=new MeterFace();f.SetSkin(skin);f.Limits=DemoLimits();f.Limits.Secondary=null;f.Limits.Primary.Remaining=99;f.Limits.Primary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+17640;foreach(int size in new[]{100,200,300,500,800,1000})f.Export(Path.Combine(output,skin.Id+"-"+language+"-"+size+".png"),size);f.Limits.Secondary=new Quota{Remaining=100,Minutes=10080,Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+518400};f.Export(Path.Combine(output,skin.Id+"-"+language+"-pair.png"));}}
                Lang.Code="ru";return 0;
            }
            if(args.Length>0&&args[0]=="--update-test"){UpdateTests.Run(root);return 0;}
            if(args.Length>0 && args[0]=="--custom-test"){
                CustomSkins.SelfTest(root);File.WriteAllText(Path.Combine(root,"custom-test.txt"),"PASS: valid import and reload, stable custom ID/number, duplicate preservation, path rejection, color and font validation, invalid PNG rejection, embedded guide.");return 0;
            }
            if(args.Length>0 && args[0]=="--self-test") { SelfTest();return 0; }
            string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClaudeMeter");
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--data-dir")data=Path.GetFullPath(args[i+1]);
            Directory.CreateDirectory(data);
            if(args.Length>0&&args[0]=="--smoke") { Smoke(root,data);return 0; }
            if(args.Length>0&&args[0]=="--ui-test") {
                var testApp=new Application();var w=new Widget(root,data,false,false){WindowState=WindowState.Minimized,ShowInTaskbar=false};
                int result=1;var tick=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};tick.Tick+=async delegate{tick.Stop();try{w.VerifyStartup(args[1]);w.VerifyShape();await w.VerifyAnimation();await w.VerifyToggle();await w.VerifyTray();result=0;File.WriteAllText(Path.Combine(root,"ui-test.txt"),"PASS: WPF startup, CLI connection, signed-out UI, tray icon, menu while hidden, show/hide, close to tray, recovery with tray disabled, isolated startup enable/disable, clean shutdown.");}catch(Exception e){File.WriteAllText(Path.Combine(root,"error.txt"),e.Message);}w.Quit();};tick.Start();testApp.Run(w);return result;
            }
            using(var instance=new SingleInstance(data)) {
                if(!instance.Owner){instance.ShowExisting();return 0;}
                var app=new Application();var widget=new Widget(root,data,args.Contains("--demo"),!args.Contains("--data-dir"));
                instance.ShowRequested+=delegate{widget.Dispatcher.BeginInvoke((Action)widget.ShowDisplay);};app.Run(widget);return 0;
            }
        }catch(Exception e){File.WriteAllText(Path.Combine(root,"error.txt"),e.Message,Encoding.UTF8);if(!args.Contains("--smoke")&&!args.Contains("--self-test")&&!args.Contains("--update-test")&&!args.Contains("--custom-test")&&!args.Contains("--render"))MessageBox.Show(e.Message,"Claude Meter");return 1;}
    }
    // Live check against the real account: one usage request, no model calls. Values are not written to the report.
    static void Smoke(string root,string data){
        var login=ClaudeAccount.Read(ClaudeAccount.Require(data));Check(!login.Expired,"Claude Code sign-in is current");
        var limits=Limits.Parse(Json.Read(ClaudeAccount.Usage(login.AccessToken,AppUpdate.CurrentVersion)));Check(limits.Windows.Count>0,"usage windows returned");
        File.WriteAllText(Path.Combine(root,"smoke-test.txt"),"PASS: Claude Code sign-in read without modification, one usage request, "+limits.Windows.Count+" limit window(s) parsed. No model calls.");
    }
    static void Check(bool ok,string description){if(!ok)throw new Exception("TEST FAILED: "+description);}
    static void SelfTest(){
        var v=Limits.Parse(Json.Read("{\"five_hour\":{\"utilization\":28.0,\"resets_at\":\"2026-10-04T09:43:25.5+00:00\"},\"seven_day\":{\"utilization\":25},\"seven_day_opus\":{\"utilization\":99}}"));
        Check(v.Primary.Remaining==72&&v.Secondary.Remaining==75,"used vs remaining, Claude windows");
        Check(v.Primary.Minutes==300&&v.Secondary.Minutes==10080&&v.Primary.Reset==1791107005&&v.Secondary.Reset==0,"window durations and ISO reset time");
        Check(Limits.Parse(Json.Read("{}")).Primary==null,"missing is unknown");
        Check(Limits.Parse(Json.Read("{\"five_hour\":{\"utilization\":110}}")).Primary.Remaining==0,"clamp zero");
        Check(Limits.Parse(Json.Read("{\"five_hour\":{\"utilization\":-1}}")).Primary.Remaining==100,"clamp full");
        Check(Limits.Parse(Json.Read("{\"seven_day_opus\":{\"utilization\":20},\"five_hour\":null}")).Windows.Count==0,"do not use other bucket");
        var real=Limits.Parse(Json.Read("{\"five_hour\":{\"utilization\":96.0,\"resets_at\":\"2026-11-12T12:20:00.108712+00:00\",\"limit_dollars\":null,\"locked_reason\":null},\"seven_day\":{\"utilization\":23.0,\"resets_at\":\"2026-11-18T12:00:00.108729+00:00\",\"limit_dollars\":null,\"locked_reason\":null},\"seven_day_opus\":null,\"seven_day_sonnet\":null,\"pace\":null,\"extra_usage\":{\"is_enabled\":true,\"monthly_limit\":null,\"used_credits\":1250.0,\"utilization\":null,\"currency\":\"USD\",\"decimal_places\":2,\"disabled_reason\":null},\"limits\":[{\"kind\":\"session\",\"group\":\"session\",\"percent\":96,\"severity\":\"critical\",\"resets_at\":\"2026-11-12T12:20:00.108712+00:00\",\"scope\":null,\"is_active\":true},{\"kind\":\"weekly_all\",\"group\":\"weekly\",\"percent\":23,\"severity\":\"normal\",\"resets_at\":\"2026-11-18T12:00:00.108729+00:00\",\"scope\":null,\"is_active\":false},{\"kind\":\"weekly_scoped\",\"group\":\"weekly\",\"percent\":23,\"severity\":\"normal\",\"resets_at\":\"2026-11-18T12:00:00.108973+00:00\",\"scope\":{\"model\":{\"id\":null,\"display_name\":\"Fable\"},\"surface\":null},\"is_active\":false}],\"spend\":{\"used\":{\"amount_minor\":1250,\"currency\":\"USD\",\"exponent\":2},\"limit\":null,\"percent\":0,\"enabled\":true},\"seven_day_breakdown\":{\"rows\":[{\"key\":\"claude_code\",\"display_name\":\"Claude Code\",\"percent\":100},{\"key\":\"chat\",\"display_name\":\"Chats\",\"percent\":0}]}}"));
        Check(real.Primary.Remaining==4&&real.Secondary.Remaining==77&&real.Primary.Reset==1794486000&&real.Secondary.Minutes==10080,"recorded Claude Pro response");
        Check(real.ModelName=="Fable"&&real.ModelValue=="77%"&&real.CreditBalance=="$12.50"&&real.ResetLine.StartsWith("Fable"),"model limit and extra usage");
        var listed=Limits.Parse(Json.Read("{\"limits\":[{\"kind\":\"session\",\"percent\":96,\"resets_at\":null},{\"kind\":\"weekly_all\",\"percent\":23}]}"));
        Check(listed.Primary.Remaining==4&&listed.Secondary.Remaining==77&&listed.Primary.Reset==0,"limits list fallback");
        Check(Limits.Parse(Json.Read("{\"extra_usage\":{\"is_enabled\":false,\"used_credits\":5494.0}}")).CreditBalance=="","disabled extra usage hidden");
        var signIn=ClaudeAccount.Parse("{\"claudeAiOauth\":{\"accessToken\":\"t\",\"expiresAt\":1,\"subscriptionType\":\"pro\"}}");
        Check(signIn.Expired&&signIn.Plan=="pro"&&signIn.AccessToken=="t","credentials parsing and expiry");
        Check(!ClaudeAccount.Parse("{\"claudeAiOauth\":{\"accessToken\":\"t\",\"expiresAt\":99999999999999}}").Expired,"current sign-in accepted");
        bool rejected=false;try{ClaudeAccount.Parse("{\"other\":1}");}catch(ClaudeAuthException){rejected=true;}Check(rejected,"file without Claude sign-in rejected");
        var claudePlan=new Limits{PlanType="claude_pro"};Check(claudePlan.PlanLabel=="CLAUDE PRO"&&!claudePlan.Square,"Claude plan label and wide layout");
        Check(v.Primary.Label("")==Lang.T("5 часов")&&v.Secondary.Label("")==Lang.T("Неделя"),"windows labels");
        Check(TrayDisplay.Value(v)=="75","tray prioritizes weekly limit");
        Check(TrayDisplay.Value(new Limits())=="—","unknown tray is not zero");
        Check(TrayDisplay.Value(new Limits{Primary=new Quota{Remaining=100,Minutes=300}})=="100","tray supports 100 and nonweekly fallback");
        var details=TrayDisplay.Details(new Limits{PlanType="prolite",Primary=v.Primary,Secondary=v.Secondary,ResetCount=2},true,true,DateTime.Now,"");
        Check(details.Contains("PRO $100")&&details.Contains("72%")&&details.Contains("75%")&&details.Contains("Доступно сбросов: 2")&&details.Contains("Сброс:")&&!details.Contains("Данные устарели"),"tooltip contains all windows, plan, resets, time, no stale text");
        foreach(var area in new[]{new System.Drawing.Rectangle(0,0,1920,1080),new System.Drawing.Rectangle(-1920,0,1920,1080)}){var point=TrayHover.Place(new System.Drawing.Point(area.Right-20,area.Bottom-20),new System.Drawing.Size(420,510),area);Check(area.Contains(new System.Drawing.Rectangle(point,new System.Drawing.Size(420,510))),"tooltip remains on anchor monitor");}
        var selectionFace=new MeterFace{Limits=v,SelectedMinutes=300};Check(selectionFace.SmallQuota==v.Primary,"select five-hour window");selectionFace.SelectedMinutes=10080;Check(selectionFace.SmallQuota==v.Secondary,"select weekly window");selectionFace.SelectedMinutes=1440;Check(selectionFace.SmallQuota==null,"missing selected window stays unknown");
        Check(TrayHover.DecorPadding(new Skin{Kind="space",Number=9})==64&&TrayHover.DecorPadding(new Skin{Kind="space",Number=6})==48&&TrayHover.DecorPadding(new Skin{Kind="glass"})==12,"popup reserves decorative frame padding");
        var paletteSkins=Skin.Load(AppDomain.CurrentDomain.BaseDirectory);
        Check(paletteSkins.Take(8).Select(s=>TrayDisplay.Accent(s).ToArgb()).Distinct().Count()==8,"legacy popup skins retain individual accents");
        foreach(var theme in paletteSkins.Where(s=>!string.IsNullOrEmpty(s.Accent)))Check(TrayDisplay.Accent(theme).ToArgb()==System.Drawing.ColorTranslator.FromHtml(theme.Accent).ToArgb(),"popup respects declared accent "+theme.Id);
        Check(TrayDisplay.Accent(new Skin{Id="user.test",Accent="#AB1234"}).ToArgb()==System.Drawing.ColorTranslator.FromHtml("#AB1234").ToArgb(),"custom popup accent");
        Check(TrayDisplay.NumberColor(null,19)!=TrayDisplay.NumberColor(null,20)&&TrayDisplay.NumberColor(null,9)!=TrayDisplay.NumberColor(null,10),"warning thresholds at 20 and 10");
        foreach(string language in Lang.Names.Keys){Lang.Code=language;Check(Lang.T("Сброс через")!=""&&Lang.T("Неделя")!="","language entries");}Lang.Code="ru";
        var rotationThemes=Skin.Load(AppDomain.CurrentDomain.BaseDirectory);foreach(var theme in rotationThemes)Check(SkinOptions.Next(rotationThemes,theme.Id,new Random(1)).Id!=theme.Id,"rotation avoids previous skin");Check(SkinOptions.Next(rotationThemes.Take(1).ToList(),rotationThemes[0].Id,new Random(1))==rotationThemes[0],"one-skin rotation remains valid");Check(rotationThemes.Select(x=>x.Number).SequenceEqual(Enumerable.Range(1,16)),"stable skin numbers");
        var themes=Skin.Load(AppDomain.CurrentDomain.BaseDirectory);
        Check(themes.Select(theme=>TrayDisplay.Background(theme,false).ToArgb()).Distinct().Count()==themes.Count,"all tray skins have distinct palettes");
        foreach(var theme in themes){Check(TrayDisplay.Foreground(theme).ToArgb()==System.Drawing.ColorTranslator.FromHtml(theme.Ink).ToArgb(),"tray uses skin ink");Check(TrayDisplay.Background(theme,true)!=TrayDisplay.Background(theme,false),"stale state retains a distinct shade");}
        using(var icon=TrayDisplay.CreateIcon("100",false))Check(icon.Width==64,"tray icon generated");
        var weekly=new Limits{PlanType="prolite",Primary=new Quota{Remaining=66,Minutes=10080},ResetCount=2};
        Check(weekly.Windows.Count==1&&weekly.Windows[0].Remaining==66&&weekly.PlanLabel=="PRO $100"&&weekly.ResetCount==2,"Pro Lite single weekly window and resets");
        Check(weekly.Square,"Pro Lite is square");Check(!DemoLimits().Square,"Claude is horizontal");weekly.PlanType="pro";Check(weekly.Square,"Pro is square");Check(weekly.PlanLabel=="PRO","ambiguous Pro price stays unspecified");
        weekly.PlanType="future_plan";Check(weekly.PlanLabel=="FUTURE_PLAN","unknown plan remains visible");
        Check(new Limits().Windows.Count==0&&!new Limits().ResetCount.HasValue,"missing does not mean zero or unlimited");
        var reversed=new Limits{Primary=v.Secondary,Secondary=v.Primary};Check(reversed.Windows[0].Minutes==300,"sort windows by duration rather than field position");
        using(var first=new SingleInstance(Path.Combine(Path.GetTempPath(),"claude-meter-instance-test-"+Guid.NewGuid())))
            Check(first.Owner,"first process owns instance lock");
        string instancePath=Path.Combine(Path.GetTempPath(),"claude-meter-instance-test-"+Guid.NewGuid());
        using(var first=new SingleInstance(instancePath))using(var second=new SingleInstance(instancePath))using(var received=new System.Threading.ManualResetEvent(false)){
            Check(first.Owner&&!second.Owner,"duplicate instance suppressed");first.ShowRequested+=delegate{received.Set();};second.ShowExisting();Check(received.WaitOne(2000),"duplicate launch signals show");
        }
        foreach(var theme in Skin.Load(AppDomain.CurrentDomain.BaseDirectory)){var image=SkinImage.Load(theme.Background);Check(image.PixelWidth>0&&image.PixelHeight>0,"WebP decoded "+theme.Id);}
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"PASS: quota conversion, recorded Claude response, credentials parsing, missing data, boundaries, window labels, single weekly Pro Lite window, plan fallback, reset count, chronological windows, single-instance activation.");
    }
}
}
