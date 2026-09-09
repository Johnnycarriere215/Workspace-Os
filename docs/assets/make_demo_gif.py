#!/usr/bin/env python3
"""
Generate docs/assets/winw-demo.gif — a small animated demo of the Win+W
"close focused window" hotkey.

Frames are drawn as SVG (real theme palette from AppConfig.cs) and rasterized
with rsvg-convert, then assembled with ImageMagick into a looping GIF.
Only needs: python3, rsvg-convert, ImageMagick, JetBrainsMono Nerd Font.

Usage:  python3 docs/assets/make_demo_gif.py   (run from repo root)
"""
import subprocess, os, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_GIF = os.path.join(HERE, "winw-demo.gif")

# Theme palette — mirrors AppConfig.AppearanceConfig defaults (Charizard theme)
NAVY = "#0F2138"        # bar background / desktop backdrop
NAVY_DARK = "#0A1830"   # focused window fill
NAVY_MID = "#16294a"    # unfocused window fill
CREAM = "#FDF4C4"       # bar text / bright content
STEEL = "#7893B4"       # secondary text / unfocused accents
RED = "#C56363"         # Charizard accent (active workspace, close button)
SEP = "#434F57"
DIALOG = "#1D3357"      # save-prompt dialog fill

W, BAR_H = 900, 34
FONT = "JetBrainsMono NF"
font, mono = f'font-family="{FONT}"', f'font-family="{FONT}"'


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def svg(win_close_flash, dialog_progress, prompt_alpha, closing, gap,
        merge_progress):
    """One frame. Arguments in [0,1] unless noted (bools)."""
    p = []
    # ---- desktop backdrop
    p.append(f'<rect width="{W}" height="380" fill="{NAVY}"/>')
    # subtle monitor frame
    p.append(f'<rect x="6" y="6" width="{W-12}" height="340" fill="{NAVY}" '
             f'stroke="{SEP}" stroke-width="2" rx="6"/>')

    # ---- top bar
    p.append(f'<rect x="6" y="6" width="{W-12}" height="{BAR_H}" fill="{NAVY}" '
             f'rx="4"/>')
    p.append(f'<line x1="6" y1="{6+BAR_H}" x2="{W-6}" y2="{6+BAR_H}" '
             f'stroke="{SEP}" stroke-width="1.5"/>')
    # workspaces: 1 2 [3] 4
    wx, active = 26, 3
    for i in range(1, 5):
        if i == active:
            p.append(f'<circle cx="{wx+8}" cy="{6+BAR_H//2}" r="11" fill="{RED}"/>')
            p.append(f'<text x="{wx+8}" y="{6+BAR_H//2+5}" text-anchor="middle" '
                     f'font-size="13" fill="{NAVY}" {mono} font-weight="bold">{i}</text>')
        else:
            p.append(f'<text x="{wx+8}" y="{6+BAR_H//2+5}" text-anchor="middle" '
                     f'font-size="13" fill="{STEEL}" {mono}>{i}</text>')
        wx += 30
    # center clock
    p.append(f'<text x="{W//2}" y="{6+BAR_H//2+5}" text-anchor="middle" '
             f'font-size="13" fill="{CREAM}" {mono}>21:37</text>')
    # right modules (labels only — glyph coverage varies across Nerd Font builds)
    mods = [("CPU 12%", 74), ("RAM 38%", 74), ("GPU 4%", 62)]
    mx = W - 24
    for label, lw in mods:
        p.append(f'<text x="{mx}" y="{6+BAR_H//2+5}" text-anchor="end" '
                 f'font-size="12" fill="{CREAM}" {mono}>{esc(label)}</text>')
        mx -= lw + 26

    # ---- windows: editor (focused, left) + terminal (right)
    # editor window rect
    ew = int(470 * (1 - 0.12 * merge_progress))   # shrinks as terminal grows
    ex, ey, eh = 40, 90, 200
    e_close_flash = win_close_flash and not closing

    # editor body
    fill = NAVY_DARK if not closing else NAVY_MID
    p.append(f'<rect x="{ex}" y="{ey}" width="{ew}" height="{eh}" rx="3" '
             f'fill="{fill}" stroke="{RED if e_close_flash else "#2a3f66"}" '
             f'stroke-width="{2.5 if e_close_flash else 1.5}"/>')
    # editor titlebar
    p.append(f'<text x="{ex+12}" y="{ey+22}" font-size="13" fill="{CREAM}" {mono}>'
             f'{esc("notes.txt — Editor")}</text>')
    # fake code lines
    code = [("text", "def close_window():", CREAM),
            ("text", '    send(WM_CLOSE)', STEEL),
            ("text", "", STEEL),
            ("text", "# unsaved: buffer.txt", RED)]
    for i, (kind, s, c) in enumerate(code):
        p.append(f'<text x="{ex+16}" y="{ey+48+i*20}" font-size="12" '
                 f'fill="{c}" {mono}>{esc(s)}</text>')

    # terminal window (grows as the editor slot is re-adopted)
    tw = int(300 + 170 * merge_progress)
    tx, ty, th = 540 - int(50 * merge_progress), 90, 200
    p.append(f'<rect x="{tx}" y="{ty}" width="{tw}" height="{th}" rx="3" '
             f'fill="{NAVY_MID}" stroke="#2a3f66" stroke-width="1.5"/>')
    p.append(f'<text x="{tx+12}" y="{ty+22}" font-size="13" fill="{STEEL}" {mono}>'
             f'{esc("terminal")}</text>')
    for i, s in enumerate(["$ make demo", "…", "$ _"]):
        p.append(f'<text x="{tx+16}" y="{ty+50+i*20}" font-size="12" '
                 f'fill="{CREAM if i != 1 else STEEL}" {mono}>{esc(s)}</text>')

    # ---- save-prompt dialog (overlay while prompt_alpha > 0)
    if prompt_alpha > 0:
        dw, dh = 340, 120
        dx, dy = (W - dw) // 2, 130
        p.append(f'<g opacity="{prompt_alpha:.2f}">')
        p.append(f'<rect x="{dx}" y="{dy}" width="{dw}" height="{dh}" rx="6" '
                 f'fill="{DIALOG}" stroke="{CREAM}" stroke-width="1.5"/>')
        p.append(f'<text x="{dx+18}" y="{dy+30}" font-size="14" fill="{CREAM}" {mono}>'
                 f'{esc("Save changes to notes.txt?")}</text>')
        p.append(f'<text x="{dx+18}" y="{dy+52}" font-size="11" fill="{STEEL}" {mono}>'
                 f'{esc("Your unsaved changes will be lost.")}</text>')
        # buttons
        by = dy + dh - 40
        for label, bx, accent in (("Don't save", dx + 18, False),
                                  ("Cancel", dx + 128, False),
                                  ("Save", dx + 210, True)):
            bw, bh = 100 if not accent else 70, 28
            if accent:
                p.append(f'<rect x="{bx}" y="{by}" width="{bw}" height="{bh}" rx="4" '
                         f'fill="{RED}"/>')
                p.append(f'<text x="{bx+bw//2}" y="{by+19}" text-anchor="middle" '
                         f'font-size="12" fill="{NAVY}" {mono} font-weight="bold">'
                         f'{label}</text>')
            else:
                p.append(f'<rect x="{bx}" y="{by}" width="{bw}" height="{bh}" rx="4" '
                         f'fill="none" stroke="{STEEL}" stroke-width="1.5"/>')
                p.append(f'<text x="{bx+bw//2}" y="{by+19}" text-anchor="middle" '
                         f'font-size="12" fill="{CREAM}" {mono}>{label}</text>')
        p.append('</g>')

    # ---- key chord indicator (bottom strip)
    p.append(f'<rect x="6" y="352" width="{W-12}" height="26" fill="{NAVY_DARK}"/>')
    keys = []
    if not closing:
        keys = [("Win", True), ("W", True)] if not prompt_alpha else \
               [("Win", False), ("W", False)]
    else:
        keys = [("Win", False), ("W", False)]
    kx = 20
    p.append(f'<text x="{kx}" y="370" font-size="12" fill="{STEEL}" {mono}>'
             f'{"send(WM_CLOSE) — same as the title-bar ✕" if closing else ""}</text>')
    # draw keycaps for the press moment
    if win_close_flash and not closing:
        kx = W - 150
        for label in ("Win", "W"):
            p.append(f'<rect x="{kx}" y="356" width="46" height="18" rx="3" '
                     f'fill="{RED}"/>')
            p.append(f'<text x="{kx+23}" y="369" text-anchor="middle" font-size="11" '
                     f'fill="{NAVY}" {mono} font-weight="bold">{label}</text>')
            kx += 54
    elif not closing:
        kx = W - 150
        for label in ("Win", "W"):
            p.append(f'<rect x="{kx}" y="356" width="46" height="18" rx="3" '
                     f'fill="none" stroke="{STEEL}" stroke-width="1.2"/>')
            p.append(f'<text x="{kx+23}" y="369" text-anchor="middle" font-size="11" '
                     f'fill="{STEEL}" {mono}>{label}</text>')
            kx += 54

    p.append('</svg>')
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="380" '
            'viewBox="0 0 %d 380">' % (W, W)) + "\n".join(p)


# ---- timeline -------------------------------------------------------------
# (win_close_flash, dialog_progress, prompt_alpha, closing, gap, merge_progress)
FRAMES = [
    (False, 0.0, 0, False, 0, 0.0),    # idle — 2 frames
    (False, 0.0, 0, False, 0, 0.0),
    (True,  0.0, 0, False, 0, 0.0),    # Win+W pressed — keycaps flash red
    (True,  0.0, 0, False, 0, 0.0),
    (False, 0.35, 0.7, False, 0, 0.0),  # app shows its save prompt
    (False, 0.7, 1.0, False, 0, 0.0),
    (False, 0.7, 1.0, False, 0, 0.0),
    (False, 1.0, 0.55, False, 0, 0.0),  # user confirms
    (False, 1.0, 0.0, True, 0, 0.0),   # window closes
    (False, 1.0, 0.0, True, 0, 0.35),  # tiling engine re-adopts the slot
    (False, 1.0, 0.0, True, 0, 0.75),
    (False, 1.0, 0.0, True, 0, 1.0),   # terminal takes the full width — 3 frames
    (False, 1.0, 0.0, True, 0, 1.0),
]

# durations in 10ms units for ImageMagick -delay (per frame)
DELAYS = [70, 70, 45, 45, 55, 55, 90, 55, 45, 40, 40, 80, 120]

def main():
    tmp = tempfile.mkdtemp(prefix="wosgif")
    pngs = []
    for i, fr in enumerate(FRAMES):
        svg_path = os.path.join(tmp, f"f{i:02d}.svg")
        png_path = os.path.join(tmp, f"f{i:02d}.png")
        with open(svg_path, "w") as fh:
            fh.write(svg(*fr))
        subprocess.run(["rsvg-convert", "-w", str(W * 2), "-o", png_path, svg_path],
                       check=True)
        pngs.append(png_path)
    # assemble: 2x size for crispness, then finalize at natural GIF scaling
    # -delay must precede each image read; rsvg renders at 2x for crisp downscale
    args = ["magick"]
    for i in range(len(pngs)):
        args += ["-delay", str(DELAYS[i]), pngs[i]]
    args += ["-loop", "0", "-layers", "Optimize", "-resize", "900x", OUT_GIF]
    subprocess.run(args, check=True)
    print("wrote", OUT_GIF, os.path.getsize(OUT_GIF) // 1024, "KiB")

if __name__ == "__main__":
    main()
