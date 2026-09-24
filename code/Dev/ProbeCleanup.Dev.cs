namespace WOMENACE.Code;

// Dev probes borrow persistent state across native calls that can throw after
// mutating it. Register recovery first, and never replace the probe's exception.
internal sealed class ProbeCleanup : IDisposable
{
    private readonly Action<string> _warn;
    private readonly Stack<(string Description, Action Restore)> _actions = new();

    internal ProbeCleanup(Action<string> warn) => _warn = warn;

    internal void Defer(string description, Action restore)
        => _actions.Push((description, restore));

    internal T Borrow<T>(string description, Func<bool> exists, Func<T> find,
        Func<bool> add, Action remove) where T : class
    {
        if (exists())
            return find();
        // Add can succeed before lookup fails, or throw after inserting.
        Defer(description, remove);
        return add() ? find() : null;
    }

    internal static void RemoveBorrowed(Func<int> countLive, Func<int> removeInstances)
    {
        var before = countLive();
        var attempted = removeInstances();
        var remaining = countLive();
        // RemoveInstances reports attempts, not successful removals. A garbage
        // flag counts as removed while the native dispatch defers its sweep.
        if (attempted != before || remaining != 0)
            throw new InvalidOperationException(
                $"removal found {before}, attempted {attempted}, still live {remaining}");
    }

    public void Dispose()
    {
        while (_actions.Count > 0)
        {
            var action = _actions.Pop();
            try
            {
                action.Restore();
            }
            catch (Exception error)
            {
                var message = $"Melee probe cleanup failed ({action.Description}): {error}";
                try { _warn(message); }
                catch
                {
                    // A broken logger must not block the remaining restorations.
                    try { Console.Error.WriteLine(message); }
                    catch { }
                }
            }
        }
    }
}
