using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BetterStardewValley.MultiplayerRescue;

internal sealed partial class RescueService
{
    private Rectangle GiveUpBounds;
    private string CancelledAttempt = "";
    private DisplayText.EmergencyLayout GiveUpTextLayout()
    {
        string text=Tr("give-up");
        try { return DisplayText.Emergency(text,Game1.uiViewport.Width,Game1.uiViewport.Height,
            Game1.smallFont.LineSpacing,s=>Game1.smallFont.MeasureString(s).X); }
        catch { return DisplayText.Emergency("Give Up",Game1.uiViewport.Width,Game1.uiViewport.Height,22,s=>s.Length*12); }
    }
    private Rectangle GiveUpLayout(){var r=GiveUpTextLayout();return new(r.X,r.Y,r.Width,r.Height);}
    [EventPriority(EventPriority.High)]
    private void Input(object? sender, ButtonPressedEventArgs e)
    {
        if (!World) return;
        Safe(() =>
        {
            if (CanGiveUp)
            {
                if (e.Button is SButton.K or SButton.ControllerBack || e.Button == SButton.MouseLeft
                    && GiveUpLayout().Contains(Game1.getMouseX(true),Game1.getMouseY(true))) GiveUpLocal();
                Helper.Input.Suppress(e.Button); return;
            }
            if(!Healthy)return;
            var attempt = View.Values.FirstOrDefault(r => r.Outcome == RescueOutcome.Downed && r.Rescuer == LocalId);
            if (attempt != null)
            {
                CancelledAttempt = attempt.AttemptId;
                Interrupt("input"); return; // movement input immediately returns normal control
            }
            if (!Multiplayer || Busy || Game1.player.health <= 0 || !e.Button.IsActionButton() && e.Button != SButton.MouseRight) return;
            Vector2 world = e.Cursor.AbsolutePixels;
            var target = View.Values.Where(r => r.Outcome == RescueOutcome.Downed && r.Target != LocalId && r.Location == Game1.currentLocation.NameOrUniqueName)
                .Where(r => Vector2.Distance(new Vector2((float)r.X,(float)r.Y),Game1.player.StandingPixel.ToVector2()) <= RescueModel.Reach)
                .Where(r => e.Button != SButton.MouseRight || new Rectangle((int)r.X-40,(int)r.Y-96,80,112).Contains(world.ToPoint()))
                .OrderBy(r => Vector2.DistanceSquared(new Vector2((float)r.X,(float)r.Y),Game1.player.StandingPixel.ToVector2())).ThenBy(r => r.Target).FirstOrDefault();
            if (target == null) return;
            Helper.Input.Suppress(e.Button);
            PendingRescueUntil = Now + 2;
            Send(new RescueCommand { Kind = "begin", Target = target.Target, EventId = target.EventId, AttemptId = Guid.NewGuid().ToString("N") });
            AfkFishing.AfkFishingService.YieldForRescue();
        });
    }
    private static void Fill(SpriteBatch b, Rectangle r, Color c) { if(r.Width>0&&r.Height>0) b.Draw(Game1.staminaRect,r,c); }
    private static void Panel(SpriteBatch b, Rectangle r)
    {
        Fill(b,r,new Color(55,29,20));
        Fill(b,new(r.X+2,r.Y+2,r.Width-4,r.Height-4),new Color(151,91,43));
        Fill(b,new(r.X+4,r.Y+4,r.Width-8,2),new Color(229,175,94));
        Fill(b,new(r.X+4,r.Y+7,r.Width-8,r.Height-11),new Color(71,43,27));
    }
    private void DrawWorld(SpriteBatch b)
    {
        if (!Multiplayer || Game1.eventUp || Game1.currentMinigame != null) return;
        foreach (var r in View.Values)
        {
            if(r.Target==LocalId&&(Released||TerminatedEvent==r.EventId||AppliedEvent==r.EventId))continue;
            bool completedFlash = r.Outcome == RescueOutcome.Revived && HostNow-r.ResolvedAt <= .2;
            if (r.Outcome != RescueOutcome.Downed && !completedFlash || r.Location != Game1.currentLocation.NameOrUniqueName) continue;
            var target = Game1.GetPlayer(r.Target, onlyOnline: true); if (target?.currentLocation != Game1.currentLocation) continue;
            // World viewport coordinates: zoom is already in SpriteBatch's world transform.
            // Do not apply UI scale a second time. Keep the complete frame on-screen.
            Vector2 anchor = Game1.GlobalToLocal(Game1.viewport, target.StandingPixel.ToVector2());
            int width = Math.Min(208,Math.Max(96,Game1.viewport.Width-16));
            int x = Math.Clamp((int)anchor.X-width/2,8,Math.Max(8,Game1.viewport.Width-width-8));
            int y = Math.Clamp((int)anchor.Y-138,8,Math.Max(8,Game1.viewport.Height-64));
            bool cancelledHere = r.Rescuer == LocalId && r.AttemptId == CancelledAttempt;
            string label = completedFlash ? Tr("rescued") : r.Rescuer != 0 && !cancelledHere ? Tr("reviving") : Tr("waiting",new{seconds=(int)Math.Ceiling(Math.Max(0,r.Deadline-HostNow))});
            Vector2 size = Game1.smallFont.MeasureString(label);
            float scale = Math.Min(.8f,(width-16)/Math.Max(1,size.X));
            var title = new Rectangle(x,y,width,30); Panel(b,title);
            b.DrawString(Game1.smallFont,label,new Vector2(x+(width-size.X*scale)/2,y+5),new Color(255,240,197),0,Vector2.Zero,scale,SpriteEffects.None,1);
            var frame = new Rectangle(x,y+28,width,28); Panel(b,frame);
            var inner = new Rectangle(x+28,y+36,width-37,12);
            Fill(b,inner,new Color(33,55,38));
            // Only the interior's right edge changes. Static frame/cross never scale.
            double p = completedFlash ? 1 : r.Rescuer == 0 || cancelledHere ? 0 : RescueModel.Progress(HostNow,r.StartedAt);
            int fill = (int)Math.Floor(inner.Width*p);
            TraceFrame(r,p,new Rectangle(inner.X,inner.Y,fill,inner.Height));
            Fill(b,new(inner.X,inner.Y,fill,inner.Height),new Color(77,169,65));
            Fill(b,new(inner.X,inner.Y,fill,3),new Color(164,227,102));
            Fill(b,new(x+7,y+33,18,18),new Color(54,109,50));
            Fill(b,new(x+14,y+35,4,14),new Color(246,250,221));
            Fill(b,new(x+9,y+40,14,4),new Color(246,250,221));
        }
    }
    private void DrawHud(SpriteBatch b)
    {
        GiveUpBounds = Rectangle.Empty;
        if (!World) return;
        if(Released && Game1.player.modData.ContainsKey(LocalDebtKey))
        {
            // No dark overlay, input bounds or repeated action while native owns recovery.
            var notice=DisplayText.HudNotice(Tr("native-handoff"),Game1.uiViewport.Width,Game1.uiViewport.Height,
                Math.Max(Game1.smallFont.LineSpacing,Game1.smallFont.MeasureString("Ágj").Y),s=>Game1.smallFont.MeasureString(s).X);
            if(notice.Text.Length>0)b.DrawString(Game1.smallFont,notice.Text,new Vector2(notice.X,notice.Y),Color.Wheat);
            return;
        }
        if (!CanGiveUp) return;
        double fade = Pending.Length > 0 ? Now-PendingAt : View.TryGetValue(LocalId,out var r) ? HostNow-r.DownAt : 1;
        Fill(b,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),Color.Black*(.28f*(float)Math.Clamp(fade/.4,0,1)));
        var button=GiveUpTextLayout();
        int width = Math.Min(Game1.uiViewport.Width-24,Math.Max(540,button.Width+16));
        int x = (Game1.uiViewport.Width-width)/2, y = Math.Max(0,button.Y-49);
        Panel(b,new Rectangle(x,y,width,button.Height+60));
        string text = Pending.Length > 0 ? Tr("connecting") : Tr("downed");
        float scale = Math.Min(1f,(width-24)/Math.Max(1,Game1.smallFont.MeasureString(text).X));
        b.DrawString(Game1.smallFont,text,new Vector2(x+12,y+10),new Color(255,240,197),0,Vector2.Zero,scale,SpriteEffects.None,1);
        GiveUpBounds = GiveUpLayout(); Panel(b,GiveUpBounds);
        var size = Game1.smallFont.MeasureString(button.Text);
        b.DrawString(Game1.smallFont,button.Text,new Vector2(GiveUpBounds.Center.X-size.X/2,GiveUpBounds.Y+6),Color.White);
    }
}
