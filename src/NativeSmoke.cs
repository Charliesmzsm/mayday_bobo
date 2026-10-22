using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MojoCarrot {
    public sealed partial class PetForm {
        int smokeIndex=-1,smokePaints,smokeGdi;
        long smokeStart,smokeDue;
        TimeSpan smokeCpu;
        readonly StringBuilder smokeTrace=new StringBuilder();
        void SmokeTick() {
            long now=clock.Now;
            if(smokeIndex<0) {
                smokeIndex=0;smokeStart=now;
                using(Process p=Process.GetCurrentProcess()){smokeGdi=MotionTests.GetGuiResources(p.Handle,0);smokeCpu=p.TotalProcessorTime;}
                StartSmokeClip();
            }
            if(now>=smokeDue) {
                smokeTrace.AppendLine(player.Current.Id+" played "+(now-(smokeDue-(player.Current.Id=="gaze"?1000:player.Current.Duration)))+"ms");
                smokeIndex++;
                if(smokeIndex>=catalog.Actions.Length){FinishSmoke();return;}
                StartSmokeClip();
            }
            // Exercise the production clock, frame cache and UpdateLayeredWindow at normal speed.
            RenderCurrent(false);smokePaints++;
        }
        void StartSmokeClip(){ActionSpec action=catalog.Actions[smokeIndex];player.Start(action,ActionOrigin.Manual);smokeDue=clock.Now+(action.Id=="gaze"?1000:action.Duration);smokeTrace.AppendLine("Start "+action.Id+" frames="+action.Frames.Length);}
        void FinishSmoke() {
            timer.Stop();
            Directory.CreateDirectory(Path.GetDirectoryName(smokeReport));
            using(Process p=Process.GetCurrentProcess()) {
                double seconds=(clock.Now-smokeStart)/1000.0,cpu=(p.TotalProcessorTime-smokeCpu).TotalSeconds;
                smokeTrace.AppendLine("playback_only_elapsed_seconds="+seconds.ToString("F3")+" playback_only_cpu_seconds="+cpu.ToString("F3")+" playback_only_single_core_cpu_percent="+(cpu/seconds*100).ToString("F2"));
            }
            Idle();long before=clock.Now;
            var click=new MouseEventArgs(MouseButtons.Left,1,Width/2,Height/2,0);
            OnMouseDown(this,click);OnMouseUp(this,click);
            MotionTests.Require(player.IsIdle,"native single click does not play early");
            MotionTests.Require(clicks.Poll(before+SystemInformation.DoubleClickTime+100),"native single click arbiter releases wave");
            StartAction("wave",ActionOrigin.Manual);MotionTests.Require(player.Current.Id=="wave","native wave");
            MotionTests.Require(!StartAction("think",ActionOrigin.Random)&&player.Current.Id=="wave","random cannot interrupt manual action");
            MotionTests.Require(StartAction("happy_bounce",ActionOrigin.Manual)&&player.Current.Id=="happy_bounce","manual action interrupts active clip");
            OnMouseDown(this,click);OnMouseUp(this,click);OnMouseDown(this,click);
            OnMouseDoubleClick(new MouseEventArgs(MouseButtons.Left,2,Width/2,Height/2,0));OnMouseUp(this,click);
            MotionTests.Require(player.Current.Id=="happy_bounce"||player.Current.Id=="surprise_star","native double click plays happy/surprise");
            MotionTests.Require(!clicks.Poll(clock.Now+5000),"native double click no pending wave");
            OnMouseDown(this,click);dragMouse=new Point(Cursor.Position.X-10,Cursor.Position.Y);OnMouseMove(this,click);
            MotionTests.Require(wasDragged&&player.IsIdle,"drag immediately cancels animation");OnMouseUp(this,click);
            OnMouseDown(this,click);Capture=false;Application.DoEvents();MotionTests.Require(!dragging&&!blocked,"lost capture cannot leave animation frozen");
            foreach(int size in new[]{60,100,200}){ResizePet(size);RenderCurrent(true);MotionTests.Require(Width==192*size/100,"native resize");}
            paused=true;UpdateBlock();int frame=player.Frame;locked=true;petHidden=true;UpdateBlock();MotionTests.Require(player.Frame==frame,"nested pause/hide/lock");
            paused=false;locked=false;petHidden=false;UpdateBlock();MotionTests.Require(scheduler.NextAction>clock.Now,"native resume schedules future");
            settings.ClickThrough=true;SetClickThrough();MotionTests.Require((Native.GetWindowLong(Handle,-20)&0x20)!=0,"click-through flag");
            RestorePet();MotionTests.Require((Native.GetWindowLong(Handle,-20)&0x20)==0,"tray restore disables click-through");
            petHidden=true;Hide();UpdateBlock();RestorePet();MotionTests.Require(Visible,"hidden restore");
            menu.Show(Cursor.Position);Application.DoEvents();MotionTests.Require(menuOpen&&blocked,"menu blocks playback");
            using(Bitmap image=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(image,new Rectangle(0,0,menu.Width,menu.Height));image.Save(smokeReport+".menu.png");}
            menu.Close();Application.DoEvents();MotionTests.Require(!menuOpen&&!blocked,"menu resumes safely");
            settings.Wander=true;BeginWalk(1,2000);Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;Location=new Point(area.Right-Width,Top);walkX=Left;UpdateWalk(clock.Now,100);MotionTests.Require(player.IsIdle&&Left<=area.Right-Width,"native boundary stops walk");
            using(Process p=Process.GetCurrentProcess()) {
                int gdi=MotionTests.GetGuiResources(p.Handle,0);
                // Menu/font initialization legitimately creates process-lifetime handles.
                // Compare repeated painting after that initialization, not against a cold UI.
                for(int pass=0;pass<2;pass++) {
                    foreach(ActionSpec action in catalog.Actions)for(int i=0;i<action.Frames.Length;i++)Native.Paint(Handle,sprites.GetFrame(action.Id,i),Location);
                    int after=MotionTests.GetGuiResources(p.Handle,0);
                    smokeTrace.AppendLine("native paint stress pass "+pass+": GDI="+after);
                    MotionTests.Require(after-gdi<3,"stable GDI handles during repeated native painting");gdi=after;
                }
                double elapsed=(clock.Now-smokeStart)/1000.0,cpu=(p.TotalProcessorTime-smokeCpu).TotalSeconds;
                smokeTrace.AppendLine("elapsed_seconds="+elapsed.ToString("F3")+" cpu_seconds="+cpu.ToString("F3")+" single_core_cpu_percent="+(cpu/elapsed*100).ToString("F2"));
                smokeTrace.AppendLine("timer_ticks="+smokePaints+" GDI before/after="+smokeGdi+"/"+gdi+" working_set_bytes="+p.WorkingSet64);
                smokeTrace.AppendLine("cache frame/page peaks="+sprites.PeakFrameBytes+"/"+sprites.PeakPageBytes);
            }
            File.WriteAllText(smokeReport,"PASS: normal-speed native playback of every loaded clip; deferred single-click; double-click arbitration; 60/100/200% resize; nested pause/hide/lock; menu; click-through recovery; window clamp and walking boundary.\r\n"+smokeTrace);
            Close();
        }
    }
}
