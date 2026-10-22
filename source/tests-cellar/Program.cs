using BetterStardewValley;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;
class Program {
 static int count;static void Check(bool b,string n){count++;if(!b)throw new Exception("FAIL "+n);}
 // Representative native-sized fixture using real xTile; NOT a bundled game asset.
 static Map Fixture(){
  var m=new Map("CellarFixture");var s=new TileSheet("fixture",m,"not-rendered.png",new Size(16,16),new Size(16,16));m.AddTileSheet(s);
  s.TileIndexProperties[3]["Shadow"]="T";s.TileIndexProperties[4]["Passable"]="T";
  foreach(string id in new[]{"Back","Buildings","Front","AlwaysFront","Paths"})m.AddLayer(new Layer(id,m,new Size(20,18),new Size(16,16)));
  var b=m.GetLayer("Back");var c=m.GetLayer("Buildings");
  for(int y=0;y<18;y++)for(int x=0;x<20;x++){
   b.Tiles[x,y]=new StaticTile(b,s,BlendMode.Alpha,1);
   if(!(x>=2&&x<=18&&y>=5&&y<=15)&&!(x==3&&y>=3&&y<=4))c.Tiles[x,y]=new StaticTile(c,s,BlendMode.Alpha,2);
  }
  var front=m.GetLayer("Front");front.Tiles[1,9]=new AnimatedTile(front,new[]{new StaticTile(front,s,BlendMode.Alpha,8),new StaticTile(front,s,BlendMode.Alpha,9)},200);
  front.Tiles[1,9].Properties["Sentinel"]="keep";
  m.Properties["Warp"]="3 3 FarmHouse 4 25 3 2 FarmHouse 4 25";
  m.Properties["Light"]="1 9 4";m.Properties["DayTiles"]="Front 1 9 8";m.Properties["NightTiles"]="Front 1 9 9";
  m.Properties["AmbientLight"]="100 100 100";return m;
 }
 static void Main(){
  var m=Fixture();var old=CellarMap.FloorTiles(m);Check(old.Count==189,"fixture baseline 189");
  var tiles=old.ToDictionary(p=>p,p=>m.GetLayer("Back").Tiles[p.X,p.Y].TileIndex);
  string warp=m.Properties["Warp"].ToString();var stamp=CellarMap.Expand(m);
  var floors=CellarMap.FloorTiles(m);Check(floors.Count==376,"189 fixture expands to 376 connected slots (~2x)");
  var restored=Fixture();Check(CellarMap.Expand(restored,stamp)==stamp,"saved expansion restores exact recorded geometry");
  var mismatch=Fixture();var beforeLayers=mismatch.Layers.ToArray();bool mismatchRefused=false;
  try{CellarMap.Expand(mismatch,"foreign-layout");}catch(InvalidOperationException){mismatchRefused=true;}
  Check(mismatchRefused&&mismatch.Layers.SequenceEqual(beforeLayers)&&!mismatch.Properties.ContainsKey(CellarMap.LayoutKey),"mismatched saved layout leaves external map layers intact");
  Check(m.Layers.All(l=>l.LayerWidth==20&&l.LayerHeight==29),"all layer bounds and draw dimensions resized");
  Check(old.IsSubsetOf(floors),"every original item/cask/player floor coordinate stays open");
  foreach(var p in tiles)Check(m.GetLayer("Back").Tiles[p.Key.X,p.Key.Y].TileIndex==p.Value,"original floor art preserved");
  Check(m.Properties["Warp"].ToString()==warp,"entrance and farmhouse target unchanged");
  Check(m.Properties["AmbientLight"].ToString()=="100 100 100","ambient light unchanged");
  Check(m.Properties["Light"].ToString()=="1 9 4 1 18 4","wall lamps extend to correct coordinates");
  Check(m.Properties["DayTiles"].ToString()=="Front 1 9 8 Front 1 18 8"&&m.Properties["NightTiles"].ToString()=="Front 1 9 9 Front 1 18 9","day/night lighting follows extensions");
  Check(m.GetLayer("Front").Tiles[1,18] is AnimatedTile&&m.GetLayer("Front").Tiles[1,18].Properties["Sentinel"].ToString()=="keep","animated wall detail and instance metadata retained");
  Check(m.GetLayer("Buildings").Tiles[10,27]!=null,"bottom collision wall moved outward");
  Check(CellarMap.Expand(m)==stamp&&CellarMap.FloorTiles(m).Count==376,"reloading expanded map cannot double again");
  var cabin=Fixture();cabin.Properties["Warp"]="3 3 Cabin_playerB 4 25";CellarMap.Expand(cabin);Check(cabin.Properties["Warp"].ToString()=="3 3 Cabin_playerB 4 25","cabin exit name retained");
  var incompatible=Fixture();incompatible.GetLayer("Back").LayerSize=new Size(21,18);bool refused=false;try{CellarMap.Expand(incompatible);}catch(InvalidOperationException){refused=true;}
  Check(refused&&incompatible.GetLayer("Buildings").LayerHeight==18&&!incompatible.Properties.ContainsKey(CellarMap.LayoutKey),"other map dimensions refused atomically");
  var invalid=Fixture();invalid.Properties["Light"]="1 9";refused=false;try{CellarMap.Expand(invalid);}catch(InvalidOperationException){refused=true;}
  Check(refused&&invalid.GetLayer("Back").LayerHeight==18,"malformed map metadata cannot partially expand");
  var blocked=Fixture();blocked.GetLayer("Back").Tiles[10,10]=new StaticTile(blocked.GetLayer("Back"),blocked.TileSheets[0],BlendMode.Alpha,4);
  Check(!CellarMap.FloorTiles(blocked).Contains((10,10)),"native Back Passable marker blocks the tile");
  var shadow=Fixture();shadow.GetLayer("Buildings").Tiles[10,10]=new StaticTile(shadow.GetLayer("Buildings"),shadow.TileSheets[0],BlendMode.Alpha,3);
  Check(CellarMap.FloorTiles(shadow).Contains((10,10)),"native Buildings Shadow marker remains passable");
  // 2.0.13: the polish varies the floor of the added band; maps with light sources inside the band are left untouched.
  static void NoLights(Map map){foreach(var k in new[]{"Light","DayTiles","NightTiles"})map.Properties.Remove(k);}
  var lit=Fixture();CellarMap.Expand(lit);var litBack=lit.GetLayer("Back").Tiles[5,20];
  Check(!CellarAppearance.Apply(lit)&&ReferenceEquals(lit.GetLayer("Back").Tiles[5,20],litBack),"band with wall lamps is left untouched");
  var polished=Fixture();NoLights(polished);
  for(int y=7;y<16;y++)for(int x=2;x<=18;x++)polished.GetLayer("Back").Tiles[x,y]=new StaticTile(polished.GetLayer("Back"),polished.TileSheets[0],BlendMode.Alpha,5+y%3);
  CellarMap.Expand(polished);var bounds=CellarMap.FloorTiles(polished);var wall=polished.GetLayer("Buildings");
  var collisions=Enumerable.Range(0,29).SelectMany(y=>Enumerable.Range(0,20).Select(x=>wall.Tiles[x,y])).ToArray();
  var nativeBack=Enumerable.Range(0,16).SelectMany(y=>Enumerable.Range(0,20).Select(x=>polished.GetLayer("Back").Tiles[x,y])).ToArray();
  object Snapshot(Map map)=>new{width=20,height=29,cut=16,added=11,back=Enumerable.Range(0,29).Select(y=>Enumerable.Range(0,20).Select(x=>map.GetLayer("Back").Tiles[x,y]?.TileIndex??-1).ToArray()).ToArray(),blocked=Enumerable.Range(0,29).Select(y=>Enumerable.Range(0,20).Select(x=>!CellarMap.FloorTiles(map).Contains((x,y))).ToArray()).ToArray(),lights=map.Properties.TryGetValue("Light",out var l)?l.ToString():""};
  var artBefore=Snapshot(polished);
  Check(CellarAppearance.Apply(polished),"visual polish recognizes only BSV expanded native-shaped fixture");
  Check(CellarMap.FloorTiles(polished).SetEquals(bounds),"visual polish changes no cask placement/collision tile");
  Check(Enumerable.Range(0,29).SelectMany(y=>Enumerable.Range(0,20).Select(x=>wall.Tiles[x,y])).SequenceEqual(collisions),"Buildings layer and collision references untouched");
  Check(Enumerable.Range(0,16).SelectMany(y=>Enumerable.Range(0,20).Select(x=>polished.GetLayer("Back").Tiles[x,y])).SequenceEqual(nativeBack),"entrance, stairs and all original floor art untouched");
  Check(polished.Properties["Warp"].ToString()==warp&&polished.Properties["AmbientLight"].ToString()=="100 100 100","exits and ambient balance unchanged");
  Check(!polished.Properties.ContainsKey("Light"),"no light sources invented");
  Check(CellarAppearance.Apply(polished)&&CellarMap.FloorTiles(polished).SetEquals(bounds),"appearance reload idempotent");
  var foreign=Fixture();NoLights(foreign);CellarMap.Expand(foreign);foreign.GetLayer("Front").Tiles[5,20]=new StaticTile(foreign.GetLayer("Front"),foreign.TileSheets[0],BlendMode.Alpha,44);
  Check(!CellarAppearance.Apply(foreign)&&foreign.GetLayer("Front").Tiles[5,20].TileIndex==44,"foreign band edits cause conservative opt-out, no overwritten decor");
  var foreignLight=Fixture();CellarMap.Expand(foreignLight);foreignLight.Properties["Light"]+=" 7 21 4";
  Check(!CellarAppearance.Apply(foreignLight)&&!foreignLight.Properties.ContainsKey(CellarAppearance.Key),"foreign light record is not deleted or relocated");
  string? evidence=Environment.GetEnvironmentVariable("BSV_CELLAR_EVIDENCE");
  if(!string.IsNullOrEmpty(evidence))File.WriteAllText(evidence,System.Text.Json.JsonSerializer.Serialize(new{synthetic=true,real_game=false,before=artBefore,after=Snapshot(polished)}));
  Console.WriteLine($"{{\"suite\":\"cellar-xTile-map\",\"assertions\":{count},\"status\":\"PASS\",\"fixtureBefore\":189,\"fixtureAfter\":376,\"liveGameAsset\":false}}");
 }
}
