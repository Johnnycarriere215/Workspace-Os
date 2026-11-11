#!/usr/bin/env python3
"""Generate the animated README previews for the current Geometry Dash bar.

The images are drawn from the live TopBarWindow.xaml palette and layout:
void-black translucent surface, electric cyan active slot, GD fish, spike row,
centred clock, module chips and a bottom energy hairline.  Unlike the old
rsvg/ImageMagick generator this needs only Pillow, so it works on a stock
Windows machine.

Usage (from the repo root):
    python docs/assets/make_readme_assets.py

Outputs:
    docs/assets/bar-preview.gif  - hero bar animation
    docs/assets/winw-demo.gif     - Win+W close-window demo in the same theme
"""

import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_BAR = os.path.join(HERE, "bar-preview.gif")
OUT_WINW = os.path.join(HERE, "winw-demo.gif")

# ---------------------------------------------------------------------------
# Palette (mirrors src/WorkspaceOS/UI/TopBarWindow.xaml)
# ---------------------------------------------------------------------------
CYAN = (0, 224, 207, 255)
CYAN_SOFT = (12, 143, 136, 255)
INK = (232, 248, 245, 255)
MUTED = (127, 230, 221, 255)
DIM = (122, 160, 156, 255)
BORDER = (18, 58, 55, 255)
SURFACE = (8, 16, 18, 255)
VOID = (2, 6, 7, 255)
HAIR = (14, 74, 69, 255)

MONO = "C:/Windows/Fonts/consola.ttf"
MONO_BOLD = "C:/Windows/Fonts/consolab.ttf"
UI = "C:/Windows/Fonts/seguisb.ttf"


def font(path, size):
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.load_default()


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(4))


def rgba(color, alpha):
    return (color[0], color[1], color[2], max(0, min(255, int(alpha))))


# ---------------------------------------------------------------------------
# Desktop / wallpaper
# ---------------------------------------------------------------------------
def make_desktop(w, h, pattern=True):
    """A dark energy-glass desktop with just enough geometry to read as the GD theme."""
    im = Image.new("RGBA", (w, h), VOID)
    d = ImageDraw.Draw(im)

    for y in range(h):
        t = y / max(1, h - 1)
        c = (
            round(2 + 4 * t),
            round(6 + 13 * t),
            round(7 + 10 * t),
            255,
        )
        d.line((0, y, w, y), fill=c)

    # Cyan bloom behind the top-right of the bar.
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((w * 0.52, -h * 0.95, w * 1.15, h * 0.9), fill=(0, 224, 207, 24))
    gd.ellipse((w * 0.02, -h * 0.7, w * 0.38, h * 0.55), fill=(0, 224, 207, 10))
    glow = glow.filter(ImageFilter.GaussianBlur(max(18, w // 32)))
    im = Image.alpha_composite(im, glow)

    if pattern:
        d = ImageDraw.Draw(im)
        # Hard, low-contrast energy shards in the wallpaper.
        shards = [
            ((w * 0.62, 0), (w * 0.77, 0), (w * 0.54, h * 0.58)),
            ((w * 0.78, 0), (w * 0.93, 0), (w * 0.67, h * 0.48)),
            ((w * 0.32, h * 0.52), (w * 0.43, h * 0.38), (w * 0.40, h * 0.9)),
        ]
        for poly in shards:
            # Opaque, pre-blended colours: ImageDraw writes RGBA values directly
            # instead of compositing, so a low alpha here would become bright
            # cyan once the GIF is converted to an opaque frame.
            d.polygon(poly, fill=(3, 16, 16, 255))
        # Faint horizon / grid lines, also pre-blended.
        for i in range(1, 7):
            y = round(h * 0.52 + i * h * 0.075)
            d.line((0, y, w, y), fill=(5, 24, 23, 255), width=1)
        for x in range(0, w, max(90, w // 12)):
            d.line((x, h * 0.44, x + w * 0.12, h), fill=(4, 20, 19, 255), width=1)

    # Slight vignette keeps the bar and windows readable.
    shade = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    sd.rectangle((0, 0, w, h), outline=(0, 0, 0, 0))
    for i in range(30):
        a = int(i * 1.4)
        sd.rectangle((i, i, w - 1 - i, h - 1 - i), outline=(0, 0, 0, a))
    return Image.alpha_composite(im, shade)


def draw_window(im, box, title, lines, active=False, alpha=255, accent=None):
    """A restrained editor/terminal window; no tiling, just real desktop windows."""
    x0, y0, x1, y1 = box
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    accent = accent or (CYAN if active else BORDER)

    # Shadow, then glass surface.
    d.rounded_rectangle((x0 + 5, y0 + 7, x1 + 5, y1 + 7), 7, fill=(0, 0, 0, 80))
    d.rounded_rectangle(box, 7, fill=(5, 13, 13, 244), outline=accent, width=2 if active else 1)
    d.rounded_rectangle((x0 + 1, y0 + 1, x1 - 1, y0 + 30), 5, fill=(8, 19, 20, 248))
    d.line((x0 + 1, y0 + 30, x1 - 1, y0 + 30), fill=BORDER, width=1)

    # Window dots and title.
    d.ellipse((x0 + 12, y0 + 11, x0 + 19, y0 + 18), fill=(36, 78, 74, 255))
    d.ellipse((x0 + 24, y0 + 11, x0 + 31, y0 + 18), fill=(26, 57, 54, 255))
    d.ellipse((x0 + 36, y0 + 11, x0 + 43, y0 + 18), fill=(20, 45, 43, 255))
    d.text((x0 + 52, y0 + 10), title, font=font(UI, 11), fill=(202, 235, 230, 255))
    if active:
        d.line((x0 + 52, y0 + 27, x1 - 14, y0 + 27), fill=(0, 224, 207, 50), width=1)

    body_font = font(MONO, 11)
    small = font(MONO, 10)
    y = y0 + 48
    for i, line in enumerate(lines):
        color = INK if i == 0 else (135, 184, 178, 255)
        if line.startswith("PS"):
            color = (202, 235, 230, 255)
        if line.startswith("#"):
            color = (127, 230, 221, 255)
        d.text((x0 + 18, y), line, font=body_font if i == 0 else small, fill=color)
        y += 20
        if y > y1 - 18:
            break

    if alpha < 255:
        a = layer.getchannel("A").point(lambda v: int(v * alpha / 255))
        layer.putalpha(a)
    im.alpha_composite(layer)


# ---------------------------------------------------------------------------
# Bar renderer
# ---------------------------------------------------------------------------
def fish(d, x, y, scale=1.0):
    def pt(p):
        return (x + p[0] * scale, y + p[1] * scale)

    body = [pt(p) for p in ((12, 4), (31, 4), (43, 12), (31, 20), (12, 20), (4, 12))]
    tail = [pt(p) for p in ((12, 4), (3, 0), (6, 12), (3, 24), (12, 20))]
    d.polygon(body, fill=(242, 255, 253, 255), outline=(4, 16, 15, 255))
    d.line(body + [body[0]], fill=(4, 16, 15, 255), width=max(1, int(1.4 * scale)))
    d.polygon(tail, fill=(242, 255, 253, 255), outline=(4, 16, 15, 255))
    d.line(tail + [tail[0]], fill=(4, 16, 15, 255), width=max(1, int(1.4 * scale)))
    eye = (x + 34.5 * scale, y + 10 * scale)
    r = 2.2 * scale
    d.ellipse((eye[0] - r, eye[1] - r, eye[0] + r, eye[1] + r),
              fill=(4, 16, 15, 255), outline=(0, 224, 207, 255), width=1)


def draw_bar(im, active_pos=1.0, occupied=(0, 1), metrics=None,
             clock_text="2:37 PM", date_text="Wed 11 Nov", opacity=1.0,
             bar_h=40, compact=False, key_flash=0.0, clock_alpha=1.0):
    """Draw the live bar over `im`.  active_pos is a zero-based, possibly fractional index."""
    w = im.width
    metrics = metrics or {}
    bar = Image.new("RGBA", (w, bar_h), (0, 0, 0, 0))
    d = ImageDraw.Draw(bar)

    # Translucent energy-glass surface.
    for y in range(bar_h):
        t = y / max(1, bar_h - 1)
        c = (
            round(4 + 3 * t),
            round(7 + 10 * t),
            round(8 + 7 * t),
            round(236 - 58 * t),
        )
        d.line((0, y, w, y), fill=c)
    d.line((0, 0, w, 0), fill=(37, 102, 96, 70))

    # Left: GD fish mark.
    # A tiny native glow under the fish (draw again through a blurred layer).
    gl = Image.new("RGBA", (w, bar_h), (0, 0, 0, 0))
    gld = ImageDraw.Draw(gl)
    fish(gld, 9, 11, 0.7)
    gl = gl.filter(ImageFilter.GaussianBlur(5))
    bar.alpha_composite(gl)
    fish(d, 9, 11, 0.7)

    # Workspaces.
    ws_w, ws_gap = 28, 5
    ws_x = 53
    ws_y = (bar_h - 20) // 2
    pill_x = ws_x + active_pos * (ws_w + ws_gap) - 2
    pill_w, pill_h = ws_w + 4, 22
    pill_y = (bar_h - pill_h) // 2

    glow = Image.new("RGBA", (w, bar_h), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.rounded_rectangle((pill_x - 3, pill_y - 2, pill_x + pill_w + 3, pill_y + pill_h + 2),
                         5, fill=(0, 224, 207, 120))
    glow = glow.filter(ImageFilter.GaussianBlur(8))
    bar.alpha_composite(glow)
    d = ImageDraw.Draw(bar)
    d.rounded_rectangle((pill_x, pill_y, pill_x + pill_w, pill_y + pill_h),
                        4, fill=CYAN)

    ws_font = font(MONO_BOLD, 11)
    for i in range(5):
        x = ws_x + i * (ws_w + ws_gap)
        base = INK if i in occupied else DIM
        dist = abs(i - active_pos)
        active_w = max(0.0, 1.0 - dist)
        color = mix(base, (4, 16, 15, 255), active_w)
        d.text((x + ws_w / 2, bar_h / 2 + 0.5), str(i + 1), font=ws_font,
               fill=color, anchor="mm")

    # Clock (centred, with the 12-hour date line).
    clock = f"{clock_text}   {date_text}"
    cfont = font(MONO_BOLD, 12)
    tw = d.textlength(clock, font=cfont)
    cx = w / 2
    # Soft cyan bloom behind the clock.
    bloom = Image.new("RGBA", (w, bar_h), (0, 0, 0, 0))
    bd = ImageDraw.Draw(bloom)
    bd.text((cx, bar_h / 2 + 0.5), clock, font=cfont,
            fill=(0, 224, 207, int(90 * clock_alpha)), anchor="mm")
    bloom = bloom.filter(ImageFilter.GaussianBlur(5))
    bar.alpha_composite(bloom)
    d = ImageDraw.Draw(bar)
    d.text((cx, bar_h / 2 + 0.5), clock, font=cfont,
           fill=(232, 248, 245, int(235 * clock_alpha)), anchor="mm")

    # Right: spikes, compact modules and the two vector icon chips.
    right = w - 9
    icon_w = 29
    icon_gap = 5
    right -= icon_w * 2 + icon_gap
    d.rounded_rectangle((right, (bar_h - 23) / 2, right + icon_w, (bar_h + 23) / 2),
                        3, fill=(8, 16, 18, 238), outline=BORDER, width=1)
    # monitor icon
    mx, my = right + 7, (bar_h - 12) / 2
    d.rectangle((mx, my, mx + 14, my + 9), outline=MUTED, width=1)
    d.line((mx + 5, my + 12, mx + 10, my + 12), fill=MUTED, width=1)
    right += icon_w + icon_gap
    d.rounded_rectangle((right, (bar_h - 23) / 2, right + icon_w, (bar_h + 23) / 2),
                        3, fill=(8, 16, 18, 238), outline=BORDER, width=1)
    sx, sy = right + 7, (bar_h - 12) / 2
    d.line((sx, sy + 3, sx + 14, sy + 3), fill=MUTED, width=1)
    d.line((sx, sy + 10, sx + 14, sy + 10), fill=MUTED, width=1)
    d.ellipse((sx + 3, sy + 1, sx + 7, sy + 5), fill=MUTED)
    d.ellipse((sx + 9, sy + 8, sx + 13, sy + 12), fill=MUTED)

    # spikes
    right -= 24
    for i, col in enumerate((CYAN_SOFT, CYAN, CYAN_SOFT)):
        bx = right + i * 7
        d.polygon(((bx, bar_h / 2 + 4), (bx + 3.5, bar_h / 2 - 5),
                   (bx + 7, bar_h / 2 + 4)), fill=col)
    right -= 8

    values = [
        ("CPU", metrics.get("cpu", "45%"), 55),
        ("RAM", metrics.get("ram", "10.7G"), 70),
        ("GPU", metrics.get("gpu", "1%"), 51),
        ("NET", metrics.get("net", "↓ 6M"), 60),
        ("BAT", metrics.get("bat", "70%"), 60),
    ]
    if compact:
        values = values[:3] + values[-1:]
    total = sum(v[2] for v in values) + 4 * (len(values) - 1)
    mx = right - total - 4
    if mx < 370:
        mx = 370
    for i, (label, value, chip_w) in enumerate(values):
        x = mx + sum(v[2] + 4 for v in values[:i])
        d.rounded_rectangle((x, (bar_h - 23) / 2, x + chip_w, (bar_h + 23) / 2),
                            3, fill=(8, 16, 18, 238), outline=BORDER, width=1)
        d.text((x + 6, bar_h / 2), label, font=font(MONO, 8), fill=(111, 216, 207, 235),
               anchor="lm")
        d.text((x + chip_w - 5, bar_h / 2), value, font=font(MONO_BOLD, 9),
               fill=(232, 248, 245, 255), anchor="rm")

    # Energy hairline with a faint cyan bloom.
    hline = Image.new("RGBA", (w, bar_h), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hline)
    hd.line((0, bar_h - 1, w, bar_h - 1), fill=(0, 224, 207, 100), width=1)
    hline = hline.filter(ImageFilter.GaussianBlur(2))
    bar.alpha_composite(hline)
    d = ImageDraw.Draw(bar)
    d.line((0, bar_h - 1, w, bar_h - 1), fill=HAIR, width=1)
    d.line((0, bar_h - 1, w, bar_h - 1), fill=(0, 224, 207, 45), width=1)

    # Key flash is only used by the Win+W demo; it tints the bar edge.
    if key_flash:
        d.rectangle((0, 0, w, 2), fill=rgba(CYAN, 120 * key_flash))

    if opacity < 1:
        a = bar.getchannel("A").point(lambda v: int(v * max(0, min(1, opacity))))
        bar.putalpha(a)
    im.alpha_composite(bar)


# ---------------------------------------------------------------------------
# Hero animation
# ---------------------------------------------------------------------------
def hero_frame(f):
    w, h = 1200, 240
    im = make_desktop(w, h)

    draw_window(im, (46, 60, 704, 242), "notes.txt — Editor", [
        "# WorkspaceOS",
        "Scratchpad is ready.",
        "Ctrl+Win+S stashes the focused window.",
        "Win+S opens it from any workspace.",
        "The bar stays out of the way.",
    ], active=True)
    draw_window(im, (726, 60, 1154, 242), "PowerShell", [
        "PS C:\\Users\\jmoney> workspaceos status",
        "workspace 2 of 5  ·  focus: notes.txt",
        "scratchpad: ready",
        "PS C:\\Users\\jmoney> _",
    ], active=False)

    # Timeline: active 2 -> 3 -> 2, with the real 240-260ms slide.
    if f < 8:
        pos = 1.0
        fade = ease(f / 7)
    elif f < 20:
        pos = 1.0
        fade = 1.0
    elif f < 32:
        pos = 1.0 + ease((f - 20) / 11)
        fade = 1.0
    elif f < 44:
        pos = 2.0
        fade = 1.0
    elif f < 56:
        pos = 2.0 - ease((f - 44) / 11)
        fade = 1.0
    else:
        pos = 1.0
        fade = 1.0

    # Metrics interpolate the way the bar's 33ms timer does.
    pulse = 0.5 + 0.5 * math.sin(f / 60 * math.tau)
    cpu = 42 + 4 * pulse + (2 if pos > 1.5 else 0)
    ram = 10.7 + 0.2 * pulse
    gpu = 1 + (1 if pos > 1.5 else 0)
    metrics = {
        "cpu": f"{cpu:.0f}%",
        "ram": f"{ram:.1f}G",
        "gpu": f"{gpu:.0f}%",
    }
    # Swap the clock minute at the midpoint with a short, real-looking fade.
    if f < 29:
        clock_text = "2:37 PM"
    elif f == 30:
        clock_text = "2:38 PM"
    else:
        clock_text = "2:38 PM"
    clock_alpha = 1.0
    if 30 <= f <= 34:
        clock_alpha = 0.55 + 0.45 * abs(f - 32) / 2

    # Feed the animated metric values into the chip renderer.
    # draw_bar's default labels are static; use a small local replacement by
    # painting the animated values over the default chips afterwards.
    draw_bar(im, active_pos=pos, occupied=(0, 1), metrics=metrics, opacity=fade,
             clock_text=clock_text, date_text="Wed 11 Nov", clock_alpha=clock_alpha)

    # A tiny cyan hairline just under the bar, outside its surface.
    if fade > 0:
        d = ImageDraw.Draw(im)
        d.line((0, 40, im.width, 40), fill=(0, 224, 207, int(30 * fade)), width=1)
    return im


def build_hero():
    frames = [hero_frame(f) for f in range(60)]
    save_gif(frames, OUT_BAR, duration=90, colors=128)
    print(f"wrote {OUT_BAR} ({len(frames)} frames, {os.path.getsize(OUT_BAR) // 1024} KiB)")


# ---------------------------------------------------------------------------
# Win+W demo
# ---------------------------------------------------------------------------
def draw_key(d, x, y, label, width, flash=0.0):
    fill = mix((8, 16, 18, 255), CYAN, flash)
    text = mix(INK, (4, 16, 15, 255), flash)
    d.rounded_rectangle((x, y, x + width, y + 25), 4, fill=fill,
                        outline=mix(BORDER, CYAN, flash), width=1)
    d.text((x + width / 2, y + 12.5), label, font=font(MONO_BOLD, 10),
           fill=text, anchor="mm")


def draw_save_dialog(im, alpha):
    w, h = im.size
    x0, y0, x1, y1 = 286, 122, 614, 264
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle((x0 + 6, y0 + 8, x1 + 6, y1 + 8), 8, fill=(0, 0, 0, 110))
    d.rounded_rectangle((x0, y0, x1, y1), 8, fill=(7, 17, 18, 250), outline=CYAN, width=1)
    d.line((x0 + 1, y0 + 35, x1 - 1, y0 + 35), fill=BORDER, width=1)
    d.text((x0 + 18, y0 + 14), "Save changes?", font=font(UI, 12), fill=INK)
    d.text((x0 + 18, y0 + 52), "notes.txt has unsaved changes.",
           font=font(MONO, 10), fill=(150, 196, 190, 255))
    d.text((x0 + 18, y0 + 72), "Close it and lose the changes?",
           font=font(MONO, 10), fill=(111, 216, 207, 255))
    # Buttons.
    for label, bx, bw, primary in (
        ("Cancel", 300, 72, False),
        ("Don't save", 380, 92, False),
        ("Save", 480, 72, True),
    ):
        fill = (0, 224, 207, 255) if primary else (8, 22, 22, 255)
        fg = (4, 16, 15, 255) if primary else INK
        d.rounded_rectangle((bx, y0 + 104, bx + bw, y0 + 132), 4, fill=fill,
                            outline=(0, 224, 207, 255) if primary else BORDER, width=1)
        d.text((bx + bw / 2, y0 + 118), label, font=font(UI, 10), fill=fg, anchor="mm")
    if alpha < 255:
        a = layer.getchannel("A").point(lambda v: int(v * alpha / 255))
        layer.putalpha(a)
    im.alpha_composite(layer)


def winw_frame(f):
    w, h = 900, 380
    im = make_desktop(w, h)
    # Key flash on frames 3-4; dialog from 5-9; editor closes 10-12.
    key_flash = 1.0 if f in (3, 4) else 0.0
    dialog_alpha = 0
    if 5 <= f <= 9:
        dialog_alpha = 255 if f >= 7 else int(255 * (f - 4) / 3)
    close_alpha = 255
    if f >= 11:
        close_alpha = max(0, int(255 * (1 - (f - 10) / 3)))

    draw_window(im, (42, 58, 532, 330), "notes.txt — Editor", [
        "My notes",
        "A window can be closed from the keyboard.",
        "Win+W asks the app to close, exactly like the X.",
        "Unsaved work still gets its save prompt.",
        "", "The other windows stay where they are.",
    ], active=close_alpha > 0, alpha=close_alpha)
    draw_window(im, (554, 58, 858, 330), "PowerShell", [
        "PS C:\\Users\\jmoney>",
        "PS C:\\Users\\jmoney> Get-Process notes",
        "Handles  NPM(K)  PM(K)  WS(K)  CPU(s)",
        "  421      28   18420  22104    0.13",
        "PS C:\\Users\\jmoney> _",
    ], active=False)

    if dialog_alpha:
        draw_save_dialog(im, dialog_alpha)

    d = ImageDraw.Draw(im)
    # Small caption strip / keycaps.
    d.text((42, 346), "Win + W  closes the focused window",
           font=font(MONO, 11), fill=(164, 205, 199, 255))
    draw_key(d, 642, 340, "Win", 52, key_flash)
    d.text((700, 352), "+", font=font(MONO_BOLD, 14), fill=MUTED, anchor="mm")
    draw_key(d, 716, 340, "W", 40, key_flash)
    d.text((770, 352), "WM_CLOSE", font=font(MONO, 10), fill=(92, 139, 134, 255),
           anchor="lm")
    # Draw the bar last so it always occupies the top edge.
    draw_bar(im, active_pos=1.0, occupied=(0, 1), compact=True, bar_h=38)
    return im


def build_winw():
    frames = [winw_frame(f) for f in range(16)]
    save_gif(frames, OUT_WINW, duration=90, colors=128)
    print(f"wrote {OUT_WINW} ({len(frames)} frames, {os.path.getsize(OUT_WINW) // 1024} KiB)")


# ---------------------------------------------------------------------------
# GIF encoder
# ---------------------------------------------------------------------------
def save_gif(frames, path, duration=90, colors=128):
    # Quantize against a fully populated frame so the active cyan and dark
    # glass don't get starved by the fade-in frame.
    sample = frames[min(12, len(frames) - 1)].convert("RGB")
    palette = sample.quantize(colors=colors, method=Image.Quantize.MEDIANCUT)
    p_frames = []
    for frame in frames:
        p = frame.convert("RGB").quantize(palette=palette, dither=Image.Dither.NONE)
        p_frames.append(p)
    p_frames[0].save(
        path,
        save_all=True,
        append_images=p_frames[1:],
        duration=duration,
        loop=0,
        optimize=True,
        disposal=2,
    )


if __name__ == "__main__":
    build_hero()
    build_winw()
