using System.Runtime.CompilerServices;
using System.Text.Json;
using Eddie.FishingMachine.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
namespace Eddie.FishingMachine;

internal sealed partial class ProductionService
{
    private const string InputKey=ModEntry.Tag+"InputObservations/v1";
    private sealed record InputEvent(long Sequence,long At,Dictionary<string,long> Fuel);
    private sealed class InputJournal
    {
        public long Sequence {get;set;}
        public List<InputEvent> Events {get;set;}=new();
    }
    private sealed class InputWatch
    {
        internal readonly ConditionalWeakTable<Item,object> Items=new();
        internal Action Scan=()=>{};
    }
    private readonly ConditionalWeakTable<Chest,InputWatch> InputWatches=new();
    private static InputJournal InputRead(Chest c)=>c.modData.TryGetValue(InputKey,out string raw)
        ?JsonSerializer.Deserialize<InputJournal>(raw)??throw new InvalidOperationException("Invalid input observation journal."):new();
    partial void TrackInputs(Chest c)
    {
        if(InputWatches.TryGetValue(c,out var existing)){existing.Scan();return;}
        var watch=new InputWatch();InputWatches.Add(c,watch);
        void AttachItem(Item? item)
        {
            if(item==null||watch.Items.TryGetValue(item,out _))return;watch.Items.Add(item,new());
            item.stack.fieldChangeVisibleEvent+=(_,_,_)=>{if(c.Items.Any(current=>ReferenceEquals(current,item)))Record();};
        }
        void Attach(){foreach(var item in c.Items)AttachItem(item);}
        watch.Scan=Attach;
        void Record()
        {
            if(InventoryMutation>0||!Context.IsWorldReady||!Context.IsMainPlayer||Mod.Saving||Now<360)return;
            // Evidence only: no cycle, inventory, counter or quota mutation while a player owns the chest.
            // Net-field callbacks timestamp what the host actually observes, never a client-supplied date.
            var journal=InputRead(c);var fuel=FuelInventory(c);
            var previous=journal.Events.LastOrDefault()?.Fuel;
            if(previous!=null&&previous.Count==fuel.Count&&previous.All(p=>fuel.GetValueOrDefault(p.Key)==p.Value))return;
            journal.Events.Add(new(checked(++journal.Sequence),Now,fuel));
            c.modData[InputKey]=JsonSerializer.Serialize(journal);
        }
        c.Items.OnSlotChanged+=(_,_,_,after)=>{AttachItem(after);Record();};
        c.Items.OnInventoryReplaced+=(_,_,after)=>{foreach(var item in after)AttachItem(item);Record();};
        Attach();
    }
    partial void ApplyInputEvents(Chest c,MachineContinuity history)
    {
        foreach(var e in InputRead(c).Events.Where(e=>e.Sequence>history.InputCursor).OrderBy(e=>e.Sequence))
        {history.ObserveFuel(e.At,e.Fuel);history.InputCursor=e.Sequence;}
    }
    partial void PruneInputEvents(Chest c,long cursor)
    {
        if(!c.modData.ContainsKey(InputKey))return;
        var journal=InputRead(c);if(journal.Events.RemoveAll(e=>e.Sequence<=cursor)>0)c.modData[InputKey]=JsonSerializer.Serialize(journal);
    }
}
