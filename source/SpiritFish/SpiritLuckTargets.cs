namespace BetterStardewValley.SpiritFish;
// Method identifiers and read counts extracted from the supplied 1.6.15.24356 assembly.
// Metadata only; no third-party implementation bodies. Refuse different assembly MVIDs.
internal static class SpiritLuckTargets
{
    internal const string Mvid = "aa1f513a-94f0-4ef6-a35f-2d5b4a3721bf";
    internal readonly record struct Site(int Token,int Daily,int Level);
    internal static readonly Site[] Sites = {
        new(0x06003bf5, 0, 1), // System.Void StardewValley.Event/DefaultCommands::MineDeath(StardewValley.Event,System.String[],StardewValley.EventContext),
        new(0x060008a7, 1, 1), // System.Int32 StardewValley.Farmer::LoseItemsOnDeath(System.Random),
        new(0x06000946, 0, 1), // System.Int32 StardewValley.Farmer::GetSkillLevel(System.Int32),
        new(0x0600094f, 0, 1), // System.Void StardewValley.Farmer::takeDamage(System.Int32,System.Boolean,StardewValley.Monsters.Monster),
        new(0x06000a2b, 1, 0), // System.Double StardewValley.FarmerTeam::AverageDailyLuck(StardewValley.GameLocation),
        new(0x06000a2c, 0, 1), // System.Double StardewValley.FarmerTeam::AverageLuckLevel(StardewValley.GameLocation),
        new(0x06003da7, 1, 0), // System.Boolean StardewValley.GameStateQuery/Helpers::RandomImpl(System.Random,System.String[],System.Int32),
        new(0x060046e0, 0, 1), // System.Int32 StardewValley.GameStateQuery/DefaultResolvers/<>c::<PLAYER_LUCK_LEVEL>b__54_0(StardewValley.Farmer),
        new(0x06000eed, 0, 3), // System.Void StardewValley.GameLocation::monsterDrop(StardewValley.Monsters.Monster,System.Int32,System.Int32,StardewValley.Farmer),
        new(0x06000ef2, 0, 1), // System.Boolean StardewValley.GameLocation::damageMonster(Microsoft.Xna.Framework.Rectangle,System.Int32,System.Int32,System.Boolean,System.Single,System.Int32,System.Single,System.Single,System.Boolean,StardewValley.Farmer,System.Boolean),
        new(0x06000f4f, 1, 0), // System.Boolean StardewValley.GameLocation::CheckGarbage(System.String,Microsoft.Xna.Framework.Vector2,StardewValley.Farmer,System.Boolean,System.Boolean,System.Action`1<System.String>),
        new(0x06000fb7, 1, 1), // StardewValley.Item StardewValley.GameLocation::GetFishFromLocationData(System.String,Microsoft.Xna.Framework.Vector2,System.Int32,StardewValley.Farmer,System.Boolean,System.Boolean,StardewValley.GameLocation,StardewValley.Internal.ItemQueryContext),
        new(0x06000fb8, 1, 0), // System.Boolean StardewValley.GameLocation::CheckGenericFishRequirements(StardewValley.Item,System.Collections.Generic.Dictionary`2<System.String,System.String>,StardewValley.GameLocation,StardewValley.Farmer,StardewValley.GameData.Locations.SpawnFishData,System.Int32,System.Boolean,System.Boolean,System.Boolean,System.Boolean),
        new(0x06000fd3, 1, 1), // System.Boolean StardewValley.GameLocation::breakStone(System.String,System.Int32,System.Int32,StardewValley.Farmer,System.Random),
        new(0x060011e4, 1, 1), // System.Boolean StardewValley.NPC::tryToReceiveActiveObject(StardewValley.Farmer,System.Boolean),
        new(0x0600180f, 1, 1), // System.Void StardewValley.Tools.FishingRod::startMinigameEndFunction(StardewValley.Item),
        new(0x06001833, 1, 4), // System.Void StardewValley.Tools.FishingRod::openTreasureMenuEndFunction(System.Int32),
        new(0x06001890, 5, 3), // System.Collections.Generic.List`1<StardewValley.Item> StardewValley.Tools.Pan::getPanItems(StardewValley.GameLocation,StardewValley.Farmer),
        new(0x06001a3e, 1, 1), // System.Int32 StardewValley.TerrainFeatures.Tree::extraWoodCalculator(Microsoft.Xna.Framework.Vector2),
        new(0x06001f3f, 0, 1), // System.Void StardewValley.Objects.Ring::onMonsterSlay(StardewValley.Monsters.Monster,StardewValley.GameLocation,StardewValley.Farmer),
        new(0x06001f96, 4, 0), // System.Void StardewValley.Objects.TV::setFortuneOverlay(StardewValley.Farmer),
        new(0x06001f97, 5, 0), // System.String StardewValley.Objects.TV::getFortuneForecast(StardewValley.Farmer),
        new(0x06001fd6, 1, 1), // System.Void StardewValley.Objects.Trinkets.Trinket::TrySpawnTrinket(StardewValley.GameLocation,StardewValley.Monsters.Monster,Microsoft.Xna.Framework.Vector2,System.Double),
        new(0x0600275c, 1, 0), // System.Void StardewValley.Menus.BobberBar::update(Microsoft.Xna.Framework.GameTime),
        new(0x06002c79, 0, 1), // System.Void StardewValley.Menus.SkillsPage::.ctor(System.Int32,System.Int32,System.Int32,System.Int32),
        new(0x06002c7e, 0, 2), // System.Void StardewValley.Menus.SkillsPage::draw(Microsoft.Xna.Framework.Graphics.SpriteBatch),
        new(0x06002d71, 0, 2), // System.Void StardewValley.Menus.WheelSpinGame::update(Microsoft.Xna.Framework.GameTime),
        new(0x06003169, 0, 2), // StardewValley.Item StardewValley.Locations.MineShaft::getFish(System.Single,System.String,System.Int32,StardewValley.Farmer,System.Double,Microsoft.Xna.Framework.Vector2,System.String),
        new(0x06003199, 2, 1), // System.Void StardewValley.Locations.MineShaft::checkStoneForItems(System.String,System.Int32,System.Int32,StardewValley.Farmer),
        new(0x0600382d, 1, 0), // System.Boolean StardewValley.BellsAndWhistles.Butterfly::update(Microsoft.Xna.Framework.GameTime,StardewValley.GameLocation)
    };
}
