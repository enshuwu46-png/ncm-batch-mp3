#!/usr/bin/env python3
from __future__ import annotations

import struct
import sys
from pathlib import Path


CHUNKS = [
    ("icp4", "icon_16x16.png", 16),
    ("icp5", "icon_32x32.png", 32),
    ("icp6", "icon_32x32@2x.png", 64),
    ("ic07", "icon_128x128.png", 128),
    ("ic08", "icon_256x256.png", 256),
    ("ic09", "icon_512x512.png", 512),
    ("ic10", "icon_512x512@2x.png", 1024),
]


def main() -> int:
    iconset = Path(sys.argv[1])
    output = Path(sys.argv[2])
    body = bytearray()

    for code, filename, size in CHUNKS:
        data = (iconset / filename).read_bytes()
        if (len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n"
                or data[12:16] != b"IHDR"
                or struct.unpack_from(">II", data, 16) != (size, size)):
            raise ValueError(f"Expected {size}x{size} PNG for {code}: {filename}")
        body += code.encode("ascii")
        body += struct.pack(">I", len(data) + 8)
        body += data

    output.write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + body)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
