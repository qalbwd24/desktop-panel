$ErrorActionPreference = 'Stop'
$assetRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.IO;
public static class PanelIconDrawing {
 static GraphicsPath Round(float x,float y,float w,float h,float r){var p=new GraphicsPath();p.AddArc(x,y,r*2,r*2,180,90);p.AddArc(x+w-r*2,y,r*2,r*2,270,90);p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90);p.AddArc(x,y+h-r*2,r*2,r*2,90,90);p.CloseFigure();return p;}
 public static byte[] Png(int size){using(var b=new Bitmap(size,size,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(b)){
  g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.ScaleTransform(size/256F,size/256F);
  using(var fill=new SolidBrush(ColorTranslator.FromHtml("#172a32")))using(var path=Round(8,8,240,240,54))g.FillPath(fill,path);
  using(var pen=new Pen(ColorTranslator.FromHtml("#d2fff0"),14)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;using(var path=Round(58,62,140,144,18))g.DrawPath(pen,path);g.DrawLine(pen,58,104,198,104);g.DrawLine(pen,88,46,88,78);g.DrawLine(pen,168,46,168,78);}
  using(var fill=new SolidBrush(ColorTranslator.FromHtml("#71e5bd"))){using(var p=Round(82,128,16,16,4))g.FillPath(fill,p);using(var p=Round(116,128,16,16,4))g.FillPath(fill,p);}
  using(var pen=new Pen(ColorTranslator.FromHtml("#71e5bd"),14)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;g.DrawLines(pen,new[]{new PointF(112,166),new PointF(136,185),new PointF(181,144)});}
  using(var stream=new MemoryStream()){b.Save(stream,ImageFormat.Png);return stream.ToArray();}
 }}
 public static void Write(string folder){int[] sizes={16,20,24,32,40,48,64,128,256};var frames=new byte[sizes.Length][];for(int i=0;i<sizes.Length;i++)frames[i]=Png(sizes[i]);
  File.WriteAllBytes(Path.Combine(folder,"app.png"),frames[frames.Length-1]);
  using(var file=File.Create(Path.Combine(folder,"app.ico")))using(var writer=new BinaryWriter(file)){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}foreach(var frame in frames)writer.Write(frame);}
 }
}
'@
[PanelIconDrawing]::Write($assetRoot)
Write-Output ('图标已生成：' + (Join-Path $assetRoot 'app.ico'))
