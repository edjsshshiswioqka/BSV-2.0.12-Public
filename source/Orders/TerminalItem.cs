using System.Xml;
using System.Xml.Serialization;
using StardewValley;
using SObject = StardewValley.Object;

namespace BetterStardewValley.Orders;

internal static class TerminalItem
{
    private static readonly Lazy<XmlSerializer> Serializer = new(() => new(typeof(SObject)));
    internal static string Pack(SObject obj)
    {
        if (!OrderTerminal.IsTerminal(obj) || obj.GetType() != typeof(SObject) || obj.heldObject.Value != null || obj.questItem.Value)
            throw new InvalidOperationException("Terminal contains unsupported state; preserved.");
        var item = (SObject)obj.getOne(); item.Stack = 1; TerminalEntity.Strip(item);
        item.TileLocation = Microsoft.Xna.Framework.Vector2.Zero;
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Serializer.Value.Serialize(text, item);
        string result = text.ToString(); Read(result); return result;
    }
    internal static SObject Read(string raw)
    {
        if (raw.Length > 262144) throw new InvalidOperationException("Terminal escrow exceeds size limit.");
        using var reader = XmlReader.Create(new StringReader(raw), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
        if (Serializer.Value.Deserialize(reader) is not SObject obj || !OrderTerminal.IsTerminal(obj) || obj.Stack != 1
            || obj.GetType() != typeof(SObject) || obj.heldObject.Value != null || obj.questItem.Value)
            throw new InvalidOperationException("Invalid terminal escrow; preserved.");
        TerminalEntity.Strip(obj); obj.Fragility = 0; obj.CanBeGrabbed = true; return obj;
    }
}
