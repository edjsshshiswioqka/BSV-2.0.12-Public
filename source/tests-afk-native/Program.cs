using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using StardewValley.GameData.Objects;
using StardewValley.ItemTypeDefinitions;
using System.Text.Json;
using SObject=StardewValley.Object;
class Program
{
 static GameLocation Location=null!;
 static bool Map(ref GameLocation __result){__result=Location;return false;}
 static FishingRod Rod=null!;static SObject? Bait;static int Pulls;static object[]? Last;
 static bool Skip()=>false;
 static bool Tool(ref Tool __result){__result=Rod;return false;}
 static void Init(object o,Type t,string name,params object[] args){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;f.SetValue(o,Activator.CreateInstance(f.FieldType,args));}
 static bool CurrentItem(ref Item __result){__result=Rod;return false;}
 static bool Food(ref SObject __result){__result=Bait!;return false;}
 static bool Luck(ref double __result){__result=0;return false;}
 static bool Pull(string fishId,int fishSize,int fishQuality,int fishDifficulty,bool treasureCaught,bool wasPerfect,bool fromFishPond,string setFlagOnCatch,bool isBossFish,int numCaught)
 {Pulls++;Last=new object[]{fishId,fishSize,fishQuality,fishDifficulty,treasureCaught,wasPerfect,fromFishPond,setFlagOnCatch,isBossFish,numCaught};return false;}
 static void Main(string[] args)
 {
  var h=new Harmony("BSV.Afk.NativePlayback");int checks=0;var names=new List<string>();void Check(bool v,string s){checks++;if(!v)throw new Exception(s);names.Add(s);}
  void Patch(Type t,string n,string callback,params Type[] p)=>h.Patch(t.GetMethod(n,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,p,null)!,prefix:new HarmonyMethod(typeof(Program),callback));
  ItemRegistry.AddTypeDefinition(new ObjectDataDefinition());Game1.objectData=new Dictionary<string,ObjectData>();Game1.random=new Random(74);
  Game1.objectData["774"]=new(){Name="Wild Bait",DisplayName="Wild Bait",Type="Basic",Category=-21,Texture="Maps/springobjects",SpriteIndex=774};
  Rod=(FishingRod)FormatterServices.GetUninitializedObject(typeof(FishingRod));
  Init(Rod,typeof(Tool),"numAttachmentSlots");Init(Rod,typeof(Tool),"attachments");Rod.AttachmentSlotsCount=1;
  var farmer=(Farmer)FormatterServices.GetUninitializedObject(typeof(Farmer));typeof(Game1).GetField("_player",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,farmer);
  Patch(typeof(Farmer),"get_CurrentTool",nameof(Tool));Patch(typeof(Farmer),"get_CurrentItem",nameof(CurrentItem));Patch(typeof(Farmer),"get_DailyLuck",nameof(Luck));
  Patch(typeof(BobberBar),"Reposition",nameof(Skip));
  Location=(GameLocation)FormatterServices.GetUninitializedObject(typeof(GameLocation));Init(Location,typeof(GameLocation),"name");Location.name.Value="Beach";Game1.game1=(Game1)FormatterServices.GetUninitializedObject(typeof(Game1));Game1.game1.instanceGameLocation=Location;Patch(typeof(Game1),"get_currentLocation",nameof(Map));
  Patch(typeof(Game1),"exitActiveMenu",nameof(Skip));Patch(typeof(Game1),"set_activeClickableMenu",nameof(Skip),typeof(IClickableMenu));Patch(typeof(Game1),"setRichPresence",nameof(Skip),typeof(string),typeof(object));
  Patch(typeof(FishingRod),"pullFishFromWater",nameof(Pull),typeof(string),typeof(int),typeof(int),typeof(int),typeof(bool),typeof(bool),typeof(bool),typeof(string),typeof(bool),typeof(int));
  var mod=Assembly.LoadFrom(args[0]);var type=mod.GetType("BetterStardewValley.AfkFishing.AfkFishingService",true)!;
  var finish=type.GetMethod("CompleteMinigame",BindingFlags.Static|BindingFlags.NonPublic)!;
  void Complete(BobberBar b)=>finish.Invoke(null,new object[]{b,new GameTime(TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(16))});
  // Parameterless menu would initialize graphics: allocate only its native data for the exercised success branch.
  BobberBar Make(bool treasure=false,bool boss=false,int challenge=0)
  {var b=(BobberBar)FormatterServices.GetUninitializedObject(typeof(BobberBar));b.whichFish="128";b.fishSize=20;b.maxFishSize=30;b.fishQuality=2;b.difficulty=85;b.treasure=treasure;b.bossFish=boss;b.challengeBaitFishes=challenge;b.setFlagOnCatch="native-flag";return b;}
  var one=Make();Complete(one);Complete(one);
  Check(Pulls==1,"production CompleteMinigame calls native landing once, even when invoked twice");
  Check((string)Last![0]=="128"&&(int)Last[2]==2&&(int)Last[3]==85&&(bool)Last[5],"native chosen fish/quality/difficulty preserved; Perfect true");
  Check(!(bool)Last[4]&&(string)Last[7]=="native-flag"&&(int)Last[9]==1,"ordinary catch flags and count preserved");
  var chest=Make(true);Complete(chest);Check((bool)Last![4]&&Pulls==2,"rolled native treasure retained once");
  Complete(Make(false,false,3));Check((int)Last![9]==3,"actual native Challenge Bait completion yields three");
  Bait=new SObject("774",5);Rod.attachments[0]=Bait;bool single=false,doubled=false;
  for(int i=0;i<100;i++){Complete(Make());single|=(int)Last![9]==1;doubled|=(int)Last![9]==2;}
  Check(single&&doubled,"actual native Wild Bait roll remains probabilistic");
  Complete(Make(false,true));Check((int)Last![9]==1&&(bool)Last[8],"native Wild Bait legendary exclusion retained");
  var max=Make();max.fishSize=max.maxFishSize;Complete(max);Check((int)Last![1]==30,"native Perfect keeps maximum fish size");
  Check(!one.fadeOut&&one.handledFishResult,"native success consumed before presentation; no pending minigame work");
  Game1.objectData["testFood"]=new(){Name="Test food",DisplayName="Test food",Type="Cooking",Category=-7,Edibility=20,Texture="Maps/springobjects",SpriteIndex=0};
  Game1.objectData["badFood"]=new(){Name="Bad food",DisplayName="Bad food",Type="Basic",Category=-7,Edibility=-10,Texture="Maps/springobjects",SpriteIndex=0};
  var foodCheck=type.GetMethod("Food",BindingFlags.Static|BindingFlags.NonPublic)!;
  bool FoodOK(Item i)=>(bool)foodCheck.Invoke(null,new object[]{i})!;
  var edible=new SObject("testFood",3);Check(!FoodOK(edible),"unknown energy-positive food fails closed without terrestrial provenance");
  edible.questItem.Value=true;Check(!FoodOK(edible),"real native quest food refused");edible.questItem.Value=false;
  Check(!FoodOK(new SObject("badFood",2))&&!FoodOK(Bait!),"negative-energy food and bait excluded");
  edible.Quality=2;edible.modData["afk-test"]= "metadata preserved";
  var serializer=new System.Xml.Serialization.XmlSerializer(typeof(Item[]));var text=new StringWriter();serializer.Serialize(text,new Item[]{edible});
  var restored=(Item[])serializer.Deserialize(new StringReader(text.ToString()))!;
  Check(restored[0].QualifiedItemId==edible.QualifiedItemId&&restored[0].Stack==3&&restored[0].Quality==2&&restored[0].modData["afk-test"]=="metadata preserved","native Item[] serialization retains identity/quantity/quality/modData for pending outputs");
  // Exercise the real production chest filter, including the integrated machine marker.
  ItemRegistry.AddTypeDefinition(new BigCraftableDataDefinition());
  Game1.bigCraftableData=new Dictionary<string,StardewValley.GameData.BigCraftables.BigCraftableData>{{"130",new(){Name="Chest",DisplayName="Chest",Texture="fixture",SpriteIndex=0}}};
  var eligible=mod.GetType("BetterStardewValley.AfkFishing.AfkWorld",true)!.GetMethod("EligibleChest",BindingFlags.Static|BindingFlags.NonPublic)!;
  bool ChestOK(SObject c)=>(bool)eligible.Invoke(null,new object[]{c})!;
  var box=new StardewValley.Objects.Chest(true);
  Check(ChestOK(box),"production filter accepts native ordinary chest");
  var machine=mod.GetType("Eddie.FishingMachine.ModEntry",true)!;
  string tag=(string)machine.GetField("Tag",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(null)!;
  box.modData[tag+"MachineId"]="fixture-machine";box.modData[tag+"Role"]="anchor";
  Check(!ChestOK(box),"production filter rejects real integrated fishing/crab machine anchor despite native Chest base type");
  box.modData.Clear();box.fridge.Value=true;Check(!ChestOK(box),"production filter rejects fridge");box.fridge.Value=false;
  box.GlobalInventoryId="fixture-global";Check(!ChestOK(box),"production filter rejects linked global/special storage");
  Check(!ChestOK(new SObject("testFood",1)),"production filter rejects menu/object devices that are not native chests");
  h.UnpatchAll(h.Id);
  Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,scenarios=names,real_native_bobber_update=true,graphics_audio_and_landing_effects="stubbed at explicit native boundaries",live_game=false,real_multiplayer=false}));
 }
}
