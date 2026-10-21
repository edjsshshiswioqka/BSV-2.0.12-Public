namespace Eddie.FishingMachine.Core;
public readonly record struct GridTile(int X, int Y);
public sealed record IngredientSlot(int Index, string Id, int Count, bool SafeTool = true);
public sealed record IngredientSpend(int Index, int Count);
public static class PreparationRules
{
    public const string Copper = "(O)334", Iron = "(O)335", Wood = "(O)388", FiberglassRod = "(T)FiberglassRod";
    public static readonly (string Id,int Count)[] Requirements = {(Copper,10),(Iron,10),(Wood,250),("(O)709",10),(FiberglassRod,6)};
    public static GridTile[] Footprint(GridTile a) => new[] { a, new GridTile(a.X+1,a.Y), new GridTile(a.X,a.Y+1), new GridTile(a.X+1,a.Y+1) };
    public static int Craftable(int baseLevel, IEnumerable<IngredientSlot> inventory)
    {
        if (baseLevel < 5) return 0;
        var s = inventory.ToArray();
        return Requirements.Min(r=>r.Id==FiberglassRod
            ? s.Count(x=>x.Id==FiberglassRod && x.SafeTool && x.Count==1)/r.Count
            : (int)Math.Min(int.MaxValue,s.Where(x=>x.Id==r.Id).Sum(x=>(long)Math.Max(x.Count,0))/r.Count));
    }
    public static IReadOnlyList<IngredientSpend>? PlanCraft(int baseLevel, IEnumerable<IngredientSlot> inventory)
    {
        var slots=inventory.ToArray();
        if (slots.Select(x=>x.Index).Distinct().Count()!=slots.Length) throw new ArgumentException("Duplicate slots");
        if (Craftable(baseLevel,slots)==0) return null;
        var plan=new List<IngredientSpend>();
        foreach (var (id,required) in Requirements)
        {
            int left=required;
            foreach (var slot in slots.OrderByDescending(x=>x.Index))
            {
                if(slot.Id!=id || slot.Count<=0 || (id==FiberglassRod && !slot.SafeTool)) continue;
                int take=Math.Min(left,id==FiberglassRod?1:slot.Count);
                plan.Add(new(slot.Index,take)); left-=take;
                if(left==0) break;
            }
        }
        return plan;
    }
}
