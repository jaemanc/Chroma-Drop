#!/usr/bin/env python3
"""Chroma Drop — 배경 SVG 를 블러 넣은 PNG 로 굽는다.

  python3 Tools/svg-to-png.py Assets/puzzle_bg_vertical.svg Assets/Resources/puzzle_bg.png

이 SVG 는 rect/circle 과 rotate 그룹으로만 되어 있어 직접 래스터화한다.
Unity 는 com.unity.vectorgraphics 없이는 SVG 를 임포트하지 못하고,
포스트프로세싱 패키지도 없어서 런타임 블러를 걸 수 없다. 그래서 미리 구워 둔다.
"""
import math, re, struct, sys, xml.etree.ElementTree as ET, zlib
import numpy as np

NS = "{http://www.w3.org/2000/svg}"
SCALE = 0.5        # 절반 해상도 — 어차피 블러가 들어가고 용량도 준다
BLUR_PX = 3.0      # 원본 기준 블러 반경 (아주 살짝)
TILE = 2           # 패턴을 N x N 로 반복해 넣는다. 화면에서 블록이 그만큼 작아진다.
ROTATE = 30.0      # 패턴 전체를 기울인다 (도). 블록이 비스듬히 누워 보인다.


def hex2rgb(v):
    v = v.strip()
    if not v.startswith("#"):
        return (255, 255, 255)
    v = v[1:]
    if len(v) == 3:
        v = "".join(c * 2 for c in v)
    return tuple(int(v[i:i + 2], 16) for i in (0, 2, 4))


def blend(buf, mask, color, alpha):
    """mask(0~1) 만큼 color 를 덮어쓴다."""
    a = (mask * alpha)[..., None]
    buf *= (1.0 - a)
    buf += a * np.array(color, dtype=np.float32)


def rot(px, py, deg, cx, cy):
    """캔버스 좌표 → 회전 전 로컬 좌표 (역회전)"""
    r = math.radians(-deg)
    c, s = math.cos(r), math.sin(r)
    dx, dy = px - cx, py - cy
    return dx * c - dy * s + cx, dx * s + dy * c + cy


def main():
    src, dst = sys.argv[1], sys.argv[2]
    root = ET.parse(src).getroot()
    W = int(float(root.get("width")) * SCALE)
    H = int(float(root.get("height")) * SCALE)
    buf = np.zeros((H, W, 3), dtype=np.float32)

    svgW, svgH = float(root.get("width")), float(root.get("height"))
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    # 출력 캔버스를 TILE x TILE 로 나눠 각 칸에 SVG 를 한 번씩 그린다.
    # 배경 이미지는 화면을 덮도록 확대되므로, 타일을 늘려야 블록이 작아 보인다.
    # 캔버스 전체를 기울인다. 샘플 좌표를 반대로 돌리면 그림이 돌아간 것처럼 보인다.
    if ROTATE:
        a = math.radians(-ROTATE)
        ca, sa = math.cos(a), math.sin(a)
        cx0, cy0 = W / 2.0, H / 2.0
        dx, dy = xs - cx0, ys - cy0
        xs, ys = dx * ca - dy * sa + cx0, dx * sa + dy * ca + cy0

    # 홀수 번째 칸은 뒤집어 붙인다(미러 타일링). 그냥 반복하면 경계에 이음선이 보인다.
    tw, th = W / TILE, H / TILE
    ix, iy = np.floor(xs / tw), np.floor(ys / th)
    fx, fy = (xs % tw) / tw, (ys % th) / th
    fx = np.where(ix % 2 == 1, 1.0 - fx, fx)
    fy = np.where(iy % 2 == 1, 1.0 - fy, fy)
    xs, ys = fx * svgW, fy * svgH

    def draw(node, deg, cx, cy, gop):
        tag = node.tag.replace(NS, "")
        op = float(node.get("opacity", 1)) * gop
        fill = node.get("fill")
        if tag == "g":
            t = node.get("transform", "")
            m = re.match(r"rotate\(([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\)", t)
            nd, ncx, ncy = (float(m.group(1)), float(m.group(2)), float(m.group(3))) if m else (deg, cx, cy)
            for ch in node:
                draw(ch, nd, ncx, ncy, op)
            return
        if not fill or fill == "none":
            return

        px, py = (rot(xs, ys, deg, cx, cy) if deg else (xs, ys))
        if tag == "rect":
            x, y = float(node.get("x", 0)), float(node.get("y", 0))
            w, h = float(node.get("width")), float(node.get("height"))
            r = float(node.get("rx", 0))
            # 둥근 사각 SDF
            qx = np.abs(px - (x + w / 2)) - (w / 2 - r)
            qy = np.abs(py - (y + h / 2)) - (h / 2 - r)
            d = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
        elif tag == "circle":
            ccx, ccy = float(node.get("cx")), float(node.get("cy"))
            d = np.hypot(px - ccx, py - ccy) - float(node.get("r"))
        else:
            return
        mask = np.clip(0.5 - d * SCALE / TILE, 0, 1)   # 경계 안티에일리어싱
        if mask.max() <= 0:
            return
        blend(buf, mask, hex2rgb(fill), op)

    for ch in root:
        draw(ch, 0, 0, 0, 1.0)

    # 분리 가능 가우시안 — 아주 살짝만
    sigma = BLUR_PX * SCALE
    rad = max(1, int(sigma * 3))
    k = np.exp(-0.5 * (np.arange(-rad, rad + 1) / sigma) ** 2)
    k /= k.sum()
    for axis in (0, 1):
        buf = np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), axis, buf)

    img = np.clip(buf, 0, 255).astype(np.uint8)
    raw = b"".join(b"\x00" + img[y].tobytes() for y in range(H))
    def chunk(t, d):
        c = t + d
        return struct.pack(">I", len(d)) + c + struct.pack(">I", zlib.crc32(c))
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    open(dst, "wb").write(png)
    print(f"{dst}  {W}x{H}  타일 {TILE}x{TILE}  회전 {ROTATE}°  블러 {BLUR_PX}px  {len(png)//1024}KB")


if __name__ == "__main__":
    main()
