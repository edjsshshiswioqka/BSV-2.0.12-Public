using StardewModdingAPI;
using StardewValley;
namespace BetterStardewValley.Orders;

// A — explicit vanilla trade templates. No discovery, shipping or crafting-unlock prerequisite.
internal static class OrderCatalog
{
 internal static List<MarketCandidate> BuildMarket(IEnumerable<string>? exclusions = null)
 {
  var objects=DataLoader.Objects(Game1.content);var fish=DataLoader.Fish(Game1.content);
  var seeds=DataLoader.Crops(Game1.content).Keys.ToHashSet(StringComparer.Ordinal);
  return MarketCandidates(objects.Select(p=>new CatalogItem(p.Key,p.Value.Price,p.Value.Category,p.Value.Type)),fish.Where(p=>!p.Value.Split('/').Skip(1).Take(1).Contains("trap")).Select(p=>p.Key).ToHashSet(StringComparer.Ordinal),seeds,exclusions);
 }
 internal sealed record CatalogItem(string Id,int Price,int Category,string? Type = null);
 internal static List<MarketCandidate> MarketCandidates(IEnumerable<CatalogItem> objects,ISet<string> fish,ISet<string> seeds,IEnumerable<string>? exclusions = null)
 {
  var result=new List<MarketCandidate>();
  var excluded = new HashSet<string>(exclusions ?? Array.Empty<string>(),StringComparer.Ordinal);
  foreach(var data in objects.OrderBy(d=>d.Id,StringComparer.Ordinal))
  {
   string id=data.Id;
   // Data identity only, never translated names. Apply at generation, leaving all
   // existing contracts, prices, receipts and accepted orders unchanged.
   if(excluded.Contains("(O)"+id)||data.Type=="Quest")continue;
   if(id is "889" or "890")continue; // Qi quest-only crop/seed; never trade protected quest progress.
   // Vanilla and the established BSV grape wine only. Other mod/debug items require an explicit safety policy.
   if((!int.TryParse(id,out _)&&!ModernCrops.Contains(id)&&id!=WineRules.GrapeWine.Replace("(O)",""))||data.Price>10000)continue;
   int price=data.Price;
   if(seeds.Contains(id)&&price<=0)price=20; // stable seed reference fallback, never a free contract
   if(price<=0)continue;
   string q="(O)"+id;bool crop=data.Category is -75 or -79 or -80;
   bool edibleFish=data.Category==-4&&fish.Contains(id)&&id is not("159" or "160" or "163" or "682" or "775" or "898" or "899" or "900" or "901" or "902");
   if(crop)result.Add(new(q,"Pierre",price));
   if(edibleFish)result.Add(new(q,"Willy",price));
   if(Robin.Contains(id))result.Add(new(q,"Robin",price,1));
   if(Gus.Contains(id))result.Add(new(q,"Gus",price));
   if(Marnie.Contains(id))result.Add(new(q,"Marnie",price));
   if(Clint.Contains(id))result.Add(new(q,"Clint",price,1));
   if(crop||edibleFish||seeds.Contains(id)||id is "378" or "380" or "384" or "386")result.Add(new(q,"Morris",price,id is "378" or "380" or "384" or "386"?1:0,crop||edibleFish));
   if((id is "216" or "194" or "195" or "211" or "210" or "196")&&data.Type=="Cooking"&&data.Category==-7)result.Add(new(q,"Dwarf",price));
  }
  return result;
 }
 private static readonly HashSet<string> ModernCrops=new(){"Carrot","CarrotSeeds","SummerSquash","SummerSquashSeeds","Broccoli","BroccoliSeeds","Powdermelon","PowdermelonSeeds"};
 private static readonly HashSet<string> Robin=new(){"388","709","390","330"};
 private static readonly HashSet<string> Gus=new(){"346","303","348","424","426","395",WineRules.GrapeWine.Replace("(O)","")};
 private static readonly HashSet<string> Marnie=new(){"176","180","174","182","184","186","436","438","440","424","426","306","307","442"};
 private static readonly HashSet<string> Clint=new(){"378","380","384","386","334","335","336","337","382"};
 internal static string ItemName(string id)
 {
  return ItemRegistry.GetDataOrErrorItem(id).DisplayName;
 }
 internal static string NpcName(string id)=>Game1.getCharacterFromName(id)?.displayName??id;
}
