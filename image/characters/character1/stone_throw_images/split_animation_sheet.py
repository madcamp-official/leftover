"""애니메이션 시트에서 "투명 체커보드"가 픽셀로 구워진 배경을 걷어내고 프레임별 PNG로 분리.

원본은 알파가 전부 255고 체커가 그림으로 박혀 있어서 Unity에 그대로 넣으면 회색 체크무늬가
따라다닌다. 게다가 프레임끼리 팔이 겹쳐서 Grid/Automatic 슬라이스로는 제대로 안 잘린다.

핵심 아이디어
  1) 체커는 43px 격자의 두 색으로 완벽히 규칙적이다. 격자 주기/위상을 실측하고, 칸마다
     '실제 관측된' 배경 밝기로 dark/light를 정한다(일부 구간은 위상이 뒤집혀 있어 전역
     패리티 공식만으로는 안 맞는다).
  2) 배경으로 판정된 픽셀을 '덩어리 단위'로 지운다. 이러면 팔과 몸통 사이처럼 실루엣에
     둘러싸인 배경(겨드랑이)도 같이 지워진다 - 예전에 fill_holes로 무조건 메워서 체커 색
     얼룩이 남던 문제가 사라진다.
  3) 캐릭터의 검은 외곽선은 체커 어두운 칸과 색이 똑같아서 2)에서 같이 깎여나간다. 깎인
     자국은 닫기 연산으로 되메우되 '검은색으로 칠한다' - 원본 색을 쓰면 체커가 다시
     묻어나므로. 외곽선 자리라 검정으로 칠하는 게 원래 그림과 같다.
"""
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"C:/Users/idote/몰입캠프4주차/leftover/image/characters/character1/charactor1_animation_rock _throw.png"
OUT_DIR = r"C:/Users/idote/몰입캠프4주차/leftover/image/characters/character1/stone_throw_images"
PREFIX = "rock_throw"

# 빈 영역의 체커 경계를 선형회귀로 실측한 값.
PERIOD_X, PHASE_X = 43.05444, 42.963
PERIOD_Y, PHASE_Y = 43.00084, 36.957
DARK = np.array([0, 2, 1])
LIGHT = np.array([86, 86, 86])

MODEL_TOL = 14        # 예측 체커색과 이 이하 차이면 배경 후보
MIN_BG_BLOB = 400     # 이보다 큰 배경 후보 덩어리만 실제 배경으로 간주(작은 건 외곽선 틈)
BITE_RADIUS = 14      # 외곽선이 깎인 자국을 되메울 닫기 반경
MIN_FIGURE = 20000    # 캐릭터 한 명으로 볼 최소 채색 면적


def _disk(r):
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    return x * x + y * y <= r * r


DISK_BITE = _disk(BITE_RADIUS)


def analyze(rgb):
    """(전경 마스크, 채색 마스크) 반환."""
    h, w, _ = rgb.shape
    rgbi = rgb.astype(np.int16)
    mx = rgbi.max(axis=2)
    mn = rgbi.min(axis=2)
    chroma = (mx - mn) >= 25                       # 살/옷 등 확실한 그림

    # --- 칸별 배경색 관측 ---
    sample = ~ndimage.binary_dilation(chroma, np.ones((3, 3)), iterations=14)
    iu = np.floor((np.arange(w) - PHASE_X) / PERIOD_X).astype(int)
    iv = np.floor((np.arange(h) - PHASE_Y) / PERIOD_Y).astype(int)
    iu -= iu.min()
    iv -= iv.min()
    cell = iv[:, None] * (iu.max() + 1) + iu[None, :]
    ncell = int(cell.max()) + 1
    cnt = np.bincount(cell[sample], minlength=ncell)
    tot = np.bincount(cell[sample], weights=mx[sample].astype(float), minlength=ncell)
    cell_dark = (tot / np.maximum(cnt, 1)) < 43

    ys, xs = np.mgrid[0:h, 0:w]
    parity = ((np.floor((xs - PHASE_X) / PERIOD_X).astype(int)
               + np.floor((ys - PHASE_Y) / PERIOD_Y).astype(int)) % 2)
    dark_parity = 0 if rgb[0:200][parity[0:200] == 0].mean() < 43 else 1
    is_dark = np.where(cnt[cell] >= 60, cell_dark[cell], parity == dark_parity)
    expected = np.where(is_dark[..., None], DARK, LIGHT).astype(np.int16)

    bg_like = np.abs(rgbi - expected).max(axis=2) <= MODEL_TOL
    # 발밑 그림자(체커를 덮은 반투명 타원)와 체커 잔재도 배경 취급. 그림에서 무채색인 건
    # 검은 외곽선(25 미만)과 흰 눈/이빨(200 이상)뿐이다.
    bg_like |= (~chroma) & (mx >= 25) & (mx < 200)
    print(f"  배경 후보 비율 {bg_like.mean():.1%}")

    # --- 배경을 덩어리 단위로 확정 ---
    lbl, n = ndimage.label(bg_like)
    if n:
        sizes = np.bincount(lbl.ravel())
        big = np.zeros(sizes.size, bool)
        big[1:] = sizes[1:] >= MIN_BG_BLOB
        bg = big[lbl]
    else:
        bg = bg_like
    fg = ndimage.binary_opening(~bg, np.ones((3, 3)))
    return fg, chroma


def extract(rgb, fg, chroma):
    """캐릭터별로 (마스크, 깎인자국 마스크, bbox) 리스트 반환."""
    body = ndimage.binary_closing(chroma, np.ones((9, 9)))
    lbl, n = ndimage.label(body, ndimage.generate_binary_structure(2, 2))
    sizes = ndimage.sum(body, lbl, range(1, n + 1))
    keep = [i + 1 for i, s in enumerate(sizes) if s > MIN_FIGURE]
    boxes = ndimage.find_objects(lbl)
    figs = sorted((boxes[i - 1] for i in keep), key=lambda s: s[1].start)
    print(f"  캐릭터 {len(figs)}명")

    out = []
    pad = BITE_RADIUS + 30
    for sl in figs:
        y0 = max(0, sl[0].start - pad)
        y1 = min(rgb.shape[0], sl[0].stop + pad)
        x0 = max(0, sl[1].start - pad)
        x1 = min(rgb.shape[1], sl[1].stop + pad)

        sub = fg[y0:y1, x0:x1]
        l2, n2 = ndimage.label(sub, ndimage.generate_binary_structure(2, 2))
        if n2 > 1:                                   # 옆 프레임 침범분 제거
            s2 = ndimage.sum(sub, l2, range(1, n2 + 1))
            sub = l2 == (int(np.argmax(s2)) + 1)

        closed = ndimage.binary_closing(sub, DISK_BITE)
        bites = closed & ~sub                        # 외곽선이 깎인 자리
        out.append({"rgb": rgb[y0:y1, x0:x1], "mask": closed, "bites": bites})
    return out


def main():
    rgb = np.array(Image.open(SRC).convert("RGB"))
    fg, chroma = analyze(rgb)
    crops = extract(rgb, fg, chroma)

    # 발바닥(하단)과 접지 중심으로 정렬해 공통 캔버스에 담는다 - 재생 시 안 튀게.
    anchors, bounds = [], []
    for c in crops:
        ys, xs = np.nonzero(c["mask"])
        top, bottom, left, right = ys.min(), ys.max(), xs.min(), xs.max()
        foot = xs[ys >= bottom - (bottom - top) * 0.08]
        anchors.append((float(foot.mean()), float(bottom)))
        bounds.append((top, bottom, left, right))

    left_ext = max(a[0] - b[2] for a, b in zip(anchors, bounds))
    right_ext = max(b[3] - a[0] for a, b in zip(anchors, bounds))
    top_ext = max(a[1] - b[0] for a, b in zip(anchors, bounds))
    W, H = int(left_ext + right_ext) + 20, int(top_ext) + 20
    ax, ay = int(left_ext) + 10, int(top_ext) + 10
    print(f"  캔버스 {W}x{H}, 발 기준점 ({ax},{ay})")

    for i, (c, a) in enumerate(zip(crops, anchors), start=1):
        px = c["rgb"].copy()
        px[c["bites"]] = 0                           # 되메운 자리는 검정(외곽선)으로
        rgba = np.dstack([px, c["mask"].astype(np.uint8) * 255])
        frame = Image.fromarray(rgba)
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.paste(frame, (ax - int(a[0]), ay - int(a[1])), frame)
        canvas.save(f"{OUT_DIR}/{PREFIX}_{i}.png")
    print(f"  저장 완료: {PREFIX}_1..{len(crops)}.png")


if __name__ == "__main__":
    main()
