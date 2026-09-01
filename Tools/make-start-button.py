#!/usr/bin/env python3
"""Chroma Drop — 시작 버튼 이미지를 따뜻한 색조로 다시 칠하고 살짝 기울인다.

  python3 Tools/make-start-button.py ~/Downloads/btn_start_pink.png Assets/Resources/btn_start.png

원본은 분홍 단색 버튼이다. 면/두께/하이라이트/테두리가 각각 단색이라
색을 하나씩 갈아끼우면 된다. 면에는 빨강→주황→노랑 가로 그라디언트를 준다.
마지막에 오른쪽을 살짝 좁혀 옆에서 비스듬히 본 것처럼 만든다.
"""
import struct, sys, zlib
import numpy as np

# 원본 색 → 새 색. 면(FACE)과 하이라이트는 x 위치에 따라 그라디언트로 채운다.
FACE, HILITE = (0xFF, 0x4D, 0x9E), (0xFF, 0x9D, 0xCA)
FIXED = {
    (0xD1, 0x3F, 0x81): (0xC4, 0x53, 0x1A),   # 안쪽 그늘
    (0xCC, 0x3E, 0x7E): (0xBC, 0x4E, 0x19),
    (0xC0, 0x3A, 0x77): (0xB0, 0x47, 0x16),
    (0xB2, 0x2C, 0x6E): (0xA3, 0x32, 0x12),   # 아래 두께
    (0x14, 0x16, 0x2B): (0x14, 0x16, 0x2B),   # 테두리 — 그대로
    (0xFF, 0xFF, 0xFF): (0xFF, 0xFF, 0xFF),   # 글자 — 그대로
}
GRAD = [(0.0, (0xFF, 0x3B, 0x2F)),            # 빨강
        (0.5, (0xFF, 0x8A, 0x00)),            # 주황
        (1.0, (0xFF, 0xC9, 0x3C))]            # 노랑
SKEW = 0.90        # 오른쪽 세로 축소율 — 1.0 이면 정면
BLEND_HI = 0.42    # 하이라이트를 흰색 쪽으로 섞는 정도


def decode(path):
    d = open(path, "rb").read()
    i, idat, w, h, ct = 8, b"", None, None, None
    while i < len(d):
        ln = struct.unpack(">I", d[i:i + 4])[0]
        typ, data = d[i + 4:i + 8], d[i + 8:i + 8 + ln]
        i += 12 + ln
        if typ == b"IHDR":
            w, h, _, ct = struct.unpack(">IIBB", data[:10])
        elif typ == b"IDAT":
            idat += data
        elif typ == b"IEND":
            break
    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 4: 2, 6: 4}[ct]
    stride = w * ch
    out = np.zeros((h, stride), dtype=np.uint8)
    prev = np.zeros(stride, dtype=np.uint8)
    p = 0
    for y in range(h):
        f = raw[p]; p += 1
        ln_ = np.frombuffer(raw[p:p + stride], dtype=np.uint8).astype(np.int32).copy(); p += stride
        if f == 1:
            for x in range(ch, stride): ln_[x] = (ln_[x] + ln_[x - ch]) & 255
        elif f == 2:
            ln_ = (ln_ + prev) & 255
        elif f == 3:
            for x in range(stride):
                a = ln_[x - ch] if x >= ch else 0
                ln_[x] = (ln_[x] + ((a + int(prev[x])) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = int(ln_[x - ch]) if x >= ch else 0
                b = int(prev[x]); c = int(prev[x - ch]) if x >= ch else 0
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                ln_[x] = (ln_[x] + pr) & 255
        ln_ = ln_.astype(np.uint8)
        out[y] = ln_; prev = ln_
    return out.reshape(h, w, ch)


def encode(img, path):
    h, w, _ = img.shape
    raw = b"".join(b"\x00" + img[y].tobytes() for y in range(h))
    def chunk(t, d):
        c = t + d
        return struct.pack(">I", len(d)) + c + struct.pack(">I", zlib.crc32(c))
    open(path, "wb").write(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b""))


def grad_at(t):
    for i in range(len(GRAD) - 1):
        a, ca = GRAD[i]; b, cb = GRAD[i + 1]
        if a <= t <= b:
            k = (t - a) / (b - a)
            return tuple(ca[j] + (cb[j] - ca[j]) * k for j in range(3))
    return GRAD[-1][1]


def main():
    src, dst = sys.argv[1], sys.argv[2]
    img = decode(src).astype(np.float32)
    h, w, _ = img.shape
    rgb, alpha = img[..., :3], img[..., 3:4]

    # 원본 팔레트 중 가장 가까운 색으로 분류해 새 색을 입힌다 (경계 안티에일리어싱 포함)
    keys = [FACE, HILITE] + list(FIXED.keys())
    pal = np.array(keys, dtype=np.float32)
    idx = np.argmin(((rgb[:, :, None, :] - pal[None, None, :, :]) ** 2).sum(-1), axis=2)

    xs = np.linspace(0, 1, w, dtype=np.float32)
    gr = np.array([grad_at(float(t)) for t in xs], dtype=np.float32)      # (w,3)
    gr = np.broadcast_to(gr[None, :, :], (h, w, 3))
    hi = gr + (255.0 - gr) * BLEND_HI

    out = np.zeros_like(rgb)
    out[idx == 0] = gr[idx == 0]
    out[idx == 1] = hi[idx == 1]
    for n, k in enumerate(FIXED.keys()):
        out[idx == n + 2] = np.array(FIXED[k], dtype=np.float32)

    res = np.concatenate([out, alpha], axis=2)

    # 오른쪽을 세로로 좁혀 비스듬히 본 느낌 (역매핑 + 이중선형)
    warped = np.zeros_like(res)
    cy = (h - 1) / 2.0
    for x in range(w):
        s = 1.0 + (SKEW - 1.0) * (x / (w - 1.0))
        ys = (np.arange(h, dtype=np.float32) - cy) / s + cy
        y0 = np.clip(np.floor(ys).astype(int), 0, h - 1)
        y1 = np.clip(y0 + 1, 0, h - 1)
        f = (ys - y0)[:, None]
        inside = (ys >= 0) & (ys <= h - 1)
        col = res[y0, x] * (1 - f) + res[y1, x] * f
        col[~inside] = 0
        warped[:, x] = col

    encode(np.clip(warped, 0, 255).astype(np.uint8), dst)
    print(f"{dst}  {w}x{h}  기울기 {SKEW}")


if __name__ == "__main__":
    main()
