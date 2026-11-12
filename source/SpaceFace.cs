using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
namespace ClaudeMeter {
public partial class MeterFace {
    static Brush SpaceAccent(Skin skin){return Solid(!string.IsNullOrEmpty(skin.Accent)?skin.Accent:skin.Id=="beskar"?"#F2AF43":skin.Id=="pilot"?"#74F34B":"#40B8EB");}
    void SpaceNumber(DrawingContext dc,string value,double y,Brush ink,bool percent,double available=400,double digitScale=1){
        double w=value.Length>=3?78:100,h=144,gap=12,percentWidth=percent?58:0,total=value.Length*w+(value.Length-1)*gap+percentWidth;
        double scale=Math.Min(1,available/total)*digitScale,x=(500-total*scale)/2;
        dc.PushTransform(new TranslateTransform(x,y));dc.PushTransform(new ScaleTransform(scale,scale));
        double offset=0;foreach(char c in value){Digit(dc,c,offset,0,w,h,ink);offset+=w+gap;}
        if(percent)Text(dc,"%",offset-3,67,58,ink,"Consolas",false);dc.Pop();dc.Pop();
    }
    void GameText(DrawingContext dc,string text,double cx,double y,double size,double maxWidth,Brush ink){
        string font=Skin.IsCustom?(Skin.Font=="serif"?"Georgia":Skin.Font=="mono"?"Consolas":"Segoe UI"):Skin.Id=="heroes3"?"Palatino Linotype":Skin.Id=="diablo"?"Georgia":Skin.Id=="fallout"||Skin.Id=="starcraft"?"Consolas":Skin.Id=="counterstrike"||Skin.Id=="doom2"||Skin.Id=="terminator2"?"Bahnschrift":"Segoe UI";
        var ft=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(font),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),size,ink,1);if(ft.Width>maxWidth){size=size*maxWidth/ft.Width;ft.SetFontSize(size);}if(Skin.Id=="heroes3"&&!text.Any(char.IsDigit)){ft.SetForegroundBrush(Solid("#C0000000"));dc.DrawText(ft,new Point(cx-ft.Width/2+1,y+1));ft.SetForegroundBrush(ink);}DrawNumericText(dc,text,ft,cx-ft.Width/2,y,font,size,ink,FontWeights.SemiBold);
    }
    void SpaceBar(DrawingContext dc,Quota quota,bool compact){
        if(Skin.Id=="heroes3"){
            double top=compact?69:80;var outer=new Rect(66,top-4,368,40);dc.DrawRectangle(Solid("#1B130C"),new Pen(Solid("#D4B96D"),1.8),outer);dc.DrawRectangle(null,new Pen(Solid("#70552E"),1),new Rect(69,top-1,362,34));
            var track=new Rect(73,top+3,354,26);dc.DrawRectangle(Solid("#351E16"),new Pen(Solid("#0E0906"),1),track);
            double fraction=quota==null?0:Math.Max(0,Math.Min(1,quota.Remaining/100));
            if(fraction>0){dc.PushClip(new RectangleGeometry(new Rect(track.X,track.Y,track.Width*fraction,track.Height)));var gold=new LinearGradientBrush();gold.StartPoint=new Point(0,0);gold.EndPoint=new Point(0,1);gold.GradientStops.Add(new GradientStop(Color.FromRgb(239,215,143),0));gold.GradientStops.Add(new GradientStop(Color.FromRgb(172,132,51),.48));gold.GradientStops.Add(new GradientStop(Color.FromRgb(116,79,28),.52));gold.GradientStops.Add(new GradientStop(Color.FromRgb(201,166,83),1));dc.DrawRectangle(gold,null,track);for(int j=1;j<20;j++)dc.DrawLine(new Pen(Solid("#80634A20"),1),new Point(track.X+j*track.Width/20,track.Y+1),new Point(track.X+j*track.Width/20,track.Bottom-1));dc.Pop();}
            return;
        }
        Brush accent=WarningInk(quota,SpaceAccent(Skin));bool game=Skin.Number>=9;double y=compact?69:game?80:51,width=game?360:386,x=(500-width)/2;
        dc.DrawRoundedRectangle(Solid(Skin.Id=="imperial"?"#30303945":"#B0000000"),new Pen(Solid(Skin.Id=="imperial"?"#807D858C":"#90666C66"),1.3),new Rect(x-4,y-4,width+8,40),4,4);
        const int count=24;double gap=3,w=(width-(count-1)*gap)/count;
        for(int i=0;i<count;i++){
            var r=new Rect(x+i*(w+gap),y,w,32);dc.PushOpacity(.16);dc.DrawRectangle(accent,null,r);dc.Pop();
            double fill=quota==null?0:Math.Max(0,Math.Min(1,quota.Remaining*count/100-i));if(fill>0){dc.PushClip(new RectangleGeometry(new Rect(r.X,r.Y,r.Width*fill,r.Height)));dc.DrawRectangle(accent,null,r);dc.Pop();}
        }
    }
    void DrawSpace(DrawingContext dc,double width,double height){
        bool game=Skin.Number>=9;bool small=width<=240,pair=!small&&Limits.Windows.Count==2;double tile=pair?width/2:width;
        for(int i=0;i<(pair?2:1);i++){
            dc.PushTransform(new TranslateTransform(i*tile,0));dc.PushTransform(new ScaleTransform(tile/500,height/500));
            dc.PushClip(new RectangleGeometry(new Rect(0,0,500,500),7,7));dc.DrawImage(background,new Rect(0,0,500,500));
            Quota q=small?SmallQuota:pair?Limits.Windows[i]:Limits.Windows.FirstOrDefault();Brush ink=Solid(Skin.Ink),accent=SpaceAccent(Skin);
            Brush number=q!=null&&q.Remaining<10?Solid(!TrayDisplay.Dark(Skin)?"#B12A1E":"#FF9282"):q!=null&&q.Remaining<20?Solid(!TrayDisplay.Dark(Skin)?"#944C00":"#FFC46D"):ink;
            string value=q==null?"--":Math.Round(q.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);dc.PushOpacity(Stale?.5:1);
            dc.PushTransform(new TranslateTransform(39,30));dc.PushTransform(new ScaleTransform(.844,.86));
            if(width<=160){if(Skin.Id=="heroes3")GameText(dc,value,250,155,155,370,number);else SpaceNumber(dc,value,175,number,false);}
            else if(width<=240){SpaceBar(dc,q,true);if(Skin.Id=="heroes3")GameText(dc,value,250,170,150,370,number);else SpaceNumber(dc,value,190,number,false);}
            else{
                SpaceBar(dc,q,false);GameText(dc,Limits.WindowTitle(q),250,game?120:102,23,370,ink);
                if(Skin.Id=="heroes3"||Skin.Id=="diablo"||Skin.Id=="vicecity"||Skin.IsCustom&&Skin.Font!="digital")GameText(dc,value+"%",250,151,116,370,number);else SpaceNumber(dc,value,game?153:147,number,true,400,game?.82:1);
                GameText(dc,Lang.T("Осталось"),250,game?280:300,19,370,ink);
                GameText(dc,Lang.T("Сброс через")+" "+(q==null?"—":q.Countdown),250,game?307:320,26,370,Skin.Id=="imperial"?ink:accent);
                GameText(dc,q==null?"—":q.ResetText,250,game?343:360,17,370,ink);
                dc.DrawLine(new Pen(ink,.6),new Point(52,game?372:394),new Point(448,game?372:394));
                GameText(dc,Limits.ExtraLabel,210,game?388:412,18,304,ink);GameText(dc,Limits.ExtraValue,416,game?377:398,game?34:40,52,ink);
                GameText(dc,Limits.CreditLine,250,game?416:438,17,340,ink);
                if(!string.IsNullOrEmpty(Status))GameText(dc,Lang.T(Status),250,game?439:462,10,350,ink);
            }
            dc.Pop();dc.Pop();dc.Pop();dc.Pop();dc.Pop();dc.Pop();
        }
    }
}
}
