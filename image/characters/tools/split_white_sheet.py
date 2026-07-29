"""흰 배경 애니메이션 시트를 프레임별 투명 PNG로 분리한다.

체커보드가 구워진 돌던지기 시트(split_anim.py)와 달리 이쪽은 배경이 흰색이라 훨씬 단순하다.
다만 두 가지를 조심해야 한다:

  1) 눈/이빨의 흰색은 배경과 색이 같다. 그래서 '색으로' 지우면 눈에 구멍이 뚫린다.
     테두리에서 flood fill로 '바깥과 이어진 흰색'만 지워서 갇힌 흰색(눈/이빨)은 살린다.
  2) 발밑 소프트 그림자는 외곽선이 없는 옅은 회색이라 flood가 그대로 먹어치운다(원하는 동작).
     반면 코코넛 시트의 돌 탁자는 회색이지만 검은 외곽선으로 막혀 있어 안 먹힌다.

JPEG로 저장된 시트가 있어 경계에 압축 잡티가 생기므로 임계값에 여유를 뒀다.
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

FLOOD_MIN_BRIGHT = 120   # 이보다 밝고 무채색이면 배경으로 번져 나갈 수 있음
CHROMA_MIN = 25          # 이 이상이면 유채색 = 그림
MIN_FIGURE = 8000        # 프레임 하나로 볼 최소 면적
MAX_EYE_RATIO = 0.012   # 갇힌 흰색이 캐릭터 면적의 이 비율을 넘으면 눈이 아니라 배경 틈
FOOT_BAND = 0.08         # 하단 몇 %를 접지면으로 볼지


def foreground(rgb):
    rgbi = rgb.astype(np.int16)
    mx = rgbi.max(axis=2)
    mn = rgbi.min(axis=2)
    chroma = (mx - mn) >= CHROMA_MIN

    floodable = (~chroma) & (mx >= FLOOD_MIN_BRIGHT)

    # 테두리와 이어진 floodable 영역만 배경. 갇힌 흰색(눈/이빨)은 남는다.
    seed = np.zeros_like(floodable)
    seed[0, :] = seed[-1, :] = True
    seed[:, 0] = seed[:, -1] = True
    seed &= floodable
    bg = ndimage.binary_propagation(seed, mask=floodable)

    # 갇힌 흰색(눈/이빨 vs 다리 사이 틈)의 구분은 캐릭터 크기를 알아야 하므로 여기서 하지
    # 않는다 - split()에서 프레임별로 처리한다. 여기서 절대 면적으로 자르면 캐릭터가 큰
    # 이미지에서 눈이 통째로 배경 취급돼 구멍이 뚫린다.
    fg = ndimage.binary_opening(~bg, np.ones((3, 3)))
    return ndimage.binary_closing(fg, np.ones((5, 5)))


def split(path, out_dir, prefix, expect=None):
    rgb = np.array(Image.open(path).convert("RGB"))
    fg = foreground(rgb)

    lbl, n = ndimage.label(fg, ndimage.generate_binary_structure(2, 2))
    sizes = ndimage.sum(fg, lbl, range(1, n + 1))
    keep = [i + 1 for i, s in enumerate(sizes) if s >= MIN_FIGURE]
    boxes = ndimage.find_objects(lbl)
    figs = sorted(((boxes[i - 1], i) for i in keep), key=lambda t: t[0][1].start)
    print(f"{prefix}: 덩어리 {len(figs)}개 (면적 {[int(sizes[i-1]) for _, i in figs]})")
    if expect and len(figs) != expect:
        print(f"  !! 예상 {expect}개와 다름 - 확인 필요")

    # 다리 사이처럼 실루엣에 갇힌 배경이 흰 얼룩으로 남는다. 눈/이빨과 면적이 겹쳐서
    # (둘 다 200~250px) 크기만으로는 못 가르므로 위치도 본다 - 눈/이빨은 언제나 상반신이다.
    rgbi = rgb.astype(np.int16)
    white = (((rgbi.max(axis=2) - rgbi.min(axis=2)) < CHROMA_MIN)
             & (rgbi.max(axis=2) >= FLOOD_MIN_BRIGHT))

    crops = []
    for sl, idx in figs:
        m = (lbl[sl] == idx)
        height, area = m.shape[0], m.sum()
        holes, hn = ndimage.label(white[sl] & m)
        for hi in range(1, hn + 1):
            blob = holes == hi
            rows = np.nonzero(blob.any(axis=1))[0]
            # 실측: 눈/이빨은 상반신(상대높이 0.3 이내)에 있고 캐릭터 면적의 0.7%를 안 넘는다.
            if rows.mean() / height > 0.40 or blob.sum() > area * MAX_EYE_RATIO:
                m = m & ~blob
        crops.append({"rgb": rgb[sl], "mask": m})

    anchors, bounds = [], []
    for c in crops:
        ys, xs = np.nonzero(c["mask"])
        top, bottom = ys.min(), ys.max()
        foot = xs[ys >= bottom - (bottom - top) * FOOT_BAND]
        anchors.append((float(foot.mean()), float(bottom)))
        bounds.append((top, bottom, xs.min(), xs.max()))

    lx = max(a[0] - b[2] for a, b in zip(anchors, bounds))
    rx = max(b[3] - a[0] for a, b in zip(anchors, bounds))
    ty = max(a[1] - b[0] for a, b in zip(anchors, bounds))
    W, H = int(lx + rx) + 20, int(ty) + 20
    ax, ay = int(lx) + 10, int(ty) + 10
    print(f"  캔버스 {W}x{H}")

    for i, (c, a) in enumerate(zip(crops, anchors), start=1):
        rgba = np.dstack([c["rgb"], c["mask"].astype(np.uint8) * 255])
        frame = Image.fromarray(rgba)
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.paste(frame, (ax - int(a[0]), ay - int(a[1])), frame)
        canvas.save(f"{out_dir}/{prefix}_{i}.png")
    print(f"  저장: {prefix}_1..{len(crops)}.png")


if __name__ == "__main__":
    split(sys.argv[1], sys.argv[2], sys.argv[3],
          int(sys.argv[4]) if len(sys.argv) > 4 else None)
