Checks the compiled mod's direct UI Toolkit method references against MENACE's generated IL2CPP assemblies. Unity Editor previews cannot detect game-stripped methods such as `MeshWriteData.SetNextIndex`, whose generated wrapper throws and leaves allocated triangle indices unwritten.

`mise test` compiles the mod and runs both the Procurement behaviour tests and this compatibility check. It uses the game-assembly path configured for `code/WOMENACE.Code.csproj`.

To run this check separately after compiling, pass the installed game's `MelonLoader/Il2CppAssemblies/UnityEngine.UIElementsModule.dll`:

```sh
dotnet run --project tests/ShopRendering -- compiled/code/WOMENACE.Code.dll /path/to/Menace/MelonLoader/Il2CppAssemblies/UnityEngine.UIElementsModule.dll
```

The check compares decoded method signatures, including parameter types, so an unused stripped overload does not reject a supported call. Generated wrappers may have a native binding or restored managed code, as several `Painter2D` methods do. The check reports missing types, missing methods and throwing stripped placeholders among the mod's direct references, continuing through the remaining calls. It does not validate every transitive call made inside Unity. Visual rendering and interaction checks cover the layout, colours and animation behaviour separately.

The scan excludes fields and references on constructed generic types. Assembly metadata cannot prove that `RegisterCallback<ChangeEvent<int>>` has a usable generic instantiation in the running IL2CPP game. That event-delivery path still requires in-game verification.
