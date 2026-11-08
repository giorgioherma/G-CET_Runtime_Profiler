"""Release packaging from verified portable CI, without rebuilding CET."""
from __future__ import annotations
import argparse
import hashlib
import json
from io import BytesIO
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from PIL import Image

APP = "G-CET-Runtime-Profiler"
ASI_SHA = "f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f"
NAME = "G-CET-Runtime-Profiler-v1.0.3-CP2077-2.5-CET-1.38.1.zip"

def sha(data: bytes):
    return hashlib.sha256(data).hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--icon-source", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    source, root = args.source, args.source / APP
    manifest = json.loads((root / "MANIFEST.json").read_text(encoding="utf-8-sig"))
    assert manifest["packageVersion"] == "1.0.3"
    assert manifest["targetCET"]["version"] == "1.38.1"
    assert manifest["targetCET"]["profilerSha256"] == ASI_SHA
    dll = root / "payload/cyber_engine_tweaks.PROFILER.dll"
    assert dll.is_file() and sha(dll.read_bytes()) == ASI_SHA, "Native ASI does not match pinned tested bytes"
    for file in ["G-CET-Runtime-Profiler.exe", "app/G-CET-Runtime-Profiler.App.exe",
                 "payload/CETProfilerControls/init.lua",
                 "payload/0-Engine/modules/G-CET/Scheduler.lua",
                 "VERSION.txt"]:
        assert (root / file).is_file(), "Missing executable/payload: " + file
    assert not (root / "G-CET-Runtime-Profiler.ico").exists(), "Unexpected duplicate icon"

    artwork = Image.open(args.icon_source).convert("RGBA")
    assert min(artwork.size) >= 48, "Icon source too small"
    iconbuf = BytesIO()
    artwork.save(iconbuf, format="ICO", sizes=[(16,16),(24,24),(32,32),(48,48)])
    icon = iconbuf.getvalue()
    Image.open(BytesIO(icon)).verify()

    instructions = (source / "Install_Instructions.html").read_text(encoding="utf-8-sig")
    anchor = '<div class="wrap">'
    assert anchor in instructions, "Instructions layout changed"
    banner = ('<div class="important" style="border-left-color:#ffd84b;background:#302611">'
              '<b>v1.0.3 / Cyberpunk 2077 2.5 / CET 1.38.1 ONLY.</b> '
              'For Cyberpunk 2.31 / CET 1.37.1, use the older release. '
              '<b>0-Engine is required for advanced Scheduler attribution.</b> '
              'Core CET profiling does not require 0-Engine. '
              'Restore the previously installed profiler before updating. '
              'F11 is manual/unlimited; F10 is a 120-second automatic capture.'
              '</div>')
    instructions = instructions.replace(anchor, anchor + banner, 1)
    args.out.mkdir(parents=True, exist_ok=True)
    archive = args.out / NAME
    with ZipFile(archive, "w", compression=ZIP_DEFLATED, compresslevel=6) as z:
        z.writestr("Install_Instructions.html", instructions.encode("utf-8"))
        for file in sorted(root.rglob("*")):
            if not file.is_file() or "docs" in file.relative_to(root).parts:
                continue
            z.write(file, file.relative_to(source).as_posix())
        z.writestr(APP + "/G-CET-Runtime-Profiler.ico", icon)

    with ZipFile(archive) as z:
        assert z.testzip() is None
        names = z.namelist()
        assert len(names) == len(set(names)), "Duplicate path"
        assert set(n.split("/",1)[0] for n in names) == {APP, "Install_Instructions.html"}
        assert not any("/docs/" in n or n.endswith("SHA256.txt") for n in names)
        assert not any("SchedulerProbe" in n or "GCET_25_Validation" in n or "CET-2.5-Validation" in n for n in names)
        assert sha(z.read(APP + "/payload/cyber_engine_tweaks.PROFILER.dll")) == ASI_SHA
        assert z.read(APP + "/G-CET-Runtime-Profiler.ico") == icon
    digest = sha(archive.read_bytes())
    (args.out / (NAME + ".sha256.txt")).write_text(digest + "  " + NAME + "\n", encoding="ascii")
    print("Release ZIP:", archive)
    print("Release ZIP SHA256:", digest)
    print("Unchanged native ASI:", ASI_SHA)
    print("Entries:", len(names))

if __name__ == "__main__":
    main()
