using Jiangyu.Sdk;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace WOMENACE.Code;

// Reads every loaded font asset's tables on the main thread at boot.
//
// UI Toolkit builds text meshes on job threads, and a font asset builds its lookup dictionaries the
// first time one of them asks. When several labels in the first frame share a font no one has read
// yet, two jobs build the same dictionaries at once and one throws (KeyNotFoundException from
// InitializeCharacterLookupDictionary, or ArgumentException "same key" from
// InitializeLigatureSubstitutionLookupDictionary, on the splash screen). MENACE answers any logged
// exception by opening its error window, which it does in the middle of the repaint, and from then on
// the panel draws no text at all. Reading the tables here, before the first screen opens, leaves the
// jobs nothing to build.
public sealed class FontWarmupSystem : JiangyuSystem
{
    private readonly HashSet<IntPtr> _read = new();

    public override void OnInit() => Warm();

    public override void OnSceneLoaded(int buildIndex, string sceneName) => Warm();

    private void Warm()
    {
        var read = 0;
        foreach (var font in Resources.FindObjectsOfTypeAll<FontAsset>())
        {
            if (font == null || !_read.Add(font.Pointer))
                continue;
            try
            {
                font.ReadFontAssetDefinition();
                read++;
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"font warm-up: {font.name} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (read > 0)
            Context.Log.Info($"font warm-up: read {read} font assets");
    }
}
