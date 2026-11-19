using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Game.Strategy;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// Dev verbs for getting into a tactical mission over the bridge, which otherwise
// needs a human clicking through the campaign map. MENACE only ever saves at the
// strategy layer, so no save can drop you straight into combat.
//
// Both screens expose a public entry point, so this drives the game's own path
// rather than synthesising UI clicks: MissionPrepUIScreen.TryOpen skips the map
// POI and Plan Mission steps, and StartMission is what the launch button calls.
//
//   Sortie.Where         -> where we are (scene, active screen, prep readiness, in-mission)
//   Sortie.EndOperation  -> end the current operation, cancelled, without saving
//   Sortie.Missions      -> every available operation's missions, by index
//   Sortie.OpenMission   -> open mission prep for one mission of one available operation
//   Sortie.Open          -> open mission prep for the current operation's mission
//   Sortie.Launch        -> launch the prepped mission, once its preview has landed
//   Sortie.Skills        -> an actor's skills as the engine judges them, with lifetime and cooldown counters
//   Sortie.EndTurn       -> end the active unit's turn and hand over to the next actor
//   Sortie.Use           -> use one of the active actor's skills on a tile through its own Use
//   Sortie.Aim           -> raise or lower the active actor's weapon for one of its skills
//   Sortie.MapTexture    -> write the prep screen's map picture to a PNG
//   Sortie.PreviewProbe  -> what the prep screen's generated preview holds
//
// Named Sortie, not Mission: the verb runner matches class short names
// case-insensitively and the SDK's own Mission class would shadow it.
[DevVerb]
public static class Sortie
{
    public static object Where()
    {
        var screen = ActiveScreen();
        var prep = Prep(screen);
        return new
        {
            inMission = TacticalManager.IsMissionRunning(),
            activeScreen = screen != null ? screen.GetIl2CppType().Name : "none",
            missionPrepOpen = prep != null,
            // The prep screen generates its preview asynchronously and only auto-deploys
            // the squad once it lands. Launching before then starts an empty mission with
            // no map and no actors, so wait for this before calling Launch.
            previewReady = prep != null && prep.GetMissionPreview() != null,
            loadoutValid = prep != null && prep.m_IsLoadoutValid,
        };
    }

    // End the current operation, cancelled, as abandoning it from mission select would. Nothing is
    // saved: a later load brings it back.
    [MutatingVerb]
    public static object EndOperation()
    {
        var operation = Operations.Current();
        if (operation == null)
            return new { error = "no current operation" };
        Operations.Finish(operation, cancel: true);
        return new { ended = Operations.Name(operation) };
    }

    // Every available operation's missions, by index, for OpenMission.
    public static object Missions()
    {
        var operations = Operations.Available();
        var lines = new List<string>();
        for (var o = 0; o < operations.Count; o++)
        {
            var missions = Operations.Missions(operations[o]);
            for (var m = 0; m < missions.Count; m++)
                lines.Add($"{o}/{m} {Operations.Name(operations[o])}: {missions[m]?.GetTemplate()?.name}");
        }
        return lines;
    }

    // Open the prep screen for one mission of one available operation, to look at its map.
    [MutatingVerb]
    public static object OpenMission(int operation, int mission)
    {
        var operations = Operations.Available();
        if (operation < 0 || operation >= operations.Count)
            return new { error = $"{operations.Count} operations available" };
        var missions = Operations.Missions(operations[operation]);
        if (mission < 0 || mission >= missions.Count || missions[mission] == null)
            return new { error = $"{missions.Count} missions in that operation" };
        return new { opened = MissionPrepUIScreen.TryOpen(missions[mission]) };
    }

    // Open the prep screen for the operation's current mission.
    [MutatingVerb]
    public static object Open()
    {
        var operation = Operations.Current();
        if (operation == null)
            return new { error = "no current operation" };
        var mission = Operations.CurrentMission(operation);
        if (mission == null)
            return new { error = "operation has no current mission" };

        var opened = MissionPrepUIScreen.TryOpen(mission);
        return new
        {
            opened,
            operation = Operations.Name(operation),
            maxSupplies = mission.GetMaxSupplies().GetAmount(),
            next = "call Sortie.Launch once the prep screen has settled",
        };
    }

    // Launch whatever the prep screen currently has deployed. The two resource
    // arguments are what the launch button passes: the mission's supply ceiling,
    // and the cost of the loadout as the screen itself totals it.
    [MutatingVerb]
    public static object Launch()
    {
        var screen = Prep(ActiveScreen());
        if (screen == null)
            return new { error = "mission prep screen is not the active screen, call Sortie.Open first" };

        var mission = screen.GetMission();
        if (mission == null)
            return new { error = "prep screen has no mission" };
        // Launching before the preview lands starts a mission with no map and no
        // actors, which no verb can recover from. The guard belongs here, on the
        // mutating call, not only on the diagnostic that reports the flags.
        if (screen.GetMissionPreview() == null || !screen.m_IsLoadoutValid)
            return new
            {
                error = "prep screen is not ready, poll Sortie.Where until previewReady and loadoutValid are both true",
                previewReady = screen.GetMissionPreview() != null,
                loadoutValid = screen.m_IsLoadoutValid,
            };

        var maxSupplies = mission.GetMaxSupplies();
        var deployCosts = screen.UpdateSupplies(maxSupplies);
        // The mission's own ceiling reads 0 here, and launching a loadout that costs more
        // than the ceiling deploys nothing: the scene loads with no map and no actors.
        // Raise the ceiling to what the loadout actually costs so the launch is accepted.
        if (maxSupplies.GetAmount() < deployCosts.GetAmount())
            maxSupplies = new Il2CppMenace.Strategy.OperationResources(deployCosts.GetAmount());
        screen.StartMission(deployCosts, maxSupplies);
        return new
        {
            ok = true,
            deployCosts = deployCosts.GetAmount(),
            maxSupplies = maxSupplies.GetAmount(),
            next = "poll Sortie.Where until inMission is true",
        };
    }

    // An actor's skill list as the engine judges it (the active actor unless one is given),
    // for checking a once-per-mission gate or an effect's remaining life without reading the
    // skill bar: whether each skill is usable right now, which status effects (a spent marker
    // among them) the actor carries, and the counters the timed handlers keep, a lifetime's
    // turns left and a cooldown's rounds left.
    public static object Skills(Actor actor = null)
    {
        actor ??= TacticalManager.Get()?.GetActiveActor();
        var skills = actor?.GetSkills()?.GetAllSkills();
        if (skills == null)
            return new { error = "no active actor" };
        var rows = new List<string>();
        for (var i = 0; i < skills.Count; i++)
        {
            var skill = skills[i];
            if (skill == null)
                continue;
            var active = skill.TryCast<Il2CppMenace.Tactical.Skills.Skill>();
            var handlers = active?.GetSkillEventHandlers();
            var kinds = new List<string>();
            for (var h = 0; handlers != null && h < handlers.Length; h++)
                kinds.Add(Describe(handlers[h]));
            var stacks = active != null && active.StackCount > 0 ? $" stacks={active.StackCount}" : "";
            rows.Add(active != null
                ? $"{skill.GetID()} usable={active.IsUsable()} enabled={skill.IsEnabled()} hidden={skill.IsHidden()} uses={active.GetUses()}/{active.GetMaxUses()}{stacks} handlers=[{string.Join(",", kinds)}]"
                : $"{skill.GetID()} enabled={skill.IsEnabled()} hidden={skill.IsHidden()}");
        }
        // Joined into one string: the verb runner's JSON layer renders arrays as their type name.
        return new { actor = actor.GetTemplate()?.GetID(), count = rows.Count, skills = string.Join("\n", rows) };
    }

    // End the active unit's turn, what the End Turn button does. Turns alternate unit by unit,
    // so this hands over to the next actor (often one enemy), not to the next round.
    [MutatingVerb]
    public static object EndTurn()
    {
        var state = Il2CppMenace.States.TacticalState.Get();
        if (state == null || !TacticalManager.IsMissionRunning())
            return new { error = "not in a mission" };
        state.EndTurn();
        return new { ok = true, round = TacticalManager.Get()?.GetRound() ?? -1 };
    }

    // Use one of the active actor's skills on a tile, through the skill's own Use so every
    // handler fires as it would from the skill bar. The tile defaults to the actor's own for
    // self-buffs, and an attack takes the target's tile.
    [MutatingVerb]
    public static object Use(string skillId, Tile target = null)
    {
        var actor = TacticalManager.Get()?.GetActiveActor();
        if (actor?.GetSkills()?.GetAllSkills() == null)
            return new { error = "no active actor" };
        var skill = FindSkill(actor, skillId);
        if (skill == null)
            return new { error = $"active actor has no skill '{skillId}'" };
        var tile = target ?? actor.GetTile();
        var usable = skill.IsUsable();
        var used = skill.Use(tile, Il2CppMenace.Tactical.Skills.UsageParameter.Default);
        return new { actor = actor.GetTemplate()?.GetID(), skill = skillId, wasUsable = usable, used, tile = tile == null ? "none" : $"{tile.GetX()},{tile.GetZ()}" };
    }

    // Raise (or lower) the active actor's weapon for one of its skills without firing, for
    // inspecting the aiming pose and the support-hand IK on a held weapon.
    [MutatingVerb]
    public static object Aim(string skillId, bool on = true)
    {
        var actor = TacticalManager.Get()?.GetActiveActor();
        if (actor?.GetSkills()?.GetAllSkills() == null)
            return new { error = "no active actor" };
        var skill = FindSkill(actor, skillId);
        if (skill == null)
            return new { error = $"active actor has no skill '{skillId}'" };
        actor.SetAiming(on, skill);
        return new { actor = actor.GetTemplate()?.GetID(), skill = skillId, aiming = on };
    }

    // The actor's active skill with this id, or null.
    private static Il2CppMenace.Tactical.Skills.Skill FindSkill(Actor actor, string skillId)
    {
        var skills = actor.GetSkills()?.GetAllSkills();
        for (var i = 0; skills != null && i < skills.Count; i++)
            if (skills[i]?.TryCast<Il2CppMenace.Tactical.Skills.Skill>() is { } skill && skill.GetID() == skillId)
                return skill;
        return null;
    }

    private static string Describe(Il2CppMenace.Tactical.Skills.SkillEventHandler handler)
    {
        if (handler == null)
            return "null";
        var name = handler.GetIl2CppType().Name;
        var lifetime = handler.TryCast<Il2CppMenace.Tactical.Skills.Effects.LifetimeLimitHandler>();
        if (lifetime != null)
            return $"{name}:{lifetime.m_TurnsLeft}";
        var cooldown = handler.TryCast<Il2CppMenace.Tactical.Skills.Effects.CooldownEffectHandler>();
        if (cooldown != null)
            return $"{name}:{cooldown.m_Cooldown}";
        return name;
    }

    // Writes the map picture vanilla's prep screen draws to a PNG at a Windows path.
    public static object MapTexture(string path)
    {
        var source = Prep(ActiveScreen())?.m_MapTexture;
        if (source == null)
            return new { error = "no map texture" };
        var rt = UnityEngine.RenderTexture.GetTemporary(source.width, source.height, 0);
        UnityEngine.Graphics.Blit(source, rt);
        var active = UnityEngine.RenderTexture.active;
        UnityEngine.RenderTexture.active = rt;
        var copy = new UnityEngine.Texture2D(source.width, source.height, UnityEngine.TextureFormat.RGBA32, false);
        copy.ReadPixels(new UnityEngine.Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();
        UnityEngine.RenderTexture.active = active;
        UnityEngine.RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, (byte[])UnityEngine.ImageConversion.EncodeToPNG(copy));
        UnityEngine.Object.Destroy(copy);
        return new { path, source.width, source.height };
    }

    // What the prep screen's generated preview holds, and how long one pass over its tiles takes.
    // With a path, writes the grid as text: one line per row, each tile "elevation:entity type:road".
    public static object PreviewProbe(string dumpPath = "")
    {
        var screen = Prep(ActiveScreen());
        var mission = screen?.GetMission();
        var preview = mission?.GetPreview();
        if (preview == null)
            return new { error = "no preview: open mission prep and wait for previewReady" };

        int sizeX = preview.SizeX, sizeZ = preview.SizeZ;
        var tiles = preview.Tiles;
        var kinds = new Dictionary<string, int>();
        var templates = new Dictionary<string, int>();
        float low = float.MaxValue, high = float.MinValue;
        var rows = new System.Text.StringBuilder();
        var roads = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                var tile = tiles.GetTile(x, z);
                var elevation = tile.GetElevation();
                var kind = tile.GetEntityType().ToString();
                var road = preview.HasRoad(x, z);
                low = Math.Min(low, elevation);
                high = Math.Max(high, elevation);
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                if (road)
                    roads++;
                var template = tile.m_EntityTemplate;
                if (template != null)
                    templates[template.name] = templates.GetValueOrDefault(template.name) + 1;
                if (dumpPath.Length > 0)
                    rows.Append(elevation.ToString("0.##")).Append(':').Append(kind).Append(':').Append(road ? 1 : 0).Append(' ');
            }
            rows.Append('\n');
        }
        watch.Stop();
        if (dumpPath.Length > 0)
            File.WriteAllText(dumpPath, rows.ToString());

        var blueprint = preview.Blueprint;
        var heights = blueprint?.GetHeightmap()?.TryCast<Il2CppSystem.Array>();
        return new
        {
            sizeX,
            sizeZ,
            readMs = watch.Elapsed.TotalMilliseconds,
            elevation = $"{low:0.##} to {high:0.##}",
            roads,
            kinds = string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")),
            templates = string.Join(", ", templates.OrderByDescending(k => k.Value).Take(25).Select(k => $"{k.Key} {k.Value}")),
            deploymentZones = string.Join(" ", preview.DeploymentZones.ToArray().Select(r => $"[{r.x},{r.y} {r.width}x{r.height}]")),
            areas = preview.Areas?.Count ?? -1,
            pois = preview.PointsOfInterest?.Count ?? -1,
            scaledZones = string.Join(" ", preview.DeploymentZones.ToArray().Select(r => MissionPreviewResult.ScaleDeploymentZone(r, screen.m_DeploymentScale, sizeX, sizeZ)).Select(r => $"[{r.x},{r.y} {r.width}x{r.height}]")),
            scaleIsIdentity = screen.m_DeploymentScale.IsIdentity(),
            validRows = ValidRows(preview, sizeX, sizeZ),
            zoneTiles = ZoneTiles(preview, sizeX, sizeZ),
            pixelsPerTile = preview.PixelsPerTile,
            blueprint = blueprint == null ? "none" : $"{blueprint.Width}x{blueprint.Height} tiles, heightmap res {blueprint.HeightMapRes}, terrain y {blueprint.ActualTerrainMinY:0.##} to {blueprint.ActualTerrainMaxY:0.##}, chunks {blueprint.Chunks?.Count ?? -1}, terrain points {blueprint.TerrainPoints?.Count ?? -1}, base colour {blueprint.m_BaseColor?.Length ?? -1}",
            heightmap = heights == null ? "none" : $"rank {heights.Rank}, {heights.Length} values",
        };
    }

    // The map's first four rows, a letter a tile: B blocked, I isolated, S a structure, x one the
    // first deployed squad may not stand on for another reason, and a dot where it may.
    private static string ZoneTiles(MissionPreviewResult preview, int sizeX, int sizeZ)
    {
        MissionPrepDeployedEntity first = null;
        var screen = UIManager.Get()?.GetActiveScreen()?.TryCast<MissionPrepUIScreen>();
        var deployed = screen?.m_Deployed;
        for (var i = 0; deployed != null && i < deployed.childCount && first == null; i++)
            first = deployed.ElementAt(i).TryCast<MissionPrepDeployedEntity>();
        var entity = first?.GetEntity();
        if (entity == null)
            return "no squad";
        var rows = new List<string>();
        for (var z = 0; z < Math.Min(4, sizeZ); z++)
        {
            var row = new System.Text.StringBuilder();
            for (var x = 0; x < sizeX; x++)
            {
                var tile = preview.Tiles.GetTile(x, z);
                row.Append(tile.IsBlocked() ? 'B' : tile.IsIsolated() ? 'I' : tile.GetEntityType() == EntityType.Structure ? 'S'
                    : preview.IsValidDeployPosition(new UnityEngine.Vector2Int(x, z), entity, -1) ? '.' : 'x');
            }
            rows.Add(row.ToString());
        }
        return string.Join(" / ", rows);
    }

    // For the first deployed squad found, which of the map's first six rows it may stand on, by
    // vanilla's own rule, counted over the row's tiles.
    private static string ValidRows(MissionPreviewResult preview, int sizeX, int sizeZ)
    {
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                var entity = MissionPrepUIScreen.GetDeployedEntityOnTile(new UnityEngine.Vector2Int(x, z));
                if (entity == null)
                    continue;
                var rows = new List<string>();
                for (var row = 0; row < Math.Min(6, sizeZ); row++)
                {
                    var valid = 0;
                    for (var column = 0; column < sizeX; column++)
                        if (preview.IsValidDeployPosition(new UnityEngine.Vector2Int(column, row), entity, 0))
                            valid++;
                    rows.Add($"row {row}: {valid}");
                }
                return $"squad at {x},{z}: " + string.Join(", ", rows);
            }
        }
        return "no deployed squad found";
    }

    private static UIScreen ActiveScreen()
    {
        try { return UIManager.Get()?.GetActiveScreen(); }
        catch { return null; }
    }

    private static MissionPrepUIScreen Prep(UIScreen screen)
    {
        try { return screen?.TryCast<MissionPrepUIScreen>(); }
        catch { return null; }
    }
}
