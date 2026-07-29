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
MIN_PANEL = 15000   # 컷 하나로 볼 최소 면적
PANEL_PAD = 80      # 별/파편 같은 떨어진 효과까지 담을 여유
MIN_BLOB = 50       # 이보다 작으면 JPEG 잡티로 보고 버린다(별/파편은 70px 이상)
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


def find_panels(fg, shape):
    """컷을 연결요소로 찾는다.

    개정판 시트는 컷이 격자가 아니라 엇갈려 배치돼 있어(c1은 3번컷과 4번컷이 세로로 겹침)
    행/열 투영으로는 못 나눈다. 컷마다 '캐릭터+의자'가 붙어 한 덩어리라 연결요소가 안전하다.
    읽는 순서(위→아래, 같은 줄이면 왼→오른쪽)로 정렬해서 돌려준다."""
    lbl, n = ndimage.label(fg, ndimage.generate_binary_structure(2, 2))
    sizes = ndimage.sum(fg, lbl, range(1, n + 1))
    keep = [i + 1 for i, s in enumerate(sizes) if s >= MIN_PANEL]
    boxes = ndimage.find_objects(lbl)

    items = []
    for i in keep:
        sl = boxes[i - 1]
        items.append({"id": i, "sl": sl,
                      "cy": (sl[0].start + sl[0].stop) / 2,
                      "cx": (sl[1].start + sl[1].stop) / 2})
    # 세로로 비슷한 높이면 같은 줄로 묶는다(이미지 높이의 20% 이내).
    items.sort(key=lambda d: d["cy"])
    rows, cur = [], [items[0]] if items else []
    for it in items[1:]:
        if it["cy"] - cur[0]["cy"] <= shape[0] * 0.20:
            cur.append(it)
        else:
            rows.append(cur)
            cur = [it]
    if cur:
        rows.append(cur)

    ordered = []
    for row in rows:
        ordered.extend(sorted(row, key=lambda d: d["cx"]))
    return ordered, lbl


def _panel_distance(cy, cx, sl):
    """조각에서 컷 '중심'까지의 거리.

    상자 가장자리까지의 거리로 재면 안 된다 - 아래 컷의 타격 이펙트(머리 위로 튀는 별)가
    위 컷 상자의 아래 모서리에 더 붙어 있어서 엉뚱한 컷으로 배정된다. 중심 거리로 재면
    가로 위치가 반영돼 제 컷으로 간다."""
    cyp = (sl[0].start + sl[0].stop) / 2
    cxp = (sl[1].start + sl[1].stop) / 2
    return ((cy - cyp) ** 2 + (cx - cxp) ** 2) ** 0.5


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
    # JPEG 잡티만 걷어낸다. 별/코코넛 파편이 70~300px라 문턱을 높이면 효과가 통째로 날아간다.
    lbl, n = ndimage.label(fg, ndimage.generate_binary_structure(2, 2))
    if n:
        sizes = np.bincount(lbl.ravel())
        sizes[0] = 0
        fg = np.isin(lbl, np.nonzero(sizes > MIN_BLOB)[0])

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
    """가장 큰 회색 덩어리 = 앉는 의자(돌/나무). 그 가로중심/바닥을 기준점으로 쓴다."""
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


def load_rgb(path):
    """알파가 있는 시트(eye_fight.png 등)는 흰 배경에 합성해서 읽는다.

    그냥 convert('RGB')하면 투명 픽셀의 밑색이 그대로 드러나 배경 판정이 어긋난다.
    어차피 투명한 곳은 배경이므로 흰색으로 깔아주면 흰 배경 시트와 동일하게 처리된다."""
    im = Image.open(path)
    if im.mode in ("RGBA", "LA") or "transparency" in im.info:
        im = im.convert("RGBA")
        base = Image.new("RGBA", im.size, (255, 255, 255, 255))
        im = Image.alpha_composite(base, im)
    return np.array(im.convert("RGB"))


def main(src, out_dir, prefix, expect=None):
    rgb_full = load_rgb(src)
    fg_full = panel_mask(rgb_full)
    panels, lbl = find_panels(fg_full, rgb_full.shape)
    print(f"{prefix}: 컷 {len(panels)}개")
    if expect and len(panels) != expect:
        print(f"  !! 예상 {expect}개와 다름")

    # 별/코코넛 파편처럼 본체에서 떨어진 조각은 '가장 가까운 컷'에 배정한다. 컷 상자를
    # 넓혀서 담으면 아래 컷의 별이 위 컷에 딸려 들어간다.
    panel_ids = {p["id"] for p in panels}
    extras = {p["id"]: [] for p in panels}
    for i, sl in enumerate(ndimage.find_objects(lbl), start=1):
        if sl is None or i in panel_ids:
            continue
        cy = (sl[0].start + sl[0].stop) / 2
        cx = (sl[1].start + sl[1].stop) / 2
        # 작은 조각은 전부 어느 한 컷의 효과이므로 가장 가까운 컷에 그냥 배정한다.
        best = min(panels, key=lambda p: _panel_distance(cy, cx, p["sl"]))
        extras[best["id"]].append(i)

    frames = []
    for p in panels:
        own = np.isin(lbl, [p["id"]] + extras[p["id"]])
        ys, xs = np.nonzero(own)
        y0, y1 = max(0, ys.min() - 6), min(rgb_full.shape[0], ys.max() + 7)
        x0, x1 = max(0, xs.min() - 6), min(rgb_full.shape[1], xs.max() + 7)

        sub = rgb_full[y0:y1, x0:x1]
        m = own[y0:y1, x0:x1]
        anc = table_anchor(sub, m)
        if anc is None:                       # 의자를 못 찾으면 전체 bbox 하단 중심으로 대체
            ys, xs = np.nonzero(m)
            anc = (float(xs.mean()), float(ys.max()))
            print("  (의자 미검출 - 전체 기준으로 정렬)")
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
    print(f"  캔버스 {W}x{H}, 의자 기준점 ({ax},{ay})")

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
