#!/usr/bin/env python3
"""
Generates src/ElwMeteo.App/Assets/elw-meteo.ico.

The icon is drawn from primitives rather than exported from a design tool so it
stays reproducible and reviewable in the repository: run this script and the
committed .ico is byte-for-byte reproduced.

Motif: the application's dark surface, a blue radar sweep, and the amber wind
arrow that runs through the whole UI as the "direction the air travels" cue.

No third-party dependencies — PNGs are assembled with zlib and struct, and the
ICO container embeds them directly (supported since Windows Vista).
"""

import math
import pathlib
import struct
import zlib

# Palette straight from Themes/Theme.xaml.
BACKGROUND = (0x0E, 0x11, 0x16)
PANEL = (0x1F, 0x26, 0x30)
ACCENT = (0x3B, 0x82, 0xF6)
AMBER = (0xF5, 0x9E, 0x0B)
INK = (0xE6, 0xED, 0xF3)

SIZES = [256, 128, 64, 48, 32, 16]

# Supersampling factor: everything is drawn large and averaged down, which is
# what keeps the diagonals clean at 16 px without any hinting logic.
SS = 4


def blend(dst, src, alpha):
    """Alpha-composite a single pixel tuple."""
    return tuple(int(round(d + (s - d) * alpha)) for d, s in zip(dst, src))


def render(size):
    """Draws one square icon at `size` pixels, returning RGBA rows."""
    n = size * SS
    centre = (n - 1) / 2.0
    # Accumulate at supersampled resolution, then box-filter down.
    pixels = [[(0, 0, 0, 0.0) for _ in range(n)] for _ in range(n)]

    radius_outer = n * 0.46
    corner = n * 0.22

    for y in range(n):
        for x in range(n):
            dx = x - centre
            dy = y - centre

            # --- rounded-square body -----------------------------------
            ax = abs(dx) - (n / 2.0 - corner)
            ay = abs(dy) - (n / 2.0 - corner)
            if ax > 0 and ay > 0:
                inside = math.hypot(ax, ay) <= corner
            else:
                inside = abs(dx) <= n / 2.0 and abs(dy) <= n / 2.0

            if not inside:
                continue

            colour = BACKGROUND
            alpha = 1.0

            distance = math.hypot(dx, dy)

            # --- radar rings -------------------------------------------
            for ring in (0.42, 0.29, 0.16):
                r = radius_outer * ring / 0.46
                width = max(n * 0.012, 1.0)
                if abs(distance - r) <= width:
                    colour = PANEL if ring != 0.42 else ACCENT
                    if ring == 0.42:
                        colour = ACCENT

            # --- radar sweep wedge, fading with angle ------------------
            if distance <= radius_outer * 0.92:
                angle = math.degrees(math.atan2(-dy, dx)) % 360.0
                # A 70-degree wedge opening up and to the right.
                sweep = (angle - 35.0) % 360.0
                if sweep <= 70.0:
                    fade = 1.0 - sweep / 70.0
                    colour = blend(colour, ACCENT, 0.30 * fade)

            # --- wind arrow, pointing north-east (downwind cue) --------
            # Rotate into arrow space: the shaft runs along +v.
            theta = math.radians(35.0)
            u = dx * math.cos(theta) + dy * math.sin(theta)
            v = -dx * math.sin(theta) + dy * math.cos(theta)

            shaft_half = n * 0.030
            shaft_from = -n * 0.055
            shaft_to = n * 0.30
            head_from = -n * 0.32
            head_to = shaft_from

            in_shaft = abs(u) <= shaft_half and shaft_from <= v <= shaft_to
            # Triangular head: half-width shrinks to zero at the tip.
            if head_from <= v <= head_to:
                span = (v - head_from) / (head_to - head_from)
                in_head = abs(u) <= n * 0.105 * span
            else:
                in_head = False

            if in_shaft or in_head:
                colour = AMBER

            # --- centre dot --------------------------------------------
            if distance <= n * 0.022:
                colour = INK

            pixels[y][x] = (colour[0], colour[1], colour[2], alpha)

    # --- box filter down to the requested size --------------------------
    rows = []
    for y in range(size):
        row = bytearray()
        for x in range(size):
            r = g = b = a = 0.0
            for sy in range(SS):
                for sx in range(SS):
                    pr, pg, pb, pa = pixels[y * SS + sy][x * SS + sx]
                    r += pr * pa
                    g += pg * pa
                    b += pb * pa
                    a += pa

            total = SS * SS
            if a > 0:
                # Un-premultiply so edge pixels keep their colour.
                row += bytes((
                    int(round(r / a)),
                    int(round(g / a)),
                    int(round(b / a)),
                    int(round(a / total * 255)),
                ))
            else:
                row += bytes((0, 0, 0, 0))
        rows.append(bytes(row))

    return rows


def png(rows, size):
    """Wraps RGBA rows into a PNG byte string."""
    raw = b"".join(b"\x00" + row for row in rows)

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main():
    images = []
    for size in SIZES:
        print(f"  rendering {size}x{size} ...")
        images.append((size, png(render(size), size)))

    # ICO: header, one directory entry per image, then the payloads.
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)

    entries = b""
    payloads = b""
    for size, data in images:
        entries += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,   # 0 means 256
            0 if size >= 256 else size,
            0, 0, 1, 32, len(data), offset)
        payloads += data
        offset += len(data)

    target = (pathlib.Path(__file__).resolve().parent.parent
              / "src" / "ElwMeteo.App" / "Assets" / "elw-meteo.ico")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(header + entries + payloads)

    print(f"wrote {target} ({target.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
