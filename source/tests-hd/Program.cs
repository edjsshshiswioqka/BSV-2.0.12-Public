using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
static class Program
{
 static int Checks;
 static void Check(bool ok,string reason){Checks++;if(!ok)throw new Exception(reason);}
 static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Static;
 static Texture2D Texture(int width,int height)
 {
  var t=(Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));GC.SuppressFinalize(t);
  var fields=typeof(Texture2D).GetFields(BindingFlags.NonPublic|BindingFlags.Instance);
  foreach(var field in fields.Where(f=>f.FieldType==typeof(int)))
  {if(field.Name.Contains("width",StringComparison.OrdinalIgnoreCase))field.SetValue(t,width);
   if(field.Name.Contains("height",StringComparison.OrdinalIgnoreCase))field.SetValue(t,height);}
  Check(t.Width==width&&t.Height==height,"native texture metadata initialized without a graphics device");return t;
 }
 static void Main(string[] args)
 {
  var type=Assembly.LoadFrom(args[0]).GetType("BetterStardewValley.HdSprites",true)!;
  var entry=type.GetNestedType("Entry",BindingFlags.NonPublic)!;
  var dict=(IDictionary)type.GetField("Replacements",Private)!.GetValue(null)!;
  var original=Texture(16,16);var hd=Texture(64,64);var other=Texture(16,16);
  dict.Add(original,Activator.CreateInstance(entry,hd,4f)!);
  foreach(float scale in new[]{.5f,1f,2f,4f})foreach(Vector2 origin in new[]{Vector2.Zero,new Vector2(8,8),new Vector2(3,5)})
  {
   object?[] a={original,(Rectangle?)new Rectangle(0,0,16,16),origin,scale};
   type.GetMethod("VectorScalar",Private)!.Invoke(null,a);
   Check(ReferenceEquals(a[0],hd),"only BSV texture remapped");
   Check((Rectangle)a[1]! ==new Rectangle(0,0,64,64),"complete HD source rect");
   Check((Vector2)a[2]! ==origin*4 && (float)a[3]! ==scale/4,"origin and visible dimensions invariant");
   // Nested MonoGame overloads must not apply the conversion twice.
   type.GetMethod("VectorScalar",Private)!.Invoke(null,a);Check((float)a[3]! ==scale/4,"no double conversion");
  }
  object?[] ordinary={other,(Rectangle?)new Rectangle(0,0,16,16),new Vector2(8),4f};type.GetMethod("VectorScalar",Private)!.Invoke(null,ordinary);
  Check(ReferenceEquals(ordinary[0],other)&&(float)ordinary[3]! ==4,"other mods and vanilla untouched");
  object?[] vector={original,(Rectangle?)new Rectangle(2,3,10,9),new Vector2(5,6),new Vector2(2,3)};type.GetMethod("VectorScale",Private)!.Invoke(null,vector);
  Check((Rectangle)vector[1]! ==new Rectangle(8,12,40,36)&&(Vector2)vector[3]! ==new Vector2(.5f,.75f),"subrect and nonuniform UI scale");
  object?[] dest={original,new Rectangle(0,0,64,64),null,new Vector2(8,4)};type.GetMethod("Destination",Private)!.Invoke(null,dest);
  Check((Rectangle)dest[2]! ==new Rectangle(0,0,64,64)&&(Vector2)dest[3]! ==new Vector2(32,16),"destination rectangle path keeps origin");
  var t=typeof(Texture2D);var v=typeof(Vector2);var r=typeof(Rectangle);var rn=typeof(Rectangle?);var c=typeof(Color);var f=typeof(float);var e=typeof(SpriteEffects);
  foreach(var signature in new[]{new[]{t,v,rn,c,f,v,f,e,f},new[]{t,v,rn,c,f,v,v,e,f},new[]{t,r,rn,c,f,v,e,f},new[]{t,v,rn,c},new[]{t,v,c},new[]{t,r,rn,c},new[]{t,r,c}})
   Check(typeof(SpriteBatch).GetMethod("Draw",signature)!=null,"native MonoGame overload exists");
  var pad=type.GetMethod("PadSource",Private)!;
  var sources=new List<object>();
  void VerifyPixels(Color[] pixels,int width,int height,string name,string? output=null)
  {
   object?[] call={pixels,width,height,0};var padded=(Color[])pad.Invoke(null,call)!;int side=(int)call[3]!;
   Check(side%16==0&&side>=width+2&&side>=height+2,"full resolution and transparent edge padding");
   int left=(side-width)/2,top=(side-height)/2;bool exact=true,edge=true;
   for(int y=0;y<side;y++)for(int x=0;x<side;x++)
    if(x>=left&&x<left+width&&y>=top&&y<top+height)exact&=padded[y*side+x]==pixels[(y-top)*width+x-left];
    else edge&=padded[y*side+x]==Color.Transparent;
   Check(exact,"every source RGBA preserved including partial alpha: "+name);Check(edge,"only added transparent padding: "+name);
   var handle=Texture(16,16);var full=Texture(side,side);float factor=side/16f;
   dict.Add(handle,Activator.CreateInstance(entry,full,factor)!);
   foreach(float scale in new[]{.5f,1f,2f,4f})
   {
    object?[] a={handle,(Rectangle?)new Rectangle(0,0,16,16),new Vector2(8),scale};
    type.GetMethod("VectorScalar",Private)!.Invoke(null,a);
    Check(ReferenceEquals(a[0],full)&&(Rectangle)a[1]! ==new Rectangle(0,0,side,side),"original resolution is the actual draw source");
    Check(Math.Abs(side*(float)a[3]!-16*scale)<.001f&&(Vector2)a[2]! ==new Vector2(side/2f),"display dimensions and center unchanged");
    type.GetMethod("VectorScalar",Private)!.Invoke(null,a);Check(Math.Abs((float)a[3]!-scale/factor)<.00001f,"no nested double remap at source resolution");
   }
   type.GetMethod("Unregister",Private)!.Invoke(null,new object[]{handle});Check(!dict.Contains(handle),"invalidation removes the source-resolution binding");
   if(output!=null)
   {
    using var writer=new System.IO.BinaryWriter(System.IO.File.Create(output));writer.Write(side);writer.Write(side);
    foreach(var pixel in padded){writer.Write(pixel.R);writer.Write(pixel.G);writer.Write(pixel.B);writer.Write(pixel.A);}
   }
   sources.Add(new{name,width,height,textureSide=side,rgbaPreserved=exact});
  }
  foreach(var dims in new[]{(1,1),(16,16),(1254,1254),(1402,1122),(1536,1024),(37,91)})
  {
   var pixels=new Color[dims.Item1*dims.Item2];var rng=new Random(147);
   for(int i=0;i<pixels.Length;i++)pixels[i]=new Color((byte)rng.Next(256),(byte)rng.Next(256),(byte)rng.Next(256),(byte)rng.Next(256));
   VerifyPixels(pixels,dims.Item1,dims.Item2,"fixture");
  }
  if(args.Length>1)foreach(string path in System.IO.Directory.GetFiles(args[1],"*.rgba"))
  {
   using var reader=new System.IO.BinaryReader(System.IO.File.OpenRead(path));int width=reader.ReadInt32(),height=reader.ReadInt32();
   var pixels=new Color[width*height];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(reader.ReadByte(),reader.ReadByte(),reader.ReadByte(),reader.ReadByte());
   VerifyPixels(pixels,width,height,System.IO.Path.GetFileName(path),path+".padded");
  }
  var filter=type.GetMethod("Downsample",Private)!;
  object?[] avg={new[]{new Color(255,255,255,255),new Color(0,0,0,0),new Color(0,0,0,0),new Color(255,255,255,255)},2,2,0,0};
  var filtered=(Color[])filter.Invoke(null,avg)!;
  Check(filtered.Length==1&&filtered[0]==new Color(128,128,128,128),"runtime area filter averages every premultiplied RGBA texel, preserving partial alpha");
  object?[] edge={Enumerable.Repeat(new Color(80,40,20,120),9).ToArray(),3,3,0,0};
  var edges=(Color[])filter.Invoke(null,edge)!;
  Check((int)edge[3]! ==2&&(int)edge[4]! ==2&&edges.All(c=>c==new Color(80,40,20,120)),"odd dimensions include edge texels without dark padding or alpha cutoff");
  bool failedClosed=false;
  try{type.GetMethod("CreateSourceResolution",Private)!.Invoke(null,new object?[]{original,null});}
  catch(TargetInvocationException ex){failedClosed=ex.InnerException is InvalidOperationException&&ex.InnerException.Message.Contains("requires");}
  Check(failedClosed,"disabled HD adapter does not silently substitute compressed artwork");
  using var assembly=AssemblyDefinition.ReadAssembly(args[0]);
  foreach(string service in new[]{"GiantLobster.LobsterService","KingCrab.KingCrabService","LobsterKing.KingService","MutantLobsterKing.MutantService"})
  {
   var method=assembly.MainModule.GetType("BetterStardewValley."+service).Methods.Single(m=>m.Name=="Texture");
   var calls=method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
   Check(calls.Count(m=>m.Name=="CreateSourceResolution")==1&&!calls.Any(m=>m.Name=="Import"||m.Name=="Texture"),"service uses full source without 16/64 pixel importer: "+service);
  }
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,assertions=Checks,sources,scope="Actual production padding, native texture remap math and API signatures; no GPU or live game"}));
 }
}
