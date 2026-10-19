"""Write a 256x256 placeholder icon (PNG, stdlib only): a white mountain peak on a blue sky.

Usage: python make-icon.py OUT.png
TODO: replace assets/icon.png with real art.
"""
import struct
import sys
import zlib

SIZE = 256


def pixel(x: int, y: int) -> bytes:
    # Peak: triangle with apex at (128, 48) and base at y = 224.
    half_width = (y - 48) * 0.75
    if 48 <= y <= 224 and abs(x - 128) <= half_width:
        if y < 96:
            return bytes((245, 248, 255))  # snow cap
        return bytes((70, 78, 96))  # rock
    if y > 224:
        return bytes((40, 46, 58))  # ground
    shade = 120 + y // 3
    return bytes((40, 90, min(255, shade + 60)))  # sky gradient


def chunk(tag: bytes, data: bytes) -> bytes:
    body = tag + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)


def main() -> None:
    out = sys.argv[1]
    raw = b"".join(b"\x00" + b"".join(pixel(x, y) for x in range(SIZE)) for y in range(SIZE))
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    with open(out, "wb") as f:
        f.write(png)


if __name__ == "__main__":
    main()
