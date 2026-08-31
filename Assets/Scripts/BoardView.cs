// BoardView.cs — 보드 렌더링/연출 계층.
// 스프라이트(타일/아이템 아이콘/파티클)는 전부 런타임 생성 — 외부 에셋 의존 없음.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ColorMatcher.Core;

public class BoardView : MonoBehaviour
{
    static readonly Color EmptyColor = new Color(0.06f, 0.06f, 0.08f);
    static readonly Color BrickColor = new Color(0.42f, 0.35f, 0.30f);   // 무채색에 가까운 갈색 — 팔레트와 안 겹친다
    const float TileScale = 0.92f;

    SpriteRenderer[,] tiles;
    SpriteRenderer[,] overlays;   // 아이템 아이콘
    SpriteRenderer[] ghost;
    SpriteRenderer[] ghostRing;   // 고스트 테두리 — 타일 색과 무관하게 위치를 읽히게 한다
    int ghostCount;               // 현재 표시 중인 고스트 칸 수 (펄스용)
    Color ghostRingColor;
    Sprite tile;                  // 둥근 모서리 + 세로 그라데이션 (타일/고스트)
    Sprite plain;                 // 평면 사각 (프레임)
    Sprite ring;                  // 둥근 사각 테두리 (고스트)
    Sprite soft;                  // 파티클용 소프트 원
    Sprite[] brick;               // 내구도별 벽돌 (금 0/1/2줄)
    Sprite rainbow;               // 무지개 타일
    readonly Dictionary<ItemType, Sprite> icons = new Dictionary<ItemType, Sprite>();
    bool built;

    // ---- 파티클 풀 (파괴 버스트 타격감) ----
    const int MaxParts = 200;
    Transform[] pTr;
    SpriteRenderer[] pSr;
    Vector2[] pVel;
    float[] pLife, pMax, pSpin, pRot, pSize;
    int liveParts;

    public void Build()
    {
        if (built) return;
        built = true;

        tile = MakeTileSprite();
        plain = MakeSquareSprite();
        ring = MakeRingSprite();
        soft = MakeSoftSprite();
        brick = new[] { MakeBrickSprite(0), MakeBrickSprite(1), MakeBrickSprite(2) };
        rainbow = MakeRainbowSprite();
        icons[ItemType.Row] = MakeIcon(ItemType.Row);
        icons[ItemType.Col] = MakeIcon(ItemType.Col);
        icons[ItemType.Diag] = MakeIcon(ItemType.Diag);
        icons[ItemType.Bomb5] = MakeIcon(ItemType.Bomb5);
        icons[ItemType.ColorClear] = MakeIcon(ItemType.ColorClear);

        var frameGo = new GameObject("frame");
        frameGo.transform.SetParent(transform, false);
        frameGo.transform.localPosition = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f, 1);
        frameGo.transform.localScale = new Vector3(Board.W + 0.7f, Board.H + 0.7f, 1);
        var fsr = frameGo.AddComponent<SpriteRenderer>();
        fsr.sprite = plain;
        fsr.color = new Color(0.13f, 0.13f, 0.18f);
        fsr.sortingOrder = -2;

        tiles = new SpriteRenderer[Board.W, Board.H];
        overlays = new SpriteRenderer[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                var go = new GameObject("t_" + x + "_" + y);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, y, 0);
                go.transform.localScale = Vector3.one * TileScale;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tile;
                tiles[x, y] = sr;

                var og = new GameObject("i_" + x + "_" + y);
                og.transform.SetParent(transform, false);
                og.transform.localPosition = new Vector3(x, y, -0.5f);
                og.transform.localScale = Vector3.one * 0.8f;
                var osr = og.AddComponent<SpriteRenderer>();
                osr.sortingOrder = 2;
                osr.enabled = false;
                overlays[x, y] = osr;
            }

        ghost = new SpriteRenderer[8]; // 최대 조각 5칸 + 여유
        ghostRing = new SpriteRenderer[8];
        for (int i = 0; i < ghost.Length; i++)
        {
            var rg = new GameObject("ghostring_" + i);
            rg.transform.SetParent(transform, false);
            rg.transform.localScale = Vector3.one * TileScale * 1.22f;
            var rsr = rg.AddComponent<SpriteRenderer>();
            rsr.sprite = ring;
            rsr.sortingOrder = 5;
            rsr.enabled = false;
            ghostRing[i] = rsr;

            var go = new GameObject("ghost_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 1.06f;   // 밑 타일(0.92)보다 크게 — 얹힌 느낌
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tile;
            sr.sortingOrder = 6;
            sr.enabled = false;
            ghost[i] = sr;
        }

        BuildParticlePool();
    }

    void BuildParticlePool()
    {
        pTr = new Transform[MaxParts];
        pSr = new SpriteRenderer[MaxParts];
        pVel = new Vector2[MaxParts];
        pLife = new float[MaxParts];
        pMax = new float[MaxParts];
        pSpin = new float[MaxParts];
        pRot = new float[MaxParts];
        pSize = new float[MaxParts];
        var root = new GameObject("particles").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < MaxParts; i++)
        {
            var go = new GameObject("p" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = soft;
            sr.sortingOrder = 8;
            sr.enabled = false;
            pTr[i] = go.transform;
            pSr[i] = sr;
        }
    }

    public void SetVisible(bool v) { gameObject.SetActive(v); }

    /// <summary>칸 하나의 스프라이트·색을 결정한다. 벽돌/무지개/빈칸/일반색을 한 곳에서 다룬다.</summary>
    void PaintTile(int x, int y, int c, int hp, Color[] palette)
    {
        var sr = tiles[x, y];
        if (c == Board.Brick)
        {
            // hp 3→금 없음, 2→한 줄, 1→두 줄
            sr.sprite = brick[Mathf.Clamp(Rules.BrickHp - hp, 0, brick.Length - 1)];
            sr.color = BrickColor;
        }
        else if (c == Board.Rainbow)
        {
            sr.sprite = rainbow;
            sr.color = Color.white;          // 스프라이트 자체가 색을 갖는다
        }
        else
        {
            sr.sprite = tile;
            sr.color = c == Board.Empty ? EmptyColor : palette[c];
        }
    }

    /// <summary>보드 최종 상태를 즉시 반영 (색/아이템/위치·스케일 리셋)</summary>
    public void Refresh(Board b, Color[] palette)
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int c = b.GetTile(x, y);
                PaintTile(x, y, c, b.GetBrickHp(x, y), palette);
                tiles[x, y].transform.localPosition = new Vector3(x, y, 0);
                tiles[x, y].transform.localScale = Vector3.one * TileScale;

                var it = b.GetItem(x, y);
                overlays[x, y].enabled = it != ItemType.None;
                if (it != ItemType.None) overlays[x, y].sprite = icons[it];
                overlays[x, y].transform.localPosition = new Vector3(x, y, -0.5f);
            }
    }

    /// <summary>연쇄 한 단계가 끝난 시점의 보드를 반영 (색/아이템만; 위치는 FallIn 이 잡는다).</summary>
    public void ApplyState(int[] t, ItemType[] it, int[] hp, Color[] palette)
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int c = t[x * Board.H + y];
                PaintTile(x, y, c, hp[x * Board.H + y], palette);
                tiles[x, y].transform.localScale = Vector3.one * TileScale;

                var item = it[x * Board.H + y];
                overlays[x, y].enabled = item != ItemType.None;
                if (item != ItemType.None) overlays[x, y].sprite = icons[item];
            }
    }

    /// <summary>새로 내려온 칸을 잠깐 반짝여 "이번에 채워진 칸"임을 알린다.</summary>
    public IEnumerator GlowNew(List<Point> pts, float dur)
    {
        if (pts == null || pts.Count == 0) yield break;
        var baseCols = new Color[pts.Count];
        for (int i = 0; i < pts.Count; i++) baseCols[i] = tiles[pts[i].X, pts[i].Y].color;

        float t = 0;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = 1f - t / dur;                       // 1 → 0 으로 잦아든다
            for (int i = 0; i < pts.Count; i++)
            {
                var sr = tiles[pts[i].X, pts[i].Y];
                sr.color = Color.Lerp(baseCols[i], Color.white, 0.6f * k);
                sr.transform.localScale = Vector3.one * TileScale * (1f + 0.09f * k);
            }
            yield return null;
        }
        for (int i = 0; i < pts.Count; i++)
        {
            var sr = tiles[pts[i].X, pts[i].Y];
            sr.color = baseCols[i];
            sr.transform.localScale = Vector3.one * TileScale;
        }
    }

    /// <summary>스탬프 연출: 찍힌 칸을 조각 색으로 칠하고 살짝 팝</summary>
    public void PaintCells(List<Point> cells, Color c)
    {
        foreach (var p in cells)
        {
            tiles[p.X, p.Y].sprite = tile;      // 무지개/벽돌 자리였어도 색 칸으로 덮인다
            tiles[p.X, p.Y].color = c;
            tiles[p.X, p.Y].transform.localScale = Vector3.one * 1.14f;
        }
        StartCoroutine(ShrinkCells(cells));
    }

    IEnumerator ShrinkCells(List<Point> cells)
    {
        float t = 0, d = 0.14f;
        while (t < d)
        {
            t += Time.deltaTime;
            // ease-out-back 느낌으로 탄력 있게 수축
            float k = t / d;
            float s = Mathf.Lerp(1.14f, TileScale, k * k * (3f - 2f * k));
            foreach (var p in cells) tiles[p.X, p.Y].transform.localScale = Vector3.one * s;
            yield return null;
        }
        foreach (var p in cells) tiles[p.X, p.Y].transform.localScale = Vector3.one * TileScale;
    }

    /// <summary>파괴 직전 플래시. 색으로 직접 파괴/연계 파괴를 구분한다.</summary>
    public void FlashCells(List<Point> pts, Color c)
    {
        foreach (var p in pts)
        {
            tiles[p.X, p.Y].color = c;
            tiles[p.X, p.Y].transform.localScale = Vector3.one * 1.10f;
        }
    }

    /// <summary>파괴 버스트: 각 칸 위치에서 색 파편이 튀어나가며 페이드 (타격감)</summary>
    public void Burst(List<Point> pts, List<Color> colors, float energy, Color tint)
    {
        if (pTr == null) return;
        int perCell = pts.Count > 60 ? 1 : (pts.Count > 24 ? 2 : 3);
        for (int i = 0; i < pts.Count; i++)
        {
            Color col = (colors != null && i < colors.Count) ? colors[i] : Color.white;
            for (int k = 0; k < perCell; k++)
                Spawn(pts[i].X, pts[i].Y, col, energy, tint);
        }
    }

    void Spawn(float x, float y, Color col, float energy, Color tint)
    {
        // 비활성 파티클 찾기 (풀 소진 시 스킵)
        int idx = -1;
        for (int i = 0; i < MaxParts; i++)
            if (!pSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        var ang = Random.value * Mathf.PI * 2f;
        var spd = Random.Range(2.2f, 5.5f) * Mathf.Clamp(energy, 0.7f, 2.2f);
        pVel[idx] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd;
        pLife[idx] = 0f;
        pMax[idx] = Random.Range(0.28f, 0.5f);
        pSpin[idx] = Random.Range(-540f, 540f);
        pRot[idx] = Random.value * 360f;
        pSize[idx] = Random.Range(0.18f, 0.34f);
        var c = Color.Lerp(col, tint, 0.45f);
        c.a = 1f;
        pSr[idx].color = c;
        pTr[idx].localPosition = new Vector3(x + Random.Range(-0.15f, 0.15f), y + Random.Range(-0.15f, 0.15f), -1.2f);
        pTr[idx].localScale = Vector3.one * pSize[idx];
        pTr[idx].localRotation = Quaternion.Euler(0, 0, pRot[idx]);
        pSr[idx].enabled = true;
        liveParts++;
    }

    void Update()
    {
        PulseGhost();
        if (liveParts <= 0) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < MaxParts; i++)
        {
            if (!pSr[i].enabled) continue;
            pLife[i] += dt;
            float k = pLife[i] / pMax[i];
            if (k >= 1f) { pSr[i].enabled = false; liveParts--; continue; }
            pVel[i] *= 0.90f;               // 감쇠
            pVel[i].y -= 6f * dt;           // 중력
            var pos = pTr[i].localPosition;
            pos.x += pVel[i].x * dt;
            pos.y += pVel[i].y * dt;
            pTr[i].localPosition = pos;
            pRot[i] += pSpin[i] * dt;
            pTr[i].localRotation = Quaternion.Euler(0, 0, pRot[i]);
            pTr[i].localScale = Vector3.one * pSize[i] * (1f - 0.6f * k);
            var c = pSr[i].color;
            c.a = 1f - k * k;
            pSr[i].color = c;
        }
    }

    /// <summary>바뀐 칸들이 위에서 떨어져 들어오는 낙하 연출. 색은 이미 Refresh로 최종 상태.</summary>
    public IEnumerator FallIn(bool[,] changed, float dur) { return FallIn(changed, dur, 0.02f); }

    /// <summary>stagger = 위쪽 칸이 늦게 떨어지는 간격. 연쇄 단계 사이에는 짧게 줘서 늘어지지 않게 한다.</summary>
    public IEnumerator FallIn(bool[,] changed, float dur, float stagger)
    {
        var xs = new List<int>(); var ys = new List<int>(); var delays = new List<float>();
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (changed[x, y]) { xs.Add(x); ys.Add(y); delays.Add((Board.H - 1 - y) * stagger); }
        if (xs.Count == 0) yield break;

        const float drop = 3.5f;
        float maxDelay = (Board.H - 1) * stagger;
        float t = 0, total = dur + maxDelay;
        while (t < total)
        {
            t += Time.deltaTime;
            for (int i = 0; i < xs.Count; i++)
            {
                float k = Mathf.Clamp01((t - delays[i]) / dur);
                k = 1 - (1 - k) * (1 - k); // ease-out
                float off = (1 - k) * drop;
                tiles[xs[i], ys[i]].transform.localPosition = new Vector3(xs[i], ys[i] + off, 0);
                overlays[xs[i], ys[i]].transform.localPosition = new Vector3(xs[i], ys[i] + off, -0.5f);
            }
            yield return null;
        }
        for (int i = 0; i < xs.Count; i++)
        {
            tiles[xs[i], ys[i]].transform.localPosition = new Vector3(xs[i], ys[i], 0);
            overlays[xs[i], ys[i]].transform.localPosition = new Vector3(xs[i], ys[i], -0.5f);
        }
    }

    public void ShowGhost(Piece p, int ax, int ay, bool can, Color pieceColor)
    {
        // 놓을 수 없으면 붉은 테두리 — 팔레트가 무슨 색이든 구분된다.
        ghostRingColor = can ? Color.white : new Color(1f, 0.35f, 0.35f);
        ghostCount = p.Cells.Count;

        for (int i = 0; i < ghost.Length; i++)
        {
            if (i < p.Cells.Count)
            {
                int gx = ax + p.Cells[i].X, gy = ay + p.Cells[i].Y;
                ghost[i].enabled = true;
                ghost[i].transform.localPosition = new Vector3(gx, gy, -1);
                ghost[i].color = can
                    ? new Color(pieceColor.r, pieceColor.g, pieceColor.b, 1f)
                    : new Color(0.1f, 0.1f, 0.12f, 0.75f);

                ghostRing[i].enabled = true;
                ghostRing[i].transform.localPosition = new Vector3(gx, gy, -1.05f);
                ghostRing[i].color = ghostRingColor;
            }
            else { ghost[i].enabled = false; ghostRing[i].enabled = false; }
        }
    }

    public void HideGhost()
    {
        if (ghost == null) return;
        ghostCount = 0;
        foreach (var g in ghost) if (g != null) g.enabled = false;
        foreach (var g in ghostRing) if (g != null) g.enabled = false;
    }

    // 고스트 테두리를 천천히 맥동시켜 배경 타일에 묻히지 않게 한다.
    void PulseGhost()
    {
        if (ghostCount <= 0) return;
        float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.5f);
        float scale = Mathf.Lerp(TileScale * 1.16f, TileScale * 1.30f, k);
        var c = ghostRingColor;
        c.a = Mathf.Lerp(0.55f, 1f, k);
        for (int i = 0; i < ghostCount && i < ghostRing.Length; i++)
        {
            if (!ghostRing[i].enabled) continue;
            ghostRing[i].transform.localScale = Vector3.one * scale;
            ghostRing[i].color = c;
        }
    }

    // 평면 흰 사각 (프레임)
    static Sprite MakeSquareSprite()
    {
        var tex = new Texture2D(8, 8);
        tex.filterMode = FilterMode.Point;
        var px = new Color[64];
        for (int i = 0; i < 64; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8);
    }

    // 둥근 모서리 + 위가 밝은 세로 그라데이션 + 얇은 안쪽 하이라이트 (SpriteRenderer.color로 틴트)
    // GameUI 의 배경 블록도 같은 모양을 쓴다.
    public static Sprite MakeTileSprite()
    {
        const int S = 32; const float r = 7f;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - dist + 0.5f); // 모서리 안티에일리어싱
                float g = Mathf.Lerp(0.80f, 1.0f, fy / (S - 1)); // 아래 어둡게, 위 밝게
                // 상단 하이라이트 밴드
                if (fy > S - 6) g = Mathf.Min(1f, g + 0.06f);
                px[y * S + x] = new Color(g, g, g, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 둥근 사각 테두리 (고스트 위치 표시)
    static Sprite MakeRingSprite()
    {
        const int S = 32; const float r = 7f, thick = 3.4f;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float sdf = Mathf.Sqrt(dx * dx + dy * dy) - r; // 0 = 모서리 경계, 음수 = 내부
                // 경계 안쪽 thick 폭만 남긴다 (양끝 안티에일리어싱)
                float a = Mathf.Clamp01(-sdf + 0.5f) * Mathf.Clamp01(sdf + thick + 0.5f);
                px[y * S + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 벽돌: 둥근 사각에 가로 이음매, cracks 만큼 사선 금이 간다
    static Sprite MakeBrickSprite(int cracks)
    {
        const int S = 32; const float r = 4f;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                float g = Mathf.Lerp(0.78f, 1.0f, fy / (S - 1));
                if (y % 10 == 0 || (y / 10) % 2 == 0 && x == (S / 2)) g *= 0.72f;   // 벽돌 이음매

                // 금: 위에서 아래로 내려오는 사선. cracks 개수만큼.
                for (int k = 0; k < cracks; k++)
                {
                    float cx = S * (0.32f + 0.36f * k);
                    float line = cx + (fy - S * 0.5f) * (k % 2 == 0 ? 0.45f : -0.45f);
                    if (Mathf.Abs(fx - line) < 1.3f) g *= 0.38f;
                }
                px[y * S + x] = new Color(g, g, g, a);
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 무지개: 둥근 사각 안에 대각 방향 무지개 띠
    static Sprite MakeRainbowSprite()
    {
        const int S = 32; const float r = 7f;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                float t = Mathf.Clamp01((fx + fy) / (2f * S));        // 대각 그라데이션
                var c = Palette.HslToRgb(t, 0.85, 0.58);
                float sh = Mathf.Lerp(0.86f, 1.05f, fy / (S - 1));    // 위가 밝게
                px[y * S + x] = new Color(c.r * sh, c.g * sh, c.b * sh, a);
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 소프트 원판 (파티클)
    static Sprite MakeSoftSprite()
    {
        const int S = 16;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        float c = (S - 1) / 2f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1, 1, 1, a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 아이템 아이콘: 반투명 어두운 원판 + 흰 글리프 (타일 색과 무관하게 대비 확보)
    static Sprite MakeIcon(ItemType t)
    {
        const int S = 24;
        var tex = new Texture2D(S, S);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[S * S];
        float c = (S - 1) / 2f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                Color col = Color.clear;
                if (dist <= 11f) col = new Color(0, 0, 0, 0.5f);

                bool glyph = false;
                switch (t)
                {
                    case ItemType.Row: glyph = Mathf.Abs(dy) <= 2f && Mathf.Abs(dx) <= 8.5f; break;
                    case ItemType.Col: glyph = Mathf.Abs(dx) <= 2f && Mathf.Abs(dy) <= 8.5f; break;
                    case ItemType.Diag:
                        glyph = (Mathf.Abs(dx - dy) <= 2f || Mathf.Abs(dx + dy) <= 2f) && dist <= 9f;
                        break;
                    case ItemType.Bomb5: glyph = dist <= 6f; break;
                    case ItemType.ColorClear:
                        // 8방향 스파클
                        glyph = ((Mathf.Abs(dx) <= 1.6f || Mathf.Abs(dy) <= 1.6f) && Mathf.Abs(dx) + Mathf.Abs(dy) <= 9f)
                             || (Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) <= 1.4f && dist <= 6.5f);
                        break;
                }
                if (glyph) col = Color.white;
                px[y * S + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }
}
