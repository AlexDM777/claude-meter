using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace ClaudeMeter {
public partial class MeterFace {
    public double Aspect(double width){return width<=240?1:Skin!=null&&(Skin.Kind=="glass"||Skin.Kind=="space")?(Limits.Windows.Count==2?.5:1):Limits.Square?1:.45;}
    static Brush Solid(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
    void Fit(DrawingContext dc,string text,double cx,double y,double size,double maxWidth,Brush ink){
        var ft=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),size,ink,1);
        if(ft.Width>maxWidth){size=size*maxWidth/ft.Width;ft.SetFontSize(size);}
        DrawNumericText(dc,text,ft,cx-ft.Width/2,y,"Segoe UI",size,ink,FontWeights.SemiBold);
    }
    void GlassBackground(DrawingContext dc,Rect rect,bool night){
        // Alpha is preserved, so the user's wallpaper remains visible through the body.
        var gradient=new LinearGradientBrush();gradient.StartPoint=new Point(0,0);gradient.EndPoint=new Point(1,1);
        gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(night?"#EA344654":"#EAF6FAFF"),0));
        gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(night?"#D81B2836":"#D8CFDFEE"),.5));
        gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(night?"#EA253443":"#EAF9FBFF"),1));
        for(int i=6;i>0;i--)dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(3+i),0,0,0)),null,new Rect(rect.X-i*.4,rect.Y+i*.6,rect.Width+i*.8,rect.Height+i*.5),24,24);
        dc.DrawRoundedRectangle(gradient,new Pen(Solid(night?"#708FA7B8":"#CFFFFFFF"),1.2),rect,24,24);
        var inner=new Rect(rect.X+2,rect.Y+2,rect.Width-4,rect.Height-4);dc.DrawRoundedRectangle(null,new Pen(Solid(night?"#3089C2EB":"#5089B8E2"),1),inner,22,22);
        var highlight=new LinearGradientBrush(Color.FromArgb(night?(byte)20:(byte)100,255,255,255),Colors.Transparent,90);
        dc.DrawRoundedRectangle(highlight,null,new Rect(rect.X+3,rect.Y+3,rect.Width-6,rect.Height*.22),21,21);
    }
    void GlassBar(DrawingContext dc,Quota q,double x,double y,double width,double height,bool night){
        dc.DrawRoundedRectangle(Solid(night?"#4820394B":"#282C567D"),new Pen(Solid(night?"#48759AB7":"#70FFFFFF"),1),new Rect(x,y,width,height),height/2,height/2);
        const int count=24;double gap=2.2,inset=3,w=(width-2*inset-(count-1)*gap)/count;
        for(int i=0;i<count;i++){
            var r=new Rect(x+inset+i*(w+gap),y+3,w,height-6);
            dc.DrawRoundedRectangle(Solid(night?"#263E5B71":"#183C6C9C"),null,r,2,2);
            double fraction=q==null?0:Math.Max(0,Math.Min(1,q.Remaining*count/100-i));if(fraction<=0)continue;
            var light=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(night?"#DEFAFF":"#BCEAFF"),(Color)ColorConverter.ConvertFromString(night?"#6FB9ED":"#3892DD"),90);
            dc.PushClip(new RectangleGeometry(new Rect(r.X,r.Y,r.Width*fraction,r.Height)));dc.DrawRoundedRectangle(light,new Pen(Solid(night?"#90DAF6FF":"#90D6F4FF"),.5),r,2,2);dc.Pop();
        }
    }
    void DrawGlass(DrawingContext dc,double width,double height){
        bool small=width<=240,pair=!small&&Limits.Windows.Count==2;double tile=pair?width/2:width;
        for(int i=0;i<(pair?2:1);i++){
            dc.PushTransform(new TranslateTransform(i*tile,0));dc.PushTransform(new ScaleTransform(tile/500,height/500));
            bool night=Skin.Id=="glass-night";Brush ink=Solid(night?"#EDF5FC":"#1C2737"),secondary=Solid(night?"#B5C8D9":"#536779"),accent=Solid(night?"#A1E2FF":"#0070DE");
            GlassBackground(dc,new Rect(7,5,486,486),night);Quota q=small?SmallQuota:pair?Limits.Windows[i]:Limits.Windows.FirstOrDefault();
            dc.PushOpacity(Stale?.5:1);Brush numberInk=q!=null&&q.Remaining<10?Solid(night?"#FF9F94":"#B12A1E"):q!=null&&q.Remaining<20?Solid(night?"#FFCD88":"#944C00"):ink;
            string value=q==null?"—":Math.Round(q.Remaining,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
            if(width<=160){Fit(dc,value,250,100,230,420,numberInk);}
            else if(width<=240){GlassBar(dc,q,48,63,404,36,night);Fit(dc,value,250,160,205,420,numberInk);}
            else{
                GlassBar(dc,q,43,43,414,32,night);
                Fit(dc,Limits.WindowTitle(q),250,94,24,425,ink);
                Fit(dc,value+"%",250,128,130,425,numberInk);Fit(dc,Lang.T("Осталось"),250,278,20,425,secondary);
                Fit(dc,Lang.T("Сброс через")+" "+(q==null?"—":q.Countdown),250,323,32,432,ink);
                Fit(dc,q==null?"—":q.ResetText,250,369,18,425,secondary);
                dc.DrawLine(new Pen(Solid(night?"#405C7C97":"#40647F9E"),.7),new Point(38,410),new Point(462,410));
                Fit(dc,Limits.ExtraLabel,205,428,22,324,secondary);
                Fit(dc,Limits.ExtraValue,426,414,40,70,accent);
                Fit(dc,Limits.CreditLine,250,459,17,400,secondary);
                if(!string.IsNullOrEmpty(Status)){Fit(dc,Lang.T(Status),250,482,10,430,secondary);}
            }
            dc.Pop();dc.Pop();dc.Pop();
        }
    }
}
}
