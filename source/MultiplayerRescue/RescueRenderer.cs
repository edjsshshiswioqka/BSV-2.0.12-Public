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
                if (!ChatKey(e.Button)) Helper.Input.Suppress(e.Button); // the downed player can still chat
                return;
            }
            if(!Healthy)return;
            var attempt = View.Values.FirstOrDefault(r => r.Outcome == RescueOutcome.Downed && r.Rescuer == LocalId);
            if (attempt != null)
            {
                // Pressing the rescue button again keeps rescuing; only other input (moving, tools) stops it.
                if (e.Button.IsActionButton() || e.Button == SButton.MouseRight) { Helper.Input.Suppress(e.Button); return; }
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
            PendingRescueUntil = Now + 2; BeginSentAt = Now; BeginTarget = target.Target;
            Send(new RescueCommand { Kind = "begin", Target = target.Target, EventId = target.EventId, AttemptId = Guid.NewGuid().ToString("N") });
            AfkFishing.AfkFishingService.YieldForRescue();
        });
    }
    private static void Fill(SpriteBatch b, Rectangle r, Color c) { if(r.Width>0&&r.Height>0) b.Draw(Game1.staminaRect,r,c); }
    private static void Panel(SpriteBatch b, Rectangle r, float a = 1f)
    {
        Fill(b,r,new Color(55,29,20)*a);
        Fill(b,new(r.X+2,r.Y+2,r.Width-4,r.Height-4),new Color(151,91,43)*a);
        Fill(b,new(r.X+4,r.Y+4,r.Width-8,2),new Color(229,175,94)*a);
        Fill(b,new(r.X+4,r.Y+7,r.Width-8,r.Height-11),new Color(71,43,27)*a);
    }
    // Per-target display state, local time only: smooth catch-up, interrupt drain, revived flash.
    private sealed class BarAnim { public string Event=""; public double Shown, LastAt=-1, DrainFrom, DrainAt=-100, RevivedAt=-100; public long Rescuer; }
    private readonly Dictionary<long,BarAnim> Bars = new();
    private long BeginTarget;
    private const double DrainSeconds=.25, FlashSeconds=.8, FadeSeconds=.4;
    private void BeginFeedback()
    {
        // The host may refuse a rescue (too far, moving, already being rescued). Say so instead of doing nothing.
        if (BeginSentAt < 0 || Now - BeginSentAt < 1.5) return;
        bool started = View.Values.Any(r => r.Target == BeginTarget && (r.Rescuer == LocalId || r.Outcome != RescueOutcome.Downed));
        BeginSentAt = -100;
        if (!started) Game1.addHUDMessage(new HUDMessage(Tr("not-started"), 3));
    }
    private void DrawWorld(SpriteBatch b)
    {
        if (!Multiplayer || Game1.eventUp || Game1.currentMinigame != null) return;
        BeginFeedback();
        foreach (var r in View.Values)
        {
            if(r.Target==LocalId&&(Released||TerminatedEvent==r.EventId||AppliedEvent==r.EventId))continue;
            if (r.Location != Game1.currentLocation.NameOrUniqueName) continue;
            if (!Bars.TryGetValue(r.Target, out var a) || a.Event != r.EventId) Bars[r.Target] = a = new BarAnim { Event = r.EventId };
            double dt = a.LastAt < 0 ? 0 : Math.Clamp(Now - a.LastAt, 0, .1); a.LastAt = Now;
            bool cancelledHere = r.Rescuer == LocalId && r.AttemptId == CancelledAttempt;
            long rescuer = r.Outcome == RescueOutcome.Downed && !cancelledHere ? r.Rescuer : 0;
            float alpha = 1f; bool gold = false, red = false; string label; double p;
            if (r.Outcome == RescueOutcome.Revived)
            {
                if (a.RevivedAt < 0) { if (HostNow - r.ResolvedAt > 3) continue; a.RevivedAt = Now; }
                double t = Now - a.RevivedAt;
                if (t > FlashSeconds + FadeSeconds) continue;
                alpha = t < FlashSeconds ? 1f : (float)(1 - (t - FlashSeconds) / FadeSeconds);
                gold = true; p = 1; label = Tr("rescued");
            }
            else if (r.Outcome == RescueOutcome.ReviveAuthorized) { p = 1; a.Shown = 1; label = Tr("reviving"); }
            else if (r.Outcome != RescueOutcome.Downed) continue;
            else if (rescuer != 0)
            {
                if (a.Rescuer != rescuer) a.Shown = 0;
                double target = RescueModel.Progress(HostNow, r.StartedAt);
                // Ease toward the authoritative value so network corrections never jump.
                a.Shown = target <= a.Shown ? Math.Max(target, a.Shown - dt * .5) : a.Shown + (target - a.Shown) * Math.Min(1, dt * 12);
                p = a.Shown; label = Tr("reviving");
            }
            else
            {
                if (a.Rescuer != 0 && a.Shown > .01) { a.DrainFrom = a.Shown; a.DrainAt = Now; }
                a.Shown = 0;
                double k = (Now - a.DrainAt) / DrainSeconds;
                if (k < 1) { p = a.DrainFrom * (1 - k); red = true; label = Tr("interrupted"); }
                else { p = 0; label = Tr("waiting",new{seconds=(int)Math.Ceiling(Math.Max(0,r.Deadline-HostNow))}); }
            }
            a.Rescuer = rescuer;
            var target2 = Game1.GetPlayer(r.Target, onlyOnline: true); if (target2?.currentLocation != Game1.currentLocation) continue;
            // World viewport coordinates: zoom is already in SpriteBatch's world transform.
            // Do not apply UI scale a second time. Keep the complete frame on-screen.
            Vector2 anchor = Game1.GlobalToLocal(Game1.viewport, target2.StandingPixel.ToVector2());
            int width = Math.Min(208,Math.Max(96,Game1.viewport.Width-16));
            int x = Math.Clamp((int)anchor.X-width/2,8,Math.Max(8,Game1.viewport.Width-width-8));
            int y = Math.Clamp((int)anchor.Y-138,8,Math.Max(8,Game1.viewport.Height-64));
            Vector2 size = Game1.smallFont.MeasureString(label);
            float scale = Math.Min(.8f,(width-16)/Math.Max(1,size.X));
            var title = new Rectangle(x,y,width,30); Panel(b,title,alpha);
            b.DrawString(Game1.smallFont,label,new Vector2(x+(width-size.X*scale)/2,y+5),new Color(255,240,197)*alpha,0,Vector2.Zero,scale,SpriteEffects.None,1);
            var frame = new Rectangle(x,y+28,width,28); Panel(b,frame,alpha);
            var inner = new Rectangle(x+28,y+36,width-37,12);
            Fill(b,inner,new Color(33,55,38)*alpha);
            Color body = gold ? new Color(232,182,58) : red ? new Color(196,84,60) : new Color(77,169,65);
            Color shine = gold ? new Color(255,236,140) : red ? new Color(240,150,120) : new Color(164,227,102);
            double exact = inner.Width * Math.Clamp(p, 0, 1);
            int fill = (int)Math.Floor(exact); float edge = (float)(exact - fill);
            TraceFrame(r,p,new Rectangle(inner.X,inner.Y,fill,inner.Height));
            Fill(b,new(inner.X,inner.Y,fill,inner.Height),body*alpha);
            Fill(b,new(inner.X,inner.Y,fill,3),shine*alpha);
            if (fill < inner.Width && edge > 0)   // partial last pixel: growth looks continuous, not stepped
            { Fill(b,new(inner.X+fill,inner.Y,1,inner.Height),body*(alpha*edge)); Fill(b,new(inner.X+fill,inner.Y,1,3),shine*(alpha*edge)); }
            if (!gold && p > 0 && p < 1 && fill > 1) Fill(b,new(inner.X+fill-1,inner.Y,1,inner.Height),shine*alpha); // bright leading edge
            Fill(b,new(x+7,y+33,18,18),new Color(54,109,50)*alpha);
            Fill(b,new(x+14,y+35,4,14),new Color(246,250,221)*alpha);
            Fill(b,new(x+9,y+40,14,4),new Color(246,250,221)*alpha);
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
