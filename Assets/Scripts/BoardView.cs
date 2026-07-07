// BoardView.cs — 보드 렌더링/연출 계층.
// 스프라이트(타일/아이템 아이콘)는 전부 런타임 생성 — 외부 에셋 의존 없음.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ColorMatcher.Core;

public class BoardView : MonoBehaviour
{
    static readonly Color EmptyColor = new Color(0.06f, 0.06f, 0.08f);
    const float TileScale = 0.92f;

    SpriteRenderer[,] tiles;
    SpriteRenderer[,] overlays;   // 아이템 아이콘
    SpriteRenderer[] ghost;
    Sprite square;
    readonly Dictionary<ItemType, Sprite> icons = new Dictionary<ItemType, Sprite>();
    bool built;

    public void Build()
    {
        if (built) return;
        built = true;

        square = MakeSquareSprite();
        icons[ItemType.Row] = MakeIcon(ItemType.Row);
        icons[ItemType.Col] = MakeIcon(ItemType.Col);
        icons[ItemType.Diag] = MakeIcon(ItemType.Diag);
        icons[ItemType.Bomb9] = MakeIcon(ItemType.Bomb9);
        icons[ItemType.ColorClear] = MakeIcon(ItemType.ColorClear);

        var frameGo = new GameObject("frame");
        frameGo.transform.SetParent(transform, false);
        frameGo.transform.localPosition = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f, 1);
        frameGo.transform.localScale = new Vector3(Board.W + 0.7f, Board.H + 0.7f, 1);
        var fsr = frameGo.AddComponent<SpriteRenderer>();
        fsr.sprite = square;
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
                sr.sprite = square;
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
        for (int i = 0; i < ghost.Length; i++)
        {
            var go = new GameObject("ghost_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.7f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = square;
            sr.sortingOrder = 5;
            sr.enabled = false;
            ghost[i] = sr;
        }
    }

    public void SetVisible(bool v) { gameObject.SetActive(v); }

    /// <summary>보드 최종 상태를 즉시 반영 (색/아이템/위치·스케일 리셋)</summary>
    public void Refresh(Board b, Color[] palette)
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int c = b.GetTile(x, y);
                tiles[x, y].color = c == Board.Empty ? EmptyColor : palette[c];
                tiles[x, y].transform.localPosition = new Vector3(x, y, 0);
                tiles[x, y].transform.localScale = Vector3.one * TileScale;

                var it = b.GetItem(x, y);
                overlays[x, y].enabled = it != ItemType.None;
                if (it != ItemType.None) overlays[x, y].sprite = icons[it];
                overlays[x, y].transform.localPosition = new Vector3(x, y, -0.5f);
            }
    }

    /// <summary>스탬프 연출: 찍힌 칸을 조각 색으로 칠하고 살짝 팝</summary>
    public void PaintCells(List<Point> cells, Color c)
    {
        foreach (var p in cells)
        {
            tiles[p.X, p.Y].color = c;
            tiles[p.X, p.Y].transform.localScale = Vector3.one * 1.08f;
        }
        StartCoroutine(ShrinkCells(cells));
    }

    IEnumerator ShrinkCells(List<Point> cells)
    {
        float t = 0, d = 0.12f;
        while (t < d)
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(1.08f, TileScale, t / d);
            foreach (var p in cells) tiles[p.X, p.Y].transform.localScale = Vector3.one * s;
            yield return null;
        }
        foreach (var p in cells) tiles[p.X, p.Y].transform.localScale = Vector3.one * TileScale;
    }

    /// <summary>파괴 직전 플래시</summary>
    public void FlashCells(List<Point> pts)
    {
        foreach (var p in pts) tiles[p.X, p.Y].color = Color.white;
    }

    /// <summary>바뀐 칸들이 위에서 떨어져 들어오는 낙하 연출. 색은 이미 Refresh로 최종 상태.</summary>
    public IEnumerator FallIn(bool[,] changed, float dur)
    {
        var xs = new List<int>(); var ys = new List<int>(); var delays = new List<float>();
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (changed[x, y]) { xs.Add(x); ys.Add(y); delays.Add((Board.H - 1 - y) * 0.02f); }
        if (xs.Count == 0) yield break;

        const float drop = 3.5f;
        float maxDelay = (Board.H - 1) * 0.02f;
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
        for (int i = 0; i < ghost.Length; i++)
        {
            if (i < p.Cells.Count)
            {
                int gx = ax + p.Cells[i].X, gy = ay + p.Cells[i].Y;
                ghost[i].enabled = true;
                ghost[i].transform.localPosition = new Vector3(gx, gy, -1);
                ghost[i].color = can
                    ? new Color(pieceColor.r, pieceColor.g, pieceColor.b, 0.95f)
                    : new Color(1, 1, 1, 0.25f);
            }
            else ghost[i].enabled = false;
        }
    }

    public void HideGhost()
    {
        if (ghost == null) return;
        foreach (var g in ghost) if (g != null) g.enabled = false;
    }

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
                    case ItemType.Bomb9: glyph = dist <= 6f; break;
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
