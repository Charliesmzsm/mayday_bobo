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
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace MojoCarrot {
    public static class Program {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MojoCarrotDesktop");
        public static readonly int RestoreMessage = Native.RegisterWindowMessage("MojoCarrotDesktop.Restore.1");
        [STAThread] public static int Main(string[] args) {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                if (args.Length == 2 && args[0] == "--self-test") return MotionTests.Run(args[1]);
                if (args.Length == 2 && args[0] == "--legacy-self-test") return LegacyTests.Run(args[1]);
                if (args.Length == 2 && (args[0] == "--smoke-test" || args[0] == "--legacy-smoke-test")) {
                    using (PetForm pet = new PetForm(true, args[1], args[0] == "--legacy-smoke-test")) Application.Run(pet);
                    return File.Exists(args[1]) ? 0 : 1;
                }
                bool created;
                using (Mutex mutex = new Mutex(true, @"Local\MojoCarrotDesktop.1", out created)) {
                    if (!created) { Native.PostMessage(new IntPtr(0xffff), RestoreMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    try { using (PetForm pet = new PetForm(false, null, args.Length == 1 && args[0] == "--legacy")) Application.Run(pet); }
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
        public string RandomProfile = "standard";
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

    static class LegacyTests {
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
