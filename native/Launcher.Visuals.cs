using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MokoLauncher {
    public class MokoButton : Button {
        public bool Primary { get; set; }
        private bool hover;
        public MokoButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor=Cursors.Hand; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0;
            Font=new Font("맑은 고딕",10,FontStyle.Bold); ForeColor=Color.FromArgb(224,239,220);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        public static GraphicsPath Round(RectangleF r,float radius){
            float d=radius*2;var p=new GraphicsPath();
            p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            var r=new RectangleF(1,1,Width-3,Height-3);
            Color top=Primary?Color.FromArgb(186,238,131):Color.FromArgb(36,58,43);
            Color bottom=Primary?Color.FromArgb(134,199,96):Color.FromArgb(28,47,36);
            if(hover&&Enabled){top=Primary?Color.FromArgb(204,250,157):Color.FromArgb(50,76,53);}
            if(!Enabled){top=bottom=Color.FromArgb(39,50,42);}
            using(var p=Round(r,Primary?15:9))using(var b=new LinearGradientBrush(r,top,bottom,90))using(var pen=new Pen(Primary?Color.FromArgb(192,238,157):Color.FromArgb(64,86,66))){g.FillPath(b,p);g.DrawPath(pen,p);}
            var color=!Enabled?Color.FromArgb(122,144,123):Primary?Color.FromArgb(24,49,26):ForeColor;
            TextRenderer.DrawText(g,Text,Font,new Rectangle(4,2,Width-8,Height-4),color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if(Focused&&Enabled)ControlPaint.DrawFocusRectangle(g,new Rectangle(8,8,Width-16,Height-16),color,Color.Transparent);
        }
    }
    public class MainWindow : Form {
        public readonly MokoButton PlayButton=new MokoButton(),LoginButton=new MokoButton(),InstallButton=new MokoButton(),SettingsButton=new MokoButton(),SaveButton=new MokoButton();
        public readonly TextBox ServerInput=new TextBox(),PatchInput=new TextBox();
        private readonly Image background,downloading,connecting,logo;
        private readonly EventHandler frameChanged;
        private readonly Timer animationTimer=new Timer();
        private string mode="Idle",statusTitle="함께 놀 준비를 해볼까?",statusMessage="처음 한 번 계정을 연결하고, 게임 시작을 눌러 주세요.";
        private bool settingsOpen, downloadAnimating, resourcesDisposed;
        private int progressTick;
        private readonly string dataPath;
        public string PublicDomain=GitHubDistribution.ServerDomain;
        public int AnimationFrames { get; private set; }
        public string CurrentMode { get{return mode;} }
        public bool SettingsOpen {
            get{return settingsOpen;}
            set {settingsOpen=value;ServerInput.Visible=PatchInput.Visible=SaveButton.Visible=value;SettingsButton.Text=value?"설정 닫기":"접속 설정";Invalidate();}
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
        public MainWindow(string assets,string data){
            dataPath=data;Text="뫀코코의 마을 · Minecraft Launcher";ClientSize=new Size(860,580);
            StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
            AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(16,31,24);ForeColor=Color.FromArgb(237,244,229);
            Font=new Font("맑은 고딕",10);Icon=new Icon(Path.Combine(assets,"app.ico"));
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);
            background=Image.FromFile(Path.Combine(assets,"background.gif"));downloading=Image.FromFile(Path.Combine(assets,"downloading.gif"));
            connecting=Image.FromFile(Path.Combine(assets,"connecting.png"));logo=Image.FromFile(Path.Combine(assets,"icon.png"));
            frameChanged=delegate {
                if(IsHandleCreated&&!IsDisposed){try{BeginInvoke((MethodInvoker)delegate{if(!IsDisposed){AnimationFrames++;Invalidate(new Rectangle(380,103,480,289));}});}catch(InvalidOperationException){}}
            };
            ImageAnimator.Animate(background,frameChanged);
            PlayButton.Text="게임 시작  ▶";PlayButton.Primary=true;PlayButton.Font=new Font("맑은 고딕",20,FontStyle.Bold);PlayButton.SetBounds(548,435,280,88);
            LoginButton.Text="계정 연결";LoginButton.SetBounds(32,500,120,36);
            InstallButton.Text="패치 점검";InstallButton.SetBounds(162,500,120,36);
            SettingsButton.Text="접속 설정";SettingsButton.SetBounds(292,500,120,36);
            SaveButton.Text="주소 저장";SaveButton.SetBounds(696,334,132,36);SaveButton.Visible=false;
            ServerInput.SetBounds(154,196,674,32);PatchInput.SetBounds(154,250,674,32);
            foreach(var t in new[]{ServerInput,PatchInput}){t.Font=new Font("맑은 고딕",12);t.BackColor=Color.FromArgb(29,47,36);t.ForeColor=ForeColor;t.BorderStyle=BorderStyle.FixedSingle;t.Visible=false;}
            Controls.AddRange(new Control[]{PlayButton,LoginButton,InstallButton,SettingsButton,ServerInput,PatchInput,SaveButton});
            AcceptButton=PlayButton;
            animationTimer.Interval=60;animationTimer.Tick+=delegate {if(mode=="Patching"){progressTick=(progressTick+12)%900;Invalidate(new Rectangle(32,400,796,5));}};
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);try{int on=1;DwmSetWindowAttribute(Handle,20,ref on,sizeof(int));}catch{}}
        public void SetMode(string next,string title,string message){
            mode=next;statusTitle=title;statusMessage=message;
            if(mode=="Patching"){
                if(!downloadAnimating){ImageAnimator.Animate(downloading,frameChanged);downloadAnimating=true;}
                animationTimer.Start();PlayButton.Text="준비 중…";
            }else{
                if(downloadAnimating){ImageAnimator.StopAnimate(downloading,frameChanged);downloadAnimating=false;}
                animationTimer.Stop();PlayButton.Text="게임 시작  ▶";
            }
            Invalidate();
        }
        public void SetMessage(string message){if(statusMessage!=message){statusMessage=message;Invalidate(new Rectangle(28,420,510,68));}}
        private void TextAt(Graphics g,string text,float size,FontStyle style,Color color,RectangleF rect){
            using(var f=new Font("맑은 고딕",size,style))using(var b=new SolidBrush(color))using(var sf=new StringFormat()){sf.Trimming=StringTrimming.EllipsisCharacter;g.DrawString(text,f,b,rect,sf);}
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
            ImageAnimator.UpdateFrames(background);if(downloadAnimating)ImageAnimator.UpdateFrames(downloading);
            // The supplied GIF remains the animated backdrop, with a dark fade under the controls.
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(background,new Rectangle(540,104,288,288));
            using(var fade=new LinearGradientBrush(new Rectangle(400,104,428,288),Color.FromArgb(235,16,31,24),Color.FromArgb(20,16,31,24),0f)){g.FillRectangle(fade,400,104,428,288);}
            using(var top=new SolidBrush(Color.FromArgb(245,16,31,24)))g.FillRectangle(top,0,0,860,104);
            g.DrawImage(logo,new Rectangle(32,24,56,56));
            var white=Color.FromArgb(237,244,229);var muted=Color.FromArgb(161,185,159);var green=Color.FromArgb(193,231,156);
            TextAt(g,"뫀코코의 마을",23,FontStyle.Bold,white,new RectangleF(104,20,520,42));
            TextAt(g,"MINECRAFT JAVA 1.21.8 · LAUNCHER 3.0",9,FontStyle.Regular,muted,new RectangleF(107,64,400,23));
            using(var pen=new Pen(Color.FromArgb(48,69,48))){g.DrawLine(pen,32,103,828,103);}
            if(settingsOpen){
                using(var b=new SolidBrush(Color.FromArgb(249,16,31,24)))g.FillRectangle(b,0,104,860,288);
                TextAt(g,"접속 설정",20,FontStyle.Bold,white,new RectangleF(32,125,680,42));
                TextAt(g,"서버 주소",11,FontStyle.Regular,muted,new RectangleF(32,201,120,28));
                TextAt(g,"패치 주소",11,FontStyle.Regular,muted,new RectangleF(32,255,120,28));
                TextAt(g,"한글 도메인도 그대로 입력할 수 있어요.",10,FontStyle.Regular,green,new RectangleF(154,298,660,26));
                TextAt(g,"설치 폴더  "+dataPath,8,FontStyle.Regular,muted,new RectangleF(32,344,650,38));
            }else if(mode=="Launching"){
                // Center the original GOOD artwork at a smaller display size.
                using(var b=new SolidBrush(Color.FromArgb(235,16,31,24)))g.FillRectangle(b,0,104,860,288);
                using(var sf=new StringFormat()){sf.Alignment=StringAlignment.Center;
                    g.DrawImage(connecting,new Rectangle(366,144,128,128));
                    using(var f=new Font("맑은 고딕",17,FontStyle.Bold))using(var b=new SolidBrush(green))g.DrawString("게임에서 만나!",f,b,new RectangleF(150,288,560,38),sf);
                    using(var f=new Font("맑은 고딕",12,FontStyle.Bold))using(var b=new SolidBrush(white))g.DrawString(PublicDomain,f,b,new RectangleF(150,330,560,28),sf);
                    using(var f=new Font("맑은 고딕",9))using(var b=new SolidBrush(muted))g.DrawString("JAVA 1.21.8   ·   GitHub 자동 패치",f,b,new RectangleF(150,365,560,22),sf);
                }
            }else{
                TextAt(g,"우리끼리 함께하는 작은 세계",10,FontStyle.Bold,green,new RectangleF(32,130,430,26));
                TextAt(g,"오늘도\n같이 놀자!",32,FontStyle.Bold,white,new RectangleF(27,160,440,116));
                TextAt(g,PublicDomain,15,FontStyle.Bold,green,new RectangleF(32,295,420,36));
                TextAt(g,"JAVA 1.21.8   ·   GitHub 자동 패치",9,FontStyle.Regular,muted,new RectangleF(32,346,430,26));
                if(mode=="Patching"){
                    using(var p=MokoButton.Round(new RectangleF(476,126,352,254),20))using(var b=new SolidBrush(Color.FromArgb(250,30,45,26)))g.FillPath(b,p);
                    g.DrawImage(downloading,new Rectangle(502,149,298,186));
                    TextAt(g,"잠깐만! 준비하고 있어요",12,FontStyle.Bold,green,new RectangleF(520,341,285,30));
                }
            }
            using(var b=new SolidBrush(Color.FromArgb(16,31,24)))g.FillRectangle(b,0,392,860,188);
            using(var b=new SolidBrush(Color.FromArgb(45,66,45)))g.FillRectangle(b,32,400,796,3);
            if(mode=="Patching"){
                int x=32+progressTick-110;var clip=g.Clip;g.SetClip(new Rectangle(32,400,796,3));
                using(var b=new SolidBrush(green))g.FillRectangle(b,x,400,110,3);g.Clip=clip;clip.Dispose();
            }
            TextAt(g,statusTitle,13,FontStyle.Bold,mode=="Error"?Color.FromArgb(255,172,143):white,new RectangleF(32,420,496,28));
            TextAt(g,statusMessage,9,FontStyle.Regular,muted,new RectangleF(32,453,484,42));
            TextAt(g,"Microsoft 계정으로 연결 · 친구들과 함께하는 서버",8,FontStyle.Regular,muted,new RectangleF(32,551,796,23));
        }
        protected override void Dispose(bool disposing){
            if(disposing&&!resourcesDisposed){resourcesDisposed=true;animationTimer.Stop();animationTimer.Dispose();ImageAnimator.StopAnimate(background,frameChanged);if(downloadAnimating)ImageAnimator.StopAnimate(downloading,frameChanged);background.Dispose();downloading.Dispose();connecting.Dispose();logo.Dispose();if(Icon!=null)Icon.Dispose();}
            base.Dispose(disposing);
        }
    }
}
