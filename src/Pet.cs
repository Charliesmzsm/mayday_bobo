using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

[assembly: AssemblyTitle("Mojo 萝卜桌宠")]
[assembly: AssemblyDescription("独立离线桌面宠物")]
[assembly: AssemblyCompany("Mojo Carrot")]
[assembly: AssemblyProduct("Mojo 萝卜桌宠")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MojoCarrot {
    public static class Program {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MojoCarrotDesktop");
        public static readonly int RestoreMessage = Native.RegisterWindowMessage("MojoCarrotDesktop.Restore.1");
        [STAThread] public static int Main(string[] args) {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                if (args.Length == 2 && args[0] == "--self-test") return Tests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--smoke-test") {
                    using (PetForm pet = new PetForm(true, args[1])) Application.Run(pet);
                    return File.Exists(args[1]) ? 0 : 1;
                }
                bool created;
                using (Mutex mutex = new Mutex(true, @"Local\MojoCarrotDesktop.1", out created)) {
                    if (!created) { Native.PostMessage(new IntPtr(0xffff), RestoreMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    try { using (PetForm pet = new PetForm(false, null)) Application.Run(pet); }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) {
                try { string report=(args.Length==2)?args[1]+".error.txt":Path.Combine(DataDir,"last-error.txt"); Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report,ex.ToString()); } catch { }
                if (args.Length == 0) MessageBox.Show("桌宠启动失败：\n"+ex.Message,"Mojo 萝卜",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    public class Settings {
        public int X = int.MinValue, Y = int.MinValue;
        public int SizePercent = 100;
        public bool Topmost = true, FollowMouse = true, Wander = false, AutoActions = true, ClickThrough = false;
        public static Settings Load() {
            try { using (FileStream f = File.OpenRead(Path.Combine(Program.DataDir,"settings.xml"))) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); }
            catch { return new Settings(); }
        }
        public void Save() {
            Directory.CreateDirectory(Program.DataDir);
            string target=Path.Combine(Program.DataDir,"settings.xml"), temp=target+".tmp";
            using (FileStream f=File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(f,this);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp,target);
        }
    }

    public sealed class SpriteBank : IDisposable {
        public const int CellWidth=192, CellHeight=208;
        public static readonly string[] Names={"待机","向右蹦跳","向左蹦跳","叶片招手","开心跳跃","委屈","等待","思考","检查"};
        public static readonly int[][] Durations={
            new[]{280,110,110,140,140,320}, new[]{120,120,120,120,120,120,120,220},
            new[]{120,120,120,120,120,120,120,220},new[]{140,140,140,280},
            new[]{140,140,140,140,280},new[]{140,140,140,140,140,140,140,240},
            new[]{150,150,150,150,150,260},new[]{120,120,120,120,120,220},new[]{150,150,150,150,150,280}};
        readonly Bitmap atlas;
        readonly Bitmap[] frames=new Bitmap[88];
        public int Width {get;private set;} public int Height {get;private set;}
        public SpriteBank(int percent) {
            using (Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("sprites.png")) using (Bitmap source=new Bitmap(s)) atlas=new Bitmap(source);
            if (atlas.Width!=1536 || atlas.Height!=2288) throw new InvalidDataException("动画图集尺寸不正确");
            Resize(percent);
        }
        public void Resize(int percent) {
            percent=Math.Max(60,Math.Min(200,percent)); Width=CellWidth*percent/100; Height=CellHeight*percent/100;
            for(int i=0;i<frames.Length;i++) {
                if(frames[i]!=null) frames[i].Dispose();
                Bitmap b=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
                using(Graphics g=Graphics.FromImage(b)) {
                    g.CompositingMode=CompositingMode.SourceCopy;
                    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    using(ImageAttributes attributes=new ImageAttributes()) {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        // Crop first: interpolation must never sample a neighboring atlas cell.
                        using(Bitmap cell=atlas.Clone(new Rectangle((i%8)*192,(i/8)*208,192,208),PixelFormat.Format32bppArgb))
                            g.DrawImage(cell,new Rectangle(0,0,Width,Height),0,0,192,208,GraphicsUnit.Pixel,attributes);
                    }
                }
                frames[i]=b;
            }
        }
        public Bitmap At(int row,int col){ return frames[row*8+col]; }
        public static int Direction(double dx,double dy) {
            double deg=Math.Atan2(dx,-dy)*180.0/Math.PI;
            return ((int)Math.Floor((deg+360+11.25)/22.5))%16;
        }
        public void Dispose(){foreach(Bitmap b in frames) if(b!=null)b.Dispose();atlas.Dispose();}
    }

    public sealed class PetForm : Form {
        readonly Settings settings;
        readonly SpriteBank sprites;
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly Random random=new Random();
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly NotifyIcon tray;
        readonly bool smoke;
        readonly string smokeReport;
        int state=0, frame=0, lastSprite=-1, smokeStep=-1;
        long due, lastTick, lastMouseMove, nextAction, walkUntil, suspendUntil;
        Point lastCursor, dragMouse, dragOrigin;
        bool dragging, wasDragged, paused, petHidden, initialized, closing, suppressClick;
        double walkX;
        ToolStripMenuItem pauseItem,showItem,clickItem;
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{CreateParams p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000;return p;}}
        public PetForm(bool isSmoke,string report) {
            smoke=isSmoke;smokeReport=report;settings=smoke?new Settings():Settings.Load();
            settings.SizePercent=Math.Max(60,Math.Min(200,settings.SizePercent));
            sprites=new SpriteBank(settings.SizePercent);
            Text="Mojo 萝卜桌宠"; Name="MojoCarrotDesktop";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            ClientSize=new Size(sprites.Width,sprites.Height);TopMost=settings.Topmost;
            using(Stream icon=Assembly.GetExecutingAssembly().GetManifestResourceStream("mojo.ico")) Icon=new Icon(icon);
            Rectangle area=Screen.PrimaryScreen.WorkingArea;
            Location=settings.X==int.MinValue?new Point(area.Right-Width-40,area.Bottom-Height-20):Clamp(new Point(settings.X,settings.Y),Size);
            if(smoke) Location=new Point(area.Right-Width-10,area.Bottom-Height-10);
            BuildMenu();
            tray=new NotifyIcon{Icon=Icon,Text="Mojo 萝卜 · 双击召回 / 右键菜单",ContextMenuStrip=menu,Visible=!smoke};
            tray.DoubleClick+=delegate{RestorePet();};
            timer.Interval=30;timer.Tick+=Tick;
            MouseDown+=OnMouseDown;MouseMove+=OnMouseMove;MouseUp+=OnMouseUp;
            MouseDoubleClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left){suppressClick=true;Play(4);}};
            SystemEventsSubscribe();
        }
        void SystemEventsSubscribe(){Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;Microsoft.Win32.SystemEvents.SessionSwitch+=SessionChanged;}
        void DisplayChanged(object sender,EventArgs e){if(!IsDisposed && IsHandleCreated)BeginInvoke((Action)delegate{Location=Clamp(Location,Size);});}
        void SessionChanged(object sender,Microsoft.Win32.SessionSwitchEventArgs e){if(e.Reason==Microsoft.Win32.SessionSwitchReason.SessionUnlock && IsHandleCreated)BeginInvoke((Action)delegate{lastTick=clock.ElapsedMilliseconds;});}
        protected override void OnShown(EventArgs e){base.OnShown(e);initialized=true;SetClickThrough();lastCursor=Cursor.Position;due=clock.ElapsedMilliseconds+280;nextAction=clock.ElapsedMilliseconds+25000;lastTick=clock.ElapsedMilliseconds;Render(0,0);timer.Start();}
        protected override void WndProc(ref Message m){if(m.Msg==Program.RestoreMessage){RestorePet();return;}if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
        public static Point Clamp(Point point,Size size){
            Rectangle probe=new Rectangle(point,size);Rectangle work=Screen.FromRectangle(probe).WorkingArea;
            return new Point(Math.Max(work.Left,Math.Min(point.X,work.Right-size.Width)),Math.Max(work.Top,Math.Min(point.Y,work.Bottom-size.Height)));
        }
        void Render(int row,int col){int index=row*8+col;if(index==lastSprite && initialized)return;Native.Paint(Handle,sprites.At(row,col),Location);lastSprite=index;}
        void ForceRender(){int current=lastSprite<0?0:lastSprite;lastSprite=-1;Render(current/8,current%8);}
        void Play(int row){state=row;frame=0;due=clock.ElapsedMilliseconds+SpriteBank.Durations[row][0];walkUntil=0;suspendUntil=clock.ElapsedMilliseconds+1800;lastSprite=-1;if(petHidden)RestorePet();Render(state,frame);}
        void Tick(object sender,EventArgs e){
            long now=clock.ElapsedMilliseconds;long elapsed=Math.Min(100,Math.Max(0,now-lastTick));lastTick=now;
            if(smoke){
                int step=(int)(now/160);
                if(step!=smokeStep){smokeStep=step;if(step<9) {state=step;frame=0;Render(state,0);}else if(step<25)Render(9+(step-9)/8,(step-9)%8);else {
                    timer.Stop();
                    File.WriteAllText(smokeReport+".trace.txt","begin interactions\r\n");
                    MouseEventArgs click=new MouseEventArgs(MouseButtons.Left,1,Width/2,Height/2,0);
                    OnMouseDown(this,click);OnMouseUp(this,click);
                    if(state!=3)throw new Exception("single click did not wave");
                    OnMouseDown(this,click);OnMouseDoubleClick(new MouseEventArgs(MouseButtons.Left,2,Width/2,Height/2,0));OnMouseUp(this,click);
                    if(state!=4)throw new Exception("double click jump was overwritten");
                    foreach(int percent in new[]{60,100,200}){sprites.Resize(percent);ClientSize=new Size(sprites.Width,sprites.Height);ForceRender();}
                    settings.ClickThrough=true;SetClickThrough();if((Native.GetWindowLong(Handle,-20)&0x20)==0)throw new Exception("click-through flag missing");
                    RestorePet();if((Native.GetWindowLong(Handle,-20)&0x20)!=0)throw new Exception("tray restore did not disable click-through");
                    petHidden=true;Hide();RestorePet();if(!Visible)throw new Exception("restore hidden pet failed");
                    File.AppendAllText(smokeReport+".trace.txt","interactions passed; menu begin\r\n");menu.Show(Cursor.Position);Application.DoEvents();using(Bitmap image=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(image,new Rectangle(0,0,menu.Width,menu.Height));image.Save(Path.Combine(Path.GetDirectoryName(smokeReport),"menu-preview.png"));}menu.Close();File.AppendAllText(smokeReport+".trace.txt","menu passed\r\n");
                    File.WriteAllText(smokeReport,"PASS: native layered window; 9 animation rows + 16 look directions; single-click wave; double-click jump; 60/100/200% native resize; click-through and tray recovery; hide/show recovery; menu render. OS="+Environment.OSVersion+"; CLR="+Environment.Version);Close();}}
                return;
            }
            if(paused||petHidden||dragging)return;
            Point cursor=Cursor.Position;
            if(cursor!=lastCursor){lastCursor=cursor;lastMouseMove=now;}
            if(walkUntil>now){
                walkX+=(state==1?1:-1)*elapsed*.055;
                Point dest=Clamp(new Point((int)Math.Round(walkX),Top),Size);
                if(dest.X!=(int)Math.Round(walkX)){walkUntil=0;state=0;frame=0;due=now+280;}Location=dest;
            } else if(walkUntil!=0){walkUntil=0;state=0;frame=0;due=now+280;suspendUntil=now+700;}
            if(now-due>3000)due=now;
            while(now>=due){
                frame++;
                if(frame>=SpriteBank.Durations[state].Length){frame=0;if(state!=0&&walkUntil<=now)state=0;}
                due+=SpriteBank.Durations[state][frame];
            }
            if(state==0 && settings.FollowMouse && now-lastMouseMove<2200 && now>suspendUntil){
                double dx=cursor.X-(Left+Width/2.0),dy=cursor.Y-(Top+Height*.6);double distance=Math.Sqrt(dx*dx+dy*dy);
                if(distance>45 && distance<1000){int d=SpriteBank.Direction(dx,dy);Render(9+d/8,d%8);}else Render(0,6);
            }else Render(state,frame);
            if(now>=nextAction){
                nextAction=now+random.Next(25000,45000);
                if(state==0 && !Bounds.Contains(cursor) && now-lastMouseMove>2500){
                    if(settings.Wander && random.Next(3)==0){state=random.Next(2)+1;frame=0;walkUntil=now+random.Next(1800,3500);walkX=Left;due=now+120;}
                    else if(settings.AutoActions){int[] choices={3,4,6,7,8};Play(choices[random.Next(choices.Length)]);}
                }
            }
        }
        void OnMouseDown(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){dragMouse=Cursor.Position;dragOrigin=Location;dragging=true;wasDragged=false;Capture=true;walkUntil=0;}}
        void OnMouseMove(object sender,MouseEventArgs e){if(!dragging)return;Point p=Cursor.Position;int dx=p.X-dragMouse.X,dy=p.Y-dragMouse.Y;if(Math.Abs(dx)+Math.Abs(dy)>4)wasDragged=true;Location=Clamp(new Point(dragOrigin.X+dx,dragOrigin.Y+dy),Size);}
        void OnMouseUp(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){bool moved=wasDragged;dragging=false;Capture=false;if(moved){settings.X=Left;settings.Y=Top;Save();}else if(suppressClick){suppressClick=false;}else Play(3);suspendUntil=clock.ElapsedMilliseconds+1800;}else if(e.Button==MouseButtons.Right)menu.Show(Cursor.Position);}
        void Save(){if(smoke)return;try{settings.Save();}catch{tray.ShowBalloonTip(2500,"Mojo 萝卜","设置未保存，请检查当前用户目录权限。",ToolTipIcon.Warning);}}
        ToolStripMenuItem Toggle(string text,bool value,Action<bool> setter){ToolStripMenuItem item=new ToolStripMenuItem(text){Checked=value,CheckOnClick=true};item.CheckedChanged+=delegate{setter(item.Checked);Save();};return item;}
        void BuildMenu(){
            menu.Font=new Font("Microsoft YaHei UI",9);
            ToolStripMenuItem title=new ToolStripMenuItem("Mojo 萝卜 · 离线桌宠"){Enabled=false};menu.Items.Add(title);
            ToolStripMenuItem actions=new ToolStripMenuItem("做个动作");
            int[] states={3,4,7,8,6,5,1,2};foreach(int id in states){int row=id;actions.DropDownItems.Add(SpriteBank.Names[row],null,delegate{Play(row);});}menu.Items.Add(actions);
            ToolStripMenuItem sizes=new ToolStripMenuItem("桌宠大小");foreach(int n in new[]{60,75,100,125,150,200}){int percent=n;ToolStripMenuItem item=new ToolStripMenuItem(n+"% "+(n==100?"（原始大小）":"")){Tag=n,Checked=settings.SizePercent==n};item.Click+=delegate{settings.SizePercent=percent;sprites.Resize(percent);ClientSize=new Size(sprites.Width,sprites.Height);Location=Clamp(Location,Size);foreach(ToolStripMenuItem child in sizes.DropDownItems)child.Checked=(int)child.Tag==percent;ForceRender();Save();};sizes.DropDownItems.Add(item);}menu.Items.Add(sizes);
            menu.Items.Add(Toggle("跟随鼠标注视",settings.FollowMouse,delegate(bool value){settings.FollowMouse=value;lastSprite=-1;}));
            menu.Items.Add(Toggle("偶尔自己做动作",settings.AutoActions,delegate(bool value){settings.AutoActions=value;}));
            menu.Items.Add(Toggle("允许左右散步",settings.Wander,delegate(bool value){settings.Wander=value;if(!value)walkUntil=0;}));
            menu.Items.Add(Toggle("保持窗口置顶",settings.Topmost,delegate(bool value){settings.Topmost=value;TopMost=value;}));
            clickItem=Toggle("鼠标穿透（从托盘恢复）",settings.ClickThrough,delegate(bool value){settings.ClickThrough=value;if(initialized)SetClickThrough();});menu.Items.Add(clickItem);
            pauseItem=new ToolStripMenuItem("暂停动画"){CheckOnClick=true};pauseItem.CheckedChanged+=delegate{paused=pauseItem.Checked;if(!paused){due=clock.ElapsedMilliseconds+100;lastTick=clock.ElapsedMilliseconds;}};menu.Items.Add(pauseItem);
            showItem=new ToolStripMenuItem("隐藏桌宠");showItem.Click+=delegate{if(petHidden)RestorePet();else{petHidden=true;Hide();showItem.Text="显示桌宠";}};menu.Items.Add(showItem);
            menu.Items.Add("召回到鼠标所在屏幕",null,delegate{RestorePet();Rectangle w=Screen.FromPoint(Cursor.Position).WorkingArea;Location=Clamp(new Point(w.Right-Width-30,w.Bottom-Height-20),Size);});
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem startup=Toggle("登录 Windows 时启动",StartupEnabled(),delegate(bool enabled){SetStartup(enabled);});menu.Items.Add(startup);
            menu.Items.Add("关于 / 操作说明",null,delegate{MessageBox.Show("Mojo 萝卜桌宠 1.0.0\n\n拖动：移动位置\n单击：叶片招手\n双击：开心跳跃\n右键或托盘：动作、大小、暂停、隐藏、退出\n\n全部动画在本地运行，无需账号或网络。\n设置位置："+Program.DataDir,"Mojo 萝卜",MessageBoxButtons.OK,MessageBoxIcon.Information);});
            menu.Items.Add("退出桌宠",null,delegate{Close();});
        }
        static bool StartupEnabled(){try{using(Microsoft.Win32.RegistryKey k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null && k.GetValue("MojoCarrotDesktop")!=null;}catch{return false;}}
        static void SetStartup(bool enabled){try{using(Microsoft.Win32.RegistryKey k=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)k.SetValue("MojoCarrotDesktop","\""+Application.ExecutablePath+"\"");else k.DeleteValue("MojoCarrotDesktop",false);}}catch(Exception ex){MessageBox.Show("无法修改登录启动设置："+ex.Message,"Mojo 萝卜");}}
        void SetClickThrough(){int style=Native.GetWindowLong(Handle,-20);style=settings.ClickThrough?style|0x20:style&~0x20;Native.SetWindowLong(Handle,-20,style);}
        void RestorePet(){petHidden=false;showItem.Text="隐藏桌宠";if(settings.ClickThrough){settings.ClickThrough=false;clickItem.Checked=false;SetClickThrough();}Location=Clamp(Location,Size);Show();ForceRender();}
        protected override void OnFormClosing(FormClosingEventArgs e){if(!closing){closing=true;timer.Stop();if(!smoke){settings.X=Left;settings.Y=Top;Save();}tray.Visible=false;}base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){if(disposing){Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;timer.Dispose();tray.Dispose();menu.Dispose();sprites.Dispose();}base.Dispose(disposing);}
    }

    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;public Point(int x,int y){X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential)] public struct Size {public int W,H;public Size(int w,int h){W=w;H=h;}}
        [StructLayout(LayoutKind.Sequential,Pack=1)] public struct Blend {public byte Op,Flags,Alpha,Format;}
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr hdc,ref Point dst,ref Size size,IntPtr src,ref Point origin,uint key,ref Blend blend,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int n);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h,int n,int value);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int RegisterWindowMessage(string text);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
        public static void Paint(IntPtr hwnd,Bitmap image,System.Drawing.Point location){
            IntPtr screen=GetDC(IntPtr.Zero),memory=IntPtr.Zero,bitmap=IntPtr.Zero,old=IntPtr.Zero;
            try{memory=CreateCompatibleDC(screen);bitmap=image.GetHbitmap(Color.FromArgb(0));old=SelectObject(memory,bitmap);Point dst=new Point(location.X,location.Y),origin=new Point(0,0);Size size=new Size(image.Width,image.Height);Blend blend=new Blend{Op=0,Flags=0,Alpha=255,Format=1};if(!UpdateLayeredWindow(hwnd,screen,ref dst,ref size,memory,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
            finally{if(old!=IntPtr.Zero)SelectObject(memory,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(memory!=IntPtr.Zero)DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen);}
        }
    }

    static class Tests {
        static void Require(bool valid,string name){if(!valid)throw new Exception("FAILED: "+name);}
        public static int Run(string output){
            Directory.CreateDirectory(output);
            using(SpriteBank bank=new SpriteBank(100)){
                int populated=0;for(int row=0;row<11;row++)for(int col=0;col<8;col++){
                    bool used=row<9?col<SpriteBank.Durations[row].Length || row==0&&col==6:true;
                    Bitmap b=bank.At(row,col);int count=0;for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).A>0)count++;
                    Require(used?count>1000:count==0,"atlas occupancy "+row+","+col);if(used)populated++;
                }
                Require(populated==74,"74 used cells");
                for(int d=0;d<16;d++){double angle=d*Math.PI/8;Require(SpriteBank.Direction(Math.Sin(angle)*100,-Math.Cos(angle)*100)==d,"gaze direction "+d);}
                Require(SpriteBank.Direction(-.01,-100)==0,"gaze wraps 360");
                using(Bitmap contact=new Bitmap(960,624))using(Graphics g=Graphics.FromImage(contact)){
                    g.Clear(Color.FromArgb(242,239,232));for(int i=0;i<9;i++)g.DrawImage(bank.At(i,0),(i%5)*192,(i/5)*208);for(int i=0;i<5;i++)g.DrawImage(bank.At(9,i),i*192,416);contact.Save(Path.Combine(output,"render-check.png"));
                }
                bank.Resize(200);Require(bank.Width==384&&bank.Height==416,"resize200");bank.Resize(60);Require(bank.Width==115&&bank.Height==124,"resize60");
                Rectangle area=Screen.PrimaryScreen.WorkingArea;Point clamped=PetForm.Clamp(new Point(area.Right+10000,area.Bottom+10000),new System.Drawing.Size(192,208));Require(Screen.FromPoint(clamped).WorkingArea.Contains(clamped),"offscreen restore");
            }
            File.WriteAllText(Path.Combine(output,"self-test.txt"),"PASS: embedded atlas 1536x2288, 74 populated cells / 14 empty, 16 gaze angles including wrap, alpha decode, 60%-200% resize, off-screen recovery.\r\nNo network or account component.\r\n");return 0;
        }
    }
}
