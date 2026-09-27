#!/usr/bin/env python3
from __future__ import annotations

import shutil
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "windows" / "assets"
ICONSET = ROOT / ".build" / "AppIcon.iconset"


def write_ico(sources: list[tuple[Path, int]], output: Path) -> None:
    payloads = []
    for path, size in sources:
        payload = path.read_bytes()
        if not 1 <= size <= 256:
            raise ValueError(f"ICO dimensions must be between 1 and 256: {size}")
        if (len(payload) < 33 or payload[:8] != b"\x89PNG\r\n\x1a\n"
                or payload[12:16] != b"IHDR"
                or struct.unpack_from(">II", payload, 16) != (size, size)):
            raise ValueError(f"Expected {size}x{size} PNG: {path}")
        payloads.append((payload, size))
    offset = 6 + 16 * len(payloads)
    entries = bytearray()
    body = bytearray()

    for payload, size in payloads:
        dimension = 0 if size == 256 else size
        entries += struct.pack(
            "<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(payload), offset
        )
        body += payload
        offset += len(payload)

    output.write_bytes(struct.pack("<HHH", 0, 1, len(payloads)) + entries + body)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    sources = [
        (ICONSET / "icon_16x16.png", 16),
        (ICONSET / "icon_32x32.png", 32),
        (ICONSET / "icon_32x32@2x.png", 64),
        (ICONSET / "icon_128x128.png", 128),
        (ICONSET / "icon_256x256.png", 256),
    ]
    if not all(path.exists() for path, _ in sources):
        raise FileNotFoundError("Build the macOS iconset before generating the Windows icon")

    write_ico(sources, ASSETS / "icon.ico")
    shutil.copyfile(ICONSET / "icon_512x512.png", ASSETS / "icon.png")
    print(f"wrote {ASSETS / 'icon.ico'}")


if __name__ == "__main__":
    main()
