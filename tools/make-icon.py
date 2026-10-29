# /// script
# requires-python = ">=3.9"
# dependencies = ["pillow"]
# ///
"""Draw the Thunderstore icon: a snowy peak with a VR headset marked "XR" in front, 256x256 PNG.

Original geometric art (no game assets, no fonts), drawn at 4x and downsampled for smooth edges.
Usage: uv run tools/make-icon.py assets/icon.png
"""
import sys

from PIL import Image, ImageDraw

SIZE = 256
S = 4  # supersampling factor
W = SIZE * S

NAVY = (16, 27, 52)
SKY_TOP = (28, 52, 104)
SKY_BOTTOM = (58, 112, 176)
ROCK_BACK = (52, 70, 104)
ROCK = (92, 112, 148)
ROCK_SHADE = (70, 88, 122)
SNOW = (240, 246, 255)
HEADSET = (255, 138, 61)
HEADSET_DARK = (214, 98, 30)


def p(x: float, y: float) -> tuple[int, int]:
    """Map 0..256 design units to supersampled pixels."""
    return (round(x * S), round(y * S))


def main() -> None:
    out = sys.argv[1]
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    # Sky: vertical gradient inside a rounded square.
    sky = Image.new("RGBA", (W, W))
    sd = ImageDraw.Draw(sky)
    for y in range(W):
        t = y / (W - 1)
        sd.line([(0, y), (W, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(SKY_TOP, SKY_BOTTOM)))
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=40 * S, fill=255)
    img.paste(sky, (0, 0), mask)

    d = ImageDraw.Draw(img)
    # Back ridge, then the main peak with a shaded right face and a jagged snow cap.
    d.polygon([p(-10, 200), p(62, 92), p(118, 170), p(118, 200)], fill=ROCK_BACK)
    d.polygon([p(28, 200), p(140, 34), p(252, 200)], fill=ROCK)
    d.polygon([p(140, 34), p(252, 200), p(150, 200), p(156, 120)], fill=ROCK_SHADE)
    d.polygon([p(140, 34), p(176, 87), p(162, 80), p(150, 94), p(138, 82), p(124, 96), p(114, 73)], fill=SNOW)

    # Ground under the headset so the peak sits on something.
    d.rectangle([p(0, 196), p(256, 256)], fill=NAVY)

    # Headset: strap, visor body, nose notch.
    d.rounded_rectangle([p(14, 160), p(242, 176)], radius=6 * S, fill=HEADSET_DARK)
    d.rounded_rectangle([p(36, 128), p(220, 222)], radius=26 * S, fill=HEADSET)
    d.pieslice([p(108, 202), p(148, 242)], 180, 360, fill=NAVY)

    # "XR" as strokes, so no font is needed.
    stroke = 13 * S
    d.line([p(62, 148), p(100, 192)], fill=NAVY, width=stroke)
    d.line([p(100, 148), p(62, 192)], fill=NAVY, width=stroke)
    d.line([p(124, 145), p(124, 195)], fill=NAVY, width=stroke)  # R stem
    d.line([p(118, 151), p(150, 151)], fill=NAVY, width=stroke)  # bowl top
    d.line([p(118, 175), p(150, 175)], fill=NAVY, width=stroke)  # bowl bottom
    d.arc([p(138, 145), p(168, 181)], 270, 90, fill=NAVY, width=stroke)  # bowl curve
    d.line([p(142, 175), p(166, 195)], fill=NAVY, width=stroke)  # leg

    # Keep everything inside the rounded corners.
    alpha = Image.new("L", (W, W), 0)
    alpha.paste(img.getchannel("A"), (0, 0), mask)
    img.putalpha(alpha)

    img.resize((SIZE, SIZE), Image.LANCZOS).save(out, optimize=True)


if __name__ == "__main__":
    main()
