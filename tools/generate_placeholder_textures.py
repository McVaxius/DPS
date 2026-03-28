from __future__ import annotations

import struct
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
OUTPUT_DIR = PROJECT_ROOT / "DPS" / "Resources" / "Textures"

TEX_FORMAT_A8R8G8B8 = 5200
OPAQUE_BLACK_BGRA = bytes((0x00, 0x00, 0x00, 0xFF))


def build_dds_rgba(width: int, height: int, pixels: bytes) -> bytes:
    pitch = width * 4
    flags = 0x21007 | 0x8

    header = bytearray()
    header.extend(b"DDS ")
    header.extend(struct.pack("<I", 124))
    header.extend(struct.pack("<I", flags))
    header.extend(struct.pack("<I", height))
    header.extend(struct.pack("<I", width))
    header.extend(struct.pack("<I", pitch))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 1))
    header.extend(b"\x00" * 44)

    header.extend(struct.pack("<I", 32))
    header.extend(struct.pack("<I", 0x41))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 32))
    header.extend(struct.pack("<I", 0x00FF0000))
    header.extend(struct.pack("<I", 0x0000FF00))
    header.extend(struct.pack("<I", 0x000000FF))
    header.extend(struct.pack("<I", 0xFF000000))
    header.extend(struct.pack("<I", 0x1000))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 0))

    return bytes(header) + pixels


def build_tex_a8r8g8b8(width: int, height: int, pixels: bytes) -> bytes:
    header = bytearray()
    header.extend(struct.pack("<h", 0))
    header.extend(struct.pack("<h", 128))
    header.extend(struct.pack("<h", TEX_FORMAT_A8R8G8B8))
    header.extend(struct.pack("<h", 0))
    header.extend(struct.pack("<h", width))
    header.extend(struct.pack("<h", height))
    header.extend(struct.pack("<h", 1))
    header.extend(struct.pack("<h", 1))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 0))
    header.extend(struct.pack("<I", 80))
    header.extend(b"\x00" * (80 - len(header)))
    return bytes(header) + pixels


def parse_dds_header(data: bytes) -> tuple[int, int]:
    if data[:4] != b"DDS ":
        raise ValueError("Not a DDS file.")
    height = struct.unpack_from("<I", data, 12)[0]
    width = struct.unpack_from("<I", data, 16)[0]
    return width, height


def parse_tex_header(data: bytes) -> tuple[int, int, int]:
    format_code = struct.unpack_from("<h", data, 4)[0]
    width = struct.unpack_from("<h", data, 8)[0]
    height = struct.unpack_from("<h", data, 10)[0]
    return width, height, format_code


def write_black_texture(width: int, height: int) -> None:
    pixels = OPAQUE_BLACK_BGRA * (width * height)
    dds_bytes = build_dds_rgba(width, height, pixels)
    tex_bytes = build_tex_a8r8g8b8(width, height, pixels)

    stem = f"black_{width}x{height}_a8r8g8b8"
    dds_path = OUTPUT_DIR / f"{stem}.dds"
    tex_path = OUTPUT_DIR / f"{stem}.tex"

    dds_path.write_bytes(dds_bytes)
    tex_path.write_bytes(tex_bytes)

    dds_w, dds_h = parse_dds_header(dds_bytes)
    tex_w, tex_h, format_code = parse_tex_header(tex_bytes)

    print(f"Wrote {dds_path} ({len(dds_bytes)} bytes) width={dds_w} height={dds_h}")
    print(
        f"Wrote {tex_path} ({len(tex_bytes)} bytes) width={tex_w} height={tex_h} "
        f"format={format_code}"
    )


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    for size in (1, 16):
        write_black_texture(size, size)


if __name__ == "__main__":
    main()
