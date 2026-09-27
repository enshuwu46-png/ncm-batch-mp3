#!/usr/bin/env python3
"""Check exported icon dimensions against the ICNS and ICO directory entries."""
import importlib.util
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
ICONSET = ROOT / ".build/AppIcon.iconset"
ICNS = ROOT / "dist/NCM批量转MP3.app/Contents/Resources/AppIcon.icns"
ICO = ROOT / "windows/assets/icon.ico"


def png_size(data):
    assert data[:8] == b"\x89PNG\r\n\x1a\n" and data[12:16] == b"IHDR"
    return struct.unpack_from(">II", data, 16)


class IconAssetsTests(unittest.TestCase):
    def test_iconset_has_exact_pixel_dimensions(self):
        files = list(ICONSET.glob("*.png"))
        self.assertEqual(len(files), 10)
        for path in files:
            size = int(path.stem.split("_")[1].split("x")[0])
            size *= 2 if "@2x" in path.stem else 1
            self.assertEqual(png_size(path.read_bytes()), (size, size), path.name)
        self.assertEqual(png_size((ROOT / "windows/assets/icon.png").read_bytes()), (512, 512))

    def test_icns_directory_matches_embedded_images(self):
        data = ICNS.read_bytes()
        self.assertEqual(data[:4], b"icns")
        self.assertEqual(struct.unpack_from(">I", data, 4)[0], len(data))
        sizes = {b"icp4": 16, b"icp5": 32, b"icp6": 64, b"ic07": 128,
                 b"ic08": 256, b"ic09": 512, b"ic10": 1024}
        seen = set()
        offset = 8
        while offset < len(data):
            code, length = struct.unpack_from(">4sI", data, offset)
            self.assertGreater(length, 8)
            self.assertLessEqual(offset + length, len(data))
            self.assertNotIn(code, seen)
            self.assertEqual(png_size(data[offset + 8:offset + length]), (sizes[code], sizes[code]))
            seen.add(code)
            offset += length
        self.assertEqual(seen, set(sizes))
        self.assertEqual(offset, len(data))

    def test_ico_directory_matches_embedded_images(self):
        data = ICO.read_bytes()
        self.assertEqual(struct.unpack_from("<HHH", data), (0, 1, 5))
        offset = 6 + 16 * 5
        for index, size in enumerate([16, 32, 64, 128, 256]):
            w, h, colors, reserved, planes, bits, length, start = struct.unpack_from(
                "<BBBBHHII", data, 6 + 16 * index)
            self.assertEqual((w or 256, h or 256), (size, size))
            self.assertEqual((colors, reserved, planes, bits), (0, 0, 1, 32))
            self.assertEqual(start, offset)
            self.assertEqual(png_size(data[start:start + length]), (size, size))
            offset += length
        self.assertEqual(offset, len(data))

    def test_ico_rejects_mismatched_or_oversized_images(self):
        spec = importlib.util.spec_from_file_location("make_icon", ROOT / "windows/scripts/make_icon.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "icon.ico"
            for source, size in [("icon_32x32.png", 16), ("icon_512x512.png", 512)]:
                with self.assertRaises(ValueError):
                    module.write_ico([(ICONSET / source, size)], output)
                self.assertFalse(output.exists())

    def test_icns_rejects_retina_scaled_images(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            (folder / "icon_16x16.png").write_bytes((ICONSET / "icon_32x32.png").read_bytes())
            output = folder / "icon.icns"
            result = subprocess.run([sys.executable, str(ROOT / "scripts/make_icns.py"),
                                     str(folder), str(output)], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Expected 16x16 PNG", result.stderr)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
