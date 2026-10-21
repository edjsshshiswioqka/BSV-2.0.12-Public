using StardewValley;
using StardewValley.Tools;
using SObject=StardewValley.Object;

partial class Program
{
    static void LobsterBait139Checks()
    {
        const string baitId="Eddie.BetterStardewValley_GiantLobsterBait", bait="(O)"+baitId;
        Reset();
        foreach(string id in new[]{baitId,"685","SpecificBait"})
            Game1.objectData[id]=new(){Name=id,DisplayName=id,Category=-21,Type="Basic"};
        Game1.toolData["FiberglassRod"]=new(){ClassName="FishingRod",Name="Fiberglass Rod",DisplayName="Fiberglass Rod"};
        Rod=new FishingRod(2){ItemId="FiberglassRod"};Who.Items[0]=Rod;
        Rod.attachments[0]=ItemRegistry.Create<SObject>(bait,1);
        Run("RememberBait");Rod.attachments[0]=null; // native last-bait consumption, not a second BSV debit.
        var normal=ItemRegistry.Create<SObject>("(O)685",8);
        var targeted=ItemRegistry.Create<SObject>("(O)SpecificBait",9);
        targeted.preservedParentSheetIndex.Value="Eddie.BetterStardewValley_SpiritFish";
        Who.Items[1]=normal;Who.Items[2]=targeted;Who.Items[3]=ItemRegistry.Create(bait,7);
        Run("TryTopUpBaitFrom",Who.Items);
        Check(Rod.attachments[0]?.QualifiedItemId==bait&&Rod.attachments[0].Stack==7&&Who.Items[3]==null&&normal.Stack==8&&targeted.Stack==9,
            "AFK last special bait: production refill moves seven real backpack bait, leaves normal and Spirit-targeted bait untouched");
        var chest=AddChest(4,5);chest.Items.Add(ItemRegistry.Create(bait,5));
        Run("TryTopUpBait",chest);
        Check(Rod.attachments[0]!.Stack==12&&chest.Items.All(i=>i==null),"AFK existing chest refill shares exact-ID transfer, no virtual inventory or duplication");
        Run("TryTopUpBait",chest);Check(Rod.attachments[0]!.Stack==12,"AFK empty source repeated refill cannot create bait");
        Rod.attachments[0]=normal;Who.Items[1]=null;Run("RememberBait");
        chest.Items.Add(ItemRegistry.Create(bait,5));Run("TryTopUpBait",chest);
        Check(Rod.attachments[0]==normal&&normal.Stack==8&&chest.Items.Where(i=>i!=null).Sum(i=>i.Stack)==5,"manual bait change updates AFK choice; special bait is not substituted");
        var decorated=ItemRegistry.Create<SObject>(bait,1,quality:2);decorated.modData["test/stack-identity"]="retained";
        Rod.attachments[0]=decorated;Run("RememberBait");
        string saved=Newtonsoft.Json.JsonConvert.SerializeObject(Data);
        Rod.attachments[0]=null;
        Session.GetType().GetField("BaitTemplate",Any)!.SetValue(Session,null);
        Session.GetType().GetField("Data",Any)!.SetValue(Session,Newtonsoft.Json.JsonConvert.DeserializeObject<BetterStardewValley.AfkFishing.AfkSave>(saved));
        Run("RememberBait");
        Check(Rod.attachments[0]==null,"restoring saved bait identity never creates usable bait in rod");
        Who.Items[3]=decorated.getOne();Who.Items[3].Stack=3;
        Run("TryTopUpBaitFrom",Who.Items);
        Check(Rod.attachments[0]?.QualifiedItemId==bait&&Rod.attachments[0].Stack==3&&Rod.attachments[0].Quality==2
            &&Rod.attachments[0].modData["test/stack-identity"]=="retained"&&Who.Items[3]==null,"empty-rod saved session restores exact bait preference then transfers three real matching items");
        Run("Stop");Check(Data.BaitPreference==""&&Field<SObject?>("BaitTemplate")==null,"manual AFK stop clears saved bait preference");
    }
}
