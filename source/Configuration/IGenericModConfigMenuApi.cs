using StardewModdingAPI;
namespace BetterStardewValley.Configuration;

// Public API contract only. Source: spacechase0/StardewValleyMods,
// framework/GenericModConfigMenu/IGenericModConfigMenuApi.cs (2026-10-13).
// Upstream explicitly documents copying the interface and removing unused members.
// No GMCM implementation, UI, assets or binaries are included. See CODE_PROVENANCE.md.
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void Unregister(IManifest mod);
    void AddPage(IManifest mod,string pageId,Func<string>? pageTitle=null);
    void AddPageLink(IManifest mod,string pageId,Func<string> text,Func<string>? tooltip=null);
    void AddTextOption(IManifest mod,Func<string> getValue,Action<string> setValue,Func<string> name,Func<string>? tooltip=null,
        string[]? allowedValues=null,Func<string,string>? formatAllowedValue=null,string? fieldId=null);
    void AddParagraph(IManifest mod, Func<string> text);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int,string>? formatValue = null, string? fieldId = null);
}
