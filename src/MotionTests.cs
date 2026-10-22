using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace MojoCarrot {
    public sealed class TestClock : IClock {public long Value;public long Now {get{return Value;}}}
    public static class MotionTests {
        static int count;
        public static void Require(bool valid,string message){if(!valid)throw new Exception("FAILED: "+message);count++;}
        static void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Require(rejected,message);}
        [DllImport("user32.dll")]public static extern int GetGuiResources(IntPtr process,int flag);
        static AnimationCatalog Fresh(){return AnimationCatalog.Load(false);}
        public static int Run(string output) {
            Directory.CreateDirectory(output);count=0;LegacyTests.Run(Path.Combine(output,"legacy"));
            AnimationCatalog c=Fresh();Require(c.Has("surprise_star")&&c.FallbackReason==null,"new resources loaded");
            Require(c.Actions.Sum(a=>a.Frames.Length)==519,"438 animation frames plus 81 gaze poses");
            Require(c["gaze"].IsGrid&&c["gaze"].Frames.Length==81,"9 by 9 gaze lookup");
            Require(c["hop_right"].Duration==800&&c["hop_left"].Duration==800,"48 frames at 60fps last 800ms");
            Require(c["surprise_star"].Duration==2000,"48 frames last exactly 2 seconds");
            Require(c["surprise_star"].Durations.Contains(41)&&c["surprise_star"].Durations.Contains(42),"24fps fractional timing");
            foreach(ActionSpec a in c.Actions) {
                long t=0;for(int i=0;i<a.Frames.Length;i++){Require(a.FrameAt(t)==i,"frame boundary "+a.Id+":"+i);Require(a.FrameAt(t+a.Durations[i]-1)==i,"frame end "+a.Id+":"+i);t+=a.Durations[i];}
                Require(a.FrameAt(t)==(a.Loop?0:a.Frames.Length-1),"clip boundary "+a.Id);
            }
            var invalid=Fresh();invalid["wave"].Frames[0].Rect[0]=999999;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject out-of-page rect");
            invalid=Fresh();invalid["wave"].Frames[0].Pivot=new[]{-1,0};Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject invalid pivot");
            invalid=Fresh();invalid["wave"].Durations[0]=0;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject zero duration");
            invalid=Fresh();invalid["gaze"].GridCols=8;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject malformed gaze grid");
            invalid=Fresh();invalid["hop_right"].RootX=new[]{0.0};Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject root count");
            invalid=Fresh();invalid["hop_right"].RootX[4]=Double.NaN;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject nonfinite root curve");
            invalid=Fresh();invalid["hop_right"].RootX[10]=-1;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject reversed root curve");
            invalid=Fresh();invalid["surprise_star"].Cooldown=0;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject weakened surprise cooldown");
            invalid=Fresh();invalid.Scheduler.Profiles[0].Wait=new[]{100,1};Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject invalid range");
            invalid=Fresh();Reject(()=>invalid.Validate(delegate(string name){return null;}),"reject missing resources");
            invalid=Fresh();invalid.Actions[0]=null;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject null action");
            invalid=Fresh();invalid["wave"].Frames[0]=null;Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject null frame");
            using(var nullJson=new MemoryStream(Encoding.UTF8.GetBytes("null")))Reject(()=>AnimationCatalog.Read(nullJson),"reject null configuration");
            Require(AnimationCatalog.Load(true).Has("wave")&&!AnimationCatalog.Load(true).Has("surprise_star"),"explicit old atlas compatibility");
            invalid=Fresh();invalid["wave"].Pages[0].AlphaMode="unknown";Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject unknown alpha mode");
            invalid=Fresh();invalid["wave"].Pages[1].Resource=invalid["wave"].Pages[0].Resource;invalid["wave"].Pages[1].AlphaMode="straight";Reject(()=>invalid.Validate(AnimationCatalog.OpenResource),"reject conflicting alpha modes");
            using(var encoded=new Bitmap(2,2,System.Drawing.Imaging.PixelFormat.Format32bppArgb))using(var png=new MemoryStream()) {
                encoded.SetPixel(0,0,Color.FromArgb(128,50,40,30));encoded.SetPixel(1,0,Color.FromArgb(255,80,60,40));
                encoded.Save(png,System.Drawing.Imaging.ImageFormat.Png);byte[] bytes=png.ToArray();
                using(var stream=new MemoryStream(bytes))using(Bitmap decoded=FrameCache.DecodePage(stream,new PageSpec{Width=2,Height=2,AlphaMode="premultiplied"})) {
                    Color c0=decoded.GetPixel(0,0),c1=decoded.GetPixel(1,0),c2=decoded.GetPixel(0,1);
                    Require(c0.A==128&&Math.Abs(c0.R-100)<=1&&Math.Abs(c0.G-80)<=1&&Math.Abs(c0.B-60)<=1,"premultiplied source decoded once");
                    Require(c1.ToArgb()==Color.FromArgb(255,80,60,40).ToArgb()&&c2.A==0,"opaque and transparent pixels preserved");
                    using(var bg=new Bitmap(2,2))using(Graphics g=Graphics.FromImage(bg)) {
                        g.Clear(Color.Black);g.DrawImageUnscaled(decoded,0,0);Color composite=bg.GetPixel(0,0);
                        Require(Math.Abs(composite.R-50)<=1&&Math.Abs(composite.G-40)<=1&&Math.Abs(composite.B-30)<=1,"no double multiplication at transparent edge");
                    }
                }
                using(var stream=new MemoryStream(bytes))using(Bitmap decoded=FrameCache.DecodePage(stream,new PageSpec{Width=2,Height=2})) {
                    Color c0=decoded.GetPixel(0,0);Require(c0.A==128&&Math.Abs(c0.R-50)<=1,"missing alpha mode keeps straight-alpha compatibility");
                }
            }
            var time=new TestClock();var player=new AnimationPlayer(time);int completed=0;player.Completed+=delegate{completed++;};
            player.Start(c["surprise_star"],ActionOrigin.Manual);time.Value=1999;player.Update();Require(completed==0,"no early completion");
            time.Value=8000;player.Update();player.Update();Require(completed==1,"skipped frames complete exactly once");
            player.Start(c["surprise_star"],ActionOrigin.Random);time.Value+=500;int frozenFrame=player.Frame;player.SetFrozen(true);time.Value+=600000;player.Update();Require(player.Frame==frozenFrame&&completed==1,"pause freezes playback without completion");
            player.SetFrozen(false);Require(player.Frame==frozenFrame,"resume no catchup");time.Value+=1500;player.Update();Require(completed==2,"completion after remaining active time");
            player.Start(c["idle_breathe"],ActionOrigin.Idle);time.Value+=100000;player.Update();Require(completed==2,"loop has no completion event");
            time.Value=0;var scheduler=new ActionScheduler(c,time,new Random(12),"standard");
            Require(scheduler.NextAction>=20000&&scheduler.NextAction<=45000,"standard interval");
            Require(scheduler.TryStart("surprise_star",true,false),"first manual surprise");
            Require(!scheduler.TryStart("surprise_star",false,false),"manual cannot bypass surprise cooldown");
            time.Value=179999;Require(scheduler.Remaining("surprise_star")==1,"cooldown boundary");
            time.Value=180000;Require(scheduler.TryStart("surprise_star",false,false),"second surprise at cooldown");
            time.Value=360000;Require(!scheduler.TryStart("surprise_star",false,false)&&scheduler.Remaining("surprise_star")==240000,"rolling ten-minute cap");
            time.Value=600000;Require(scheduler.TryStart("surprise_star",true,false),"rolling window expiry");
            var filtered=Fresh();foreach(var a in filtered.Actions)a.Weight=a.Id=="surprise_star"?10000:a.Id=="wave"?1:0;
            var filterScheduler=new ActionScheduler(filtered,time,new Random(8),"standard");filterScheduler.TryStart("surprise_star",false,false);
            for(int i=0;i<100;i++)Require(filterScheduler.Choose()=="wave","filter cooldown before weighted sample");
            Require(!scheduler.ContextAllows(true,false,time.Value-2999,0,false),"pointer grace");
            Require(!scheduler.ContextAllows(true,false,0,time.Value-4999,false),"manual grace");
            Require(!scheduler.ContextAllows(true,true,0,0,false)&&!scheduler.ContextAllows(false,false,0,0,false)&&!scheduler.ContextAllows(true,false,0,0,true),"blocked/action/near exclusion");
            long oldDue=scheduler.NextAction;time.Value+=900000;scheduler.Schedule();Require(scheduler.NextAction>time.Value&&scheduler.NextAction!=oldDue,"discard missed schedule");
            foreach(string profile in new[]{"quiet","lively","standard"}){scheduler.SetProfile(profile);var range=c.Scheduler.Profiles.First(p=>p.Id==profile).Wait;Require(scheduler.NextAction-time.Value>=range[0]&&scheduler.NextAction-time.Value<=range[1],"profile "+profile);}
            time.Value=0;var weighted=new ActionScheduler(c,time,new Random(31415),"standard");var counts=new Dictionary<string,int>();
            for(int i=0;i<100000;i++){string id=weighted.Choose();if(!counts.ContainsKey(id))counts[id]=0;counts[id]++;}
            foreach(ActionSpec a in c.Actions.Where(a=>a.Weight>0))Require(Math.Abs(counts[a.Id]/100000.0-a.Weight/100.0)<.01,"unfiltered weight "+a.Id);
            var history=new ActionScheduler(c,time,new Random(45),"standard");var recent=new Queue<string>();
            for(int i=0;i<1000;i++){time.Value+=1000000;string id=history.Choose();Require(id!=null&&!recent.Contains(id),"exclude recent two when pool available");Require(history.TryStart(id,true,true),"eligible weighted start");recent.Enqueue(id);if(recent.Count>2)recent.Dequeue();}
            var reduced=Fresh();foreach(var a in reduced.Actions)a.Weight=a.Id=="wave"||a.Id=="think"?1:0;
            time.Value=0;var small=new ActionScheduler(reduced,time,new Random(2),"standard");string first=small.Choose();small.TryStart(first,true,true);time.Value=1000000;string second=small.Choose();small.TryStart(second,true,true);time.Value=2000000;Require(small.Choose()==first,"relax second-last but not last");
            reduced=Fresh();foreach(var a in reduced.Actions)a.Weight=a.Id=="wave"?1:0;
            var one=new ActionScheduler(reduced,time,new Random(2),"standard");one.TryStart("wave",false,true);time.Value+=1000000;Require(one.Choose()==null,"never repeat last when only candidate");
            var click=new ClickArbiter(500);click.Release(0,false);Require(!click.Poll(499)&&click.Poll(500)&&!click.Poll(1000),"single click delayed exactly once");
            click.Release(0,false);click.DoubleClick();click.Release(100,false);Require(!click.Poll(1000),"double click suppresses wave");
            click.Release(0,false);click.Release(50,true);Require(!click.Poll(1000),"drag cancels pending click");
            click.Release(0,false);click.Cancel();Require(!click.Poll(1000),"menu/pause cancels click");
            double x=99.5;for(int i=0;i<100;i++)x=WalkMotion.Advance(x,1,16,2000,0,100);Require(x<=100&&x>=99.5,"walk stays at right boundary");
            x=.5;for(int i=0;i<100;i++)x=WalkMotion.Advance(x,-1,16,2000,0,100);Require(x>=0&&x<=.5,"walk stays at left boundary");
            Require(PetForm.ClampTo(new Point(-999,999),new Size(192,208),new Rectangle(-100,0,800,600))==new Point(-100,392),"negative monitor and lower boundary");
            var gazeSmooth=new SmoothGaze();gazeSmooth.Update(1,-1,16);Require(gazeSmooth.X>0&&gazeSmooth.X<1&&gazeSmooth.Y<0,"gaze responds on first sample");
            for(int i=0;i<20;i++)gazeSmooth.Update(1,-1,16);Require(gazeSmooth.X>.99&&gazeSmooth.X<=1,"gaze converges without overshoot");
            double prior=gazeSmooth.X;gazeSmooth.Update(-1,1,16);Require(gazeSmooth.X<prior&&gazeSmooth.X>-1,"gaze reverses immediately and smoothly");
            gazeSmooth.Reset();Require(gazeSmooth.X==0&&gazeSmooth.Y==0,"gaze reset on blocking/action change");
            var g1=new SmoothGaze();var g2=new SmoothGaze();for(int i=0;i<10;i++)g1.Update(.7,-.4,8);g2.Update(.7,-.4,80);Require(Math.Abs(g1.X-g2.X)<1e-12&&Math.Abs(g1.Y-g2.Y)<1e-12,"gaze smoothing independent of tick rate");
            foreach(int direction in new[]{-1,1})foreach(double scale in new[]{.6,1.0,2.0}) {
                ActionSpec clip=c[direction>0?"hop_right":"hop_left"];
                var travel=new HopTravel(clip,0,direction,1601,scale,-1000,1000);Require(travel.Duration==2400,"walk ends on whole hop");
                Require(Math.Abs(travel.Position(800)-direction*32*scale)<1e-9&&Math.Abs(travel.Position(2400)-direction*96*scale)<1e-9,"scaled hop accumulates without wrap-back");
                Require(travel.Position(100)==0,"no sliding during grounded anticipation");
                Require(Math.Abs(travel.Position(600)-travel.Position(790))<1e-9,"no sliding during landing");
                var edge=new HopTravel(clip,0,direction,5000,scale,-5,5);double previous=0;
                for(int t=0;t<=edge.Duration;t+=8){double pos=edge.Position(t);Require(pos>=-5&&pos<=5&&direction*(pos-previous)>=-1e-9,"shortened edge hop remains monotonic and in bounds");previous=pos;}
                Require(Math.Abs(edge.Position(edge.Duration)-direction*5)<1e-9,"edge hop lands at boundary");
            }
            var xml=new XmlSerializer(typeof(Settings));using(var reader=new StringReader("<Settings><SizePercent>125</SizePercent><AutoActions>false</AutoActions></Settings>")){var old=(Settings)xml.Deserialize(reader);Require(old.RandomProfile=="standard"&&old.SizePercent==125&&!old.AutoActions,"old settings default new field");}
            var results=new StringBuilder();results.AppendLine("PASS: "+count+" assertions (legacy self-test also passed).");
            results.AppendLine("Unfiltered 100000-draw distribution: "+String.Join(", ",counts.Select(p=>p.Key+"="+p.Value)));
            using(var process=Process.GetCurrentProcess())using(var cache=new FrameCache(c,100)) {
                int gdiBefore=GetGuiResources(process.Handle,0);
                foreach(int percent in new[]{60,100,200}) {
                    cache.Resize(percent);
                    foreach(ActionSpec a in c.Actions) {
                        for(int i=0;i<a.Frames.Length;i++){Bitmap frame=cache.GetFrame(a.Id,i);Require(frame.Width==192*percent/100&&frame.Height==208*percent/100,"render size");Require(cache.FrameBytes<=c.FrameBudget&&cache.PageBytes<=c.PageBudget,"bounded decoded caches");}
                        using(Bitmap contact=new Bitmap(4*cache.Width,2*(cache.Height+20)))using(Graphics g=Graphics.FromImage(contact)) {
                            for(int j=0;j<8;j++){int column=j%4,row=j/4;g.FillRectangle(row==0?Brushes.WhiteSmoke:Brushes.Black,column*cache.Width,row*(cache.Height+20),cache.Width,cache.Height+20);g.DrawImageUnscaled(cache.GetFrame(a.Id,(j%4)*(a.Frames.Length-1)/3),column*cache.Width,row*(cache.Height+20));g.DrawString(percent+"% "+a.Id,SystemFonts.DefaultFont,Brushes.Gray,column*cache.Width,row*(cache.Height+20)+cache.Height);}
                            contact.Save(Path.Combine(output,a.Id+"-"+percent+".png"));
                        }
                    }
                    using(Bitmap neutral=(Bitmap)cache.GetFrame("gaze",40).Clone()) {
                        Bitmap sampled=cache.GetGaze(0,0);Require(sampled.GetPixel(sampled.Width/2,sampled.Height/2)==neutral.GetPixel(neutral.Width/2,neutral.Height/2),"gaze neutral sampling preserves pixels");
                        for(int i=0;i<120;i++) {
                            double theta=i*Math.PI/30;Bitmap blend=cache.GetGaze(Math.Sin(theta),Math.Cos(theta));
                            Require(cache.FrameBytes<=c.FrameBudget&&cache.PageBytes<=c.PageBudget,"gaze sampler scratch included in budget");
                            Require(blend.GetPixel(blend.Width/2,blend.Height*4/5)==neutral.GetPixel(neutral.Width/2,neutral.Height*4/5),"gaze cannot change mouth/body");
                        }
                        using(Bitmap contact=new Bitmap(cache.Width*5,cache.Height*2))using(Graphics graphics=Graphics.FromImage(contact)) {
                            for(int j=0;j<10;j++){graphics.FillRectangle(j<5?Brushes.WhiteSmoke:Brushes.Black,(j%5)*cache.Width,(j/5)*cache.Height,cache.Width,cache.Height);graphics.DrawImageUnscaled(cache.GetGaze((j%5-2)/2.0,j<5?-.65:.65),(j%5)*cache.Width,(j/5)*cache.Height);}
                            contact.Save(Path.Combine(output,"gaze-sampled-"+percent+".png"));
                        }
                    }
                }
                for(int pass=0;pass<3;pass++)foreach(ActionSpec a in c.Actions)for(int i=0;i<a.Frames.Length;i++)cache.GetFrame(a.Id,i);
                int gdiAfter=GetGuiResources(process.Handle,0);Require(gdiAfter-gdiBefore<16,"no cache GDI growth");
                process.Refresh();results.AppendLine("Cache peaks bytes: frames="+cache.PeakFrameBytes+", pages="+cache.PeakPageBytes+"; budgets="+c.FrameBudget+","+c.PageBudget);
                results.AppendLine("GDI before/after="+gdiBefore+"/"+gdiAfter+"; measured working set="+process.WorkingSet64+" bytes.");
            }
            results.AppendLine("Total assertions including cache rendering="+count);
            File.WriteAllText(Path.Combine(output,"self-test.txt"),results.ToString());return 0;
        }
    }
}
