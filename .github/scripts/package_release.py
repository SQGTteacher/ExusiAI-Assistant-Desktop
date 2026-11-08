"""Package the Windows publication only after the existing CI probes pass."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile


NOTICE = "请勿使用此版本用于生产环境，仅做开发者参考。"
ALLOWED_EXES = {"exusiai.desktop.exe", "viewer/exusiai.fileviewer.desktop.exe"}
REQUIRED_FILES = (
    "ExusiAI.Desktop.exe", "ExusiAI.Core.dll", "coreclr.dll",
    "viewer/ExusiAI.FileViewer.Desktop.exe", "viewer/coreclr.dll",
    "packages/exusiai.classisland/NativeClassIsland/ClassIsland.Desktop.dll",
    "packages/exusiai.classisland/ExusiAI.Plugin.ClassIsland.dll",
    "packages/exusiai.file-viewer/ExusiAI.Plugin.FileViewer.dll",
    "packages/exusiai.roll-call/ExusiAI.Plugin.RollCall.dll",
    "packages/exusiai.ark-pets/ExusiAI.Plugin.ArkPets.dll",
)


def package(repo, publication, output, source_sha, build_number):
    repo, publication, output = (Path(p).resolve() for p in (repo, publication, output))
    if output.is_relative_to(publication):
        raise ValueError("Release output must be outside the publication")
    if not re.fullmatch(r"[0-9a-f]{40}", source_sha) or not re.fullmatch(r"[0-9]+", build_number):
        raise ValueError("A full source commit and numeric CI build number are required")
    config = json.loads((repo / ".github/release.json").read_text(encoding="utf-8"))
    version = config["version"]
    if not re.fullmatch(r"0\.1\.0-developer-beta", version) or config["prerelease"] is not True:
        raise ValueError("Only the authorized developer beta can be published by this release request")
    if ET.parse(repo / "Directory.Build.props").findtext(".//Version") != version:
        raise ValueError("Product version and release request disagree")
    notes = (repo / config["notes"]).read_text(encoding="utf-8")
    if NOTICE not in notes:
        raise ValueError("Developer-only notice is missing")
    for relative in REQUIRED_FILES:
        if not (publication / relative).is_file():
            raise ValueError(f"Incomplete publication: {relative}")
    # Verification data belongs in CI logs/artifacts, never in the downloadable application.
    files = []
    for path in sorted(publication.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Unexpected symlink: {path}")
        if not path.is_file():
            continue
        relative = path.relative_to(publication).as_posix()
        if relative.split("/")[0] in {"classisland-probe", "viewer-probe.json"}:
            continue
        if path.suffix.lower() == ".exe" and relative.lower() not in ALLOWED_EXES:
            raise ValueError(f"Third-party executable is forbidden: {relative}")
        if path.name.startswith("ArkPets-v3.13.1.zip.part"):
            raise ValueError("Archived third-party executables must not ship")
        files.append((path, relative))
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"ExusiAI-Assistant-Desktop-{version}-win-x64.zip"
    build = {"version": version, "source_commit": source_sha, "build_number": build_number,
             "runtime": "win-x64", "self_contained": True, "notice": NOTICE}
    build_text = json.dumps(build, ensure_ascii=False, indent=2) + "\n"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for path, relative in files:
            bundle.write(path, relative)
        bundle.writestr("README-DEVELOPER-BETA.md", notes)
        bundle.writestr("BUILD-INFO.json", build_text)
        bundle.write(repo / "LICENSE", "LICENSE")
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise ValueError("Release archive integrity check failed")
    with archive.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    (output / f"{archive.name}.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
    (output / "BUILD-INFO.json").write_text(build_text, encoding="utf-8")
    (output / "RELEASE-NOTES.md").write_text(notes, encoding="utf-8")
    metadata = {**config, **build, "tag": f"v{version}", "archive": archive.name, "sha256": digest}
    (output / "release-metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Packaged {archive.name} ({archive.stat().st_size} bytes), SHA-256 {digest}")
    return archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=".")
    parser.add_argument("--publication", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--build-number", required=True)
    args = parser.parse_args()
    package(args.repo, args.publication, args.output, args.source_sha, args.build_number)
