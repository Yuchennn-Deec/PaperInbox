using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaperInbox {
  internal static class Design {
    public static Color Canvas {get{return SystemInformation.HighContrast?SystemColors.Window:Color.FromArgb(246,247,250);}}
    public static Color Paper {get{return SystemInformation.HighContrast?SystemColors.Window:Color.White;}}
    public static Color Ink {get{return SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(33,36,44);}}
    public static Color Muted {get{return SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(107,114,128);}}
    public static Color Line {get{return SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(230,233,239);}}
    public static Color Blue {get{return SystemInformation.HighContrast?SystemColors.Highlight:Color.FromArgb(0,113,235);}}
    static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
    static Font Cached(float size,FontStyle style){string key=size.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+style.ToString();Font font;if(!fonts.TryGetValue(key,out font)){font=new Font("Microsoft YaHei UI",size,style);fonts.Add(key,font);}return font;}
    public static Font Font(float size){return Cached(size,FontStyle.Regular);}
    public static Font Bold(float size){return Cached(size,FontStyle.Bold);}
    [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint param,out bool value,uint flags);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public static bool Animate {get{bool enabled;return !SystemInformation.HighContrast&&(!SystemParametersInfo(0x1042,0,out enabled,0)||enabled);}}
    public static void Window(Form form,string title,Size size,Size minimum) {
      form.Text=title;form.Size=size;form.MinimumSize=minimum;form.StartPosition=FormStartPosition.CenterParent;
      form.Font=Font(10);form.BackColor=Canvas;form.ForeColor=Ink;form.AutoScaleDimensions=new SizeF(96,96);form.AutoScaleMode=AutoScaleMode.Dpi;
      try{form.Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
      form.HandleCreated+=(s,e)=>{try{int rounded=2;DwmSetWindowAttribute(form.Handle,33,ref rounded,4);}catch{}};
      form.Shown+=(s,e)=>{Rectangle area=Screen.FromControl(form).WorkingArea;int inset=Unit(form,20);Size available=new Size(Math.Max(320,area.Width-inset),Math.Max(300,area.Height-inset));form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,available.Width),Math.Min(form.MinimumSize.Height,available.Height));form.Size=new Size(Math.Min(form.Width,available.Width),Math.Min(form.Height,available.Height));if(SystemInformation.HighContrast)Contrast(form);};
    }
    static void Contrast(Control parent){parent.BackColor=SystemColors.Window;parent.ForeColor=SystemColors.WindowText;Surface panel=parent as Surface;if(panel!=null){panel.FillColor=SystemColors.Window;panel.Border=true;}foreach(Control child in parent.Controls)Contrast(child);}
    public static GraphicsPath Round(RectangleF r,float radius){float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));GraphicsPath p=new GraphicsPath();if(d<=0){p.AddRectangle(r);return p;}p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static Color Blend(Color a,Color b,float t){t=Math.Max(0,Math.Min(1,t));return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
    public static Color Under(Control c){while(c!=null){Surface s=c as Surface;if(s!=null)return s.FillColor;c=c.Parent;}return Canvas;}
    public static Label Label(string text,float size,Color color){return new Label{Text=text,Font=Font(size),ForeColor=color,BackColor=Color.Transparent,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft};}
    public static Label Heading(string text,float size){Label l=Label(text,size,Ink);l.Font=Bold(size);return l;}
    public static int Unit(Control control,int value){using(Graphics g=control.CreateGraphics())return (int)Math.Round(value*g.DpiX/96f*control.Font.SizeInPoints/10f);}
    public static Surface Field(Control control,bool multiline) {
      Surface surface=new Surface{Dock=DockStyle.Fill,Radius=10,Border=true,Padding=new Padding(12,multiline?10:8,12,multiline?10:8),Margin=Padding.Empty};
      TextBox text=control as TextBox;if(text!=null){text.BorderStyle=BorderStyle.None;text.BackColor=Paper;text.ForeColor=Ink;text.Font=Font(10);text.Multiline=multiline;if(multiline)text.ScrollBars=ScrollBars.Vertical;}
      ComboBox combo=control as ComboBox;if(combo!=null){combo.FlatStyle=FlatStyle.Flat;combo.BackColor=Paper;combo.ForeColor=Ink;combo.Font=Font(10);}
      NumericUpDown number=control as NumericUpDown;if(number!=null){number.BorderStyle=BorderStyle.None;number.BackColor=Paper;number.ForeColor=Ink;number.Font=Font(10);}
      surface.Controls.Add(control);
      if(multiline)control.Dock=DockStyle.Fill;
      else {
        surface.MinimumSize=new Size(0,42);control.Dock=DockStyle.None;control.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;
        Action layout=()=>{int width=Math.Max(1,surface.ClientSize.Width-surface.Padding.Horizontal);int height=control.GetPreferredSize(new Size(width,0)).Height;control.SetBounds(surface.Padding.Left,Math.Max(surface.Padding.Top,(surface.ClientSize.Height-height)/2),width,height);};surface.Resize+=(s,e)=>layout();control.FontChanged+=(s,e)=>layout();layout();
      }
      control.Enter+=(s,e)=>{surface.FocusBorder=true;surface.Invalidate();};control.Leave+=(s,e)=>{surface.FocusBorder=false;surface.Invalidate();};return surface;
    }
    public static TableLayoutPanel Stack(int rows){TableLayoutPanel p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=rows,Margin=Padding.Empty};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return p;}
    public static void Document(Graphics g,Rectangle r,Color accent){g.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath p=Round(r,9))using(Brush b=new SolidBrush(Blend(accent,Paper,0.91f)))g.FillPath(b,p);using(Pen pen=new Pen(accent,1.6f)){int x=r.X+r.Width/4,y=r.Y+r.Height/4,w=r.Width/2,h=r.Height/2;g.DrawLines(pen,new[]{new Point(x,y),new Point(x+w-7,y),new Point(x+w,y+7),new Point(x+w,y+h),new Point(x,y+h),new Point(x,y)});g.DrawLine(pen,x+w-7,y,x+w-7,y+7);g.DrawLine(pen,x+w-7,y+7,x+w,y+7);g.DrawLine(pen,x+5,y+h-7,x+w-5,y+h-7);}}
  }
  internal sealed class Surface : Panel {
    public int Radius=18;public bool Border,FocusBorder;public Color FillColor=Design.Paper;
    public Surface(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Design.Canvas;}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Design.Under(Parent));}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;RectangleF r=new RectangleF(0.75f,0.75f,Math.Max(0,Width-1.5f),Math.Max(0,Height-1.5f));using(GraphicsPath path=Design.Round(r,Radius)){using(Brush b=new SolidBrush(FillColor))e.Graphics.FillPath(b,path);if(Border||FocusBorder)using(Pen pen=new Pen(FocusBorder?Design.Blue:Design.Line,FocusBorder?1.4f:1))e.Graphics.DrawPath(pen,path);}base.OnPaint(e);}
  }
  internal sealed class SoftButton : Button {
    public string Tone="secondary";public bool Selected;float hover,velocity;bool hot,down;readonly Timer animation=new Timer{Interval=16};
    public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Font=Design.Font(10);Cursor=Cursors.Hand;Height=42;Width=110;TabStop=true;animation.Tick+=(s,e)=>Step();}
    void Step(){float target=hot?1:0;velocity+=(target-hover)*0.14f;velocity*=0.55f;hover+=velocity;if(Math.Abs(target-hover)<0.005f&&Math.Abs(velocity)<0.005f){hover=target;animation.Stop();}Invalidate();}
    void Change(){if(!Design.Animate){hover=hot?1:0;Invalidate();}else animation.Start();}
    protected override void OnMouseEnter(EventArgs e){hot=true;Change();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hot=false;down=false;Change();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space){down=true;Invalidate();}base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){down=false;Invalidate();base.OnKeyUp(e);}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){Graphics g=e.Graphics;g.Clear(Design.Under(Parent));g.SmoothingMode=SmoothingMode.AntiAlias;bool primary=Tone=="primary",quiet=Tone=="quiet",nav=Tone=="nav";Color fill=primary?Design.Blue:quiet||nav?Design.Under(Parent):Color.FromArgb(237,240,246);Color foreground=primary?Color.White:Design.Ink;
      if(nav&&Selected){fill=Color.FromArgb(222,234,253);foreground=Design.Blue;}
      if(Enabled)fill=Design.Blend(fill,primary?Color.FromArgb(0,80,190):Color.FromArgb(215,224,238),down?0.55f:hover*0.42f);else{fill=primary?Color.FromArgb(218,227,240):Color.FromArgb(241,243,247);foreground=Color.FromArgb(155,163,176);}
      if(SystemInformation.HighContrast){fill=primary||Selected?SystemColors.Highlight:SystemColors.Control;foreground=primary||Selected?SystemColors.HighlightText:SystemColors.ControlText;if(!Enabled)foreground=SystemColors.GrayText;}
      RectangleF r=new RectangleF(1,1,Width-2,Height-2);if(down)r.Inflate(-1,-1);using(GraphicsPath p=Design.Round(r,10)){using(Brush b=new SolidBrush(fill))g.FillPath(b,p);if(Focused&&ShowFocusCues)using(Pen pen=new Pen(Design.Blue,2))g.DrawPath(pen,p);}
      Rectangle textRect=new Rectangle(nav?16:8,0,Width-(nav?28:16),Height);TextRenderer.DrawText(g,Text,Font,textRect,foreground,(nav?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
    protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
  }
  internal sealed class Toggle : CheckBox {
    public Toggle(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AutoSize=false;Height=42;Width=330;Font=Design.Font(10);Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.CheckButton;}
    protected override void OnCheckedChanged(EventArgs e){Invalidate();base.OnCheckedChanged(e);}
    protected override void OnPaint(PaintEventArgs e){Graphics g=e.Graphics;g.Clear(Design.Under(Parent));g.SmoothingMode=SmoothingMode.AntiAlias;RectangleF track=new RectangleF(Width-44,(Height-25)/2f,42,25);using(GraphicsPath p=Design.Round(track,13))using(Brush b=new SolidBrush(Checked?Design.Blue:Color.FromArgb(207,212,222)))g.FillPath(b,p);using(Brush b=new SolidBrush(Color.White))g.FillEllipse(b,Checked?Width-23:Width-41,(Height-19)/2f,19,19);TextRenderer.DrawText(g,Text,Font,new Rectangle(0,0,Width-60,Height),Design.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle);}
  }
  internal sealed class FileCard : CheckBox {
    public PendingFile File;bool hot;public FileCard(PendingFile file){File=file;Height=104;Width=700;Margin=new Padding(0,0,0,12);AutoSize=false;Cursor=Cursors.Hand;Font=Design.Font(10);AccessibleRole=AccessibleRole.CheckButton;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Text=file.Name;AccessibleName=file.Name;}
    public override Size GetPreferredSize(Size proposedSize){return new Size(Width,104);}
    protected override void OnMouseEnter(EventArgs e){hot=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hot=false;Invalidate();base.OnMouseLeave(e);}protected override void OnCheckedChanged(EventArgs e){Invalidate();base.OnCheckedChanged(e);}
    protected override void OnPaint(PaintEventArgs e){Graphics g=e.Graphics;g.Clear(Design.Canvas);g.SmoothingMode=SmoothingMode.AntiAlias;Color fill=Checked?Color.FromArgb(249,251,255):Design.Paper;if(hot)fill=Design.Blend(fill,Color.FromArgb(235,241,252),0.5f);if(SystemInformation.HighContrast)fill=Design.Paper;using(GraphicsPath p=Design.Round(new RectangleF(1,1,Width-3,Height-3),15)){using(Brush b=new SolidBrush(fill))g.FillPath(b,p);using(Pen pen=new Pen(Checked?Design.Blue:Design.Line,1))g.DrawPath(pen,p);}
      RectangleF check=new RectangleF(19,43,18,18);using(GraphicsPath p=Design.Round(check,5)){using(Brush b=new SolidBrush(Checked?Design.Blue:Design.Paper))g.FillPath(b,p);if(!Checked)using(Pen pen=new Pen(Color.FromArgb(193,200,214),1.3f))g.DrawPath(pen,p);}
      if(Checked)using(Pen pen=new Pen(Color.White,1.8f))g.DrawLines(pen,new[]{new Point(23,52),new Point(27,56),new Point(33,48)});
      Design.Document(g,new Rectangle(53,25,44,54),Color.FromArgb(220,98,101));
      string status=File.Status=="Review"?"待确认":File.Status=="Error"?"需要重试":File.Status=="Analyzing"?"分析中":File.Status=="Pending"?"待分析":"正在入库";Color accent=File.Status=="Review"?Color.FromArgb(30,140,100):File.Status=="Error"?Color.FromArgb(187,105,31):Design.Blue;
      int badgeWidth=86;Rectangle badge=new Rectangle(Width-badgeWidth-22,20,badgeWidth,27);if(SystemInformation.HighContrast)accent=Design.Ink;using(GraphicsPath p=Design.Round(badge,13))using(Brush b=new SolidBrush(Design.Blend(accent,Design.Paper,0.92f)))g.FillPath(b,p);TextRenderer.DrawText(g,status,Design.Font(9),badge,accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
      TextRenderer.DrawText(g,File.Name,Design.Bold(10),new Rectangle(113,21,Math.Max(50,Width-245),27),Design.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
      string hint=File.Status=="Review"&&File.Analysis!=null?string.Join(" · ",File.Analysis.collections??new string[0]):File.Message;
      TextRenderer.DrawText(g,(File.Size/1048576d).ToString("0.0")+" MB   ·   "+(hint??""),Design.Font(9),new Rectangle(113,55,Math.Max(50,Width-138),27),Design.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
      if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(7,7,Width-15,Height-15));}
  }
  internal sealed class EmptyInbox : Control {
    public EmptyInbox(){DoubleBuffered=true;BackColor=Design.Canvas;Dock=DockStyle.Fill;}
    protected override void OnPaint(PaintEventArgs e){Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int cx=Width/2,cy=Height/2-32;Design.Document(g,new Rectangle(cx-33,cy-56,66,80),Design.Blue);using(Pen p=new Pen(Color.FromArgb(181,198,224),2)){g.DrawLine(p,cx-50,cy+37,cx+50,cy+37);g.DrawLine(p,cx-50,cy+37,cx-57,cy+16);g.DrawLine(p,cx+50,cy+37,cx+57,cy+16);}TextRenderer.DrawText(g,"下一篇论文，会在这里等你。",Design.Bold(15),new Rectangle(0,cy+64,Width,38),Design.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);TextRenderer.DrawText(g,"下载完成后提醒，先分析，再决定是否入库。",Design.Font(10),new Rectangle(0,cy+108,Width,30),Design.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
  }
}
