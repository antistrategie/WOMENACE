#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10,<3.13"
# dependencies = ["UnityPy"]
# ///
"""Build an Armed Echelon's Unity sources and sprites from the GFL2 client, end to end.

Everything a team needs from the client is named in teams/<team>/team.json: the archives its crew,
weapon, props and clips sit in, what to export from them, the art its sprites are made from, and
where its weapon is rendered from. Its animation states are in teams/<team>/states.json. This
script runs the steps in order, each one reading what the step before it wrote in the work folder:

    cut       the team's archives, decrypted, into <work>/bundles
    export    each object with its clips to FBX (asexport, on AssetStudioMod), into <work>/export
    art       the 2D art (whole team, skill icon, faction logo, items), into <work>/art
    states    the per-state takes (build_team_states.py in Blender), into <work>/states
    render    a side render of the weapon (scripts/weapon/render_weapon.py in Blender), into <work>/art
    sprites   the portraits, badges and icons (make_team_sprites.py), into assets/
    install   the state FBXs and the crew's textures into unity/Assets/Authored/<team>/default

after which the Unity bake builds the prefab (WOMENACE.Editor.BuildSupportTeam.Build -team <team>).

    uv run scripts/support_team/prepare_team.py limonad --work <dir> --bundles <AssetBundles_Windows> \\
        --assetstudio <AssetStudioMod folder> --blender <blender executable> [--steps cut,export,...]

--bundles is the client's GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows. The work folder
is scratch space outside the repository, kept between runs so a later step can be run alone.

export builds asexport against AssetStudioMod's libraries and runs it through `dotnet`, on Windows
or Linux. On Linux --assetstudio is the portable AssetStudioModCLI build, which carries the native
FBX library for Linux beside the Windows one.
"""
import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
import gfl2_client  # noqa: E402


def run(cmd):
    print("$", " ".join(str(c) for c in cmd), flush=True)
    subprocess.run([str(c) for c in cmd], check=True)


def cut(team, work, args):
    for name, archive in team["archives"].items():
        gfl2_client.cut(args.bundles, archive, work / "bundles" / f"{name}.bundle")


def export(team, work, args):
    project = HERE / "asexport"
    run(["dotnet", "build", project / "asexport.csproj", "-nologo", "-v", "q", "-c", "Release",
         f"-p:AssetStudioDir={args.assetstudio}"])
    # Run through the dotnet host rather than the apphost, whose name differs between Windows
    # (asexport.exe) and Linux (asexport).
    dll = project / "bin" / "Release" / "net10.0" / "asexport.dll"
    sets = team.get("clip_sets", {})
    for job in team["exports"]:
        jobs = []
        for obj, clips in job["objects"].items():
            names = [c for clip in clips for c in (sets[clip[1:]] if clip.startswith("@") else [clip])]
            jobs.append(f"{obj}={','.join(names)}")
        bundles = [work / "bundles" / f"{a}.bundle" for a in job["archives"]]
        run(["dotnet", dll, work / job["out"], job["scene_root"], *bundles, "--", *jobs])


def art(team, work, args):
    for name, archive in team["art"].items():
        gfl2_client.texture(args.bundles, archive, name, work / "art" / f"{name}.png")


def states(team, work, args):
    spec = HERE / "teams" / team["team"] / "states.json"
    run([args.blender, "--background", "--factory-startup", "--python", HERE / "build_team_states.py", "--", spec, work])


def render(team, work, args):
    r = team["weapon_render"]
    w, h = r["size"]
    # Studio light with cavity, and a margin of an eighth of the weapon's length each side, which
    # leaves make_team_sprites.py room to straighten a weapon that runs at an angle to its axes.
    cmd = [args.blender, "--background", "--factory-startup", "--python", REPO / "scripts" / "weapon" / "render_weapon.py",
           "--", "--fbx", work / r["fbx"], "--diffuse", work / r["diffuse"], "--lod0-only",
           "--mode", "studio", "--padding", "0.125", "--width", w, "--height", h,
           "--out", work / "art" / "weapon_render.png"]
    if r.get("hide_groups"):
        cmd += ["--hide-groups", ",".join(r["hide_groups"])]
    if r.get("fold"):
        # The fold parts the prop as the states do, so it takes their split rules with it, and a
        # folded weapon is framed by the fold rather than by an axis.
        spec = json.loads((HERE / "teams" / team["team"] / "states.json").read_text())
        fold = work / "art" / "weapon_fold.json"
        fold.write_text(json.dumps({"fold": r["fold"], "split": spec.get("split", [])}))
        cmd += ["--fold", fold]
    else:
        cmd += ["--axis", r["axis"]]
    run(cmd)


def art_png(work, name):
    return work / "art" / f"{name}.png"


def sprites(team, work, args):
    s = team["sprites"]
    cmd = ["uv", "run", HERE / "make_team_sprites.py", "--team", team["team"], "--whole", art_png(work, s["whole"]),
           "--heads", *s["heads"], "--lead", str(s["lead"]), "--front-outline", *s["front_outline"], "--logo", art_png(work, s["logo"]), "--icon", art_png(work, s["icon"]),
           "--weapon-render", work / "art" / "weapon_render.png"]
    if s.get("straighten") is False:
        cmd += ["--no-straighten"]
    if s.get("weapon_zoom"):
        cmd += ["--weapon-zoom", str(s["weapon_zoom"])]
    if s.get("items"):
        cmd += ["--item", *[f"{n}={art_png(work, a)}" for n, a in s["items"].items()]]
    run(cmd)


def install(team, work, args):
    dest = REPO / "unity" / "Assets" / "Authored" / team["team"] / "default"
    (dest / "textures").mkdir(parents=True, exist_ok=True)
    for fbx in sorted((work / "states").glob(f"{team['team']}_*.fbx")):
        shutil.copy2(fbx, dest / fbx.name)
        print(f"{fbx.name} -> {dest}")
    skip = set(team["install"].get("skip_textures", []))
    for png in sorted((work / team["install"]["textures_from"]).glob("*/*.png")):
        if png.name not in skip:
            shutil.copy2(png, dest / "textures" / png.name)
    print(f"textures -> {dest / 'textures'}")


# In the order they run. Each step reads what the one before it wrote.
STEPS = {"cut": cut, "export": export, "art": art, "states": states, "render": render, "sprites": sprites,
         "install": install}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("team", help="a folder under teams/, e.g. limonad")
    ap.add_argument("--work", type=Path, required=True, help="scratch folder for the steps' outputs")
    ap.add_argument("--bundles", help="the client's AssetBundles_Windows folder (cut, art)")
    ap.add_argument("--assetstudio", help="AssetStudioMod's folder, holding AssetStudio.dll (export)")
    ap.add_argument("--blender", default="blender", help="Blender's executable (states, render)")
    ap.add_argument("--steps", default=",".join(STEPS), help=f"comma-separated, from {', '.join(STEPS)}")
    args = ap.parse_args()

    team = json.loads((HERE / "teams" / args.team / "team.json").read_text(encoding="utf-8"))
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=True)
    steps = [s.strip() for s in args.steps.split(",") if s.strip()]
    for step in steps:
        if step not in STEPS:
            raise SystemExit(f"unknown step {step}, expected one of {', '.join(STEPS)}")
        if step in ("cut", "art") and not args.bundles:
            raise SystemExit(f"{step} needs --bundles")
        if step == "export" and not args.assetstudio:
            raise SystemExit("export needs --assetstudio")
    for step in steps:
        print(f"== {step}", flush=True)
        STEPS[step](team, work, args)


if __name__ == "__main__":
    main()
