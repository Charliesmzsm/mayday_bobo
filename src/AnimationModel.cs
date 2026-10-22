using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MojoCarrot {
    [DataContract] public sealed class PageSpec {
        [DataMember(Name="resource")] public string Resource;
        [DataMember(Name="width")] public int Width;
        [DataMember(Name="height")] public int Height;
        [DataMember(Name="alpha_mode",EmitDefaultValue=false)] public string AlphaMode;
        public string EffectiveAlphaMode {get{return String.IsNullOrEmpty(AlphaMode)?"straight":AlphaMode;}}
    }
    [DataContract] public sealed class FrameSpec {
        [DataMember(Name="page")] public int Page;
        [DataMember(Name="rect")] public int[] Rect;
        [DataMember(Name="pivot")] public int[] Pivot;
    }
    [DataContract] public sealed class ActionSpec {
        [DataMember(Name="id")] public string Id;
        [DataMember(Name="label")] public string Label;
        [DataMember(Name="durations_ms")] public int[] Durations;
        [DataMember(Name="loop")] public bool Loop;
        [DataMember(Name="weight")] public int Weight;
        [DataMember(Name="cooldown_ms")] public int Cooldown;
        [DataMember(Name="rate_window_ms")] public int RateWindow;
        [DataMember(Name="rate_limit")] public int RateLimit;
        [DataMember(Name="pages")] public PageSpec[] Pages;
        [DataMember(Name="frames")] public FrameSpec[] Frames;
        [DataMember(Name="grid_rows",EmitDefaultValue=false)] public int GridRows;
        [DataMember(Name="grid_cols",EmitDefaultValue=false)] public int GridCols;
        [DataMember(Name="root_x_logical",EmitDefaultValue=false)] public double[] RootX;
        public bool IsGrid {get{return GridRows>0&&GridCols>0;}}
        public int Duration { get { return Durations.Sum(); } }
        public int FrameAt(long elapsed) {
            elapsed=Math.Max(0,elapsed);
            if(Loop) elapsed%=Duration;
            else if(elapsed>=Duration) return Frames.Length-1;
            for(int i=0;i<Durations.Length;i++) { if(elapsed<Durations[i]) return i; elapsed-=Durations[i]; }
            return Frames.Length-1;
        }
    }
    [DataContract] public sealed class ProfileSpec {
        [DataMember(Name="id")] public string Id;
        [DataMember(Name="wait_ms")] public int[] Wait;
    }
    [DataContract] public sealed class SchedulerSpec {
        [DataMember(Name="profiles")] public ProfileSpec[] Profiles;
        [DataMember(Name="pointer_quiet_ms")] public int PointerQuiet;
        [DataMember(Name="manual_grace_ms")] public int ManualGrace;
        [DataMember(Name="exclusion_margin")] public int ExclusionMargin;
        [DataMember(Name="retry_ms")] public int[] Retry;
        [DataMember(Name="blink_wait_ms")] public int[] BlinkWait;
        [DataMember(Name="yawn_wait_ms")] public int[] YawnWait;
        [DataMember(Name="yawn_quiet_ms")] public int YawnQuiet;
        [DataMember(Name="yawn_probability")] public double YawnProbability;
    }
    [DataContract] public sealed class InteractionSpec {
        [DataMember(Name="surprise_probability")] public double SurpriseProbability;
        [DataMember(Name="gaze_resume_ms")] public int GazeResume;
    }
    [DataContract] public sealed class AnimationCatalog {
        [DataMember(Name="schema_version")] public int Schema;
        [DataMember(Name="logical_size")] public int[] LogicalSize;
        [DataMember(Name="target_pivot")] public int[] TargetPivot;
        [DataMember(Name="actions")] public ActionSpec[] Actions;
        [DataMember(Name="scheduler")] public SchedulerSpec Scheduler;
        [DataMember(Name="interaction")] public InteractionSpec Interaction;
        [DataMember(Name="cache_frame_bytes")] public int FrameBudget;
        [DataMember(Name="cache_page_bytes")] public int PageBudget;
        public string FallbackReason {get;private set;}
        public ActionSpec this[string id] {get{return Actions.First(a=>a.Id==id);}}
        public bool Has(string id){return Actions.Any(a=>a.Id==id);}
        static void Check(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
        static void Range(int[] r){Check(r!=null&&r.Length==2&&r[0]>0&&r[1]>=r[0]&&r[1]<3600000,"Invalid wait range");}
        public static AnimationCatalog Read(Stream stream) {
            if(stream==null)throw new InvalidDataException("Missing animations.json");
            var result=(AnimationCatalog)new DataContractJsonSerializer(typeof(AnimationCatalog)).ReadObject(stream);
            Check(result!=null,"Null animation configuration");return result;
        }
        public void Validate(Func<string,Stream> open) {
            Check(Schema==1,"Unsupported animation schema");
            Check(LogicalSize!=null&&LogicalSize.SequenceEqual(new[]{192,208}),"Invalid logical size");
            Check(TargetPivot!=null&&TargetPivot.Length==2&&TargetPivot[0]>=0&&TargetPivot[0]<192&&TargetPivot[1]>=0&&TargetPivot[1]<208,"Invalid target pivot");
            Check(Actions!=null&&Actions.Length>0&&Actions.Length<=64,"Invalid actions");
            var ids=new HashSet<string>();var checkedPages=new Dictionary<string,Size>();var checkedAlpha=new Dictionary<string,string>();
            foreach(ActionSpec a in Actions) {
                Check(a!=null,"Null action");
                Check(!String.IsNullOrEmpty(a.Id)&&ids.Add(a.Id)&&!String.IsNullOrEmpty(a.Label),"Duplicate or invalid action id");
                Check(a.Frames!=null&&a.Frames.Length>0&&a.Frames.Length<=512&&a.Durations!=null&&a.Frames.Length==a.Durations.Length,"Frame timing count mismatch: "+a.Id);
                Check(a.Durations.All(t=>t>=10&&t<=10000)&&a.Duration<120000,"Invalid frame timing: "+a.Id);
                Check((a.GridRows==0&&a.GridCols==0)||(a.Id=="gaze"&&a.GridRows==9&&a.GridCols==9&&a.Frames.Length==81),"Invalid gaze grid");
                if(a.RootX!=null) {
                    Check((a.Id=="hop_right"||a.Id=="hop_left")&&a.Loop&&a.RootX.Length==a.Frames.Length,"Invalid root motion count/action");
                    Check(a.RootX.All(v=>!Double.IsNaN(v)&&!Double.IsInfinity(v)&&Math.Abs(v)<=192)&&a.RootX[0]==0,"Invalid root motion values");
                    int direction=a.Id=="hop_right"?1:-1;
                    Check(direction*a.RootX.Last()>0,"Invalid root motion direction");
                    for(int i=1;i<a.RootX.Length;i++)Check(direction*(a.RootX[i]-a.RootX[i-1])>=0,"Root motion reverses");
                }
                Check(a.Weight>=0&&a.Weight<=10000&&a.Cooldown>=0&&a.Cooldown<=3600000,"Invalid scheduler weight/cooldown");
                Check(a.RateWindow>=0&&a.RateWindow<=3600000&&a.RateLimit>=0&&a.RateLimit<=1000&&(a.RateLimit==0)==(a.RateWindow==0),"Invalid rate limit");
                Check(a.Pages!=null&&a.Pages.Length>0,"Missing pages");
                foreach(PageSpec p in a.Pages) {
                    Check(p!=null&&!String.IsNullOrEmpty(p.Resource),"Invalid page resource");
                    Check(p.Width>0&&p.Width<=8192&&p.Height>0&&p.Height<=8192&&p.Width*(long)p.Height<=5000000&&p.Width*(long)p.Height*4<=PageBudget,"Invalid page dimensions or page cache budget");
                    Check(p.EffectiveAlphaMode=="straight"||p.EffectiveAlphaMode=="premultiplied","Invalid page alpha mode");
                    string mode;
                    if(checkedAlpha.TryGetValue(p.Resource,out mode))Check(mode==p.EffectiveAlphaMode,"Conflicting alpha modes for shared page");
                    else checkedAlpha.Add(p.Resource,p.EffectiveAlphaMode);
                    Size dims;
                    if(!checkedPages.TryGetValue(p.Resource,out dims)) {
                        using(Stream s=open(p.Resource)) {
                            Check(s!=null,"Missing page: "+p.Resource);
                            byte[] h=new byte[24];int n=0,read;
                            while(n<h.Length&&(read=s.Read(h,n,h.Length-n))>0)n+=read;
                            Check(n==24&&h.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"Invalid PNG header");
                            int w=(h[16]<<24)|(h[17]<<16)|(h[18]<<8)|h[19],v=(h[20]<<24)|(h[21]<<16)|(h[22]<<8)|h[23];
                            dims=new Size(w,v);checkedPages.Add(p.Resource,dims);
                        }
                    }
                    Check(dims.Width==p.Width&&dims.Height==p.Height,"Page geometry mismatch");
                }
                foreach(FrameSpec f in a.Frames) {
                    Check(f!=null,"Null frame");
                    Check(f.Page>=0&&f.Page<a.Pages.Length&&f.Rect!=null&&f.Rect.Length==4&&f.Pivot!=null&&f.Pivot.Length==2,"Invalid frame metadata");
                    PageSpec p=a.Pages[f.Page];int[] r=f.Rect;
                    Check(r[0]>=0&&r[1]>=0&&r[2]>0&&r[3]>0&&r[0]+(long)r[2]<=p.Width&&r[1]+(long)r[3]<=p.Height,"Frame outside page");
                    Check(f.Pivot[0]>=0&&f.Pivot[0]<=r[2]&&f.Pivot[1]>=0&&f.Pivot[1]<=r[3],"Invalid frame pivot");
                }
            }
            Check(Has("idle_breathe")&&Has("gaze")&&(this["gaze"].IsGrid||this["gaze"].Frames.Length==16)&&Has("wave")&&Has("happy_bounce"),"Required actions missing");
            if(Has("surprise_star"))Check(this["surprise_star"].Cooldown>=180000&&this["surprise_star"].RateWindow==600000&&this["surprise_star"].RateLimit==2,"Unsafe surprise frequency");
            Check(FrameBudget>=1048576&&FrameBudget<=67108864&&PageBudget>=6291456&&PageBudget<=33554432,"Invalid cache budgets");
            Check(Scheduler!=null&&Scheduler.Profiles!=null&&Scheduler.Profiles.Length==3,"Missing scheduling configuration");
            var profiles=new HashSet<string>();foreach(ProfileSpec p in Scheduler.Profiles){Check(p!=null&&!String.IsNullOrEmpty(p.Id)&&profiles.Add(p.Id),"Invalid or duplicate profile");Range(p.Wait);}
            Check(profiles.SetEquals(new[]{"quiet","standard","lively"}),"Unknown random profiles");
            Range(Scheduler.Retry);Range(Scheduler.BlinkWait);Range(Scheduler.YawnWait);
            Check(Scheduler.PointerQuiet>=3000&&Scheduler.PointerQuiet<=60000&&Scheduler.ManualGrace>=5000&&Scheduler.ManualGrace<=60000&&Scheduler.ExclusionMargin>=0&&Scheduler.ExclusionMargin<=200,"Invalid interaction grace");
            Check(Scheduler.YawnQuiet>=120000&&Scheduler.YawnProbability>=0&&Scheduler.YawnProbability<=1,"Invalid yawn configuration");
            Check(Interaction!=null&&Interaction.SurpriseProbability>=0&&Interaction.SurpriseProbability<=1&&Interaction.GazeResume>=0&&Interaction.GazeResume<=5000,"Invalid interaction configuration");
        }
        public static Stream OpenResource(string name){return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);}
        public static AnimationCatalog Load(bool legacy) {
            if(legacy)return Legacy(null);
            try { using(Stream s=OpenResource("animations.json")){var c=Read(s);c.Validate(OpenResource);return c;} }
            catch(Exception ex) {
                if(!(ex is IOException||ex is SerializationException||ex is ArgumentException||ex is InvalidOperationException))throw;
                return Legacy(ex.Message);
            }
        }
        public static AnimationCatalog Legacy(string reason) {
            var c=new AnimationCatalog{Schema=1,LogicalSize=new[]{192,208},TargetPivot=new[]{96,196},FrameBudget=33554432,PageBudget=16777216,
                Scheduler=new SchedulerSpec{Profiles=new[]{new ProfileSpec{Id="quiet",Wait=new[]{45000,90000}},new ProfileSpec{Id="standard",Wait=new[]{20000,45000}},new ProfileSpec{Id="lively",Wait=new[]{12000,25000}}},PointerQuiet=3000,ManualGrace=5000,ExclusionMargin=32,Retry=new[]{5000,10000},BlinkWait=new[]{5000,12000},YawnWait=new[]{90000,180000},YawnQuiet=120000,YawnProbability=.2},
                Interaction=new InteractionSpec{SurpriseProbability=.3,GazeResume=200},FallbackReason=reason};
            string[] names={"idle_breathe","hop_right","hop_left","wave","happy_bounce","sad","waiting","think","review"};
            var actions=new List<ActionSpec>();
            for(int row=0;row<9;row++) {
                var a=new ActionSpec{Id=names[row],Label=SpriteBank.Names[row],Durations=SpriteBank.Durations[row],Loop=row<3,Weight=row==3?16:row==4?12:row==7?8:0,Cooldown=row==3?60000:row==4?90000:60000,Pages=new[]{new PageSpec{Resource="sprites.png",Width=1536,Height=2288}}};
                a.Frames=Enumerable.Range(0,a.Durations.Length).Select(i=>new FrameSpec{Page=0,Rect=new[]{i*192,row*208,192,208},Pivot=new[]{96,196}}).ToArray();actions.Add(a);
            }
            actions.Add(new ActionSpec{Id="gaze",Label="注视",Loop=true,Durations=Enumerable.Repeat(1000,16).ToArray(),Pages=new[]{new PageSpec{Resource="sprites.png",Width=1536,Height=2288}},Frames=Enumerable.Range(0,16).Select(i=>new FrameSpec{Page=0,Rect=new[]{(i%8)*192,(9+i/8)*208,192,208},Pivot=new[]{96,196}}).ToArray()});
            c.Actions=actions.ToArray();c.Validate(OpenResource);return c;
        }
    }
}
