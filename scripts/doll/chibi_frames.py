#!/usr/bin/env python3
# /// script
# requires-python = ">=3.12,<3.13"
# dependencies = ["UnityPy>=1.25", "lz4", "Pillow"]
# ///
"""Render a doll's GFL2 chibi loop to one sheet of frames, for her token on mission
preparation's map.

Every doll in the GFL2 client has an animated chibi: a Spine 3.8 rig (`<Name>.skel`,
`<Name>.atlas` and its page textures in one archive) carrying the same animations, of which
`wait` is her standing loop with her weapon out. This script finds the rig, poses it with the
official Spine runtime in a headless browser, and lays the loop's frames out in a grid:

    sprites/<doll>/chibi_wait.png    40 frames, 6 across, each 240x160

`--animation` names another animation, or several with a count of frames each (`move:12
wait:40`), which go into one sheet in that order.

Every frame is drawn at one scale and lined up on the rig's root bone, so the figure holds her
place from frame to frame. Her feet stand on the same line of every frame, the point between them in its
middle, and she is as tall as a frame allows over that line, so every doll's sheet reads the same way and the game needs no
numbers per doll. A wide pose (a long rifle held level) is shrunk only if it would leave the
frame.

    uv run scripts/doll/chibi_frames.py <Rig> <doll> --bundles <AssetBundles_Windows>

<Rig> is the client's internal name, which is not always the doll's (Nemesis in her current
outfit is `NemesisGnosis`). `--list` prints every rig. `--info` prints a rig's skins and
animations.

The client stores its archives end to end inside `.bundle` pack files, each XOR-encrypted over
its first 32 KB. The first run walks every pack to find the rigs, which takes a few minutes,
and keeps what it found in the work folder (`--work`, a folder in the system's temporary
files by default). After a client update, delete `rigs.json` there to walk them again.

The Spine runtime is downloaded once from a pinned commit of the official repository, checked
against its SHA-256 and inlined into the page, so the browser itself makes no network
requests. Chromium, Chrome or Edge is needed for the render.
"""

import argparse
import base64
import hashlib
import html
import io
import json
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.request
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

import UnityPy
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
WORK = Path(tempfile.gettempdir()) / "womenace-chibi"
# The client is built on 2019.4. Its archives strip the engine version, so UnityPy needs it
# supplied to read the type trees.
UnityPy.config.FALLBACK_UNITY_VERSION = "2019.4.29f1"

SIGNATURE = b"UnityFS\0\0\0\0\x075.x."
ENCRYPTED_SPAN = 32768


def decrypt(data: bytes) -> bytes:
    if data.startswith(b"UnityFS\0"):
        return data
    key = bytes(a ^ b for a, b in zip(data[:16], SIGNATURE))
    count = min(len(data), ENCRYPTED_SPAN)
    return bytes(data[i] ^ key[i % 16] for i in range(count)) + data[count:]


def archives(path: Path):
    """Yield (offset, decrypted archive bytes) for every archive in a pack."""
    size = path.stat().st_size
    offset = 0
    with path.open("rb") as stream:
        while offset + 64 < size:
            stream.seek(offset)
            header = decrypt(stream.read(256))
            if not header.startswith(b"UnityFS\0"):
                return
            position = 12
            for _ in range(2):
                position = header.index(b"\0", position) + 1
            length = struct.unpack_from(">q", header, position)[0]
            if not 0 < length <= size - offset:
                return
            stream.seek(offset)
            yield offset, decrypt(stream.read(length))
            offset += length


def archive_at(path: Path, offset: int) -> bytes:
    for start, data in archives(path):
        if start == offset:
            return data
    raise KeyError(f"no archive at {path.name}:{offset}")


def scan_pack(path: Path):
    """Every Spine rig in a pack, as (name, pack, offset)."""
    found = []
    try:
        for offset, data in archives(path):
            try:
                for obj in UnityPy.load(data).objects:
                    if obj.type.name != "TextAsset":
                        continue
                    try:
                        name = obj.peek_name()
                    except Exception:
                        name = obj.read().m_Name
                    if name.endswith(".skel"):
                        found.append((name[:-5], path.name, offset))
            except Exception:
                continue
    except Exception:
        pass
    return found


def rigs(bundles: Path, work: Path) -> dict:
    """Map rig name to (pack, offset), walking the client once and keeping the result."""
    cache = work / "rigs.json"
    if cache.is_file():
        return {name: tuple(place) for name, place in json.loads(cache.read_text()).items()}
    files = sorted(p for p in bundles.iterdir() if p.suffix == ".bundle")
    table = {}
    with ProcessPoolExecutor(max(2, (os.cpu_count() or 4) - 2)) as pool:
        futures = [pool.submit(scan_pack, p) for p in files]
        for done, future in enumerate(as_completed(futures), 1):
            for name, pack, offset in future.result():
                table.setdefault(name, (pack, offset))
            if done % 1000 == 0 or done == len(files):
                print(f"walked {done}/{len(files)} packs", file=sys.stderr, flush=True)
    work.mkdir(parents=True, exist_ok=True)
    cache.write_text(json.dumps(table, indent=1))
    return table


def text_bytes(asset) -> bytes:
    script = asset.m_Script
    return script.encode("utf-8", "surrogateescape") if isinstance(script, str) else bytes(script)


def page_sizes(atlas: str) -> dict:
    """Each page's declared size. A page header is its file name followed by
    its `size:` line, and a rig may carry several pages of different sizes."""
    sizes, page = {}, None
    for line in atlas.splitlines():
        stripped = line.strip()
        if stripped.lower().endswith(".png"):
            page = stripped
        elif page and (match := re.match(r"size:\s*(\d+)\s*,\s*(\d+)", stripped)):
            sizes[page] = (int(match[1]), int(match[2]))
            page = None
    return sizes


def skel_version(skel: bytes) -> str:
    """The version string after the hash. Spine strings are a length byte
    (characters plus one) followed by the characters."""
    hash_end = 1 + skel[0] - 1
    return skel[hash_end + 1:hash_end + skel[hash_end]].decode("ascii", "replace")


def extract(name: str, bundles: Path, work: Path) -> Path:
    """Write a rig's .skel, .atlas and page images under the work folder. Unity stretched each
    page to a power of two on import, so it is resized back to the size the atlas declares
    before any region in it lines up."""
    table = rigs(bundles, work)
    if name not in table:
        close = [n for n in table if name.lower() in n.lower()]
        raise SystemExit(f"no rig '{name}' in the client" + (f", near matches: {', '.join(close)}" if close else ""))
    pack, offset = table[name]
    env = UnityPy.load(archive_at(bundles / pack, offset))

    target = work / "spine" / name
    target.mkdir(parents=True, exist_ok=True)
    texts, textures = {}, {}
    for obj in env.objects:
        if obj.type.name == "TextAsset":
            asset = obj.read()
            texts[asset.m_Name] = text_bytes(asset)
        elif obj.type.name == "Texture2D":
            asset = obj.read()
            textures[asset.m_Name] = asset

    skel, atlas = texts.get(f"{name}.skel"), texts.get(f"{name}.atlas")
    if skel is None or atlas is None:
        raise SystemExit(f"{pack}:{offset} lacks {name}.skel or {name}.atlas")
    (target / f"{name}.skel").write_bytes(skel)
    (target / f"{name}.atlas").write_bytes(atlas)

    atlas_text = atlas.decode("utf-8")
    sizes = page_sizes(atlas_text)
    pages = list(sizes)
    for page in pages:
        stem = Path(page).stem
        texture = textures.get(stem)
        if texture is None:
            raise SystemExit(f"atlas page {page} has no Texture2D '{stem}' in {pack}:{offset}")
        image = texture.image.convert("RGBA")
        if image.size != sizes[page]:
            image = image.resize(sizes[page], Image.LANCZOS)
        image.save(target / page)

    meta = {"name": name, "pack": pack, "offset": offset, "pages": pages, "spineVersion": skel_version(skel)}
    (target / "rig.json").write_text(json.dumps(meta, indent=2))
    print(f"{name}: {pack}:{offset} -> {target}", file=sys.stderr)
    return target


# The official spine-ts 3.8 WebGL build, pinned to a commit and checked by hash
# so a render never runs code that differs from what was reviewed.
SPINE_COMMIT = "8b4844bd4b193ba9e54487ed397a777993cbad56"
SPINE_RUNTIME = f"https://cdn.jsdelivr.net/gh/EsotericSoftware/spine-runtimes@{SPINE_COMMIT}/spine-ts/build/spine-webgl.js"
SPINE_SHA256 = "4b108bad314c152bf0ff7a7f56fce5c98073f05a2bfa53e880d6c24b6f7c3aa5"
# GFL2 rigs are exported from Spine 3.8.75. Its binary layout is the one later
# 3.8 runtimes read, but they refuse that version string by name.
VERSION_GUARD = 'if ("3.8.75" == skeletonData.version)'

# The page writes one JSON result into #out, which --dump-dom prints back.
# Textures are uploaded premultiplied and drawn premultiplied, so the canvas
# alpha is the true coverage and toDataURL un-premultiplies it cleanly.
PAGE = """<!doctype html><meta charset="utf-8"><body><pre id="out">pending</pre>
<script>__RUNTIME__</script>
<script>
const job = JSON.parse(atob("__JOB__"));
const report = value => { document.getElementById("out").textContent = JSON.stringify(value); };
function bytes(b64) { const s = atob(b64); const a = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) a[i] = s.charCodeAt(i); return a; }
function loadImage(b64) { return new Promise((ok, fail) => { const i = new Image(); i.onload = () => ok(i); i.onerror = fail; i.src = "data:image/png;base64," + b64; }); }
(async () => {
  try {
    const images = {};
    for (const [page, b64] of Object.entries(job.pages)) images[page] = await loadImage(b64);
    const canvas = document.createElement("canvas");
    canvas.width = 16; canvas.height = 16;
    const context = new spine.webgl.ManagedWebGLRenderingContext(canvas, { alpha: true, premultipliedAlpha: true, preserveDrawingBuffer: true, antialias: true });
    const gl = context.gl;
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    const atlas = new spine.TextureAtlas(job.atlas, page => new spine.webgl.GLTexture(context, images[page]));
    const binary = new spine.SkeletonBinary(new spine.AtlasAttachmentLoader(atlas));
    const data = binary.readSkeletonData(bytes(job.skel));
    const info = {
      version: data.version,
      skins: data.skins.map(s => s.name),
      animations: data.animations.map(a => [a.name, Math.round(a.duration * 1000) / 1000]),
    };
    if (job.info) { report(info); return; }

    const renderer = new spine.webgl.SceneRenderer(canvas, context, true);
    const frames = [];
    for (const pose of job.poses) {
      const skeleton = new spine.Skeleton(data);
      if (job.skin && data.findSkin(job.skin)) skeleton.setSkinByName(job.skin);
      skeleton.setToSetupPose();
      if (pose.animation) {
        const state = new spine.AnimationState(new spine.AnimationStateData(data));
        state.setAnimation(0, pose.animation, false);
        state.update(pose.time);
        state.apply(skeleton);
      }
      skeleton.updateWorldTransform();
      for (const name of job.hide) { const slot = skeleton.findSlot(name); if (slot) slot.attachment = null; }

      const offset = new spine.Vector2(), size = new spine.Vector2();
      skeleton.getBounds(offset, size, []);
      const scale = job.scale, pad = job.pad;
      canvas.width = Math.ceil(size.x * scale) + pad * 2;
      canvas.height = Math.ceil(size.y * scale) + pad * 2;
      gl.viewport(0, 0, canvas.width, canvas.height);
      renderer.camera.setViewport(canvas.width, canvas.height);
      renderer.camera.zoom = 1 / scale;
      renderer.camera.position.x = offset.x + size.x / 2;
      renderer.camera.position.y = offset.y + size.y / 2;
      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      renderer.begin();
      renderer.drawSkeleton(skeleton, true);
      renderer.end();
      gl.finish();

      const bones = {};
      for (const bone of skeleton.bones) bones[bone.data.name] = [
        (bone.worldX - offset.x) * scale + pad,
        canvas.height - ((bone.worldY - offset.y) * scale + pad)];
      frames.push({ ...pose, png: canvas.toDataURL("image/png").split(",")[1], width: canvas.width, height: canvas.height, bones });
    }
    report({ ...info, frames });
  } catch (error) {
    report({ error: String(error && error.stack || error) });
  }
})();
</script>"""


def browser() -> Path:
    """A Chromium-family browser to pose the rig in."""
    for name in ("chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "msedge", "chrome"):
        found = shutil.which(name)
        if found:
            return Path(found)
    for root in (os.environ.get("PROGRAMFILES(X86)"), os.environ.get("PROGRAMFILES")):
        for tail in ("Microsoft/Edge/Application/msedge.exe", "Google/Chrome/Application/chrome.exe"):
            if root and (Path(root) / tail).is_file():
                return Path(root) / tail
    raise SystemExit("no Chromium, Chrome or Edge found for headless rendering")


def spine_runtime(work: Path) -> str:
    """The pinned runtime with the 3.8.75 guard removed, downloaded once into the work folder."""
    cache = work / "cache" / f"spine-webgl-{SPINE_COMMIT[:12]}.js"
    if not cache.is_file():
        with urllib.request.urlopen(SPINE_RUNTIME, timeout=60) as response:
            data = response.read()
        if hashlib.sha256(data).hexdigest() != SPINE_SHA256:
            raise SystemExit(f"{SPINE_RUNTIME} does not match its pinned SHA-256")
        cache.parent.mkdir(parents=True, exist_ok=True)
        cache.write_bytes(data)
    data = cache.read_bytes()
    if hashlib.sha256(data).hexdigest() != SPINE_SHA256:
        raise SystemExit(f"{cache} does not match its pinned SHA-256. Delete it to download again")
    source = data.decode("utf-8")
    if VERSION_GUARD not in source:
        raise SystemExit("the pinned runtime no longer carries the 3.8.75 guard this script removes")
    return source.replace(VERSION_GUARD, "if (false)").replace("</script", "<\\/script")


def run_page(job: dict, work: Path) -> dict:
    page = PAGE.replace("__RUNTIME__", spine_runtime(work)).replace(
        "__JOB__", base64.b64encode(json.dumps(job).encode()).decode())
    with tempfile.TemporaryDirectory(prefix="chibi-render-") as scratch:
        page_path = Path(scratch) / "render.html"
        page_path.write_text(page, encoding="utf-8")
        command = [str(browser()), "--headless=new", "--no-first-run", "--disable-extensions",
                   f"--user-data-dir={Path(scratch) / 'profile'}",
                   "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
                   "--virtual-time-budget=60000", "--dump-dom", page_path.as_uri()]
        result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", timeout=180)
    match = re.search(r'<pre id="out">(.*?)</pre>', result.stdout, re.DOTALL)
    if not match:
        raise SystemExit(f"the browser returned no result\n{result.stderr[-2000:]}")
    body = html.unescape(match[1])
    if body == "pending":
        raise SystemExit("the render did not finish within the browser's time budget")
    value = json.loads(body)
    if "error" in value:
        raise SystemExit(f"render failed: {value['error']}")
    return value


def render_many(rig_dir: Path, work: Path, poses, *, skin=None, hide=(), scale=4.0, pad=8, info=False) -> dict:
    """Render several poses in one browser launch.

    `poses` is a list of {"animation": name or None, "time": seconds}. The result
    carries the rig's skins and animations plus one frame per pose, each with
    its PNG (base64), size and bone positions in output pixels.
    """
    meta = json.loads((rig_dir / "rig.json").read_text())
    name = meta["name"]
    job = {
        "skel": base64.b64encode((rig_dir / f"{name}.skel").read_bytes()).decode(),
        "atlas": (rig_dir / f"{name}.atlas").read_text(encoding="utf-8"),
        "pages": {page: base64.b64encode((rig_dir / page).read_bytes()).decode() for page in meta["pages"]},
        "poses": list(poses), "skin": skin, "hide": list(hide),
        "scale": scale, "pad": pad, "info": info,
    }
    return run_page(job, work)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("rig", nargs="?", help="the rig's name in the client")
    parser.add_argument("doll", nargs="?", help="the doll's sprite folder under assets/additions/sprites")
    parser.add_argument("--bundles", type=Path, required=True, help="the client's AssetBundles_Windows folder")
    parser.add_argument("--work", type=Path, default=WORK)
    parser.add_argument("--out", type=Path, help="the sheet to write, in place of the doll's sprite")
    parser.add_argument("--list", action="store_true", help="print every rig in the client and stop")
    parser.add_argument("--animation", nargs="+", default=["wait"], metavar="NAME[:FRAMES]",
                        help="the animation to draw. Several go into one sheet one after another, at one scale, each with its own count of frames")
    parser.add_argument("--cell", type=int, default=160, help="a cell's height")
    parser.add_argument("--cell-width", type=int, default=240)
    parser.add_argument("--frames", type=int, default=40, help="this many frames spread over the loop, whatever its length")
    parser.add_argument("--ground", type=float, default=0.06, help="how far up a cell the figure's feet stand, as a share of its height")
    parser.add_argument("--columns", type=int, default=6)
    parser.add_argument("--origin", type=float, default=0.5, help="how far across a cell the rig's origin stands, as a share of its width, for a rig that moves to one side of it")
    parser.add_argument("--scale", type=float, default=2.0, help="render pixels per skeleton unit, before fitting the cell")
    parser.add_argument("--skin")
    parser.add_argument("--hide", nargs="*", default=[])
    parser.add_argument("--info", action="store_true", help="print the rig's skins and animations and stop")
    args = parser.parse_args()

    if args.list:
        print("\n".join(sorted(rigs(args.bundles, args.work))))
        return
    if not args.rig:
        parser.error("a rig name is needed")
    rig_dir = args.work / "spine" / args.rig
    if not (rig_dir / "rig.json").is_file():
        extract(args.rig, args.bundles, args.work)
    info = render_many(rig_dir, args.work, [], info=True)
    if args.info:
        print(json.dumps(info, indent=1))
        return
    if not args.out and not args.doll:
        parser.error("a doll's sprite folder or --out is needed")
    durations = dict(info["animations"])
    parts = [(name, int(frames) if frames else args.frames) for name, _, frames in (given.partition(":") for given in args.animation)]
    for name, _ in parts:
        if name not in durations:
            raise SystemExit(f"{args.rig} has no animation '{name}': {', '.join(durations)}")
    if len(parts) > 1 and not args.out:
        parser.error("a sheet of several animations needs --out")
    out = args.out or REPO / "assets" / "additions" / "sprites" / args.doll / f"chibi_{parts[0][0]}.png"
    count = sum(frames for _, frames in parts)
    poses = [{"animation": name, "time": i * durations[name] / frames} for name, frames in parts for i in range(frames)]
    skin = args.skin or (args.rig if args.rig in info["skins"] else None)
    result = render_many(rig_dir, args.work, poses, skin=skin, hide=args.hide, scale=args.scale)

    # Each frame is cropped to its own pose. Its root bone says where the rig's origin fell in
    # it, which is the same point of the figure in every frame.
    frames = []
    for frame in result["frames"]:
        image = Image.open(io.BytesIO(base64.b64decode(frame["png"]))).convert("RGBA")
        root = frame["bones"].get("root") or next(iter(frame["bones"].values()))
        frames.append((image, root[0], root[1]))
    # What is drawn of the figure about that point, over the whole loop. A rig's own bounds take
    # in parts that draw nothing, so the pictures are measured instead.
    left = right = above = below = 0.0
    for image, x, y in frames:
        box = image.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
        if box is None:
            continue
        left, right = max(left, x - box[0]), max(right, box[2] - x)
        above, below = max(above, y - box[1]), max(below, box[3] - y)
    # The lowest thing drawn is the figure's feet, which stand on a set line of the cell. The
    # figure is as tall as the cell allows over that line, so every doll stands the same height.
    # A wide pose is shrunk only if it would leave the cell.
    width, height = args.cell_width, args.cell
    foot_y = height * (1 - args.ground)
    tall = (foot_y - 2) / (above + below)
    # The rig's origin, which is between a doll's feet, is the middle of the frame unless told
    # otherwise: the game stands that point on her tile.
    origin_x = width * args.origin
    wide = min((room - 2) / reach for room, reach in ((origin_x, left), (width - origin_x, right)) if reach > 0)
    fit = min(tall, wide)
    origin_y = foot_y - below * fit
    rows = math.ceil(count / args.columns)
    sheet = Image.new("RGBA", (args.columns * width, rows * height), (0, 0, 0, 0))
    for i, (image, x, y) in enumerate(frames):
        scaled = image.resize((max(1, round(image.width * fit)), max(1, round(image.height * fit))), Image.LANCZOS)
        cell_x, cell_y = i % args.columns * width, i // args.columns * height
        sheet.alpha_composite(scaled, (cell_x + round(origin_x - x * fit), cell_y + round(origin_y - y * fit)))
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)
    said = ", ".join(f"{name} {frames} frames over {durations[name]} s" for name, frames in parts)
    print(f"{out}: {said}, {args.columns}x{rows} frames of {width}x{height}, scale {fit:.3f}, "
          f"the rig's origin {origin_x:.1f} across and {origin_y:.1f} down a frame, {args.scale * fit:.4f} pixels to a rig unit"
          + (", shrunk to fit the frame's width" if wide < tall else ""), file=sys.stderr)


if __name__ == "__main__":
    main()
