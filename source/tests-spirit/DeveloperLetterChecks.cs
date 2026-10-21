using System.Reflection;
using System.Runtime.Serialization;
using BetterStardewValley;
using BetterStardewValley.DeveloperAppreciation;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Menus;

internal static class DeveloperLetterChecks
{
    private static bool Skip() => false;
    private static bool InterruptTransfer;
    private static void Fault(Item item){if(InterruptTransfer && item.QualifiedItemId==WineRules.BloodOfJesus)throw new InvalidOperationException("injected inventory-hook fault");}
    internal static object Run(Farmer player, IModHelper helper, IMonitor monitor, Action<bool,string> check)
    {
        var h=new Harmony("BSV.Letter.Headless.Effects");
        // Rendering/audio/notification effects require a running game. Native registry,
        // mail parser, actual inventory stack/slot insertion and production patches run.
        h.Patch(DrawPatchTargets.Require(typeof(Farmer),"OnItemReceived",typeof(void),typeof(Item),typeof(int),typeof(Item),typeof(bool)),prefix:new(typeof(DeveloperLetterChecks),nameof(Skip)));
        h.Patch(DrawPatchTargets.Require(typeof(Game1),"playSound",typeof(bool),typeof(string),typeof(int?)),prefix:new(typeof(DeveloperLetterChecks),nameof(Skip)));
        h.Patch(DrawPatchTargets.Require(typeof(Game1),"showRedMessage",typeof(void),typeof(string),typeof(bool)),prefix:new(typeof(DeveloperLetterChecks),nameof(Skip)));
        var service=new DeveloperLetterService(helper,monitor,"BSV.Letter.Tests");
        Game1.objectData["348"]=new ObjectData{Name="Wine",DisplayName="Wine",Type="Basic",Category=-26,Price=400,Edibility=-300};
        WineData.EditObjects(Game1.objectData,k=>k,"fixture/grape","fixture/blood");
        check(!DeveloperLetterRules.Eligible(1,Season.Winter,27)&&DeveloperLetterRules.Eligible(1,Season.Winter,28),"actual date boundary winter27/28");
        check(DeveloperLetterRules.Eligible(2,Season.Spring,1)&&DeveloperLetterRules.Eligible(7,Season.Summer,10),"old saves and late join date eligibility");
        var errors=new List<string>();
        check(DeveloperLetterService.Queue(player,errors.Add)&&!DeveloperLetterService.Queue(player,errors.Add),"personal pending entitlement and one mailbox entry");
        player.mailbox.Remove(DeveloperLetterRules.Id);player.mailForTomorrow.Add(DeveloperLetterRules.Id);
        check(!DeveloperLetterService.Queue(player,errors.Add)&&player.mailbox.Count==0,"tomorrow queue does not duplicate into today");
        player.mailForTomorrow.Remove(DeveloperLetterRules.Id);
        string text=DeveloperLetterRules.Letter(k=>k);
        LetterViewerMenu Menu(bool collection=false)
        {
            var m=(LetterViewerMenu)FormatterServices.GetUninitializedObject(typeof(LetterViewerMenu));
            m.mailTitle=DeveloperLetterRules.Id;m.isFromCollection=collection;m.isMail=true;m.scale=1;
            m.mailMessage=new(){"native parser fixture"};typeof(LetterViewerMenu).GetField("itemsToGrab")!.SetValue(m,new List<ClickableComponent>());m.width=1280;m.height=720;
            m.backButton=new(new Rectangle(0,0,10,10),null,new Rectangle(0,0,10,10),1);
            m.forwardButton=new(new Rectangle(20,0,10,10),null,new Rectangle(0,0,10,10),1);
            player.mailReceived.Add(DeveloperLetterRules.Id); // native mailbox does this before constructing menu.
            DrawPatchTargets.Require(typeof(LetterViewerMenu),"HandleItemCommand",typeof(string),typeof(string)).Invoke(m,new object[]{text});
            // Native constructor lays out attachments after parsing; this fixture
            // bypasses only the GPU-dependent constructor, not parsing or clicking.
            foreach(var slot in m.itemsToGrab) slot.bounds=new Rectangle(608,584,64,64);
            return m;
        }
        string localeRoot=Environment.GetEnvironmentVariable("BSV_TEST_I18N_PATH")!;
        var testedLocales=new List<string>();
        foreach(string file in Directory.GetFiles(localeRoot,"*.json").OrderBy(p=>p,StringComparer.Ordinal))
        {
            string locale=Path.GetFileNameWithoutExtension(file);testedLocales.Add(locale);NativeTranslationFixture.Locale=locale;
            var translations=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file))!;
            foreach(var pair in translations)
                check(BsvI18n.Get(helper,pair.Key)==pair.Value,"actual SMAPI Translation value via production BsvI18n: "+locale+"/"+pair.Key);
            text=DeveloperLetterRules.Letter(k=>BsvI18n.Get(helper,k));
            check(text.Split("%item object ").Length==2 && text.Contains(DeveloperLetterRules.Attachment),"one immutable technical attachment after composing "+locale);
            string receipt=player.modData[DeveloperLetterRules.StateKey];int queued=player.mailbox.Count;
            var localized=Menu();
            check(localized.itemsToGrab.Count==1&&localized.itemsToGrab[0].item.QualifiedItemId==WineRules.BloodOfJesus
                &&localized.itemsToGrab[0].item.Stack==1&&player.modData[DeveloperLetterRules.StateKey]==receipt&&player.mailbox.Count==queued,
                "native localized mail parsing doesn't award, queue or alter personal receipt: "+locale);
            check(Menu(true).itemsToGrab.Count==0,"native collection reread has no attachment: "+locale);
        }
        NativeTranslationFixture.Locale="unrecognized-custom-language";
        check(BsvI18n.Get(helper,"developer-letter.name")=="A Letter from the Developer","unknown custom locale defaults to English, not another custom language");
        NativeTranslationFixture.Locale="en";text=DeveloperLetterRules.Letter(k=>BsvI18n.Get(helper,k));
        var first=Menu();var gift=first.itemsToGrab.Single().item;
        check(gift.QualifiedItemId==WineRules.BloodOfJesus&&gift.Stack==1&&gift.Quality==0&&((StardewValley.Object)gift).Price==30000,"native %item qualified ID creates existing normal-quality 30000g wine x1");
        check(!gift.modData.Keys.Any(k=>k.Contains("WineBatch")||k.Contains("WinePending")),"gift carries no brewing batch state");
        player.Items.Clear();player.MaxItems=12;
        for(int i=0;i<12;i++)player.Items.Add(ItemRegistry.Create("(O)348",999));
        check(!DeveloperLetterService.TransferOne(player,gift,errors.Add)&&DeveloperLetterService.IsPending(player),"real full inventory rejects without claiming or dropping gift");
        first.itemsToGrab.Clear(); // a replaced menu has no second live item owner.
        check(DeveloperLetterService.Queue(player,errors.Add)&&player.mailReceived.Contains(DeveloperLetterRules.Id),"opened-unclaimed mail restored using explicit pending receipt, not mailReceived guess");
        string persisted=player.modData[DeveloperLetterRules.StateKey];
        player.modData[DeveloperLetterRules.StateKey]=persisted;
        var restored=Menu();
        player.Items[11]=ItemRegistry.Create(WineRules.BloodOfJesus,998);
        check(DeveloperLetterService.TransferOne(player,restored.itemsToGrab.Single().item,errors.Add)&&player.Items[11].Stack==999,"native stack merge succeeds with all slots occupied");
        check(player.modData[DeveloperLetterRules.StateKey]==DeveloperLetterRules.Claimed,"claimed receipt only after successful native transfer");
        check(!DeveloperLetterService.TransferOne(player,ItemRegistry.Create(WineRules.BloodOfJesus),errors.Add)&&player.Items[11].Stack==999,"repeated transfer cannot grant a second bottle");
        player.Items.Clear();player.mailbox.Clear();
        check(!DeveloperLetterService.Queue(player,errors.Add)&&Menu(true).itemsToGrab.Count==0&&Menu().itemsToGrab.Count==0,"sold gift, history reread and stale mailbox reconstruction never reissue claimed gift");
        player.modData.Remove(DeveloperLetterRules.StateKey);
        check(!DeveloperLetterService.Queue(player,errors.Add)&&!player.modData.ContainsKey(DeveloperLetterRules.StateKey),"ambiguous historical read without receipt is not guessed unclaimed");
        player.mailReceived.Remove(DeveloperLetterRules.Id);player.modData[DeveloperLetterRules.StateKey]="future-schema-preserve";
        check(!DeveloperLetterService.Queue(player,errors.Add)&&player.modData[DeveloperLetterRules.StateKey]=="future-schema-preserve","future/corrupt state not reset into another gift");
        player.modData.Remove(DeveloperLetterRules.StateKey);DeveloperLetterService.Queue(player,errors.Add);
        // Actual empty bags retain null slots; Clear() alone creates a zero-slot bag.
        for(int i=0;i<player.MaxItems;i++)player.Items.Add(null);
        var live=Menu();Game1.activeClickableMenu=live;
        var bounds=live.itemsToGrab.Single().bounds;
        var lease=typeof(DeveloperLetterService).GetField("Active",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;
        Console.Error.WriteLine($"LEASE same={ReferenceEquals(lease.GetType().GetProperty("Value")!.GetValue(lease),live)} instance={ReferenceEquals(DeveloperLetterService.Instance,service)} owner={ReferenceEquals(Game1.player,player)} erroritem={ItemRegistry.GetDataOrErrorItem(WineRules.BloodOfJesus).IsErrorItem}");
        Console.Error.WriteLine($"CLICK before: interact={live.ShouldShowInteractable()} bounds={bounds} contains={live.itemsToGrab[0].containsPoint(bounds.Center.X,bounds.Center.Y)} state={player.modData[DeveloperLetterRules.StateKey]} slots={player.Items.Count} local={player.IsLocalPlayer} active={Game1.activeClickableMenu==live}");
        live.receiveLeftClick(bounds.Center.X,bounds.Center.Y,false);
        Console.Error.WriteLine($"CLICK after: state={player.modData[DeveloperLetterRules.StateKey]} slots={player.Items.Count} item={live.itemsToGrab[0].item?.QualifiedItemId} errors={string.Join(";",Recorder.Logs)}");
        check(player.Items.Count(i=>i?.QualifiedItemId==WineRules.BloodOfJesus)==1&&!DeveloperLetterService.IsPending(player),"actual patched native attachment click inserts one bottle and commits receipt");
        live.receiveLeftClick(bounds.Center.X,bounds.Center.Y,false);
        check(player.Items.Count(i=>i?.QualifiedItemId==WineRules.BloodOfJesus)==1,"repeat actual click no duplicate");
        // Fresh entitlement fixtures exercise the actual patched native close path.
        player.modData[DeveloperLetterRules.StateKey]=DeveloperLetterRules.Pending;
        for(int i=0;i<player.Items.Count;i++)player.Items[i]=ItemRegistry.Create("(O)348",999);
        var full=Menu();Game1.activeClickableMenu=full;
        var priorNext=Game1.nextClickableMenu;
        full.receiveLeftClick(640,616,false);
        Console.Error.WriteLine($"FULL state={player.modData[DeveloperLetterRules.StateKey]} attachments={full.itemsToGrab.Count(c=>c.item!=null)} nextChanged={!ReferenceEquals(priorNext,Game1.nextClickableMenu)} slots={player.Items.Count}");
        check(DeveloperLetterService.IsPending(player)&&full.itemsToGrab.All(c=>c.item==null)&&ReferenceEquals(priorNext,Game1.nextClickableMenu),
            "full actual click retains one durable entitlement, without overflow menu or duplicate attachment");
        var fullClose=Menu();
        DrawPatchTargets.Require(typeof(LetterViewerMenu),"cleanupBeforeExit",typeof(void)).Invoke(fullClose,null);
        check(DeveloperLetterService.IsPending(player)&&fullClose.itemsToGrab.Count==0,"full native close clears temporary item but preserves entitlement");
        var emptyClose=Menu();player.Items[11]=null;
        DrawPatchTargets.Require(typeof(LetterViewerMenu),"cleanupBeforeExit",typeof(void)).Invoke(emptyClose,null);
        check(!DeveloperLetterService.IsPending(player)&&player.Items[11]?.QualifiedItemId==WineRules.BloodOfJesus,"native close inserts exactly one when capacity exists");
        DrawPatchTargets.Require(typeof(LetterViewerMenu),"cleanupBeforeExit",typeof(void)).Invoke(emptyClose,null);
        check(player.Items[11]?.Stack==1,"repeated native cleanup cannot regrant");
        player.modData[DeveloperLetterRules.StateKey]=DeveloperLetterRules.Pending;
        var stale=Menu();var staleItem=stale.itemsToGrab.Single().item;var current=Menu();player.Items[11]=null;
        service.Transfer(stale,player,staleItem);
        check(DeveloperLetterService.IsPending(player)&&player.Items[11]==null&&stale.itemsToGrab.Count==0,"old menu lease cannot act after replacement");
        var second=new Farmer();second.UniqueMultiplayerID=99;second.MaxItems=12;for(int i=0;i<12;i++)second.Items.Add(null);
        typeof(Game1).GetField("_player",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,second);
        check(DeveloperLetterService.Queue(second,errors.Add)&&DeveloperLetterService.IsPending(player),"second local-player fixture gets independent personal entitlement");
        check(DeveloperLetterService.TransferOne(second,ItemRegistry.Create(WineRules.BloodOfJesus),errors.Add)&&DeveloperLetterService.IsPending(player),"second-player claim doesn't claim first-player record (not a real network session)");
        service.Transfer(current,player,current.itemsToGrab.Single().item);
        check(DeveloperLetterService.IsPending(player),"stale first-player callback cannot grant while another player is local");
        typeof(Game1).GetField("_player",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,player);
        h.Patch(DrawPatchTargets.Require(typeof(Farmer),"addItemToInventory",typeof(Item),typeof(Item)),prefix:new(typeof(DeveloperLetterChecks),nameof(Fault)));
        InterruptTransfer=true;
        check(!DeveloperLetterService.TransferOne(player,ItemRegistry.Create(WineRules.BloodOfJesus),errors.Add)
            &&player.modData[DeveloperLetterRules.StateKey]==DeveloperLetterRules.Transferring,"uncertain external inventory exception blocks blind reissue instead of duplicating");
        InterruptTransfer=false;
        check(!DeveloperLetterService.Queue(player,errors.Add),"interrupted transfer is diagnosed, not reset to another gift");
        check(DeveloperLetterRules.SafeText("%item @ [#]")=="％item ＠ ［＃］","translated body cannot inject attachment/title/name commands");
        return new{nativeParser=true,nativeInventory=true,nativeClick=true,testedLocales,translation_dispatch="helper boundary; actual SMAPI Translation objects and shipped resources",graphicsAudioAndItemNotification="explicit headless boundary",live_game=false,real_multiplayer=false,errorsExpected=errors};
    }
}
