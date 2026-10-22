using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MojoCarrot {
    public interface IClock {long Now {get;}}
    public sealed class MonotonicClock : IClock {readonly Stopwatch clock=Stopwatch.StartNew();public long Now {get{return clock.ElapsedMilliseconds;}}}
    public enum ActionOrigin { Idle, Micro, Random, Manual, Walk }
    public sealed class AnimationPlayer {
        readonly IClock clock;
        public ActionSpec Current {get;private set;}
        public ActionOrigin Origin {get;private set;}
        long start,freezeAt;bool completed,frozen;
        public event Action Completed;
        public AnimationPlayer(IClock time){clock=time;}
        public void Start(ActionSpec a,ActionOrigin origin){Current=a;Origin=origin;start=clock.Now;completed=false;if(frozen)freezeAt=clock.Now;}
        public int Frame {get{return Current.FrameAt((frozen?freezeAt:clock.Now)-start);}}
        public bool IsIdle {get{return Origin==ActionOrigin.Idle;}}
        public void SetFrozen(bool value){if(value==frozen)return;if(value)freezeAt=clock.Now;else start+=clock.Now-freezeAt;frozen=value;}
        public void Update(){if(frozen||completed||Current==null||Current.Loop)return;if(clock.Now-start>=Current.Duration){completed=true;if(Completed!=null)Completed();}}
    }
    public sealed class ActionScheduler {
        readonly AnimationCatalog catalog;readonly IClock clock;readonly Random random;
        readonly Dictionary<string,Queue<long>> starts=new Dictionary<string,Queue<long>>();
        readonly List<string> history=new List<string>();
        public long NextAction {get;private set;}
        public string Profile {get;private set;}
        public ActionScheduler(AnimationCatalog c,IClock time,Random rng,string profile){catalog=c;clock=time;random=rng;SetProfile(profile);}
        public int Between(int[] range){return random.Next(range[0],range[1]+1);}
        public void SetProfile(string value){Profile=catalog.Scheduler.Profiles.Any(p=>p.Id==value)?value:"standard";Schedule();}
        public void Schedule(){NextAction=clock.Now+Between(catalog.Scheduler.Profiles.First(p=>p.Id==Profile).Wait);}
        public void Defer(){NextAction=clock.Now+Between(catalog.Scheduler.Retry);}
        Queue<long> Records(string id){Queue<long> q;if(!starts.TryGetValue(id,out q)){q=new Queue<long>();starts.Add(id,q);}return q;}
        public long Remaining(string id) {
            if(!catalog.Has(id))return long.MaxValue;
            ActionSpec a=catalog[id];Queue<long> q=Records(id);long now=clock.Now;
            long retention=Math.Max(a.Cooldown,a.RateWindow);
            while(q.Count>0&&now-q.Peek()>=retention)q.Dequeue();
            if(q.Count==0)return 0;
            long wait=Math.Max(0,q.Last()+a.Cooldown-now);
            if(a.RateLimit>0) {
                long[] active=q.Where(t=>now-t<a.RateWindow).ToArray();
                if(active.Length>=a.RateLimit)wait=Math.Max(wait,active[active.Length-a.RateLimit]+a.RateWindow-now);
            }
            return wait;
        }
        public bool TryStart(string id,bool enforceCooldown,bool isRandom) {
            if(!catalog.Has(id))return false;
            if((enforceCooldown||id=="surprise_star")&&Remaining(id)>0)return false;
            Records(id).Enqueue(clock.Now);
            // Non-limited manual actions only need the latest timestamp.
            if(catalog[id].RateLimit==0)while(Records(id).Count>1)Records(id).Dequeue();
            if(isRandom){history.Add(id);if(history.Count>2)history.RemoveAt(0);}return true;
        }
        public string Choose() {
            var all=catalog.Actions.Where(a=>a.Weight>0&&Remaining(a.Id)==0).ToList();
            if(history.Count>0)all.RemoveAll(a=>a.Id==history.Last());
            var candidates=all.Where(a=>!history.Contains(a.Id)).ToList();
            if(candidates.Count==0)candidates=all;
            if(candidates.Count==0)return null;
            int roll=random.Next(candidates.Sum(a=>a.Weight));
            foreach(ActionSpec a in candidates){if(roll<a.Weight)return a.Id;roll-=a.Weight;}return null;
        }
        public bool ContextAllows(bool idle,bool blocked,long lastPointer,long lastManual,bool pointerNear) {
            return idle&&!blocked&&!pointerNear&&clock.Now-lastPointer>=catalog.Scheduler.PointerQuiet&&clock.Now-lastManual>=catalog.Scheduler.ManualGrace;
        }
    }
    public sealed class ClickArbiter {
        readonly int delay;long due;bool pending,suppressRelease;
        public ClickArbiter(int doubleClickTime){delay=doubleClickTime;}
        public void Release(long now,bool moved){if(moved){Cancel();return;}if(suppressRelease){suppressRelease=false;return;}pending=true;due=now+delay;}
        public void DoubleClick(){pending=false;suppressRelease=true;}
        public bool Poll(long now){if(!pending||now<due)return false;pending=false;return true;}
        public void Cancel(){pending=false;suppressRelease=false;}
    }
    public static class WalkMotion {
        public static double Advance(double x,int direction,long elapsed,long remaining,double min,double max) {
            double distance=direction>0?max-x:x-min;
            double speed=55*Math.Min(1,Math.Max(0,distance)/30)*Math.Min(1,Math.Max(0,remaining)/350.0);
            return Math.Max(min,Math.Min(max,x+direction*Math.Max(0,Math.Min(100,elapsed))*speed/1000));
        }
    }
}
