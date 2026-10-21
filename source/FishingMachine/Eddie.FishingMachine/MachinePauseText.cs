namespace Eddie.FishingMachine;

// Presentation only. Historical exception messages are persisted by the production
// transaction; never translate, rewrite or migrate that saved record. Unknown
// reasons get an actionable localized summary while the existing log keeps details.
internal static class MachinePauseText
{
    internal static string KeyFor(string? reason) => reason?.Trim() switch
    {
        "Machine owner unavailable" or "Machine owner unavailable." => "machine.pause.owner",
        "Paid machine moved; historical geometry does not match." => "machine.pause.moved",
        "Seasonal quota busy; opportunity retained." or
        "Dated legendary quota changed; opportunity retained." or
        "Legendary quota changed before production." => "machine.pause.quota",
        "Waiting for the owner's in-flight manual catch before settling legendary quota." => "machine.pause.manual-catch",
        "No legal non-trash output; paid opportunity retained." or "No supported trap ecology" => "machine.pause.ecology",
        "Machine water geometry is no longer valid" => "machine.pause.geometry",
        "No recorded environment for paid cycle." or
        "Missing historical environment; paid opportunity retained (no guessed weather)." or
        "Past-day quota observation was not sealed; paid opportunity retained." or
        "World clock moved backwards; history retained." or
        "Observed day is already sealed; history retained." => "machine.pause.history",
        "Missing timeline journal." or "Unsupported timeline journal; preserved." or
        "Invalid legendary reservations." or "Invalid dated legendary ledger." => "machine.pause.ledger",
        _ => "machine.pause.unknown"
    };
}
