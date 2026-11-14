import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from package_release import NOTICE, REQUIRED_FILES, package


class PackageReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.pub = self.root / "published"
        self.out = self.root / "release"
        (self.repo / ".github").mkdir(parents=True)
        (self.repo / ".github/release.json").write_text(json.dumps({
            "version": "0.1.0-developer-beta", "name": "Developer beta", "prerelease": True, "notes": "NOTES.md"
        }))
        (self.repo / "NOTES.md").write_text(NOTICE, encoding="utf-8")
        (self.repo / "LICENSE").write_text("Test license")
        (self.repo / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>0.1.0-developer-beta</Version></PropertyGroup></Project>")
        for relative in REQUIRED_FILES:
            file = self.pub / relative
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(b"test binary")

    def pack(self):
        return package(self.repo, self.pub, self.out, "a" * 40, "430")

    def test_archive_contains_notice_provenance_and_no_probe_data(self):
        (self.pub / "classisland-probe").mkdir()
        (self.pub / "classisland-probe/settings.json").write_text("test settings")
        (self.pub / "viewer-probe.json").write_text("test report")
        archive = self.pack()
        with zipfile.ZipFile(archive) as bundle:
            self.assertIsNone(bundle.testzip())
            self.assertTrue(set(REQUIRED_FILES).issubset(bundle.namelist()))
            self.assertFalse(any("probe" in p for p in bundle.namelist()))
            self.assertIn(NOTICE, bundle.read("README-DEVELOPER-BETA.md").decode())
            self.assertEqual("a" * 40, json.loads(bundle.read("BUILD-INFO.json"))["source_commit"])
        self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), (self.out / f"{archive.name}.sha256").read_text().split()[0])

    def test_rejects_foreign_executable(self):
        (self.pub / "ClassIsland.exe").write_bytes(b"foreign")
        with self.assertRaisesRegex(ValueError, "Third-party"):
            self.pack()

    def test_rejects_incomplete_runtime(self):
        (self.pub / "viewer/coreclr.dll").unlink()
        with self.assertRaisesRegex(ValueError, "Incomplete publication"):
            self.pack()

    def test_rejects_missing_developer_notice(self):
        (self.repo / "NOTES.md").write_text("ordinary release")
        with self.assertRaisesRegex(ValueError, "notice"):
            self.pack()

    def test_rejects_mismatched_product_version(self):
        (self.repo / "Directory.Build.props").write_text("<Project><Version>0.2.0</Version></Project>")
        with self.assertRaisesRegex(ValueError, "disagree"):
            self.pack()


if __name__ == "__main__":
    unittest.main()
