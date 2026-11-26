"""Generate component inventory and upstream license notices for the selected capture target."""
import json
import sys
import tomllib
from pathlib import Path

metadata_path, lock_path, destination = map(Path, sys.argv[1:])
dest = destination / "licenses"
dest.mkdir(exist_ok=True, parents=True)
metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
lock = tomllib.loads(lock_path.read_text())
checksums = {(p["name"], p["version"]): p.get("checksum") for p in lock["package"]}
rows = []
notices = []
for p in sorted(metadata["packages"], key=lambda p: (p["name"], p["version"])):
    if p["source"] is None:
        continue
    name, version = p["name"], p["version"]
    rows.append({"name": name, "version": version, "license": p["license"],
                 "purl": f"pkg:cargo/{name}@{version}", "registrySha256": checksums[name, version]})
    root = Path(p["manifest_path"]).parent
    files = sorted(f for f in root.iterdir() if f.is_file() and f.name.lower().startswith(("license", "licence", "copying", "notice")))
    if not files:
        raise RuntimeError(f"Missing upstream license for {name} {version}")
    notices.append(f"{name} {version} · {p['license']}\n{p['repository'] or ''}\n")
    for f in files:
        notices.append(f"{f.name}\n{f.read_text(encoding='utf-8-sig')}\n")
(dest / "Rust-notices.txt").write_text("\n".join(notices), encoding="utf-8")
components = {"product":"Snapline 0.9.1 Windows x64", "framework":"Windows .NET Framework 4.8",
              "ffmpeg":{"version":"7.1.5","license":"LGPL-2.1-or-later","upstreamSha256":"de668509caf9e35e3cd162473441fdb29538c6d96ed080292b3cf9e6fc5d558f","patched":True},
              "rustResolutionIncludingBuildDependencies":rows}
(destination / "components.json").write_text(json.dumps(components, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
