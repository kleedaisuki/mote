"""Check native-canvas PNG contrast without relying on platform image packages.

This intentionally examines an ASCII-only pixel region near the first two row
labels, away from colorful emoji. The check is a coarse regression sentinel,
not a substitute for visual QA, color-space-aware WCAG scoring, or shaping tests.
"""

import argparse
import json
import struct
import sys
import zlib
from pathlib import Path


PALETTES = {
    "mote-dark": ((31, 32, 35), (53, 90, 133), "light"),
    "mote-light": ((255, 255, 255), (191, 216, 246), "dark"),
    "mote-high-contrast-dark": ((0, 0, 0), (23, 79, 136), "light"),
}


def read_png(path: Path) -> tuple[int, int, list[bytes], int]:
    """Decode the simple noninterlaced 8-bit RGB/RGBA PNGs emitted by the probes."""
    data = path.read_bytes()
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"Not PNG: {path}")
    offset = 8
    width = height = channels = 0
    parts = []
    while offset < len(data):
        if offset + 12 > len(data):
            raise ValueError("Truncated PNG chunk")
        length = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        crc = struct.unpack_from(">I", data, offset + 8 + length)[0]
        if len(payload) != length or zlib.crc32(kind + payload) != crc:
            raise ValueError(f"Invalid PNG chunk {kind!r}")
        offset += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", payload
            )
            if depth != 8 or color not in (2, 6) or compression or filtering or interlace:
                raise ValueError("Unsupported PNG format")
            channels = 3 if color == 2 else 4
        elif kind == b"IDAT":
            parts.append(payload)
        elif kind == b"IEND":
            break
    if not width or not height or not parts:
        raise ValueError("PNG lacks image data")
    raw = zlib.decompress(b"".join(parts))
    stride = width * channels
    if len(raw) != height * (stride + 1):
        raise ValueError("Unexpected PNG scanline size")
    rows: list[bytes] = []
    previous = bytearray(stride)
    offset = 0
    for _ in range(height):
        filter_kind = raw[offset]
        offset += 1
        row = bytearray(raw[offset : offset + stride])
        offset += stride
        for i in range(stride):
            left = row[i - channels] if i >= channels else 0
            above = previous[i]
            upper_left = previous[i - channels] if i >= channels else 0
            if filter_kind == 1:
                row[i] = (row[i] + left) & 255
            elif filter_kind == 2:
                row[i] = (row[i] + above) & 255
            elif filter_kind == 3:
                row[i] = (row[i] + ((left + above) // 2)) & 255
            elif filter_kind == 4:
                p = left + above - upper_left
                distances = (abs(p - left), abs(p - above), abs(p - upper_left))
                predictor = (left, above, upper_left)[distances.index(min(distances))]
                row[i] = (row[i] + predictor) & 255
            elif filter_kind != 0:
                raise ValueError(f"Unsupported PNG filter {filter_kind}")
        rows.append(bytes(row))
        previous = row
    return width, height, rows, channels


def rgb(rows: list[bytes], channels: int, x: int, y: int) -> tuple[int, int, int]:
    """Read a color pixel, deliberately ignoring alpha in these opaque captures."""
    start = x * channels
    return tuple(rows[y][start : start + 3])  # type: ignore[return-value]


def near(actual: tuple[int, int, int], expected: tuple[int, int, int]) -> bool:
    """Allow one or two RGB levels of platform color-profile rounding."""
    return all(abs(a - b) <= 2 for a, b in zip(actual, expected))


def inspect(before: Path, selected: Path, theme: str) -> dict[str, object]:
    """Require distinct theme surfaces and legible ASCII text in both states."""
    background, selection, polarity = PALETTES[theme]
    width, height, before_rows, before_channels = read_png(before)
    selected_width, selected_height, selected_rows, selected_channels = read_png(selected)
    if min(width, selected_width) < 160 or min(height, selected_height) < 80:
        raise ValueError("Canvas captures are unexpectedly small")
    sampled_background = rgb(before_rows, before_channels, width - 10, 10)
    if not near(sampled_background, background):
        raise ValueError(f"Theme background {sampled_background} != {background}")

    def text_pixels(rows: list[bytes], channels: int, first: int, last: int) -> int:
        count = 0
        for y in range(first, last):
            for x in range(105):
                red, green, blue = rgb(rows, channels, x, y)
                mean = (red + green + blue) / 3
                if polarity == "light" and mean >= 150 or polarity == "dark" and mean <= 100:
                    count += 1
        return count

    normal_count = text_pixels(before_rows, before_channels, 0, 40)
    selection_pixels = [
        (x, y)
        for y in range(80)
        for x in range(150)
        if near(rgb(selected_rows, selected_channels, x, y), selection)
    ]
    selected_surface_count = len(selection_pixels)
    if not selection_pixels:
        raise ValueError("Selection color absent from first rows")
    # The first selection-colored scanline belongs to the first highlighted
    # ASCII row in this fixed fixture. Restrict foreground sampling to that
    # one row's 16-pixel band; later unselected rows cannot mask bad glyphs.
    first_selected_y = min(y for _, y in selection_pixels)
    selected_count = text_pixels(selected_rows, selected_channels,
                                 first_selected_y, first_selected_y + 16)
    first_band_surface = sum(y < first_selected_y + 16 for _, y in selection_pixels)
    if normal_count < 50 or selected_count < 50 or first_band_surface < 50:
        raise ValueError(
            f"Theme text/selection contrast sentinel failed: normal={normal_count} "
            f"selected={selected_count} first_band_surface={first_band_surface}"
        )
    return {
        "normal_text_pixels": normal_count,
        "selected_text_pixels": selected_count,
        "selection_surface_pixels": selected_surface_count,
        "first_selection_band_pixels": first_band_surface,
        "sampled_background": sampled_background,
    }


def main() -> int:
    """Run one theme check and print machine-readable evidence."""
    parser = argparse.ArgumentParser()
    parser.add_argument("before", type=Path)
    parser.add_argument("selected", type=Path)
    parser.add_argument("theme", choices=PALETTES)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.before, args.selected, args.theme), sort_keys=True))
        return 0
    except (OSError, ValueError, zlib.error) as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
