#!/usr/bin/env python3
from __future__ import annotations

import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "windows" / "assets"
INK = (29, 29, 27)
PAPER = (247, 245, 240)
LINE = (203, 201, 194)


class Canvas:
    def __init__(self, width: int, height: int) -> None:
        self.width = width
        self.height = height
        self.pixels = bytearray(PAPER * (width * height))

    def pixel(self, x: int, y: int, color: tuple[int, int, int]) -> None:
        if 0 <= x < self.width and 0 <= y < self.height:
            index = (y * self.width + x) * 3
            self.pixels[index:index + 3] = bytes(color)

    def rectangle(self, left: int, top: int, right: int, bottom: int, color: tuple[int, int, int]) -> None:
        for y in range(max(0, top), min(self.height, bottom + 1)):
            for x in range(max(0, left), min(self.width, right + 1)):
                self.pixel(x, y, color)

    def line(self, x1: int, y1: int, x2: int, y2: int, color: tuple[int, int, int], width: int = 1) -> None:
        steps = max(abs(x2 - x1), abs(y2 - y1), 1)
        for step in range(steps + 1):
            x = round(x1 + (x2 - x1) * step / steps)
            y = round(y1 + (y2 - y1) * step / steps)
            self.rectangle(x - width // 2, y - width // 2, x + width // 2, y + width // 2, color)

    def ellipse(self, left: int, top: int, right: int, bottom: int, color: tuple[int, int, int]) -> None:
        radius_x = max(1, (right - left) / 2)
        radius_y = max(1, (bottom - top) / 2)
        center_x = (left + right) / 2
        center_y = (top + bottom) / 2
        for y in range(top, bottom + 1):
            for x in range(left, right + 1):
                if ((x - center_x) / radius_x) ** 2 + ((y - center_y) / radius_y) ** 2 <= 1:
                    self.pixel(x, y, color)

    def write_bmp(self, output: Path) -> None:
        row_size = (self.width * 3 + 3) & ~3
        image_size = row_size * self.height
        header = struct.pack("<2sIHHI", b"BM", 54 + image_size, 0, 0, 54)
        dib = struct.pack("<IIIHHIIIIII", 40, self.width, self.height, 1, 24, 0, image_size, 2835, 2835, 0, 0)
        rows = bytearray()
        for y in reversed(range(self.height)):
            row = bytearray()
            for x in range(self.width):
                index = (y * self.width + x) * 3
                red, green, blue = self.pixels[index:index + 3]
                row += bytes((blue, green, red))
            row += b"\0" * (row_size - len(row))
            rows += row
        output.write_bytes(header + dib + rows)


def mark(canvas: Canvas, x: int, y: int, size: int) -> None:
    canvas.rectangle(x, y, x + size, y + size, INK)
    disc = (x + int(size * 0.18), y + int(size * 0.28), x + int(size * 0.56), y + int(size * 0.66))
    canvas.ellipse(*disc, PAPER)
    center = (x + int(size * 0.32), y + int(size * 0.42), x + int(size * 0.43), y + int(size * 0.53))
    canvas.ellipse(*center, INK)
    document = (x + int(size * 0.62), y + int(size * 0.34), x + int(size * 0.84), y + int(size * 0.62))
    canvas.rectangle(*document, PAPER)
    for index in range(3):
        top = y + int(size * (0.405 + index * 0.055))
        canvas.line(x + int(size * 0.67), top, x + int(size * 0.79), top, INK, max(1, int(size * 0.018)))
    canvas.line(x + int(size * 0.53), y + int(size * 0.49), x + int(size * 0.63), y + int(size * 0.49), PAPER, max(2, int(size * 0.035)))


def write_welcome() -> None:
    canvas = Canvas(164, 314)
    canvas.rectangle(0, 0, 163, 5, INK)
    mark(canvas, 25, 32, 112)
    canvas.line(27, 178, 137, 178, LINE)
    canvas.rectangle(27, 194, 88, 198, INK)
    canvas.rectangle(27, 207, 119, 210, LINE)
    canvas.rectangle(27, 219, 104, 222, LINE)
    canvas.write_bmp(ASSETS / "installer-welcome.bmp")


def write_header() -> None:
    canvas = Canvas(150, 57)
    canvas.rectangle(0, 53, 149, 56, INK)
    mark(canvas, 91, 6, 44)
    canvas.write_bmp(ASSETS / "installer-header.bmp")


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    write_welcome()
    write_header()


if __name__ == "__main__":
    main()
