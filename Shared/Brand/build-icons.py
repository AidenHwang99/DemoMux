#!/usr/bin/env python3
"""Generate platform-scoped icons on macOS; each platform keeps its own source."""
from pathlib import Path
import argparse
import base64
import re
import struct
import subprocess
import tempfile


def generate(root, platform):
    source = (root / "Shared/Brand/AppIcon.png" if platform == "macos"
              else root / "DemoMuxWindows/Assets/AppIcon.png")
    sizes = ((16, 24, 32, 48, 64, 128, 256, 512, 1024) if platform == "macos"
             else (16, 20, 24, 32, 40, 48, 64, 128, 256))
    with tempfile.TemporaryDirectory(prefix="demomux-icons-") as temporary:
        stage = Path(temporary)
        iconset = stage / "AppIcon.iconset"
        iconset.mkdir()
        images = {}
        for size in sizes:
            output = stage / f"{size}.png"
            subprocess.run(["sips", "-z", str(size), str(size), str(source),
                            "--out", str(output)], check=True, capture_output=True)
            images[size] = output.read_bytes()
        if platform == "macos":
            for size in (16, 32, 128, 256, 512):
                (iconset / f"icon_{size}x{size}.png").write_bytes(images[size])
                (iconset / f"icon_{size}x{size}@2x.png").write_bytes(images[size * 2])
            subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o",
                            str(root / "DemoMux/Resources/AppIcon.icns")], check=True)
            guide = root / "DemoMux/Resources/Sample/index.html"
            patterns = ((128, r'(?P<prefix>id="demomux-brand-icon"[^>]*?src=")[^"]*'),
                        (32, r'(?P<prefix>id="demomux-favicon"[^>]*?href=")[^"]*'))
        else:
            # Windows 10/11 support PNG-compressed ICO frames, including alpha.
            entries = []
            offset = 6 + 16 * len(sizes)
            for size in sizes:
                payload = images[size]
                entries.append(struct.pack("<BBBBHHII", size % 256, size % 256,
                                           0, 0, 1, 32, len(payload), offset))
                offset += len(payload)
            ico = struct.pack("<HHH", 0, 1, len(sizes)) + b"".join(entries)
            ico += b"".join(images[size] for size in sizes)
            (root / "DemoMuxWindows/Assets/DemoMux.ico").write_bytes(ico)
            (root / "DemoMuxWindows/Assets/GuideIcon.png").write_bytes(images[128])
            guide = root / "DemoMuxWindows/Assets/Guide.html"
            patterns = ((128, r'(?P<prefix>data-demomux-icon="logo" src=")[^"]*'),
                        (32, r'(?P<prefix>data-demomux-icon="favicon" href=")[^"]*'))
        # Keep the standalone guide self-contained, including its brand images.
        html = guide.read_text(encoding="utf-8")
        for size, pattern in patterns:
            uri = "data:image/png;base64," + base64.b64encode(images[size]).decode()
            html, count = re.subn(
                pattern,
                lambda match: match["prefix"] + uri, html)
            if count != 1:
                raise ValueError(f"Expected one {platform} guide image marker: {pattern}")
        guide.write_text(html, encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--platform", choices=("macos", "windows", "all"), default="macos",
                        help="default: macos; Windows uses its own approved image source")
    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parents[2]
    for platform in (("macos", "windows") if args.platform == "all" else (args.platform,)):
        generate(root, platform)


if __name__ == "__main__":
    main()
