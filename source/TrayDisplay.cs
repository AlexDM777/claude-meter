using System;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
namespace ClaudeMeter {
public static class TrayDisplay {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public static Quota Selected(Limits limits) { return limits.Windows.FirstOrDefault(q=>q.Minutes==10080)??limits.Windows.LastOrDefault(); }
    public static string Value(Limits limits) { var q=Selected(limits);return q==null?"—":Math.Round(q.Remaining,MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    public static bool Dark(Skin skin){return skin!=null&&(skin.IsCustom?skin.IsDark:skin.Id=="glass-night"||skin.Kind=="space"&&skin.Id!="imperial");}
    public static Color Background(Skin skin,bool stale) {
        Color color=skin!=null&&!string.IsNullOrEmpty(skin.Fill)?ColorTranslator.FromHtml(skin.Fill):skin!=null&&skin.Id=="beskar"?Color.FromArgb(41,39,36):skin!=null&&skin.Id=="pilot"?Color.FromArgb(6,23,14):skin!=null&&skin.Id=="imperial"?Color.FromArgb(217,218,213):skin!=null&&skin.Id=="glass-day"?Color.FromArgb(232,241,250):skin!=null&&skin.Id=="glass-night"?Color.FromArgb(36,52,68):skin!=null&&skin.Id=="lcd-blue"?Color.FromArgb(195,226,250):skin!=null&&skin.Kind=="analog"?Color.FromArgb(250,210,139):Color.FromArgb(213,234,193);
        if(!stale)return color;
        Color ink=Foreground(skin);
        return Color.FromArgb((color.R*85+ink.R*15)/100,(color.G*85+ink.G*15)/100,(color.B*85+ink.B*15)/100);
    }
    public static Color Foreground(Skin skin) { return skin==null?Color.FromArgb(24,49,57):ColorTranslator.FromHtml(skin.Ink); }
    public static Color Accent(Skin skin){
        if(skin!=null&&!string.IsNullOrEmpty(skin.Accent))return ColorTranslator.FromHtml(skin.Accent);
        switch(skin==null?"":skin.Id){
            case "lcd-green":return Color.FromArgb(38,112,81);
            case "lcd-blue":return Color.FromArgb(35,103,174);
            case "tape":return Color.FromArgb(146,83,17);
            case "glass-day":return Color.FromArgb(0,115,213);
            case "glass-night":return Color.FromArgb(118,201,244);
            case "beskar":return Color.FromArgb(242,175,67);
            case "pilot":return Color.FromArgb(116,243,75);
            case "imperial":return Color.FromArgb(0,99,144);
            default:return Foreground(skin);
        }
    }
    public static Color NumberColor(Skin skin,double? remaining){return !remaining.HasValue?Foreground(skin):remaining.Value<10?(Dark(skin)?Color.FromArgb(255,159,148):Color.FromArgb(177,42,30)):remaining.Value<20?(Dark(skin)?Color.FromArgb(255,205,136):Color.FromArgb(148,76,0)):Foreground(skin);}
    public static Icon CreateIcon(string text,bool stale,Skin skin=null,double? remaining=null) {
        using(var bitmap=new Bitmap(64,64))using(var g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            using(var path=new GraphicsPath()) {
                path.AddArc(2,2,16,16,180,90);path.AddArc(46,2,16,16,270,90);path.AddArc(46,46,16,16,0,90);path.AddArc(2,46,16,16,90,90);path.CloseFigure();
                using(var fill=new SolidBrush(Background(skin,stale)))g.FillPath(fill,path);
                using(var pen=new Pen(Foreground(skin),2))g.DrawPath(pen,path);
            }
            float size=text.Length>=3?25:39;
            using(var font=new Font("Segoe UI",size,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(NumberColor(skin,remaining)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
                g.DrawString(text,font,brush,new RectangleF(2,-1,60,64),format);
            IntPtr handle=bitmap.GetHicon();try{using(var icon=Icon.FromHandle(handle))return (Icon)icon.Clone();}finally{DestroyIcon(handle);}
        }
    }
    public static string Details(Limits limits,bool stale,bool loggedIn,DateTime updated,string status) {
        var text=new StringBuilder("ClaudeMeter • "+limits.PlanLabel);
        if(!loggedIn)text.Append("\nТребуется вход в Claude Code");
        foreach(var q in limits.Windows) {
            text.Append("\n\n").Append(q.Label("Лимит")).Append(": осталось ").Append(Math.Round(q.Remaining,MidpointRounding.AwayFromZero)).Append("%");
            text.Append("\nСброс: ").Append(q.ResetText).Append("");
            text.Append("\nЧерез: ").Append(q.Countdown);
        }
        if(limits.Windows.Count==0)text.Append("\n\nДанные лимитов недоступны");
        if(limits.ModelName.Length>0)text.Append("\n\n").Append(limits.ModelName).Append(": осталось ").Append(limits.ModelValue);else if(limits.ResetCount.HasValue)text.Append("\n\nДоступно сбросов: ").Append(limits.ResetCount.Value);
        if(limits.CreditLine.Length>0)text.Append("\n").Append(limits.CreditLine);

        if(!string.IsNullOrEmpty(status))text.Append("\n").Append(status);
        text.Append("\n\nДвойной щелчок — показать экран\nПравая кнопка — меню");return text.ToString();
    }
}
public class TrayActionButton : Control {
    public Func<Color> IconColor;public bool RefreshGlyph;bool hover;
    public TrayActionButton(){SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);BackColor=Color.Transparent;Size=new Size(40,40);Cursor=Cursors.Hand;TabStop=true;}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Color color=IconColor==null?Color.White:IconColor();if(hover||Focused)color=Color.FromArgb((color.R*55+255*45)/100,(color.G*55+255*45)/100,(color.B*55+255*45)/100);if(!Enabled)color=Color.FromArgb(100,color);using(var pen=new Pen(color,1.8f)){if(RefreshGlyph){g.DrawArc(pen,10,10,20,20,40,290);g.DrawLines(pen,new[]{new PointF(30,9),new PointF(30,17),new PointF(22,17)});}else{for(int i=0;i<8;i++){double angle=i*Math.PI/4;g.DrawLine(pen,20+(float)Math.Cos(angle)*9,20+(float)Math.Sin(angle)*9,20+(float)Math.Cos(angle)*12,20+(float)Math.Sin(angle)*12);}g.DrawEllipse(pen,11,11,18,18);g.DrawEllipse(pen,16,16,8,8);}if(Focused){using(var focus=new Pen(Color.FromArgb(120,color),1)){focus.DashStyle=DashStyle.Dot;g.DrawRectangle(focus,3,3,33,33);}}}}
}
public class TrayHover : Form {
    public event Action SettingsRequested;public event Action UpdateRequested;
    readonly TrayActionButton updateButton=new TrayActionButton{RefreshGlyph=true};string updateText="";bool updateAvailable,updateBusy;
    readonly TrayActionButton settingsButton=new TrayActionButton(); readonly ToolTip settingsTip=new ToolTip(); DateTime outsideSince=DateTime.MinValue;
    readonly Timer timer=new Timer{Interval=120};Point anchor;
    Skin skin;Limits limits=new Limits();string account="",status="";bool loggedIn,stale;DateTime updated;
    Image texture;string texturePath="";
    int decorPadding;
    public static int DecorPadding(Skin theme){return theme!=null&&theme.Kind=="space"?(theme.Number>=9?64:48):12;}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;p.ClassStyle|=0x20000;return p;}}
    public TrayHover(){
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;ClientSize=new Size(500,350);
        settingsButton.IconColor=()=>TrayDisplay.Foreground(skin);settingsButton.AccessibleName=Lang.T("Настройки");
        updateButton.IconColor=()=>TrayDisplay.Accent(skin);updateButton.AccessibleName=Lang.T("Обновить программу");updateButton.Click+=delegate{if(UpdateRequested!=null)UpdateRequested();};Controls.Add(updateButton);updateButton.Visible=false;
        settingsButton.Click+=delegate{Dismiss();if(SettingsRequested!=null)SettingsRequested();};Controls.Add(settingsButton);
        timer.Tick+=delegate{var point=Cursor.Position;int radius=Math.Max(30,SystemInformation.SmallIconSize.Width+10);if(Bounds.Contains(point)||(Math.Abs(point.X-anchor.X)<=radius&&Math.Abs(point.Y-anchor.Y)<=radius)){outsideSince=DateTime.MinValue;return;}if(outsideSince==DateTime.MinValue)outsideSince=DateTime.UtcNow;else if((DateTime.UtcNow-outsideSince).TotalMilliseconds>650)Dismiss();};
    }
    protected override void OnShown(EventArgs e){base.OnShown(e);ActiveControl=null;}
    static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public void UpdateData(Skin theme,Limits values,string name,bool signedIn,bool isStale,DateTime lastUpdated,string message){
        skin=theme;limits=values??new Limits();account=name??"";loggedIn=signedIn;stale=isStale;updated=lastUpdated;status=message??"";
        string file=skin!=null&&skin.Kind!="glass"?skin.Background:"";
        if(file!=texturePath){if(texture!=null){texture.Dispose();texture=null;}texturePath=file;if(file.Length>0)texture=SkinImage.Bitmap(file);}
        decorPadding=DecorPadding(skin);int targetWidth=500+2*decorPadding,targetHeight=170+Math.Max(1,limits.Windows.Count)*154+2*decorPadding+(limits.CreditLine.Length>0?25:0);
        if(ClientSize.Width!=targetWidth||ClientSize.Height!=targetHeight){ClientSize=new Size(targetWidth,targetHeight);using(var path=Round(new RectangleF(0,0,Width,Height),20)){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}
        int creditHeight=limits.CreditLine.Length>0?25:0;
        int footerY=78+Math.Max(1,limits.Windows.Count)*154+40+creditHeight;
        settingsButton.Location=new Point(decorPadding+440,decorPadding+footerY);settingsTip.SetToolTip(settingsButton,Lang.T("Настройки"));settingsButton.Invalidate();
        updateButton.Location=new Point(decorPadding+392,decorPadding+footerY);updateButton.Invalidate();
        if(Visible)Location=Place(anchor,Size,Screen.GetWorkingArea(anchor));Invalidate();
    }
    void DrawLabel(Graphics g,string text,RectangleF rect,float size,Color color,bool bold=false,StringAlignment align=StringAlignment.Near,bool mono=false){
        using(var font=new Font(skin!=null&&skin.Id=="heroes3"?"Palatino Linotype":(mono||skin!=null&&skin.Id=="pilot")?"Consolas":skin!=null&&skin.IsCustom?(skin.Font=="serif"?"Georgia":skin.Font=="mono"?"Consolas":"Segoe UI"):skin!=null&&skin.Kind=="analog"?"Georgia":"Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
        using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,rect,format);
    }
    void Rule(Graphics g,int y,Color ink){using(var pen=new Pen(Color.FromArgb(42,ink),1))g.DrawLine(pen,24,y,476,y);}
    void Bar(Graphics g,Quota q,RectangleF rect,Color ink,Color accent,bool segments,bool night){
        if(skin!=null&&skin.Id=="heroes3"){
            using(var fill=new SolidBrush(Color.FromArgb(36,22,14)))g.FillRectangle(fill,rect);
            using(var pen=new Pen(Color.FromArgb(199,166,83),1.5f))g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
            var inner=RectangleF.Inflate(rect,-3,-3);double f=Math.Max(0,Math.Min(1,q.Remaining/100));if(f>0){var state=g.Save();g.SetClip(new RectangleF(inner.X,inner.Y,(float)(inner.Width*f),inner.Height));using(var gold=new LinearGradientBrush(inner,Color.FromArgb(232,206,132),Color.FromArgb(139,98,32),90))g.FillRectangle(gold,inner);using(var pen=new Pen(Color.FromArgb(100,78,48,16),1))for(int j=1;j<20;j++)g.DrawLine(pen,inner.X+j*inner.Width/20,inner.Y,inner.X+j*inner.Width/20,inner.Bottom);g.Restore(state);}return;
        }
        using(var path=Round(rect,8))using(var brush=new SolidBrush(Color.FromArgb(night?80:30,ink)))g.FillPath(brush,path);
        double fraction=Math.Max(0,Math.Min(1,q.Remaining/100));
        if(segments){const int n=24;float gap=2,w=(rect.Width-8-(n-1)*gap)/n;for(int i=0;i<n;i++){var r=new RectangleF(rect.X+4+i*(w+gap),rect.Y+4,w,rect.Height-8);using(var path=Round(r,2))using(var brush=new SolidBrush(Color.FromArgb(25,ink)))g.FillPath(brush,path);float f=(float)Math.Max(0,Math.Min(1,fraction*n-i));if(f>0){var state=g.Save();g.SetClip(new RectangleF(r.X,r.Y,r.Width*f,r.Height));using(var path=Round(r,2))using(var brush=new LinearGradientBrush(r,Color.FromArgb(170,accent),accent,90))g.FillPath(brush,path);g.Restore(state);}}}
        else if(fraction>0){using(var path=Round(rect,8)){var state=g.Save();g.SetClip(path);using(var fill=new LinearGradientBrush(rect,Color.FromArgb(190,accent),accent,0f))g.FillRectangle(fill,rect.X,rect.Y,(float)(rect.Width*fraction),rect.Height);g.Restore(state);}}

    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
        bool night=TrayDisplay.Dark(skin),segmented=skin!=null&&(skin.Kind=="lcd"||skin.Kind=="space");
        Color ink=TrayDisplay.Foreground(skin),background=TrayDisplay.Background(skin,false);
        Color accent=TrayDisplay.Accent(skin);
        using(var fill=new LinearGradientBrush(ClientRectangle,background,night?Color.FromArgb(27,39,51):Color.FromArgb(246,249,252),90))g.FillRectangle(fill,ClientRectangle);
        if(texture!=null){g.DrawImage(texture,ClientRectangle);using(var wash=new SolidBrush(Color.FromArgb(skin!=null&&skin.Id=="heroes3"?110:225,background)))g.FillRectangle(wash,ClientRectangle);}
        var contentState=g.Save();g.TranslateTransform(decorPadding,decorPadding);
        Color muted=Color.FromArgb((ink.R*3+background.R*2)/5,(ink.G*3+background.G*2)/5,(ink.B*3+background.B*2)/5);
        string name=loggedIn?(string.IsNullOrEmpty(account)?Lang.T("Аккаунт"):account):Lang.T("не выполнен вход");
        DrawLabel(g,name,new RectangleF(28,20,318,34),19,ink,true);
        var pill=new RectangleF(360,25,112,26);using(var path=Round(pill,13))using(var brush=new SolidBrush(Color.FromArgb(18,ink)))g.FillPath(brush,path);DrawLabel(g,limits.PlanLabel,pill,12,ink,true,StringAlignment.Center);
        int y=78;
        foreach(var q in limits.Windows){
            var card=new RectangleF(16,y,468,140);if(skin!=null&&skin.Id=="heroes3"){using(var fill=new SolidBrush(Color.FromArgb(210,40,24,16)))g.FillRectangle(fill,card);using(var pen=new Pen(Color.FromArgb(145,174,139,69),1))g.DrawRectangle(pen,card.X,card.Y,card.Width,card.Height);}else using(var path=Round(card,16))using(var brush=new SolidBrush(Color.FromArgb(night?28:115,Color.White)))g.FillPath(brush,path);
            DrawLabel(g,q.Label("Лимит"),new RectangleF(32,y+10,260,24),14,muted,true);
            DrawLabel(g,Math.Round(q.Remaining,MidpointRounding.AwayFromZero)+"%",new RectangleF(344,y+7,124,30),24,TrayDisplay.NumberColor(skin,q.Remaining),true,StringAlignment.Far);
            DrawLabel(g,Lang.T("Сброс через")+" "+q.Countdown,new RectangleF(32,y+40,436,32),23,ink,true);
            Bar(g,q,new RectangleF(32,y+81,436,16),ink,accent,segmented,night);
            DrawLabel(g,Lang.T("Сброс")+": "+q.ResetText,new RectangleF(32,y+105,436,23),12,muted);
            y+=154;
        }
        if(limits.Windows.Count==0){DrawLabel(g,loggedIn?Lang.T("Лимиты пока недоступны"):Lang.T("Требуется вход в Claude Code"),new RectangleF(28,y,444,120),20,ink);y+=154;}
        DrawLabel(g,limits.ExtraLabel,new RectangleF(28,y,358,30),15,muted);
        DrawLabel(g,limits.ExtraValue,new RectangleF(388,y-3,84,36),28,accent,true,StringAlignment.Far);
        if(limits.CreditLine.Length>0){DrawLabel(g,limits.CreditLine,new RectangleF(28,y+30,444,25),14,ink);y+=25;}
        if(!string.IsNullOrEmpty(updateText))DrawLabel(g,updateText,new RectangleF(28,y+43,354,24),13,updateAvailable?accent:muted,updateAvailable);
        string notice=Lang.T(status);DrawLabel(g,notice,new RectangleF(28,y+69,354,20),12,muted);
        g.Restore(contentState);
        using(var path=Round(new RectangleF(.5f,.5f,Width-1,Height-1),20))using(var pen=new Pen(Color.FromArgb(night?65:35,ink),1))g.DrawPath(pen,path);
    }
    public void SetUpdate(string message,bool available,bool busy){updateText=message??"";updateAvailable=available;updateBusy=busy;updateButton.Visible=available||busy;updateButton.Enabled=available&&!busy;settingsTip.SetToolTip(updateButton,Lang.T("Обновить программу"));Invalidate();}
    public static Point Place(Point cursor,Size size,Rectangle area){return new Point(Math.Max(area.Left,Math.Min(cursor.X-size.Width/2,area.Right-size.Width)),Math.Max(area.Top,Math.Min(cursor.Y-size.Height-14,area.Bottom-size.Height)));}
    public void Present(){outsideSince=DateTime.MinValue;if(Visible)return;anchor=Cursor.Position;Location=Place(anchor,Size,Screen.GetWorkingArea(anchor));Show();Location=Place(anchor,Size,Screen.GetWorkingArea(anchor));timer.Start();}
    public void Dismiss(){timer.Stop();Hide();}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();settingsTip.Dispose();if(texture!=null)texture.Dispose();}base.Dispose(disposing);}
}
}
