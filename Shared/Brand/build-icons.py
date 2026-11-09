#!/usr/bin/env python3
"""Rebuild both desktop icons from AppIcon.png on macOS (stdlib only)."""
from pathlib import Path
import base64
import re
import struct
import subprocess
import tempfile


def main():
    brand = Path(__file__).resolve().parent
    root = brand.parent.parent
    source = brand / "AppIcon.png"
    with tempfile.TemporaryDirectory(prefix="demomux-icons-") as temporary:
        stage = Path(temporary)
        iconset = stage / "AppIcon.iconset"
        iconset.mkdir()
        images = {}
        for size in (16, 24, 32, 48, 64, 128, 256, 512, 1024):
            output = stage / f"{size}.png"
            subprocess.run(["sips", "-z", str(size), str(size), str(source),
                            "--out", str(output)], check=True, capture_output=True)
            images[size] = output.read_bytes()
        for size in (16, 32, 128, 256, 512):
            (iconset / f"icon_{size}x{size}.png").write_bytes(images[size])
            (iconset / f"icon_{size}x{size}@2x.png").write_bytes(images[size * 2])
        subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o",
                        str(root / "DemoMux/Resources/AppIcon.icns")], check=True)
        # Windows 10/11 support PNG-compressed ICO frames, including alpha.
        sizes = (16, 24, 32, 48, 64, 128, 256)
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
        # Keep the standalone guide self-contained, including its brand images.
        guide = root / "DemoMux/Resources/Sample/index.html"
        html = guide.read_text()
        for size, marker in ((128, "demomux-brand-icon"), (32, "demomux-favicon")):
            uri = "data:image/png;base64," + base64.b64encode(images[size]).decode()
            html, count = re.subn(
                rf'(?P<prefix>id="{marker}"[^>]*?(?:src|href)=")[^"]*',
                lambda match: match["prefix"] + uri, html)
            if count != 1:
                raise ValueError(f"Expected one guide image marker: {marker}")
        guide.write_text(html)


if __name__ == "__main__":
    main()
