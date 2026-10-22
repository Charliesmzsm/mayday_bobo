using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MojoCarrot {
    // Only the UI thread uses this cache. Returned images are borrowed until the next call.
    public sealed class FrameCache : IDisposable {
        sealed class Entry { public Bitmap Image; public long Used; public long Bytes; }
        readonly Dictionary<string,Entry> frames=new Dictionary<string,Entry>();
        readonly Dictionary<string,Entry> pages=new Dictionary<string,Entry>();
        readonly AnimationCatalog catalog;
        readonly Func<string,Stream> open;
        long tick;
        int percent;
        Bitmap gazeImage;byte[][] gazePixels;long gazeBytes;
        public int Width {get;private set;}
        public int Height {get;private set;}
        public long FrameBytes {get;private set;}
        public long PageBytes {get;private set;}
        public long PeakFrameBytes {get;private set;}
        public long PeakPageBytes {get;private set;}
        public FrameCache(AnimationCatalog c,int scale):this(c,scale,AnimationCatalog.OpenResource){}
        public FrameCache(AnimationCatalog c,int scale,Func<string,Stream> loader){catalog=c;open=loader;Resize(scale);}
        public void Resize(int scale) {
            percent=Math.Max(60,Math.Min(200,scale));Width=192*percent/100;Height=208*percent/100;
            Clear(frames);FrameBytes=0;
            if(gazeImage!=null)gazeImage.Dispose();gazeImage=null;gazePixels=null;gazeBytes=0;
        }
        static void Clear(Dictionary<string,Entry> entries){foreach(Entry e in entries.Values)e.Image.Dispose();entries.Clear();}
        static long Trim(Dictionary<string,Entry> entries,long bytes,long needed,long budget) {
            while(entries.Count>0&&bytes+needed>budget) {
                string key=null;long oldest=long.MaxValue;
                foreach(var pair in entries)if(pair.Value.Used<oldest){oldest=pair.Value.Used;key=pair.Key;}
                Entry e=entries[key];entries.Remove(key);bytes-=e.Bytes;e.Image.Dispose();
            }
            return bytes;
        }
        // PNG normally contains straight RGB. Some supplied motion pages store RGB*A.
        // Undo that storage convention once, before GDI+ performs its own premultiplication.
        internal static Bitmap DecodePage(Stream stream,PageSpec p) {
            using(Bitmap source=new Bitmap(stream)) {
                if(source.Width!=p.Width||source.Height!=p.Height)throw new InvalidDataException("Decoded page size mismatch");
                if(p.EffectiveAlphaMode=="straight")return new Bitmap(source);
                if(p.EffectiveAlphaMode!="premultiplied")throw new InvalidDataException("Invalid page alpha mode");
                Bitmap result=new Bitmap(p.Width,p.Height,PixelFormat.Format32bppArgb);
                try {
                    Rectangle rect=new Rectangle(0,0,p.Width,p.Height);
                    BitmapData input=source.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                    try {
                        BitmapData output=result.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                        try {
                            byte[] row=new byte[p.Width*4];
                            for(int y=0;y<p.Height;y++) {
                                Marshal.Copy(IntPtr.Add(input.Scan0,y*input.Stride),row,0,row.Length);
                                for(int x=0;x<row.Length;x+=4) {
                                    int a=row[x+3];
                                    for(int channel=0;channel<3;channel++)row[x+channel]=a==0?(byte)0:(byte)Math.Min(255,(row[x+channel]*255+a/2)/a);
                                }
                                Marshal.Copy(row,0,IntPtr.Add(output.Scan0,y*output.Stride),row.Length);
                            }
                        }finally{result.UnlockBits(output);}
                    }finally{source.UnlockBits(input);}
                    return result;
                }catch{result.Dispose();throw;}
            }
        }
        Bitmap Page(PageSpec p) {
            Entry e;if(pages.TryGetValue(p.Resource,out e)){e.Used=++tick;return e.Image;}
            long bytes=p.Width*(long)p.Height*4;
            if(bytes>catalog.PageBudget)throw new InvalidDataException("Page exceeds decoded cache budget");
            PageBytes=Trim(pages,PageBytes,bytes,catalog.PageBudget);
            Bitmap bitmap;
            using(Stream stream=open(p.Resource))bitmap=DecodePage(stream,p);
            pages.Add(p.Resource,new Entry{Image=bitmap,Used=++tick,Bytes=bytes});PageBytes+=bytes;
            PeakPageBytes=Math.Max(PeakPageBytes,PageBytes);return bitmap;
        }
        public Bitmap GetFrame(string actionId,int index) {
            ActionSpec action=catalog[actionId];
            if(index<0||index>=action.Frames.Length)throw new ArgumentOutOfRangeException("index");
            string key=actionId+":"+index;
            Entry e;if(frames.TryGetValue(key,out e)){e.Used=++tick;return e.Image;}
            long bytes=Width*(long)Height*4;
            FrameBytes=Trim(frames,FrameBytes-gazeBytes,bytes,catalog.FrameBudget-gazeBytes)+gazeBytes;
            FrameSpec f=action.Frames[index];int[] r=f.Rect;
            Bitmap page=Page(action.Pages[f.Page]);Bitmap output=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
            try {
                // Cropping before scaling prevents neighbouring frame bleed.
                using(Bitmap cell=page.Clone(new Rectangle(r[0],r[1],r[2],r[3]),PixelFormat.Format32bppArgb))
                using(Graphics g=Graphics.FromImage(output))using(ImageAttributes attributes=new ImageAttributes()) {
                    g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    float sx=Width/192f*192f/r[2],sy=Height/208f*208f/r[3];
                    float x=catalog.TargetPivot[0]*Width/192f-f.Pivot[0]*sx;
                    float y=catalog.TargetPivot[1]*Height/208f-f.Pivot[1]*sy;
                    var destination=new[]{new PointF(x,y),new PointF(x+r[2]*sx,y),new PointF(x,y+r[3]*sy)};
                    g.DrawImage(cell,destination,new RectangleF(0,0,r[2],r[3]),GraphicsUnit.Pixel,attributes);
                }
                frames.Add(key,new Entry{Image=output,Used=++tick,Bytes=bytes});FrameBytes+=bytes;
                PeakFrameBytes=Math.Max(PeakFrameBytes,FrameBytes);return output;
            }catch{output.Dispose();throw;}
        }
        // Adjacent supplied poses differ by less than one source pixel inside the eyes.
        // Bilinear sampling of their registered premultiplied pixels gives subpixel gaze;
        // the identical body/alpha stays identical. Never interpolate emotion faces.
        public Bitmap GetGaze(double x,double y) {
            ActionSpec a=catalog["gaze"];
            if(!a.IsGrid)throw new InvalidOperationException("Gaze grid unavailable");
            double gx=(Math.Max(-1,Math.Min(1,x))+1)*(a.GridCols-1)/2.0;
            double gy=(Math.Max(-1,Math.Min(1,y))+1)*(a.GridRows-1)/2.0;
            int col=Math.Min(a.GridCols-2,(int)gx),row=Math.Min(a.GridRows-2,(int)gy);
            double u=gx-col,v=gy-row;int length=Width*Height*4;
            // Reserve both the reusable output and four CPU sampling buffers.
            if(gazeImage==null) {
                if(length*9L>catalog.FrameBudget)return GetFrame("gaze",(int)Math.Round(gy)*a.GridCols+(int)Math.Round(gx));
                gazeBytes=length*5L;FrameBytes=Trim(frames,FrameBytes,gazeBytes,catalog.FrameBudget)+gazeBytes;
                gazeImage=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
                gazePixels=new[]{new byte[length],new byte[length],new byte[length],new byte[length]};
                PeakFrameBytes=Math.Max(PeakFrameBytes,FrameBytes);
            }
            int[] indices={row*a.GridCols+col,row*a.GridCols+col+1,(row+1)*a.GridCols+col,(row+1)*a.GridCols+col+1};
            Rectangle rect=new Rectangle(0,0,Width,Height);
            for(int i=0;i<4;i++) {
                Bitmap source=GetFrame("gaze",indices[i]);BitmapData data=source.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                try{for(int yy=0;yy<Height;yy++)Marshal.Copy(IntPtr.Add(data.Scan0,yy*data.Stride),gazePixels[i],yy*Width*4,Width*4);}
                finally{source.UnlockBits(data);}
            }
            int w0=(int)Math.Round((1-u)*(1-v)*65536),w1=(int)Math.Round(u*(1-v)*65536),w2=(int)Math.Round((1-u)*v*65536),w3=65536-w0-w1-w2;
            for(int i=0;i<length;i++)gazePixels[0][i]=(byte)Math.Max(0,Math.Min(255,(gazePixels[0][i]*w0+gazePixels[1][i]*w1+gazePixels[2][i]*w2+gazePixels[3][i]*w3+32768)>>16));
            BitmapData output=gazeImage.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try{for(int yy=0;yy<Height;yy++)Marshal.Copy(gazePixels[0],yy*Width*4,IntPtr.Add(output.Scan0,yy*output.Stride),Width*4);}
            finally{gazeImage.UnlockBits(output);}
            return gazeImage;
        }
        public void Dispose(){Clear(frames);Clear(pages);if(gazeImage!=null)gazeImage.Dispose();gazeImage=null;gazePixels=null;gazeBytes=0;FrameBytes=PageBytes=0;}
    }
}
