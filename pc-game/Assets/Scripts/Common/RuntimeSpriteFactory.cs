// 아트 리소스 없이 심플한 도형/실루엣으로 원시인을 표현하기 위한 절차적 스프라이트 생성기
// (저능아게임_기획_프롬프트.md "캐릭터 아트" 참고 - 손으로 그린 스프라이트 대신 코드로
// 원/캡슐 도형을 찍어서 씀). 매번 새 Texture2D를 만드는 게 아니라 크기/색 조합별로
// 캐싱해서 재사용한다.
using System.Collections.Generic;
using UnityEngine;

public static class RuntimeSpriteFactory
{
    private static readonly Dictionary<(int, int, Color), Sprite> _cache = new();

    public static Sprite CreateCircle(int diameter, Color color) => GetOrCreate(diameter, diameter, color, DrawCircle);

    public static Sprite CreateCapsule(int width, int height, Color color) => GetOrCreate(width, height, color, DrawCapsule);

    private static Sprite GetOrCreate(int width, int height, Color color, System.Action<Texture2D, Color> draw)
    {
        var key = (width, height, color);
        if (_cache.TryGetValue(key, out Sprite cached))
            return cached;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        draw(texture, color);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        _cache[key] = sprite;
        return sprite;
    }

    private static void DrawCircle(Texture2D tex, Color color)
    {
        int w = tex.width, h = tex.height;
        Vector2 center = new Vector2((w - 1) * 0.5f, (h - 1) * 0.5f);
        float radius = Mathf.Min(w, h) * 0.5f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), center);
            tex.SetPixel(x, y, dist <= radius ? color : Color.clear);
        }
    }

    private static void DrawCapsule(Texture2D tex, Color color)
    {
        int w = tex.width, h = tex.height;
        float radius = w * 0.5f;
        float straightTop = radius;
        float straightBottom = h - radius;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = x - (w - 1) * 0.5f;
            bool inside;
            if (y < straightTop)
                inside = Vector2.Distance(new Vector2(x, y), new Vector2((w - 1) * 0.5f, straightTop)) <= radius;
            else if (y > straightBottom)
                inside = Vector2.Distance(new Vector2(x, y), new Vector2((w - 1) * 0.5f, straightBottom)) <= radius;
            else
                inside = Mathf.Abs(dx) <= radius;
            tex.SetPixel(x, y, inside ? color : Color.clear);
        }
    }
}
