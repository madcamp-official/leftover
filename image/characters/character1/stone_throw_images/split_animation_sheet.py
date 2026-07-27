"""돌던지기 애니메이션 시트에서 체커보드 배경을 걷어내고 프레임별 PNG로 분리한다.

원본(image/characters/character1/charactor1_animation_rock _throw.png)은 알파가 전부 255고
"투명" 체커보드가 픽셀로 구워져 있다. 체커는 43.125px 격자의 두 색((0,2,1)/(86,86,86))으로
완벽히 규칙적이라 격자 모델로 예측색을 만들어 걷어낸다.

문제: 캐릭터 외곽선이 순수 검정이라 체커 어두운 칸과 색이 같다. 그래서 격자 모델만으로는
어두운 칸 위의 외곽선이 배경으로 잘려 실루엣이 갉아먹힌다. 채도 있는 본체 픽셀 근처의
검은 픽셀은 외곽선으로 되살리는 보정을 넣었다.
"""
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"C:/Users/idote/몰입캠프4주차/leftover/image/characters/character1/charactor1_animation_rock _throw.png"
OUT_DIR = r"C:/Users/idote/몰입캠프4주차/leftover/image/characters/character1/stone_throw_images"

# 빈 영역의 체커 경계를 선형회귀로 실측한 값 (위상이 0이 아니라 반 칸 어긋나 있다).
PERIOD_X, PHASE_X = 43.05444, 42.963
PERIOD_Y, PHASE_Y = 43.00084, 36.957
DARK = np.array([0, 2, 1])
LIGHT = np.array([86, 86, 86])
MODEL_TOL = 14                 # 예측 체커색과 이 이하로 차이나면 배경 후보
OUTLINE_REACH = 16             # 본체에서 이 거리 안의 검은 픽셀은 외곽선으로 되살림
DARK_MAX = 70                  # 외곽선으로 볼 밝기 상한


def checker_model(h, w):
    ys, xs = np.mgrid[0:h, 0:w]
    iu = np.floor((xs - PHASE_X) / PERIOD_X).astype(int)
    iv = np.floor((ys - PHASE_Y) / PERIOD_Y).astype(int)
    return (iu + iv) % 2


def build_mask(rgb):
    """격자 칸마다 '실제 관측된' 배경색으로 예측 맵을 만든다.

    전역 패리티 공식만 쓰면 이미지 일부 구간(x 1938~2358)에서 체커 위상이 뒤집혀 있어
    예측 light 자리에 실제 dark가 오고, 순수 검정 칸이 그대로 전경으로 통과해버린다.
    칸별로 배경 표본 밝기를 직접 재서 dark/light를 정하면 위상이 어떻든 맞는다."""
    h, w, _ = rgb.shape
    rgbi = rgb.astype(np.int16)
    mx = rgbi.max(axis=2)
    mn = rgbi.min(axis=2)
    chroma = (mx - mn) >= 25            # 살/옷 등 확실한 본체

    # 배경 표본: 채색에서 충분히 떨어진 픽셀만 (외곽선이 섞이면 칸 밝기가 왜곡됨).
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

    # 표본이 적은 칸(캐릭터가 거의 다 덮은 칸)은 전역 패리티로 대체.
    parity = checker_model(h, w)
    probe = slice(0, 200)
    dark_parity = 0 if rgb[probe][parity[probe] == 0].mean() < 43 else 1
    is_dark = np.where(cnt[cell] >= 60, cell_dark[cell], parity == dark_parity)
    expected = np.where(is_dark[..., None], DARK, LIGHT).astype(np.int16)

    bg_like = np.abs(rgbi - expected).max(axis=2) <= MODEL_TOL
    print(f"  model check: 상단 빈 영역 배경 일치율 {bg_like[probe].mean():.1%}")

    # 발밑 그림자(체커를 덮은 반투명 타원) 제거 - 진짜 그림에서 무채색인 건
    # 순수 검정 외곽선(밝기 25 미만)뿐이다.
    gray_junk = (~chroma) & (mx >= 25)
    fg = (~bg_like) & (~gray_junk)
    return ndimage.binary_opening(fg, np.ones((3, 3))), chroma


def repair(mask):
    """실루엣 안쪽 외곽선(둘러싸여 있음)은 구멍 메우기로, 바깥 테두리에 생긴 이빨 자국은
    얕은 닫기로 복원한다. 닫기 반경을 키우면 손가락/발가락이 뭉개지므로 최소로만."""
    mask = ndimage.binary_fill_holes(mask)
    mask = ndimage.binary_closing(mask, DISK12)
    return ndimage.binary_fill_holes(mask)


def _disk(r):
    y, x = np.mgrid[-r:r + 1, -r:r + 1]
    return x * x + y * y <= r * r


DISK12 = _disk(12)


def main():
    img = Image.open(SRC).convert("RGB")
    rgb = np.array(img)
    fg, chroma = build_mask(rgb)

    # 캐릭터 덩어리 찾기: 채도 있는 본체 기준으로 라벨링해야 그림자/체커 잔재에 안 속는다.
    body = ndimage.binary_closing(chroma, np.ones((9, 9)))
    lbl, n = ndimage.label(body, ndimage.generate_binary_structure(2, 2))
    sizes = ndimage.sum(body, lbl, range(1, n + 1))
    keep = [i + 1 for i, s in enumerate(sizes) if s > 20000]
    boxes = ndimage.find_objects(lbl)
    figs = sorted((boxes[i - 1] for i in keep), key=lambda s: s[1].start)
    print(f"figures found: {len(figs)}")

    crops = []
    for sl in figs:
        y0, y1 = sl[0].start, sl[0].stop
        x0, x1 = sl[1].start, sl[1].stop
        # 본체 bbox를 외곽선 두께만큼 넉넉히 넓혀서 fg를 가져온다.
        pad = OUTLINE_REACH + 6
        y0, y1 = max(0, y0 - pad), min(rgb.shape[0], y1 + pad)
        x0, x1 = max(0, x0 - pad), min(rgb.shape[1], x1 + pad)
        # 닫기 전에 먼저 가장 큰 덩어리만 남긴다 - 순서를 바꾸면 옆 프레임 조각이나
        # 워터마크 잔재가 닫기로 본체에 붙어버린다.
        sub_fg = fg[y0:y1, x0:x1]
        l2, n2 = ndimage.label(sub_fg, ndimage.generate_binary_structure(2, 2))
        if n2 > 1:
            s2 = ndimage.sum(sub_fg, l2, range(1, n2 + 1))
            sub_fg = l2 == (int(np.argmax(s2)) + 1)
        sub_fg = repair(sub_fg)
        ys, xs = np.nonzero(sub_fg)
        crops.append({
            "rgb": rgb[y0:y1, x0:x1],
            "mask": sub_fg,
            "top": ys.min(), "bottom": ys.max(),
            "left": xs.min(), "right": xs.max(),
        })

    # 공통 캔버스: 가장 큰 프레임 기준 + 여백. 발바닥(하단)과 접지 중심(하단 8% 무게중심)으로 정렬.
    anchors = []
    for c in crops:
        ys, xs = np.nonzero(c["mask"])
        cutoff = c["bottom"] - (c["bottom"] - c["top"]) * 0.08
        foot_x = xs[ys >= cutoff]
        anchors.append((float(foot_x.mean()), float(c["bottom"])))

    left_ext = max(a[0] - c["left"] for a, c in zip(anchors, crops))
    right_ext = max(c["right"] - a[0] for a, c in zip(anchors, crops))
    top_ext = max(a[1] - c["top"] for a, c in zip(anchors, crops))
    W = int(left_ext + right_ext) + 20
    H = int(top_ext) + 20
    ax, ay = int(left_ext) + 10, int(top_ext) + 10
    print(f"canvas {W}x{H}, anchor at ({ax},{ay})")

    for i, (c, a) in enumerate(zip(crops, anchors), start=1):
        rgba = np.zeros((c["mask"].shape[0], c["mask"].shape[1], 4), np.uint8)
        rgba[..., :3] = c["rgb"]
        rgba[..., 3] = c["mask"].astype(np.uint8) * 255
        frame = Image.fromarray(rgba)
        canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        canvas.paste(frame, (ax - int(a[0]), ay - int(a[1])), frame)
        path = f"{OUT_DIR}/rock_throw_{i}.png"
        canvas.save(path)
        print(f"  saved {path}  (body {c['right']-c['left']}x{c['bottom']-c['top']})")


if __name__ == "__main__":
    main()
