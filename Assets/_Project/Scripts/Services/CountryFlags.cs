// CountryFlags.cs — 국기 그림. 파일 이름이 곧 ISO 3166-1 alpha-3 국가 코드다 (KOR.png).
// 그림은 Game 씬 GameManager 의 인스펙터(Flags)에 연결하고, 시작할 때 Init 으로 넘겨받는다.
// 없는 나라는 null 을 돌려주고, 부르는 쪽에서 색 배지로 대신한다.

using System.Collections.Generic;
using UnityEngine;

public static class CountryFlags
{
    static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    static readonly Dictionary<Texture2D, Sprite> sprites = new Dictionary<Texture2D, Sprite>();
    static string[] codes = new string[0];

    /// <summary>씬에서 연결한 국기 그림을 받는다.</summary>
    public static void Init(Texture2D[] flags)
    {
        textures.Clear();
        if (flags != null)
            foreach (var t in flags)
                if (t != null) textures[t.name.ToUpperInvariant()] = t;
        var list = new List<string>(textures.Keys);
        list.Sort(string.CompareOrdinal);
        codes = list.ToArray();
    }

    /// <summary>국기가 있는 나라 코드 (코드순).</summary>
    public static string[] Codes { get { return codes; } }

    /// <summary>그 나라 국기. 없으면 null.</summary>
    public static Sprite Get(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        Texture2D t;
        if (!textures.TryGetValue(code.ToUpperInvariant(), out t)) return null;

        // 도메인 리로드를 끈 에디터에서는 캐시가 플레이를 넘어 남는다 — 지난 플레이에서 만든 스프라이트는 이미 파괴돼 있다
        Sprite sp;
        if (!sprites.TryGetValue(t, out sp) || sp == null)
            sprites[t] = sp = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        return sp;
    }
}
