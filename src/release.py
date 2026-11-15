#!/usr/bin/env python3
"""Publish the built mods as assets on a GitHub release.

    python3 src/release.py              # build every mod, create the release, upload the assets
    python3 src/release.py --dry-run    # build and package, then say what would be published
    python3 src/release.py --no-build   # use the DLLs already in bin/Release
    python3 src/release.py --only CustomAvatars,LootOverhaul

One release per CustomAvatars version, tagged and titled with that version (`0.42.8`), holding
every mod at whatever version it is at the time; the release notes list them. The tag is put on
the pushed HEAD. Running again for the same version replaces that release's assets. Each mod's
version comes from the MelonInfo line in its Core.cs. The build also drops the DLL into the game's Mods folder, the way every build
does, so what you run is byte-for-byte what you published — the gated mods compare hashes
between players. CustomAvatars ships as `CustomAvatars.<version>.zip` (the DLL, the Unity
exporter package and the player-facing README from `src/CustomAvatars/release/`); the others
ship as a bare `<Mod>.dll`, the way the first release was laid out.

Needs the GitHub CLI logged in: `gh auth login --web` once.
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

REPO = "InconsolableCellist/DoE-Mods"
SRC = Path(__file__).resolve().parent
MODS = ["CustomAvatars", "LootOverhaul", "VisualCues", "Descent", "StayPutVR", "PartyHealth", "HazardTweaks"]
DOTNET_CANDIDATES = ["dotnet", "/mnt/c/Program Files/dotnet/dotnet.exe"]


def die(msg):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def version_of(mod):
    core = SRC / mod / "Core.cs"
    m = re.search(r'MelonInfo\(.*?"%s",\s*"([^"]+)"' % mod, core.read_text(encoding="utf-8"))
    if not m:
        die(f"no MelonInfo version in {core}")
    return m.group(1)


def dotnet():
    for c in DOTNET_CANDIDATES:
        if shutil.which(c) or Path(c).exists():
            return c
    die("dotnet not found; install the .NET SDK or add it to PATH")


def build(mod):
    print(f"building {mod} ...")
    r = subprocess.run([dotnet(), "build", "-c", "Release", "-nologo", "-v", "q"],
                       cwd=SRC / mod, capture_output=True, text=True)
    if r.returncode != 0:
        print(r.stdout, r.stderr, file=sys.stderr)
        die(f"{mod} did not build")


def dll_of(mod):
    p = SRC / mod / "bin" / "Release" / "net6.0" / f"{mod}.dll"
    if not p.exists():
        die(f"{p} is missing; build first (drop --no-build)")
    return p


def package(mod, version, out):
    """Return the asset path for this mod, written under `out`."""
    dll = dll_of(mod)
    if mod != "CustomAvatars":
        dst = out / f"{mod}.dll"
        shutil.copy2(dll, dst)
        return dst
    extras = SRC / mod / "release"
    readme = (extras / "README.txt").read_text(encoding="utf-8").replace("{version}", version)
    dst = out / f"{mod}.{version}.zip"
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(dll, f"{mod}/{mod}.dll")
        for extra in sorted(extras.iterdir()):
            if extra.name == "README.txt":
                continue
            z.write(extra, f"{mod}/{extra.name}")
        z.writestr(f"{mod}/README.txt", readme.replace("\n", "\r\n"))
    return dst


def gh(*args, check=True):
    r = subprocess.run(["gh", *args], capture_output=True, text=True)
    if check and r.returncode != 0:
        print(r.stderr, file=sys.stderr)
        die("gh " + " ".join(args))
    return r


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dry-run", action="store_true", help="package, then only say what would be uploaded")
    ap.add_argument("--no-build", action="store_true", help="use the DLLs already in bin/Release")
    ap.add_argument("--only", help="comma-separated mods to publish (default: all)")
    ap.add_argument("--tag", help="release tag and title (default: the CustomAvatars version)")
    ap.add_argument("--out", default=str(SRC / "bin" / "release"), help="where the assets are written")
    args = ap.parse_args()

    mods = [m.strip() for m in args.only.split(",")] if args.only else MODS
    unknown = [m for m in mods if m not in MODS]
    if unknown:
        die(f"unknown mod(s) {unknown}; choose from {MODS}")

    if not shutil.which("gh"):
        die("the GitHub CLI (gh) is not on PATH")
    if gh("auth", "status", check=False).returncode != 0 and not args.dry_run:
        die("gh is not logged in — run `gh auth login --web` once, then try again")

    out = Path(args.out)
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)

    versions = {m: version_of(m) for m in MODS}
    assets = []
    for mod in mods:
        if not args.no_build:
            build(mod)
        assets.append(package(mod, versions[mod], out))
        print(f"  {mod} {versions[mod]} -> {assets[-1].name} ({assets[-1].stat().st_size:,} bytes)")

    # Release notes: one line per mod, every mod, at its current version. The release page is
    # the one place a player sees all six versions together.
    notes = "\n".join(f"* {m} {versions[m]}" for m in MODS) + "\n"
    tag = args.tag or versions["CustomAvatars"]

    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=SRC, capture_output=True, text=True).stdout.strip()
    on_remote = subprocess.run(["git", "branch", "-r", "--contains", head], cwd=SRC,
                               capture_output=True, text=True).stdout.strip()

    view = gh("release", "view", tag, "-R", REPO, "--json", "assets", check=False)
    exists = view.returncode == 0
    existing = [a["name"] for a in json.loads(view.stdout)["assets"]] if exists else []

    if args.dry_run:
        print(f"\ndry run — would {'update' if exists else 'create'} release `{tag}` on {REPO}"
              + ("" if exists else f" at {head[:7]}") + ":")
        for a in assets:
            print(f"  {'replace' if a.name in existing else 'add    '} {a.name}")
        print("notes would read:\n" + notes)
        if not exists and not on_remote:
            print(f"note: HEAD {head[:7]} is not on the remote yet — push before publishing.")
        return

    if exists:
        gh("release", "upload", tag, *[str(a) for a in assets], "-R", REPO, "--clobber")
        gh("release", "edit", tag, "-R", REPO, "--notes", notes)
        print(f"updated release `{tag}`")
    else:
        if not on_remote:
            die(f"HEAD {head[:7]} is not on the remote; push first so the tag can point at it")
        gh("release", "create", tag, *[str(a) for a in assets], "-R", REPO,
           "--target", head, "--title", tag, "--notes", notes)
        print(f"created release `{tag}` at {head[:7]}")
    for a in assets:
        print(f"  {a.name}")
    print(f"https://github.com/{REPO}/releases/tag/{tag}")

if __name__ == "__main__":
    main()
