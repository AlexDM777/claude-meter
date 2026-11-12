using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace ClaudeMeter {
public partial class MeterFace {
    sealed class NumericFrame {public string Value;public DrawingGroup Current,Previous;public long Started;public bool Roll;}
    readonly Dictionary<int,NumericFrame> numericFrames=new Dictionary<int,NumericFrame>();
    DispatcherTimer numericTimer;int numericIndex;bool animateRender;string numericLayout="";
    public MeterFace(){Unloaded+=delegate{ResetNumericAnimation();};IsVisibleChanged+=delegate{if(!IsVisible)ResetNumericAnimation();};}
    void ResetNumericAnimation(){numericFrames.Clear();if(numericTimer!=null)numericTimer.Stop();}
    void BeginNumericFrame(){
        string layout=(Skin==null?"":Skin.Id)+":"+Lang.Code+":"+ActualWidth+":"+ActualHeight+":"+SelectedMinutes+":"+string.Join(",",Limits.Windows.Select(q=>q.Minutes.ToString()));
        if(layout!=numericLayout){ResetNumericAnimation();numericLayout=layout;}
        numericIndex=0;animateRender=IsLoaded&&IsVisible&&SystemParameters.ClientAreaAnimation;
        if(!animateRender)ResetNumericAnimation();
    }
    void EndNumericFrame(){animateRender=false;}
    static double NumericProgress(long start){return Math.Min(1,Math.Max(0,(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency/280));}
    void EnsureNumericTimer(){if(numericTimer==null){numericTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};numericTimer.Tick+=delegate{if(!IsVisible||!IsLoaded){ResetNumericAnimation();return;}InvalidateVisual();if(!numericFrames.Values.Any(f=>f.Previous!=null&&NumericProgress(f.Started)<1))numericTimer.Stop();};}numericTimer.Start();}
    void AnimateNumber(DrawingContext dc,string value,Rect bounds,bool roll,Action<DrawingContext> draw){
        if(!animateRender){draw(dc);return;}int key=numericIndex++;NumericFrame frame;
        if(!numericFrames.TryGetValue(key,out frame)){var group=new DrawingGroup();using(var target=group.Open())draw(target);frame=new NumericFrame{Value=value,Current=group,Roll=roll};numericFrames[key]=frame;}
        else if(frame.Value!=value){var group=new DrawingGroup();using(var target=group.Open())draw(target);frame.Previous=frame.Current;frame.Current=group;frame.Value=value;frame.Started=System.Diagnostics.Stopwatch.GetTimestamp();EnsureNumericTimer();}
        if(frame.Previous==null){draw(dc);return;}double t=NumericProgress(frame.Started);if(t>=1){frame.Previous=null;draw(dc);return;}double ease=1-Math.Pow(1-t,3);
        dc.PushClip(new RectangleGeometry(bounds));dc.PushOpacity(1-ease);dc.PushTransform(new TranslateTransform(0,roll?bounds.Height*ease:0));dc.DrawDrawing(frame.Previous);dc.Pop();dc.Pop();
        dc.PushOpacity(ease);dc.PushTransform(new TranslateTransform(0,roll?-bounds.Height*(1-ease):0));dc.DrawDrawing(frame.Current);dc.Pop();dc.Pop();dc.Pop();
    }
    void NumericText(DrawingContext dc,string text,FormattedText ft,double x,double y){
        // Only numerical fields animate. Labels, dates and status messages stay still.
        bool roll=text.StartsWith(Lang.T("Сброс через"),StringComparison.Ordinal);
        bool percentage=text.EndsWith("%",StringComparison.Ordinal)||text.All(c=>char.IsDigit(c)||c=='—'||c=='-');
        if(!roll&&!percentage){dc.DrawText(ft,new Point(x,y));return;}
        if(!roll){AnimateNumber(dc,text,new Rect(x-3,y-3,ft.Width+6,ft.Height+6),false,target=>target.DrawText(ft,new Point(x,y)));return;}
        // Keep words fixed; roll each changed numeral in its own clipped cell.
        var typeface=new Typeface(new FontFamily(numericFont),FontStyles.Normal,numericWeight,FontStretches.Normal);
        double offset=0;for(int i=0;i<text.Length;i++){
            string letter=text[i].ToString();var glyph=new FormattedText(letter,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,typeface,numericSize,numericInk,1);
            var prefix=new FormattedText(text.Substring(0,i+1),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,typeface,numericSize,numericInk,1);double next=prefix.WidthIncludingTrailingWhitespace;double at=x+offset;
            if(char.IsDigit(text[i]))AnimateNumber(dc,letter,new Rect(at-1,y-2,Math.Max(glyph.Width,next-offset)+2,ft.Height+4),true,target=>target.DrawText(glyph,new Point(at,y)));
            else dc.DrawText(glyph,new Point(at,y));offset=next;
        }
    }
    public async System.Threading.Tasks.Task VerifyNumericAnimation(IEnumerable<Skin> themes,string output){
        if(!SystemParameters.ClientAreaAnimation)return;
        var originalSkin=Skin;var originalLimits=Limits;
        try{foreach(var theme in themes){SetSkin(theme);Limits=Program.DemoLimits();Limits.Primary.Remaining=83;Limits.Secondary.Remaining=83;Limits.Primary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+15580;Limits.Secondary.Reset=DateTimeOffset.Now.ToUnixTimeSeconds()+125580;InvalidateVisual();await System.Threading.Tasks.Task.Delay(65);
            if(numericFrames.Values.Any(f=>f.Previous!=null))throw new Exception("Initial numeric render animated: "+theme.Id);
            Limits.Primary.Remaining=82;Limits.Secondary.Remaining=82;Limits.Primary.Reset-=60;Limits.Secondary.Reset-=60;InvalidateVisual();await System.Threading.Tasks.Task.Delay(65);
            if(!numericFrames.Values.Any(f=>f.Previous!=null&&!f.Roll)||ActualWidth>240&&!numericFrames.Values.Any(f=>f.Previous!=null&&f.Roll))throw new Exception("Missing percentage/countdown animation: "+theme.Id+" visible="+IsVisible+" loaded="+IsLoaded+" count="+numericFrames.Count+" active="+numericFrames.Values.Count(f=>f.Previous!=null)+" roll="+numericFrames.Values.Count(f=>f.Roll)+" layout="+numericLayout);
            if(theme.Id=="heroes3"){var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(System.IO.Path.Combine(output,"animation-midpoint.png")))png.Save(file);}
            await System.Threading.Tasks.Task.Delay(340);if(numericTimer.IsEnabled||numericFrames.Values.Any(f=>f.Previous!=null))throw new Exception("Animation did not settle: "+theme.Id);
        }}finally{SetSkin(originalSkin);Limits=originalLimits;InvalidateVisual();}
    }
    string numericFont;double numericSize;Brush numericInk;FontWeight numericWeight;
    void DrawNumericText(DrawingContext dc,string text,FormattedText ft,double x,double y,string font,double size,Brush ink,FontWeight weight){numericFont=font;numericSize=size;numericInk=ink;numericWeight=weight;NumericText(dc,text,ft,x,y);}
}
}
