// image/ 폴더에서 (손 또는 AI로) 그려진 실제 소품 아트를 Assets/Resources/Props/ 밑에 넣어두고
// 코드에서 불러올 때 쓰는 헬퍼. RuntimeSpriteFactory(절차적 도형)와 달리 이쪽은 실제 PNG를
// 그대로 스프라이트로 쓰는 쪽을 위한 것.
using UnityEngine;

public static class ArtAssets
{
    // spriteMode가 Single이든 Multiple(자동 트림으로 슬라이스 1개)이든 상관없이 항상 동작하게
    // LoadAll을 쓴다 - Multiple일 때는 Resources.Load<Sprite>가 null을 반환할 수 있다.
    public static Sprite LoadSprite(string resourcePath)
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
        return sprites.Length > 0 ? sprites[0] : null;
    }

    public static Sprite LoadProp(string name) => LoadSprite($"Props/{name}");
    public static Sprite LoadUi(string name) => LoadSprite($"UI/{name}");

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
}
