// image/common/props/에서 (손 또는 AI로) 그려진 실제 소품 아트를 Assets/Resources/Props/ 밑에 넣어두고
// 코드에서 불러올 때 쓰는 헬퍼. RuntimeSpriteFactory(절차적 도형)와 달리 이쪽은 실제 PNG를
// 그대로 스프라이트로 쓰는 쪽을 위한 것.
using UnityEngine;

public static class ArtAssets
{
    // spriteMode가 Single이든 Multiple(자동 슬라이스)이든 상관없이 항상 동작하게 LoadAll을
    // 쓴다 - Multiple일 때는 Resources.Load<Sprite>가 null을 반환할 수 있다.
    //
    // 슬라이스가 여러 개면 "가장 큰 것"을 고른다: 자동 슬라이스는 그림에서 떨어져 나온 작은
    // 얼룩(외곽선 픽셀 몇 개 등)까지 별도 스프라이트로 잘라내는데, 그냥 첫 번째를 쓰면 그
    // 파편이 걸려서 파츠가 통째로 안 보이는 일이 생긴다 (실제로 p1_right_lower_arm_hand가
    // 7x122 파편 + 581x1754 팔뚝으로 잘려서 오른팔이 사라졌었다).
    public static Sprite LoadSprite(string resourcePath)
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
        Sprite best = null;
        float bestArea = -1f;
        foreach (Sprite sprite in sprites)
        {
            float area = sprite.rect.width * sprite.rect.height;
            if (area > bestArea) { bestArea = area; best = sprite; }
        }
        return best;
    }

    public static Sprite LoadProp(string name) => LoadSprite($"Props/{name}");
    public static Sprite LoadUi(string name) => LoadSprite($"UI/{name}");
    public static Sprite LoadStoneThrow(string name) => LoadSprite($"StoneThrow/{name}");
    public static Sprite LoadPoseMatch(string name) => LoadSprite($"PoseMatch/{name}");
    public static Sprite LoadFruitJump(string name) => LoadSprite($"FruitJump/{name}");
    public static Sprite LoadCoconutBreak(string name) => LoadSprite($"CoconutBreak/{name}");
    public static Sprite LoadStoneOrBanana(string name) => LoadSprite($"StoneOrBanana/{name}");
    public static Sprite LoadStaringContest(string name) => LoadSprite($"StaringContest/{name}");
    public static Sprite LoadMatchResult(string name) => LoadSprite($"UI/MatchResults/{name}");
    public static Sprite LoadGameIcon(string name) => LoadSprite($"UI/GameIcons/{name}");

    // 캐릭터 파츠. 원본은 image/characters/character1, character2 아래에서
    // front/back/faces와 공통 파츠명으로 정리되어 있다. Resources로 들여올 때는
    // p1_*/p2_* 이름을 사용하며 여기서는 그 통일된 이름만 쓴다.
    // part 예: "head", "torso", "left_upper_arm", "right_lower_arm_hand",
    //          "face_grimacing", "face_stone_hit_one_tooth_broken"
    public static Sprite LoadCharacter(PlayerId player, string part)
        => LoadSprite($"Characters/{(player == PlayerId.P1 ? "p1" : "p2")}_{part}");

    // 돌던지기의 오버더숄더 구도처럼 같은 플레이어를 앞/뒤에서 동시에 보여줄 때 사용한다.
    // 기존 6개 게임은 이름 호환성을 위해 앞모습 로더(위 메서드)를 그대로 쓴다.
    public static Sprite LoadCharacterBack(PlayerId player, string part)
        => LoadSprite($"Characters/{(player == PlayerId.P1 ? "p1" : "p2")}_back_{part}");

    // 원본 이미지 해상도가 몇 px든(AI 생성 이미지라 보통 1024px 안팎으로 큼) 상관없이 항상 같은
    // 인게임 크기로 보이도록, SpriteRenderer의 가로 폭을 targetWidth(월드 유닛)에 맞춰 균일
    // 스케일을 적용한다.
    public static void FitWidth(SpriteRenderer renderer, float targetWidth)
    {
        if (renderer == null || renderer.sprite == null) return;
        float nativeWidth = renderer.sprite.bounds.size.x;
        if (nativeWidth <= 0f) return;
        renderer.transform.localScale = Vector3.one * (targetWidth / nativeWidth);
    }

    // 배경을 카메라 뷰에 꽉 차게 깐다 - 화면비가 배경비(보통 16:9)와 달라도 빈 곳이 안 보이게
    // 가로/세로 중 더 크게 키워야 하는 쪽에 맞춘다(cover 방식). 모든 미니게임이 공유하는 로직.
    public static void CreateBackground(Camera cam, Sprite sprite)
    {
        if (sprite == null) return;

        var go = new GameObject("Background");
        go.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = -100;

        float viewHeight = cam.orthographicSize * 2f;
        float viewWidth = viewHeight * cam.aspect;
        Vector2 size = sprite.bounds.size;
        if (size.x <= 0f || size.y <= 0f) return;
        go.transform.localScale = Vector3.one * Mathf.Max(viewWidth / size.x, viewHeight / size.y);
        go.AddComponent<CameraBackgroundFitter>();
    }
}
