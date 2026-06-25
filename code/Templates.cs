using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tools;
using Jiangyu.Game;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Read game DataTemplates. Shared by every system that looks a template up (skin grants, the form
// swap, gift discovery), so the array-backed GetAll/TryCast/IsAlive idiom and its gotchas live in
// one place rather than being recopied, slightly differently, by each.
public static class Templates
{
    // Every live template of type T. GetAll is array-backed, so it is an IReadOnlyList: index it.
    // The Il2Cpp enumerator path does not advance (its boxed struct enumerator stays put), so never
    // foreach the raw collection. Dead (non-alive) templates are skipped.
    public static IEnumerable<T> All<T>() where T : DataTemplate
    {
        var all = DataTemplateLoader.GetAll<T>();
        var list = all?.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyList<T>>();
        if (list == null)
            yield break;
        for (var i = 0; i < all.Count; i++)
        {
            var t = list[i];
            if (t != null && t.IsAlive())
                yield return t;
        }
    }

    // A template by id, or null if none matches.
    public static T ById<T>(string id, Action<string> onError = null) where T : DataTemplate
    {
        try
        {
            foreach (var t in All<T>())
                if (t.GetID() == id)
                    return t;
        }
        catch (Exception ex) { onError?.Invoke($"template resolve <{typeof(T).Name}> '{id}' failed: {ex.Message}"); }
        return null;
    }
}
