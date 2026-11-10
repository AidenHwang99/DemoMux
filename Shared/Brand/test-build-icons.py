"""Verify platform write boundaries without sips/iconutil or real artwork changes."""
from pathlib import Path
import struct
import tempfile
import types
import unittest
from unittest.mock import patch

script = Path(__file__).with_name("build-icons.py")
brand = types.ModuleType("brand_icons")
brand.__file__ = str(script)
exec(compile(script.read_text(encoding="utf-8"), str(script), "exec"), brand.__dict__)


class PlatformBoundaries(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory(prefix="demomux-brand-test-")
        self.addCleanup(self.workspace.cleanup)
        self.root = Path(self.workspace.name)
        fixtures = {
            "Shared/Brand/AppIcon.png": b"approved-mac-source",
            "DemoMuxWindows/Assets/AppIcon.png": b"approved-windows-source",
            "DemoMux/Resources/AppIcon.icns": b"existing-mac-icon",
            "DemoMuxWindows/Assets/DemoMux.ico": b"existing-windows-icon",
            "DemoMuxWindows/Assets/GuideIcon.png": b"existing-windows-doc-icon",
            "DemoMux/Resources/Sample/index.html": b'<img id="demomux-brand-icon" src="old"><link id="demomux-favicon" href="old">',
            "DemoMuxWindows/Assets/Guide.html": b'<img data-demomux-icon="logo" src="old"><link data-demomux-icon="favicon" href="old">',
        }
        for name, data in fixtures.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self.original = fixtures

    def command(self, args, **kwargs):
        if args[0] == "sips":
            Path(args[-1]).write_bytes(Path(args[4]).read_bytes() + b":" + args[2].encode())
        elif args[0] == "iconutil":
            Path(args[-1]).write_bytes(b"generated-icns")
        else:
            raise AssertionError(args)

    def test_default_updates_only_macos(self):
        with patch.object(brand, "generate") as generate:
            brand.main([])
        generate.assert_called_once_with(script.resolve().parents[2], "macos")

    def test_macos_leaves_all_windows_assets_unchanged(self):
        with patch.object(brand.subprocess, "run", self.command):
            brand.generate(self.root, "macos")
        for name, data in self.original.items():
            if name.startswith("DemoMuxWindows/"):
                self.assertEqual((self.root / name).read_bytes(), data)

    def test_windows_leaves_mac_assets_unchanged(self):
        with patch.object(brand.subprocess, "run", self.command):
            brand.generate(self.root, "windows")
        for name, data in self.original.items():
            if name.startswith("DemoMux/") or name.startswith("Shared/"):
                self.assertEqual((self.root / name).read_bytes(), data)

    def test_windows_uses_own_source_for_ico_and_documents(self):
        with patch.object(brand.subprocess, "run", self.command):
            brand.generate(self.root, "windows")
        ico = (self.root / "DemoMuxWindows/Assets/DemoMux.ico").read_bytes()
        self.assertEqual(struct.unpack("<HHH", ico[:6]), (0, 1, 9))
        self.assertIn(b"approved-windows-source", ico)
        self.assertNotIn(b"approved-mac-source", ico)
        self.assertEqual((self.root / "DemoMuxWindows/Assets/GuideIcon.png").read_bytes(), b"approved-windows-source:128")
        self.assertNotIn('="old"', (self.root / "DemoMuxWindows/Assets/Guide.html").read_text())

    def test_all_is_explicit_and_runs_both(self):
        with patch.object(brand, "generate") as generate:
            brand.main(["--platform", "all"])
        self.assertEqual([call.args[1] for call in generate.call_args_list], ["macos", "windows"])


if __name__ == "__main__":
    unittest.main()
