"""코코넛 깨기 만화컷 시트를 프레임별 투명 PNG로 분리한다.

점프/돌던지기 시트(split_white.py)와 다른 점:
  - 캐릭터만이 아니라 '돌 탁자까지 한 프레임'으로 통째로 쓴다.
  - 그래서 발바닥이 아니라 **탁자를 기준으로 정렬**한다. 탁자가 화면에 고정된 것처럼 보이고
    캐릭터만 움직이게 하려면 이게 맞다.
  - 컷이 만화 패널로 배치돼 있어 연결요소가 아니라 '흰 여백 투영'으로 패널을 나눈다.
    줄마다 패널 간격이 달라서 행을 먼저 나눈 뒤 행별로 열을 찾는다.
  - 캐릭터2 시트는 패널마다 검은 테두리 사각형이 있어 안쪽으로 잘라낸다.

탁자는 회색(무채색 중간밝기)이지만 검은 외곽선으로 막혀 있어서 배경 flood가 못 들어간다.
반면 발밑 소프트 그림자는 외곽선이 없어 알아서 지워진다 - 의도한 동작.
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

WHITE_MIN = 235      # 이 이상 밝고 무채색이면 종이 배경
FLOOD_MIN_BRIGHT = 120
CHROMA_MIN = 25
GRAY_LO, GRAY_HI = 110, 232   # 돌 탁자로 볼 밝기 범위
MIN_TABLE = 3000
MAX_EYE_RATIO = 0.008   # 갇힌 흰색이 프레임 면적의 이 비율을 넘으면 눈이 아니라 배경 틈


def _gaps(profile, thr=0.004, minlen=8):
    runs, start = [], None
    for i, v in enumerate(profile):
        if v <= thr and start is None:
            start = i
        elif v > thr and start is not None:
            if i - start >= minlen:
                runs.append((start, i))
            start = None
    if start is not None and len(profile) - start >= minlen:
        runs.append((start, len(profile)))
    return runs


def _spans(profile, thr=0.004):
    """빈 띠 사이의 '내용이 있는' 구간들."""
    gaps = _gaps(profile, thr)
    spans, cur = [], 0
    for g0, g1 in gaps:
        if g0 > cur:
            spans.append((cur, g0))
        cur = g1
    if cur < len(profile):
        spans.append((cur, len(profile)))
    return [s for s in spans if s[1] - s[0] > 30]


def find_panels(ink):
    """행을 먼저 나누고 행별로 열을 찾는다 - 줄마다 패널 간격이 다르다."""
    panels = []
    for y0, y1 in _spans(ink.mean(axis=1)):
        for x0, x1 in _spans(ink[y0:y1].mean(axis=0)):
            panels.append((x0, y0, x1, y1))
    return panels


def strip_border(rgb):
    """패널 테두리(검은 사각 선)가 있으면 안쪽으로 잘라낸다."""
    g = rgb.astype(np.int16).max(axis=2)
    h, w = g.shape
    dark_rows = np.nonzero((g < 100).mean(axis=1) > 0.6)[0]
    dark_cols = np.nonzero((g < 100).mean(axis=0) > 0.6)[0]
    top = dark_rows[dark_rows < h * 0.15].max() + 1 if (dark_rows < h * 0.15).any() else 0
    bot = dark_rows[dark_rows > h * 0.85].min() if (dark_rows > h * 0.85).any() else h
    lef = dark_cols[dark_cols < w * 0.15].max() + 1 if (dark_cols < w * 0.15).any() else 0
    rig = dark_cols[dark_cols > w * 0.85].min() if (dark_cols > w * 0.85).any() else w
    return slice(top + 2, bot - 2), slice(lef + 2, rig - 2)


def panel_mask(rgb):
    rgbi = rgb.astype(np.int16)
    mx = rgbi.max(axis=2)
    mn = rgbi.min(axis=2)
    floodable = ((mx - mn) < CHROMA_MIN) & (mx >= FLOOD_MIN_BRIGHT)

    seed = np.zeros_like(floodable)
    seed[0, :] = seed[-1, :] = True
    seed[:, 0] = seed[:, -1] = True
    seed &= floodable
    bg = ndimage.binary_propagation(seed, mask=floodable)

    fg = ndimage.binary_opening(~bg, np.ones((3, 3)))
    fg = ndimage.binary_closing(fg, np.ones((5, 5)))
    # 자잘한 잉크 얼룩 제거, 큰 덩어리(캐릭터/탁자/효과음)만 남긴다.
    lbl, n = ndimage.label(fg, ndimage.generate_binary_structure(2, 2))
    if n:
        sizes = np.bincount(lbl.ravel())
        sizes[0] = 0
        fg = np.isin(lbl, np.nonzero(sizes > 400)[0])

    # 다리 사이/팔과 몸통 사이처럼 실루엣에 갇힌 종이 배경이 흰 얼룩으로 남는다. 눈/이빨도
    # 갇힌 흰색이라 색으로는 못 가르고, 실측상 눈은 프레임 면적의 0.75%를 안 넘는 반면
    # 얼룩은 0.8% 이상이라 그 선에서 자른다. 순백(235 이상)만 보므로 회색 돌 탁자는 안 건드린다.
    paper = fg & ((mx - mn) < CHROMA_MIN) & (mx >= WHITE_MIN)
    holes, hn = ndimage.label(paper)
    if hn:
        limit = fg.sum() * MAX_EYE_RATIO
        sizes = np.bincount(holes.ravel())
        sizes[0] = 0
        fg = fg & ~np.isin(holes, np.nonzero(sizes > limit)[0])
    return fg


def table_anchor(rgb, mask):
    """가장 큰 회색 덩어리 = 돌 탁자. 그 bbox의 가로중심/바닥을 기준점으로 쓴다."""
    rgbi = rgb.astype(np.int16)
    mx = rgbi.max(axis=2)
    mn = rgbi.min(axis=2)
    gray = mask & ((mx - mn) < 40) & (mx >= GRAY_LO) & (mx <= GRAY_HI)
    gray = ndimage.binary_closing(gray, np.ones((7, 7)))
    lbl, n = ndimage.label(gray, ndimage.generate_binary_structure(2, 2))
    if not n:
        return None
    sizes = ndimage.sum(gray, lbl, range(1, n + 1))
    if sizes.max() < MIN_TABLE:
        return None
    ys, xs = np.nonzero(lbl == int(np.argmax(sizes)) + 1)
    return (float(xs.mean()), float(ys.max()))


def main(src, out_dir, prefix, expect=None):
    rgb_full = np.array(Image.open(src).convert("RGB"))
    a = rgb_full.astype(np.int16)
    ink = ~((((a.max(axis=2) - a.min(axis=2)) < CHROMA_MIN)) & (a.max(axis=2) >= WHITE_MIN))
    panels = find_panels(ink)
    print(f"{prefix}: 패널 {len(panels)}개")
    if expect and len(panels) != expect:
        print(f"  !! 예상 {expect}개와 다름")

    frames = []
    for x0, y0, x1, y1 in panels:
        sub = rgb_full[y0:y1, x0:x1]
        rs, cs = strip_border(sub)
        sub = sub[rs, cs]
        m = panel_mask(sub)
        anc = table_anchor(sub, m)
        if anc is None:                       # 탁자를 못 찾으면 전체 bbox 하단 중심으로 대체
            ys, xs = np.nonzero(m)
            anc = (float(xs.mean()), float(ys.max()))
            print("  (탁자 미검출 - 전체 기준으로 정렬)")
        frames.append({"rgb": sub, "mask": m, "anchor": anc})

    bounds = []
    for f in frames:
        ys, xs = np.nonzero(f["mask"])
        bounds.append((ys.min(), ys.max(), xs.min(), xs.max()))

    lx = max(f["anchor"][0] - b[2] for f, b in zip(frames, bounds))
    rx = max(b[3] - f["anchor"][0] for f, b in zip(frames, bounds))
    ty = max(f["anchor"][1] - b[0] for f, b in zip(frames, bounds))
    by = max(b[1] - f["anchor"][1] for f, b in zip(frames, bounds))
    W, H = int(lx + rx) + 20, int(ty + by) + 20
    ax, ay = int(lx) + 10, int(ty) + 10
    print(f"  캔버스 {W}x{H}, 탁자 기준점 ({ax},{ay})")

    for i, f in enumerate(frames, start=1):
        rgba = np.dstack([f["rgb"], f["mask"].astype(np.uint8) * 255])
        img = Image.fromarray(rgba)
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.paste(img, (ax - int(f["anchor"][0]), ay - int(f["anchor"][1])), img)
        canvas.save(f"{out_dir}/{prefix}_{i}.png")
    print(f"  저장: {prefix}_1..{len(frames)}.png")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3],
         int(sys.argv[4]) if len(sys.argv) > 4 else None)
