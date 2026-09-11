#!/usr/bin/env python3
from __future__ import annotations

import shutil
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "windows" / "assets"
MAC_ICON = ROOT / ".build" / "AppIcon.iconset" / "icon_512x512.png"


def write_ico(png_paths: list[Path], output: Path) -> None:
    payloads = [path.read_bytes() for path in png_paths]
    header_size = 6 + 16 * len(payloads)
    offset = header_size
    entries = bytearray()
    body = bytearray()

    for path, payload in zip(png_paths, payloads):
        size = int(path.stem.split("_")[1].split("x")[0])
        dimension = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(payload), offset)
        body += payload
        offset += len(payload)

    output.write_bytes(struct.pack("<HHH", 0, 1, len(payloads)) + entries + body)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    iconset = MAC_ICON.parent
    png_paths = [
        iconset / "icon_16x16.png",
        iconset / "icon_32x32.png",
        iconset / "icon_32x32@2x.png",
        iconset / "icon_128x128.png",
        iconset / "icon_256x256.png",
        iconset / "icon_512x512.png",
    ]
    if MAC_ICON.exists() and all(path.exists() for path in png_paths):
        shutil.copyfile(MAC_ICON, ASSETS / "icon.png")
        write_ico(png_paths, ASSETS / "icon.ico")
    elif not (ASSETS / "icon.ico").exists():
        raise FileNotFoundError("macOS iconset is required to create the Windows icon")
    print(f"wrote {ASSETS / 'icon.ico'}")


if __name__ == "__main__":
    main()
