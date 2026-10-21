using StardewValley;
using StardewModdingAPI;

namespace BetterStardewValley.MultiplayerRescue;

internal sealed partial class RescueService
{
    private bool CanGiveUp => World && ActiveEvent.Length>0 && !Released && AppliedEvent!=ActiveEvent
        && !(Game1.player.modData.TryGetValue(ReceiptKey,out var done)&&done==ActiveEvent);

    private void GiveUpLocal()
    {
        if(!CanGiveUp)return;
        string id=ActiveEvent;
        // Local irreversible intent MUST precede Send (host Receive can re-enter Apply).
        ReleaseToNative("give-up");
        NextSafetySend=Now+1;
        Send(new RescueCommand{Kind="give-up",EventId=id});
    }

    private void RecoveryTick(bool send=true)
    {
        if(!World || ActiveEvent.Length==0)return;
        bool received=Game1.player.modData.TryGetValue(ReceiptKey,out var receipt)&&receipt==ActiveEvent;
        if(!received && !Released)
        {
            if(Game1.timeOfDay>=2600 || Game1.newDay)ReleaseToNative("native-night",true);
            else if(Now>=LocalDeadline)ReleaseToNative("local-safety-timeout");
            else if(!Healthy)ReleaseToNative("module-fault");
            else if(Game1.killScreen || Game1.player.passedOut || Game1.player.FarmerSprite.isPassingOut())
                ReleaseToNative("native-already-started",true);
        }
        ObserveNativeRecovery();
        if(!send || ReceiptConfirmed || !Context.IsMultiplayer || Now<NextSafetySend)return;
        NextSafetySend=Now+1;
        if(Epoch.Length==0)
        { Send(new RescueCommand{Kind="pulse",Busy=Busy,DamageSerial=DamageSerial});return; }
        if(Game1.player.modData.TryGetValue(ReceiptKey,out receipt)&&receipt==ActiveEvent)
        {
            Game1.player.modData.TryGetValue(ReceiptOutcomeKey,out string outcome);
            bool revived=outcome?.StartsWith("revived:",StringComparison.Ordinal)==true;
            Send(new RescueCommand{Kind=revived?"revive-applied":"ack",EventId=ActiveEvent,
                AttemptId=revived?outcome![8..]:""});
        }
        else if(Released)Send(new RescueCommand{Kind="give-up",EventId=ActiveEvent});
    }
}
