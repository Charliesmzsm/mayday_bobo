using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MojoCarrot {
    public sealed partial class PetForm : Form {
        readonly Settings settings;
        readonly AnimationCatalog catalog;
        readonly FrameCache sprites;
        readonly IClock clock=new MonotonicClock();
        readonly Random random=new Random();
        readonly AnimationPlayer player;
        readonly ActionScheduler scheduler;
        readonly ClickArbiter clicks=new ClickArbiter(SystemInformation.DoubleClickTime);
        readonly Timer timer=new Timer();
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly NotifyIcon tray;
        readonly bool smoke;readonly string smokeReport;
        Point lastCursor,dragMouse,dragOrigin;
        long lastTick,lastPointer,lastManual,nextBlink,nextYawn,nextWalk,gazeAfter,walkUntil,gazeCandidateSince;
        double walkX;int walkDirection,gaze=-1,gazeCandidate=-1;
        bool paused,petHidden,locked,menuOpen,dragging,wasDragged,blocked,initialized,closing;
        string lastRender;Point lastRenderLocation;
        ToolStripMenuItem showItem,clickItem,pauseItem,surpriseItem;
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{CreateParams p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000;return p;}}
        public PetForm(bool test,string report):this(test,report,false){}
        public PetForm(bool test,string report,bool legacy) {
            smoke=test;smokeReport=report;settings=test?new Settings():Settings.Load();
            settings.SizePercent=Math.Max(60,Math.Min(200,settings.SizePercent));
            catalog=AnimationCatalog.Load(legacy);sprites=new FrameCache(catalog,settings.SizePercent);
            scheduler=new ActionScheduler(catalog,clock,random,settings.RandomProfile);
            player=new AnimationPlayer(clock);player.Completed+=ActionCompleted;Idle();
            Text="Mojo 萝卜桌宠";Name="MojoCarrotDesktop";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            ClientSize=new Size(sprites.Width,sprites.Height);TopMost=settings.Topmost;
            using(Stream icon=AnimationCatalog.OpenResource("mojo.ico"))Icon=new Icon(icon);
            Rectangle area=Screen.PrimaryScreen.WorkingArea;
            Location=settings.X==int.MinValue?new Point(area.Right-Width-40,area.Bottom-Height-20):Clamp(new Point(settings.X,settings.Y),Size);
            BuildMenu();
            tray=new NotifyIcon{Icon=Icon,Text="Mojo 萝卜 · 双击召回 / 右键菜单",ContextMenuStrip=menu,Visible=!smoke};
            tray.DoubleClick+=delegate{RestorePet();};
            timer.Interval=16;timer.Tick+=Tick;
            MouseDown+=OnMouseDown;MouseMove+=OnMouseMove;MouseUp+=OnMouseUp;
            MouseCaptureChanged+=delegate{if(dragging&&!Capture){dragging=false;clicks.Cancel();Idle();UpdateBlock();}};
            MouseDoubleClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left&&!wasDragged&&!paused&&!locked){dragging=false;Capture=false;UpdateBlock();clicks.DoubleClick();ManualDoubleClick();}};
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch+=SessionChanged;
        }
        protected override void OnShown(EventArgs e) {
            base.OnShown(e);initialized=true;SetClickThrough();lastCursor=Cursor.Position;ResetSchedule();RenderCurrent(true);timer.Start();
            if(catalog.FallbackReason!=null&&!smoke) {
                try{Directory.CreateDirectory(Program.DataDir);File.WriteAllText(Path.Combine(Program.DataDir,"animation-fallback.txt"),catalog.FallbackReason);}catch(IOException){}
                tray.ShowBalloonTip(4000,"Mojo 萝卜","新动作资源不可用，已使用旧版兼容动画。详情见本机 animation-fallback.txt。",ToolTipIcon.Info);
            }
        }
        void DisplayChanged(object s,EventArgs e){if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{Location=Clamp(Location,Size);RenderCurrent(true);});}
        void SessionChanged(object s,Microsoft.Win32.SessionSwitchEventArgs e){if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{locked=e.Reason!=Microsoft.Win32.SessionSwitchReason.SessionUnlock&&e.Reason!=Microsoft.Win32.SessionSwitchReason.SessionLogon;UpdateBlock();});}
        protected override void WndProc(ref Message m){if(m.Msg==Program.RestoreMessage){RestorePet();return;}if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
        public static Point Clamp(Point point,Size size){Rectangle work=Screen.FromRectangle(new Rectangle(point,size)).WorkingArea;return ClampTo(point,size,work);}
        public static Point ClampTo(Point point,Size size,Rectangle work){return new Point(Math.Max(work.Left,Math.Min(point.X,work.Right-size.Width)),Math.Max(work.Top,Math.Min(point.Y,work.Bottom-size.Height)));}
        void Idle(){player.Start(catalog["idle_breathe"],ActionOrigin.Idle);walkUntil=0;gaze=-1;lastRender=null;}
        void ResetSchedule(){long now=clock.Now;lastTick=lastPointer=lastManual=now;scheduler.Schedule();nextBlink=now+scheduler.Between(catalog.Scheduler.BlinkWait);nextYawn=now+scheduler.Between(catalog.Scheduler.YawnWait);nextWalk=now+random.Next(25000,45001);gazeAfter=now+catalog.Interaction.GazeResume;clicks.Cancel();}
        void UpdateBlock() {
            bool next=paused||petHidden||locked||menuOpen||dragging;
            if(next==blocked)return;
            blocked=next;player.SetFrozen(blocked);clicks.Cancel();
            if(!blocked){walkUntil=0;if(player.Origin==ActionOrigin.Walk)Idle();ResetSchedule();lastCursor=Cursor.Position;}
        }
        void ActionCompleted() {
            ActionOrigin origin=player.Origin;Idle();gazeAfter=clock.Now+catalog.Interaction.GazeResume;
            if(origin==ActionOrigin.Random||origin==ActionOrigin.Manual||origin==ActionOrigin.Walk)scheduler.Schedule();
            nextBlink=clock.Now+scheduler.Between(catalog.Scheduler.BlinkWait);
        }
        bool StartAction(string id,ActionOrigin origin) {
            if(!catalog.Has(id)||paused||locked||dragging)return false;
            if(origin!=ActionOrigin.Manual&&!player.IsIdle)return false;
            if(!scheduler.TryStart(id,origin!=ActionOrigin.Manual,origin==ActionOrigin.Random))return false;
            if(petHidden)RestorePet();
            if(origin==ActionOrigin.Manual&&(id=="hop_right"||id=="hop_left")){lastManual=clock.Now;BeginWalk(id=="hop_right"?1:-1,2400);return true;}
            walkUntil=0;player.Start(catalog[id],origin);gaze=-1;lastRender=null;
            if(origin==ActionOrigin.Manual){lastManual=clock.Now;scheduler.Schedule();}
            RenderCurrent(true);return true;
        }
        void ManualDoubleClick(){if(random.NextDouble()<catalog.Interaction.SurpriseProbability&&StartAction("surprise_star",ActionOrigin.Manual))return;StartAction("happy_bounce",ActionOrigin.Manual);}
        void Tick(object sender,EventArgs e) {
            long now=clock.Now,elapsed=Math.Min(100,Math.Max(0,now-lastTick));lastTick=now;
            if(smoke){SmokeTick();return;}
            Point pointer=Cursor.Position;if(pointer!=lastCursor){lastCursor=pointer;lastPointer=now;}
            if(blocked)return;
            player.Update();
            if(clicks.Poll(now))StartAction("wave",ActionOrigin.Manual);
            if(walkUntil!=0)UpdateWalk(now,elapsed);
            bool near=Rectangle.Inflate(Bounds,catalog.Scheduler.ExclusionMargin*settings.SizePercent/100,catalog.Scheduler.ExclusionMargin*settings.SizePercent/100).Contains(pointer);
            bool context=scheduler.ContextAllows(player.IsIdle,blocked,lastPointer,lastManual,near);
            if(settings.AutoActions&&context&&now>=scheduler.NextAction) {
                string id=scheduler.Choose();if(id==null||!StartAction(id,ActionOrigin.Random))scheduler.Defer();
            }
            if(settings.AutoActions&&now>=nextYawn) {
                nextYawn=now+scheduler.Between(catalog.Scheduler.YawnWait);
                if(context&&player.IsIdle&&now-lastPointer>=catalog.Scheduler.YawnQuiet&&random.NextDouble()<catalog.Scheduler.YawnProbability)StartAction("sleepy_yawn",ActionOrigin.Micro);
            }
            if(settings.AutoActions&&now>=nextBlink) {
                nextBlink=now+scheduler.Between(catalog.Scheduler.BlinkWait);
                if(context&&player.IsIdle&&now-lastPointer>2200)StartAction("blink",ActionOrigin.Micro);
            }
            if(settings.Wander&&context&&player.IsIdle&&now>=nextWalk) {
                nextWalk=now+random.Next(25000,45001);walkDirection=random.Next(2)==0?-1:1;
                BeginWalk(walkDirection,random.Next(1800,3501));
            }
            RenderCurrent(false);
        }
        void BeginWalk(int direction,int duration) {
            walkDirection=direction;walkX=Left;walkUntil=clock.Now+duration;gaze=-1;
            player.Start(catalog[direction>0?"hop_right":"hop_left"],ActionOrigin.Walk);
        }
        void UpdateWalk(long now,long elapsed) {
            Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;
            double min=area.Left,max=Math.Max(min,area.Right-Width);
            walkX=WalkMotion.Advance(walkX,walkDirection,elapsed,walkUntil-now,min,max);
            Location=ClampTo(new Point((int)Math.Round(walkX),Top),Size,area);
            if(now>=walkUntil||walkDirection>0&&max-walkX<.8||walkDirection<0&&walkX-min<.8){Idle();gazeAfter=now+catalog.Interaction.GazeResume;scheduler.Schedule();}
        }
        int GazeDirection(double dx,double dy) {
            int candidate=SpriteBank.Direction(dx,dy);
            if(gaze<0){gaze=candidate;return gaze;}
            double angle=(Math.Atan2(dx,-dy)*180/Math.PI+360)%360;
            double distance=Math.Abs(((angle-gaze*22.5+540)%360)-180);
            if(distance<14.25){gazeCandidate=-1;return gaze;}
            if(candidate!=gazeCandidate){gazeCandidate=candidate;gazeCandidateSince=clock.Now;}
            if(clock.Now-gazeCandidateSince>=70)gaze=candidate;return gaze;
        }
        void RenderCurrent(bool force) {
            string id=player.Current.Id;int frame=player.Frame;
            if(!smoke&&!blocked&&player.IsIdle&&settings.FollowMouse&&clock.Now>=gazeAfter&&clock.Now-lastPointer<2200) {
                Point p=Cursor.Position;double dx=p.X-(Left+Width/2.0),dy=p.Y-(Top+Height*.6),d=Math.Sqrt(dx*dx+dy*dy);
                if(d>45&&d<1000){id="gaze";frame=GazeDirection(dx,dy);}
            }
            string key=id+":"+frame;
            if(!force&&key==lastRender&&Location==lastRenderLocation)return;
            Native.Paint(Handle,sprites.GetFrame(id,frame),Location);lastRender=key;lastRenderLocation=Location;
        }
        void OnMouseDown(object sender,MouseEventArgs e) {
            lastManual=clock.Now;
            if(e.Button!=MouseButtons.Left)return;
            dragMouse=Cursor.Position;dragOrigin=Location;wasDragged=false;dragging=true;Capture=true;
            // Freeze on press; an actual drag cancels immediately on crossing the drag threshold.
            UpdateBlock();
        }
        void OnMouseMove(object sender,MouseEventArgs e) {
            if(!dragging)return;Point p=Cursor.Position;int dx=p.X-dragMouse.X,dy=p.Y-dragMouse.Y;
            if(Math.Abs(dx)+Math.Abs(dy)>4&&!wasDragged){wasDragged=true;clicks.Cancel();Idle();}
            if(wasDragged){Location=Clamp(new Point(dragOrigin.X+dx,dragOrigin.Y+dy),Size);RenderCurrent(true);}
        }
        void OnMouseUp(object sender,MouseEventArgs e) {
            if(e.Button==MouseButtons.Left) {
                bool moved=wasDragged;
                // Preserve a pending first-click decision across a second press/release.
                dragging=false;Capture=false;bool oldBlocked=blocked;blocked=paused||petHidden||locked||menuOpen;player.SetFrozen(blocked);
                if(oldBlocked&&!blocked){lastTick=clock.Now;scheduler.Schedule();}
                lastManual=clock.Now;clicks.Release(clock.Now,moved);
                if(moved){settings.X=Left;settings.Y=Top;Save();}gazeAfter=clock.Now+catalog.Interaction.GazeResume;
            }else if(e.Button==MouseButtons.Right)menu.Show(Cursor.Position);
        }
        void Save(){if(smoke)return;try{settings.Save();}catch(IOException){tray.ShowBalloonTip(2500,"Mojo 萝卜","设置未保存，请检查当前用户目录权限。",ToolTipIcon.Warning);}}
        ToolStripMenuItem Toggle(string text,bool value,Action<bool> setter){var item=new ToolStripMenuItem(text){Checked=value,CheckOnClick=true};item.CheckedChanged+=delegate{setter(item.Checked);Save();};return item;}
        void BuildMenu() {
            menu.Font=new Font("Microsoft YaHei UI",9);menu.Items.Add(new ToolStripMenuItem("Mojo 萝卜 · 1.1.2 离线桌宠"){Enabled=false});
            menu.Opening+=delegate{menuOpen=true;lastManual=clock.Now;UpdateBlock();if(surpriseItem!=null){long wait=scheduler.Remaining("surprise_star");surpriseItem.Enabled=wait==0;surpriseItem.Text=wait==0?"星星眼惊喜":"星星眼惊喜（冷却 "+(wait+999)/1000+" 秒）";}};
            menu.Closed+=delegate{menuOpen=false;UpdateBlock();};
            var actions=new ToolStripMenuItem("做个动作");
            foreach(ActionSpec action in catalog.Actions.Where(a=>a.Id!="gaze"&&a.Id!="idle_breathe")) {
                string id=action.Id;var item=new ToolStripMenuItem(action.Label);item.Click+=delegate{StartAction(id,ActionOrigin.Manual);};actions.DropDownItems.Add(item);if(id=="surprise_star")surpriseItem=item;
            }
            menu.Items.Add(actions);
            var profiles=new ToolStripMenuItem("随机动作频率");
            foreach(var pair in new[]{new[]{"quiet","安静（45–90 秒）"},new[]{"standard","标准（20–45 秒）"},new[]{"lively","活泼（12–25 秒）"}}) {
                string id=pair[0];var item=new ToolStripMenuItem(pair[1]){Tag=id,Checked=scheduler.Profile==id};
                item.Click+=delegate{settings.RandomProfile=id;scheduler.SetProfile(id);foreach(ToolStripMenuItem child in profiles.DropDownItems)child.Checked=(string)child.Tag==id;Save();};profiles.DropDownItems.Add(item);
            }
            menu.Items.Add(profiles);
            var sizes=new ToolStripMenuItem("桌宠大小");foreach(int n in new[]{60,75,100,125,150,200}){int size=n;var item=new ToolStripMenuItem(n+"%"){Tag=n,Checked=settings.SizePercent==n};item.Click+=delegate{ResizePet(size);foreach(ToolStripMenuItem child in sizes.DropDownItems)child.Checked=(int)child.Tag==size;Save();};sizes.DropDownItems.Add(item);}menu.Items.Add(sizes);
            menu.Items.Add(Toggle("跟随鼠标注视",settings.FollowMouse,delegate(bool value){settings.FollowMouse=value;gaze=-1;}));
            menu.Items.Add(Toggle("自动情绪与眨眼（保留轻呼吸）",settings.AutoActions,delegate(bool value){settings.AutoActions=value;if(!value&&(player.Origin==ActionOrigin.Random||player.Origin==ActionOrigin.Micro))Idle();ResetSchedule();}));
            menu.Items.Add(Toggle("允许左右散步",settings.Wander,delegate(bool value){settings.Wander=value;if(!value&&player.Origin==ActionOrigin.Walk)Idle();}));
            menu.Items.Add(Toggle("保持窗口置顶",settings.Topmost,delegate(bool value){settings.Topmost=value;TopMost=value;}));
            clickItem=Toggle("鼠标穿透（从托盘恢复）",settings.ClickThrough,delegate(bool value){settings.ClickThrough=value;if(initialized)SetClickThrough();});menu.Items.Add(clickItem);
            pauseItem=new ToolStripMenuItem("暂停全部动画"){CheckOnClick=true};pauseItem.CheckedChanged+=delegate{paused=pauseItem.Checked;UpdateBlock();};menu.Items.Add(pauseItem);
            showItem=new ToolStripMenuItem("隐藏桌宠");showItem.Click+=delegate{if(petHidden)RestorePet();else{petHidden=true;Hide();showItem.Text="显示桌宠";UpdateBlock();}};menu.Items.Add(showItem);
            menu.Items.Add("召回到鼠标所在屏幕",null,delegate{RestorePet();Rectangle w=Screen.FromPoint(Cursor.Position).WorkingArea;Location=Clamp(new Point(w.Right-Width-30,w.Bottom-Height-20),Size);RenderCurrent(true);});
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add(Toggle("登录 Windows 时启动",StartupEnabled(),SetStartup));
            menu.Items.Add("关于 / 操作说明",null,delegate{MessageBox.Show("Mojo 萝卜桌宠 1.1.2\n\n单击：叶片招手\n双击：开心跳跃，偶尔星星眼惊喜\n拖动：移动并取消当前动作\n惊喜至少间隔 3 分钟，10 分钟最多 2 次\n关闭自动动作后保留轻呼吸；暂停冻结全部动画\n\n"+(catalog.Has("surprise_star")?"新动作素材已加载":"当前使用旧图集兼容模式")+"\n设置："+Program.DataDir,"Mojo 萝卜",MessageBoxButtons.OK,MessageBoxIcon.Information);});
            menu.Items.Add("退出桌宠",null,delegate{Close();});
        }
        void ResizePet(int percent){int bottom=Bottom,center=Left+Width/2;settings.SizePercent=percent;sprites.Resize(percent);ClientSize=new Size(sprites.Width,sprites.Height);Location=Clamp(new Point(center-Width/2,bottom-Height),Size);lastRender=null;RenderCurrent(true);}
        static bool StartupEnabled(){try{using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&k.GetValue("MojoCarrotDesktop")!=null;}catch{return false;}}
        static void SetStartup(bool enabled){try{using(var k=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)k.SetValue("MojoCarrotDesktop","\""+Application.ExecutablePath+"\"");else k.DeleteValue("MojoCarrotDesktop",false);}}catch(Exception ex){MessageBox.Show(ex.Message,"无法修改登录启动");}}
        void SetClickThrough(){int style=Native.GetWindowLong(Handle,-20);style=settings.ClickThrough?style|0x20:style&~0x20;Native.SetWindowLong(Handle,-20,style);}
        void RestorePet(){petHidden=false;showItem.Text="隐藏桌宠";if(settings.ClickThrough){settings.ClickThrough=false;clickItem.Checked=false;SetClickThrough();}Location=Clamp(Location,Size);Show();UpdateBlock();RenderCurrent(true);}
        protected override void OnFormClosing(FormClosingEventArgs e){if(!closing){closing=true;timer.Stop();if(!smoke){settings.X=Left;settings.Y=Top;Save();}tray.Visible=false;}base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){if(disposing){Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;timer.Dispose();tray.Dispose();menu.Dispose();sprites.Dispose();}base.Dispose(disposing);}
    }
}
