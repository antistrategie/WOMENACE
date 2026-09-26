# Melee probe cleanup harness

Run `dotnet run --project tests/MeleeCookOffProbe/MeleeCookOffProbe.Tests.csproj`.

This standalone console harness links the actual dev-only `ProbeCleanup` source.
It tests borrowing, partial acquisition, scope unwinding, independent weight
restorations, removal-result checks, warning diagnostics and exception identity
without loading or mutating the game.

The fake loan models queue-aware live counts. It does not validate native skill
container dispatch, Unity defect writes or the damage pipeline. Those require a
complete mod build and disposable in-game probe run.
