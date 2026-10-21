using BetterStardewValley.AfkFishing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Fences;
using StardewValley.ItemTypeDefinitions;
using xTile;
using xTile.Layers;
using xTile.Tiles;
using Size=xTile.Dimensions.Size;
using SObject=StardewValley.Object;
class Program
{
 static bool Skip()=>false;
 static bool NoIsland(ref bool __result){__result=false;return false;}
 static int Checks;static readonly List<string> Names=new();
 static void Check(bool ok,string name){Checks++;if(!ok)throw new Exception(name);Names.Add(name);}
 static void Main()
 {
  Game1.game1=(Game1)FormatterServices.GetUninitializedObject(typeof(Game1));Game1.game1._locations=new List<GameLocation>();Game1.netWorldState=new Netcode.NetRoot<StardewValley.Network.NetWorldState>(new StardewValley.Network.NetWorldState());Game1.random=new Random(17);Game1.otherFarmers=new();Game1._locationLookup=new(StringComparer.OrdinalIgnoreCase);
  var h=new Harmony("BSV.Path.Native.Collision.Tests");
  h.Patch(typeof(GameLocation).GetMethod("updateMap",Type.EmptyTypes)!,prefix:new(typeof(Program),nameof(Skip)));
  h.Patch(typeof(GameLocation).GetMethod("InIslandContext",Type.EmptyTypes)!,prefix:new(typeof(Program),nameof(NoIsland)));
  h.Patch(typeof(StardewValley.BellsAndWhistles.PlayerStatusList).GetMethod("AddSpriteDefinition",new[]{typeof(string),typeof(string),typeof(int),typeof(int),typeof(int),typeof(int)})!,prefix:new(typeof(Program),nameof(Skip)));
  h.Patch(typeof(Farmer).GetMethod("farmerInit",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!,prefix:new(typeof(Program),nameof(Skip)));
  h.Patch(typeof(AnimatedSprite).GetMethod("LoadTexture",new[]{typeof(string),typeof(bool)})!,prefix:new(typeof(Program),nameof(Skip)));
  h.Patch(typeof(Game1).GetProperty("HostPaused")!.GetMethod!,prefix:new(typeof(Program),nameof(NoIsland)));
  h.Patch(typeof(Furniture).GetMethod("minutesElapsed",new[]{typeof(int)})!,prefix:new(typeof(Program),nameof(Skip)));
  h.Patch(typeof(Game1).GetProperty("IsMasterGame")!.GetMethod!,prefix:new(typeof(Program),nameof(NoIsland)));
  h.Patch(typeof(StardewValley.Locations.FarmHouse).GetMethod("updateMap",Type.EmptyTypes)!,prefix:new(typeof(Program),nameof(Skip)));
  ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());
  Game1.objectData=new Dictionary<string,ObjectData>();
  ItemRegistry.AddTypeDefinition(new BigCraftableDataDefinition());
  Game1.bigCraftableData=new Dictionary<string,StardewValley.GameData.BigCraftables.BigCraftableData>(){{"130",new(){Name="Chest",DisplayName="Chest",Texture="fixture",SpriteIndex=0}}};
  foreach(string id in new[]{"322","323","324","325","388","390","294","343"})Game1.objectData[id]=new(){Name=id,DisplayName=id,Type="Crafting",Texture="fixture",SpriteIndex=0};
  typeof(Fence).GetField("_FenceLookup",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,new Dictionary<string,FenceData>{{"322",new(){Health=100}},{"325",new(){Health=100}}});
  var map=new GameLocation();map.name.Value="BSVPathFixture";map.map=Fixture();Game1.locations.Add(map);Game1.game1.instanceGameLocation=map;
  var who=new Farmer();typeof(Game1).GetField("_player",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,who);
  who.Position=new Vector2(64,64);who.currentLocation=map;
  Check(AfkWorld.StaticClear(map,new(5,3),who),"native fixture empty floor is walkable");
  var wall=map.Map.GetLayer("Buildings");wall.Tiles[5,3]=new StaticTile(wall,map.Map.TileSheets[0],BlendMode.Alpha,1);
  Check(!AfkWorld.StaticClear(map,new(5,3),who),"actual xTile building-layer wall collision");wall.Tiles[5,3]=null;
  var back=map.Map.GetLayer("Back");back.Tiles[5,3].Properties["Passable"]="T";
  Check(!AfkWorld.StaticClear(map,new(5,3),who),"actual xTile blocked water/cliff floor marker");back.Tiles[5,3].Properties.Remove("Passable");
  var chest=new Chest(true);map.objects[new(5,3)]=chest;
  Check(!AfkWorld.StaticClear(map,new(5,3),who),"actual native Chest blocks path");map.objects.Clear();
  var tree=new Tree();tree.growthStage.Value=5;map.terrainFeatures[new(5,3)]=tree;
  Check(!AfkWorld.StaticClear(map,new(5,3),who),"actual native mature Tree blocks path");map.terrainFeatures.Clear();
  foreach(int clump in new[]{600,602,672})
  {
   map.resourceClumps.Add(new ResourceClump(clump,2,2,new(5,3)));
   Check(!AfkWorld.StaticClear(map,new(5,3),who)&&!AfkWorld.StaticClear(map,new(6,4),who),"native large stump/log/rock footprint "+clump);map.resourceClumps.Clear();
  }
  var fruit=new FruitTree();fruit.growthStage.Value=4;map.terrainFeatures[new(5,3)]=fruit;
  Check(!AfkWorld.StaticClear(map,new(5,3),who),"actual native mature FruitTree blocks path");map.terrainFeatures.Clear();
  foreach(string id in new[]{"MachineFixture","TotemFixture","OrderTerminalFixture"})
  {
   Game1.bigCraftableData[id]=new(){Name=id,DisplayName=id,Texture="fixture",SpriteIndex=0};
   map.objects[new(5,3)]=new SObject(new Vector2(5,3),id);
   Check(!AfkWorld.StaticClear(map,new(5,3),who),"native BigCraftable collision independent of artwork/ID: "+id);map.objects.Clear();
  }
  var chair=new Furniture();chair.furniture_type.Value=0;chair.boundingBox.Value=new Rectangle(5*64,3*64,128,64);map.furniture.Add(chair);
  Check(!AfkWorld.StaticClear(map,new(5,3),who)&&!AfkWorld.StaticClear(map,new(6,3),who),"native furniture collision respects its full footprint");map.furniture.Clear();
  var building=new StardewValley.Buildings.Building();building.tileX.Value=5;building.tileY.Value=3;building.tilesWide.Value=2;building.tilesHigh.Value=2;map.buildings.Add(building);
  Check(!AfkWorld.StaticClear(map,new(5,3),who)&&!AfkWorld.StaticClear(map,new(6,4),who),"native building footprint blocks path");map.buildings.Clear();
  var npc=new NPC();npc.Sprite=new AnimatedSprite("fixture");Place(npc,new(5,3));map.characters.Add(npc);
  Check(AfkWorld.DynamicBlocked(map,AfkWorld.FootBox(new(5,3)),who)&&AfkWorld.StaticClear(map,new(5,3),who),"actual NPC is temporary occupancy, not a broken static connection");
  Check(!AfkWorld.CanStand(map,new(5,3),who),"native actor occupied tile never executable while blocked");map.characters.Clear();
  var cow=new FarmAnimal();cow.Sprite=new AnimatedSprite("fixture");Place(cow,new(5,3));map.animals[17]=cow;
  Check(AfkWorld.DynamicBlocked(map,AfkWorld.FootBox(new(5,3)),who)&&AfkWorld.StaticClear(map,new(5,3),who),"actual FarmAnimal gets dynamic occupancy with static route retained");map.animals.Clear();
  var peer=new Farmer();peer.currentLocation=map;Place(peer,new(5,3));Game1.otherFarmers[42]=peer;
  Check(AfkWorld.DynamicBlocked(map,AfkWorld.FootBox(new(5,3)),who)&&!AfkWorld.CanStand(map,new(5,3),who),"other Farmer bounding box blocks execution");Game1.otherFarmers.Clear();
  var gate=new Fence(new(5,3),"322",true);var west=new Fence(new(4,3),"322",false);var east=new Fence(new(6,3),"322",false);
  map.objects[new(5,3)]=gate;map.objects[new(4,3)]=west;map.objects[new(6,3)]=east;
  Check(AfkWorld.GateCanOpen(gate)&&gate.gatePosition.Value==0,"actual native closed gate recognized with legal fence connectivity");
  Check(AfkWorld.StaticClear(map,new(5,3),who)&&!AfkWorld.CanStand(map,new(5,3),who),"planning allows gate interaction but execution cannot stand inside closed gate");
  Check(!AfkWorld.TryOpenGate(map,new(5,3),who)&&gate.gatePosition.Value==0,"cannot remotely open a gate");
  who.Position=new Vector2(5*64,2*64-16);
  Check(who.TilePoint==new Point(5,2),"fixture farmer is adjacent to gate");
  int before=map.objects.Count();bool passableBefore=who.TemporaryPassableTiles.IsEmpty();
  Check(AfkWorld.TryOpenGate(map,new(5,3),who)&&gate.isPassable(),"actual native toggleGate opens a required gate");
  Check(map.objects.Count()==before&&who.TemporaryPassableTiles.IsEmpty()==passableBefore,"opening gate neither removes objects nor grants temporary noclip tiles");
  Check(AfkWorld.CanStand(map,new(5,3),who),"opened native gate actually becomes collision-passable");
  var home=new StardewValley.Locations.FarmHouse();home.name.Value="FarmHouse";home.map=Fixture();Game1.locations.Add(home);
  var other=new GameLocation();other.name.Value="BSVSecondFixture";other.map=Fixture();Game1.locations.Add(other);
  map.objects.Clear();map.warps.Add(new Warp(9,3,other.Name,0,3,false));other.warps.Add(new Warp(9,3,home.Name,0,3,false));
  var world=new AfkWorld(who);
  Check(world.LocationRoute(map.Name,home.Name)?.SequenceEqual(new[]{map.Name,other.Name,home.Name})==true,"native world graph reads actual Warp objects into location route");
  var route=world.Route(new(map.Name,1,3),new(home.Name,3,3));
  Check(route!=null&&route.Count(e=>e.Transition)==2,"actual world adapter joins legal local paths across native warp metadata");
  other.objects[new(8,3)]=new Chest(true);
  route=new AfkWorld(who).Route(new(map.Name,1,3),new(home.Name,3,3));
  Check(route!=null&&!route.Any(e=>e.To==new TileNode(other.Name,8,3)),"new native Chest at cross-map exit approach is bypassed on replan");
  map.warps.Add(new Warp(9,6,other.Name,0,3,false));Place(npc,new(9,3));map.characters.Add(npc);
  route=new AfkWorld(who).Route(new(map.Name,1,3),new(home.Name,3,3));
  Check(route!=null&&route.First(e=>e.Transition).DoorY==6,"NPC on source-map exit tile makes alternate legal exit preferable");
  map.characters.Clear();
  route=new AfkWorld(who).Route(new(map.Name,1,3),new(home.Name,3,3));
  Check(route!=null&&route.First(e=>e.Transition).DoorY==3,"unoccupied original exit becomes preferred again on fresh search");
  var farm=new GameLocation();farm.name.Value="Farm";farm.map=Fixture();Game1.locations.Add(farm);
  var forest=new GameLocation();forest.name.Value="Forest";forest.map=Fixture();Game1.locations.Add(forest);forest.warps.Add(new Warp(9,3,"Farm",41,64,false));
  var cave=new GameLocation();cave.name.Value="FarmCave";cave.map=Fixture();Game1.locations.Add(cave);cave.warps.Add(new Warp(9,3,"Farm",34,6,false));
  Game1.whichFarm=6;
  Check(new AfkWorld(who).Links("Forest").Single().Target==new TileNode("Farm",82,103)
   &&new AfkWorld(who).Links("FarmCave").Single().Target==new TileNode("Farm",34,16),"Beach Farm native fallback entries represented without moving Farmer");
  Game1.whichFarm=5;
  Check(new AfkWorld(who).Links("Forest").Single().Target==new TileNode("Farm",40,64)
   &&new AfkWorld(who).Links("FarmCave").Single().Target==new TileNode("Farm",30,36),"Four Corners native fallback entries represented without moving Farmer");
  farm.Map.Properties["ForestEntry"]="2 3";
  Check(new AfkWorld(who).Links("Forest").Single().Target==new TileNode("Farm",2,3),"explicit Farm entry metadata overrides farm-type fallback");
  Game1.whichFarm=0;
  h.UnpatchAll(h.Id);
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=Checks,scenarios=Names,real_native_objects=true,real_native_gate_operation=true,live_game=false,real_multiplayer=false,scope="synthetic xTile map, actual game object/terrain and gate APIs; map loading/island context supplied by headless boundaries"}));
 }
 static void Place(Character actor,Point tile)
 {actor.Position=new Vector2(64,64);var center=actor.GetBoundingBox().Center;actor.Position+=new Vector2(tile.X*64+32-center.X,tile.Y*64+32-center.Y);}
 static Map Fixture()
 {
  var map=new Map("BSVPathFixture");var sheet=new TileSheet("fixture",map,"not-rendered.png",new Size(2,2),new Size(16,16));map.AddTileSheet(sheet);
  var back=new Layer("Back",map,new Size(10,8),new Size(16,16));var wall=new Layer("Buildings",map,new Size(10,8),new Size(16,16));map.AddLayer(back);map.AddLayer(wall);
  for(int y=0;y<8;y++)for(int x=0;x<10;x++)back.Tiles[x,y]=new StaticTile(back,sheet,BlendMode.Alpha,0);
  return map;
 }
}
