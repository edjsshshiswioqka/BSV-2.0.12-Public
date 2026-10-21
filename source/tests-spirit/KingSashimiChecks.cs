using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BetterStardewValley;
using BetterStardewValley.LobsterKing;
using Eddie.FishingMachine.Core;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;
using SObject=StardewValley.Object;

internal static class KingSashimiChecks
{
    internal static object Run(Farmer player,Action<bool,string> check)
    {
        check(KingData.ApprovedSashimi.Edibility==80,"release approved recovery uses native Edibility 80");
        var recipes=new Dictionary<string,string>();KingData.Cooking(recipes,k=>k,false);
        var mail=new Dictionary<string,string>();KingMail.Data(mail,k=>k,false);
        check(recipes.Count==0&&mail.Count==0,"explicit disabled recipe and mail writers remain no-ops");
        KingData.Objects(Game1.objectData,k=>k,KingData.ApprovedSashimi);
        KingData.Cooking(recipes,k=>k,true);KingMail.Data(mail,k=>k,true);
        check(recipes[LobsterKingRules.SashimiId].Split('/')[0]==LobsterKingRules.Id+" 1"&&recipes[LobsterKingRules.SashimiId].Split('/')[2]==LobsterKingRules.SashimiId+" 1","actual recipe writer consumes one King and produces one sashimi");
        check(!CraftingRecipe.TryParseLevelRequirement(LobsterKingRules.SashimiId,recipes[LobsterKingRules.SashimiId],true,out _,out _,true),"sashimi recipe has no skill auto-unlock");
        var ledger=new KingLedger();var claim=ledger.Prepare(1,0,1,"fixture");claim.Committed=true;
        check(!KingMail.Deliver(player,ledger,2,true),"unclaimed generation does not deliver letter");
        ledger.Acquire(claim.Token,player.UniqueMultiplayerID,2);
        check(!KingMail.Deliver(player,ledger,2,true),"same-day acquisition does not deliver next-day letter");
        check(!KingMail.Deliver(player,ledger,3,false),"approval gate retains first-acquisition evidence without sending unusable recipe");
        check(KingMail.Deliver(player,ledger,3,true)&&!player.cookingRecipes.ContainsKey(LobsterKingRules.SashimiId),"next-day actual mailbox insert does not teach before reading");
        check(!KingMail.Deliver(player,ledger,3,true)&&player.mailbox.Count(s=>s==KingMail.Id)==1,"repeated/reconnected delivery is idempotent");
        player.mailbox.Remove(KingMail.Id);
        check(KingMail.Deliver(player,ledger,30,true),"late join/load catches up with persistent host ledger");
        CraftingRecipe.cookingRecipes??=new();CraftingRecipe.craftingRecipes??=new();
        CraftingRecipe.cookingRecipes[LobsterKingRules.SashimiId]=recipes[LobsterKingRules.SashimiId];
        var letter=(LetterViewerMenu)FormatterServices.GetUninitializedObject(typeof(LetterViewerMenu));
        var parse=DrawPatchTargets.Require(typeof(LetterViewerMenu),"HandleItemCommand",typeof(string),typeof(string));
        parse.Invoke(letter,new object[]{mail[KingMail.Id]});
        check(player.cookingRecipes.TryGetValue(LobsterKingRules.SashimiId,out int count)&&count==0,"actual native mail command teaches intended recipe on read");
        player.cookingRecipes[LobsterKingRules.SashimiId]=7;parse.Invoke(letter,new object[]{mail[KingMail.Id]});
        check(player.cookingRecipes[LobsterKingRules.SashimiId]==7&&!KingMail.Deliver(player,ledger,31,true),"rereading preserves crafted count and cannot resend");
        var stranger=new Farmer{UniqueMultiplayerID=203};check(!KingMail.Deliver(stranger,ledger,30,true)&&stranger.mailbox.Count==0,"nonrecipient player cannot inherit host mail");
        Game1.buffsDisplay=(BuffsDisplay)FormatterServices.GetUninitializedObject(typeof(BuffsDisplay));typeof(BuffsDisplay).GetField("updatedIDs")!.SetValue(Game1.buffsDisplay,new HashSet<string>());
        Game1.timeOfDay=800;Game1.gameTimeInterval=0;player.professions.Clear();
        var values=new List<object>();
        foreach(int quality in new[]{0,2})
        {
            var food=ItemRegistry.Create<SObject>(KingSashimi.QualifiedId,quality:quality);var b=food.GetFoodOrDrinkBuffs().Single();
            check(food.Price==2000&&b.id=="food"&&b.customFields.ContainsKey(KingSashimi.Marker),"actual native sashimi is 2000g and tagged food-slot buff");
            check(b.effects.Attack.Value==(quality==0?6:7)&&b.effects.Speed.Value==2,"actual native seasoning behavior for Attack/Speed "+quality);
            check(b.effects.WeaponSpeedMultiplier.Value==0&&b.effects.CriticalChanceMultiplier.Value==0,"native points do not become relative speed/critical multipliers");
            check(food.staminaRecoveredOnConsumption()==(quality==0?200:360)&&food.healthRecoveredOnConsumption()==(quality==0?200:360),"native recovery plus exact-food health correction: "+quality);
            player.buffs.Apply(b);check(KingSashimi.IsActive(player)&&KingSashimi.EffectiveSpeed(3,player)==5,"effective speed uses active owner's food and never mutates a weapon");
            check(KingSashimi.EffectiveSpeed(3,stranger)==3,"other farmer has no sashimi effective-speed bonus");
            NativeSpeed(player,check);
            NativeCritical(player,check);
            var effects=new StardewValley.Buffs.BuffEffects();effects.CriticalChanceMultiplier.Value=.5f;
            var relative=new Buff("BSV.Test.CriticalMultiplier",duration:10000,effects:effects);
            player.buffs.Apply(relative);
            check(Math.Abs(player.buffs.CriticalChanceMultiplier-.5f)<1e-6,"independent native critical multiplier fixture active");
            NativeCritical(player,check);player.buffs.Remove(relative.id);
            check(Math.Abs(KingSashimi.EffectiveCritical(.02f,stranger)-.02f)<1e-7,"critical bonus isolated to food owner");
            int day=Game1.Date.TotalDays;KingSashimi.Start(player,b,day,800);int before=b.millisecondsDuration;
            b.update(new GameTime(TimeSpan.Zero,TimeSpan.FromMilliseconds(1000)));
            check(b.millisecondsDuration==before,"sashimi normal update does not double-debit real-time during world pause");
            check(!KingSashimi.Advance(b,day,1750)&&KingSashimi.Advance(b,day,1800)==(quality==0),"sashimi 600-minute boundary with native quality modifier");
            if(quality==2)check(KingSashimi.Advance(b,day,2300),"seasoned sashimi native duration is 900 world minutes");
            values.Add(new{quality,attack=b.effects.Attack.Value,movement=b.effects.Speed.Value,weaponSpeedAddition=2,criticalPoints=2,criticalBaseAddition=.04,worldMinutes=quality==0?600:900,health=food.healthRecoveredOnConsumption(),energy=food.staminaRecoveredOnConsumption()});
            player.buffs.Remove("food");check(!KingSashimi.IsActive(player)&&KingSashimi.EffectiveSpeed(3,player)==3&&!KingSashimi.Advance(b,day,2400),"food removal clears speed and prevents timer resurrection");
            check(Math.Abs(KingSashimi.EffectiveCritical(.02f,player)-.02f)<1e-7,"food removal immediately removes critical addition");
        }
        // Call the real narrow gift postfix with all native taste constants; full NPC
        // friendship/acceptance remains a separate NOT TESTED IN REAL GAME scenario.
        var taste=typeof(KingSashimi).GetMethod("Taste",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(int original in new[]{NPC.gift_taste_love,NPC.gift_taste_like,NPC.gift_taste_neutral,NPC.gift_taste_dislike,NPC.gift_taste_hate})
        {
            object?[] args={ItemRegistry.Create(KingSashimi.QualifiedId),original};taste.Invoke(null,args);
            check((int)args[1]! ==(original==NPC.gift_taste_love?NPC.gift_taste_love:NPC.gift_taste_like),"production gift fallback handles native taste constant "+original);
        }
        object?[] other={ItemRegistry.Create(LobsterKingRules.Fish),NPC.gift_taste_hate};taste.Invoke(null,other);check((int)other[1]! ==NPC.gift_taste_hate,"unrelated gifts unchanged");
        check(Harmony.GetPatchInfo(DrawPatchTargets.Require(typeof(MeleeWeapon),"setFarmerAnimating",typeof(void),typeof(Farmer)))!.Transpilers.Any(p=>p.PatchMethod.DeclaringType==typeof(KingSashimi)),"audited native melee read transpiler installed and validated");
        player.mailbox.Remove(KingMail.Id);player.cookingRecipes.Remove(LobsterKingRules.SashimiId);CraftingRecipe.cookingRecipes.Remove(LobsterKingRules.SashimiId);Game1.objectData.Remove(LobsterKingRules.SashimiId);
        return new{values,mail="actual KingMail and native HandleItemCommand with production recipe",gift="actual postfix all taste constants; no full friendship test",combat="actual native arithmetic after production transpilers; graphics/monster tail isolated; animated attacks NOT TESTED",critical="native points +2 = additive .04 before existing multiplier",live_game=false};
    }
    private static float CriticalObserved;
    private static void ObserveCritical(float value)=>CriticalObserved=value;
    private static void NativeCritical(Farmer player,Action<bool,string> check)
    {
        var h=new Harmony("BSV.King.CriticalCalculationFixture");
        var method=DrawPatchTargets.Require(typeof(MeleeWeapon),"DoDamage",typeof(void),typeof(GameLocation),typeof(int),typeof(int),typeof(int),typeof(int),typeof(Farmer));
        h.Patch(method,transpiler:new HarmonyMethod(typeof(KingSashimiChecks),nameof(CriticalCalculationOnly)){priority=Priority.Last});
        try
        {
            var weapon=(MeleeWeapon)FormatterServices.GetUninitializedObject(typeof(MeleeWeapon));
            typeof(MeleeWeapon).GetField("critChance")!.SetValue(weapon,new Netcode.NetFloat());
            typeof(MeleeWeapon).GetField("type")!.SetValue(weapon,new Netcode.NetInt());
            foreach(int type in new[]{0,1,2,3})foreach(float chance in new[]{.02f,.04f,.1f})
            {
                weapon.type.Value=type;weapon.critChance.Value=chance;
                weapon.DoDamage(null!,0,0,0,0,player);
                float expected=((type==1?(chance+.005f)*1.12f:chance)+.04f)*(1+player.buffs.CriticalChanceMultiplier);
                check(Math.Abs(CriticalObserved-expected)<1e-6&&weapon.critChance.Value==chance,"native DoDamage base/dagger/food/multiplier arithmetic "+type+"/"+chance);
            }
        }
        finally{h.UnpatchAll(h.Id);}
    }
    private static IEnumerable<CodeInstruction> CriticalCalculationOnly(IEnumerable<CodeInstruction> source)
    {
        var code=source.ToList();int field=code.FindIndex(c=>c.LoadsField(AccessTools.Field(typeof(MeleeWeapon),"critChance")));
        int add=code.FindIndex(c=>c.Calls(AccessTools.Method(typeof(KingSashimi),nameof(KingSashimi.EffectiveCritical))));
        int mult=code.FindIndex(c=>c.Calls(AccessTools.PropertyGetter(typeof(StardewValley.Buffs.BuffManager),nameof(StardewValley.Buffs.BuffManager.CriticalChanceMultiplier))));
        int branch=code.FindIndex(field,c=>c.opcode==OpCodes.Bne_Un_S||c.opcode==OpCodes.Bne_Un);
        if(field<1||branch<field||add<0||mult<add)throw new Exception("critical fixture requires production transpiler and native arithmetic");
        var target=(Label)code[branch].operand;int end=code.FindIndex(branch+1,c=>c.labels.Contains(target));
        foreach(var c in code.Skip(field-1).Take(end-field+1))yield return c;
        var read=code[add-2];read.labels.Add(target);yield return read;
        foreach(var c in code.Skip(add-1).Take(mult-add+4))yield return c;
        yield return new(OpCodes.Call,AccessTools.Method(typeof(KingSashimiChecks),nameof(ObserveCritical)));
        yield return new(OpCodes.Ret);
    }
    private static void NativeSpeed(Farmer player,Action<bool,string> check)
    {
        // Keep the actual patched native arithmetic; strip only pre-animation setup and
        // the graphics/audio/damage tail. This is a calculation test, not an animation test.
        var h=new Harmony("BSV.King.SpeedCalculationFixture");
        var method=DrawPatchTargets.Require(typeof(MeleeWeapon),"setFarmerAnimating",typeof(void),typeof(Farmer));
        h.Patch(method,transpiler:new HarmonyMethod(typeof(KingSashimiChecks),nameof(CalculationOnly)){priority=Priority.Last});
        try
        {
            var weapon=(MeleeWeapon)FormatterServices.GetUninitializedObject(typeof(MeleeWeapon));
            typeof(MeleeWeapon).GetField("speed")!.SetValue(weapon,new Netcode.NetInt());
            foreach(int speed in new[]{-2,0,3,6})
            {
                weapon.speed.Value=speed;weapon.setFarmerAnimating(player);
                float expected=(400-40*(speed+2)-40*player.addedSpeed)*(1-player.buffs.WeaponSpeedMultiplier);
                check(Math.Abs((float)AccessTools.Field(typeof(MeleeWeapon),"swipeSpeed").GetValue(weapon)!-expected)<.001&&weapon.speed.Value==speed,"actual native melee calculation adds effective speed two once without writing weapon: "+speed);
            }
        }
        finally{h.UnpatchAll(h.Id);}
    }
    private static IEnumerable<CodeInstruction> CalculationOnly(IEnumerable<CodeInstruction> source)
    {
        var code=source.ToList();var stores=Enumerable.Range(0,code.Count).Where(i=>code[i].StoresField(AccessTools.Field(typeof(MeleeWeapon),"swipeSpeed"))).ToArray();
        int start=code.FindIndex(c=>c.opcode==OpCodes.Ldc_I4&&Equals(c.operand,400))-1;
        if(start<0||stores.Length<2||!code.Skip(start).Take(stores[1]-start).Any(c=>c.Calls(AccessTools.Method(typeof(KingSashimi),nameof(KingSashimi.EffectiveSpeed)))))throw new Exception("native speed fixture requires installed production hook and audited arithmetic");
        foreach(var c in code.Skip(start).Take(stores[1]-start+1))yield return c;
        yield return new(OpCodes.Ret);
    }
}
