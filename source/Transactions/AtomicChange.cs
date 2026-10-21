namespace BetterStardewValley.Transactions;

// A — original synchronous transaction coordinator, shared by runtime and fault tests.
internal sealed record AtomicStep(Action Apply, Action Undo);
internal static class AtomicChange
{
    internal static void Execute(Func<bool> valid, IReadOnlyList<AtomicStep> steps)
    {
        if (!valid()) throw new InvalidOperationException("Transaction lease is no longer valid.");
        int index = -1;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (!valid()) throw new InvalidOperationException("Transaction lease changed.");
                index = i; // Undo includes the step that may have partly applied before throwing.
                steps[i].Apply();
            }
            if (!valid()) throw new InvalidOperationException("Transaction lease changed before commit.");
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            for (int i = index; i >= 0; i--)
                try { steps[i].Undo(); } catch (Exception undo) { errors.Add(undo); }
            if (errors.Count > 1) throw new AggregateException("Transaction rollback failed; automation must pause.", errors);
            throw;
        }
    }
}
