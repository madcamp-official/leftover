// 아트 리소스 없이 "엉클그랜파" 류 카툰 실루엣(두꺼운 검은 외곽선 + 납작한 원색)으로 캐릭터를
// 표현하기 위한 절차적 스프라이트 생성기 (우가우가게임_기획_프롬프트.md "캐릭터 아트" 참고 -
// 손으로 그린 스프라이트 대신 코드로 원/캡슐 도형 + 외곽선을 찍어서 씀). 매번 새 Texture2D를
// 만드는 게 아니라 크기/색/외곽선 조합별로 캐싱해서 재사용한다.
using System.Collections.Generic;
using UnityEngine;

public static class RuntimeSpriteFactory
{
    public static readonly Color OutlineColor = new Color(0.08f, 0.08f, 0.08f);

    private static readonly Dictionary<(int, int, Color, float), Sprite> _cache = new();

    // outlinePx: 0이면 외곽선 없음. 기본값은 지름/폭의 ~8% 정도가 카툰 느낌이 잘 산다.
    public static Sprite CreateCircle(int diameter, Color color, float outlinePx = -1f) =>
        GetOrCreate(diameter, diameter, color, outlinePx < 0f ? diameter * 0.09f : outlinePx, DrawCircle);

    public static Sprite CreateCapsule(int width, int height, Color color, float outlinePx = -1f) =>
        GetOrCreate(width, height, color, outlinePx < 0f ? width * 0.12f : outlinePx, DrawCapsule);

    private static Sprite GetOrCreate(int width, int height, Color color, float outlinePx,
        System.Action<Texture2D, Color, float> draw)
    {
        var key = (width, height, color, outlinePx);
        if (_cache.TryGetValue(key, out Sprite cached))
            return cached;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        draw(texture, color, outlinePx);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        _cache[key] = sprite;
        return sprite;
    }

    private static void DrawCircle(Texture2D tex, Color color, float outlinePx)
    {
        int w = tex.width, h = tex.height;
        Vector2 center = new Vector2((w - 1) * 0.5f, (h - 1) * 0.5f);
        float radius = Mathf.Min(w, h) * 0.5f;
        float innerRadius = radius - outlinePx;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), center);
            tex.SetPixel(x, y, PixelColor(dist, radius, innerRadius, color));
        }
    }

    private static void DrawCapsule(Texture2D tex, Color color, float outlinePx)
    {
        int w = tex.width, h = tex.height;
        float radius = w * 0.5f;
        float innerRadius = radius - outlinePx;
        float straightTop = radius;
        float straightBottom = h - radius;
        float midX = (w - 1) * 0.5f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = Mathf.Abs(x - midX);
            float dist;
            if (y < straightTop)
                dist = Vector2.Distance(new Vector2(x, y), new Vector2(midX, straightTop));
            else if (y > straightBottom)
                dist = Vector2.Distance(new Vector2(x, y), new Vector2(midX, straightBottom));
            else
                dist = dx;
            tex.SetPixel(x, y, PixelColor(dist, radius, innerRadius, color));
        }
    }

    // dist가 [innerRadius, radius] 구간이면 외곽선 색, radius 밖이면 투명, 그 안쪽이면 채우기 색.
    private static Color PixelColor(float dist, float radius, float innerRadius, Color fill)
    {
        if (dist > radius) return Color.clear;
        if (innerRadius > 0f && dist > innerRadius) return OutlineColor;
        return fill;
    }
}
