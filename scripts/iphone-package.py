#!/usr/bin/env python3
"""Build the importable QuickMatch package using the launcher's net9.0 ModApi DLL."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mod_api", type=Path, help="path to STS2Mobile.ModApi.dll")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    mod = root / "mod/QuickMatch"
    work = root / ".work"
    command = ["dotnet", "build", "-c", "Release", "-p:QuickMatchPlatform=iPhone",
               f"-p:ModApiPath={args.mod_api.resolve()}"]
    if beta_data := os.environ.get("STS2_BETA_DATA"):
        command.append(f"-p:Sts2DataPath={Path(beta_data).resolve()}")
    subprocess.run(command, cwd=mod, check=True)

    manifest = json.loads((mod / "QuickMatch.json").read_text())
    manifest["min_game_version"] = "0.113.1"
    version = manifest["version"].removeprefix("v")
    package = work / f"QuickMatch-{version}-iPhone.zip"
    work.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=work) as staging:
        archive = Path(staging) / package.name
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
            output.write(mod / "bin/iPhone/Release/net9.0/QuickMatch.dll", "QuickMatch.dll")
            output.writestr("QuickMatch.json", json.dumps(manifest, indent=2) + "\n")
            output.write(root / "LICENSE", "LICENSE.txt")
            output.write(mod / "ThirdParty/Concentus/LICENSE", "LICENSE-Concentus.txt")
            if (work / "QuickMatch.cfg").is_file():
                output.write(work / "QuickMatch.cfg", "QuickMatch.cfg")
        archive.replace(package)
    print(package)


if __name__ == "__main__":
    main()
