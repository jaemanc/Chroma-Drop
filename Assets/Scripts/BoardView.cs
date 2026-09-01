// BoardView.cs — 보드 렌더링/연출 계층.
// 스프라이트(타일/아이템 아이콘/파티클)는 전부 런타임 생성 — 외부 에셋 의존 없음.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ColorMatcher.Core;

public class BoardView : MonoBehaviour
{
    // chroma-drop.html 기준 — 밝은 보드
    static readonly Color EmptyColor = Palette.Hex(0xE9F4EA);   // 격자 안쪽 — 숲 톤
    // 배경과 동떨어져 보이지 않게 테두리도 완전한 잉크색 대신 살짝 투명하게 쓴다
    static readonly Color BoardInk = new Color(0.106f, 0.129f, 0.255f, 0.55f);
    const float TileScale = 0.92f;

    SpriteRenderer[,] tiles;
    SpriteRenderer[,] overlays;   // 아이템 아이콘
    SpriteRenderer[] ghost;
    SpriteRenderer[] ghostRing;   // 고스트 테두리 — 타일 색과 무관하게 위치를 읽히게 한다
    int ghostCount;               // 현재 표시 중인 고스트 칸 수 (펄스용)
    Color ghostRingColor;
    Sprite tile;                  // 둥근 모서리 + 세로 그라데이션 (타일/고스트)
    Sprite panel;                 // 둥근 사각 (판)
    Sprite ring;                  // 둥근 사각 테두리 (고스트)
    Sprite soft;                  // 파티클용 소프트 원
    Sprite[] ice;               // 내구도별 얼음 (금 0/1/2줄)
    readonly Dictionary<ItemType, Sprite> icons = new Dictionary<ItemType, Sprite>();
    bool built;

    // ---- 파티클 풀 (파괴 버스트 타격감) ----
    const int MaxParts = 280;   // 파괴 버스트 + 착지 먼지가 겹칠 수 있다
    Transform[] pTr;
    SpriteRenderer[] pSr;
    Vector2[] pVel;
    float[] pLife, pMax, pSpin, pRot, pSize;
    int liveParts;

    // ---- 착지 충격파 링 ----
    const int MaxRings = 12;
    Sprite shock;                 // 원형 링
    Transform[] rTr;
    SpriteRenderer[] rSr;
    float[] rLife, rMax, rFrom, rTo;
    int liveRings;

    public void Build()
    {
        if (built) return;
        built = true;

        tile = MakeTileSprite();
        panel = MakePanelSprite();
        ring = MakeRingSprite();
        soft = MakeSoftSprite();
        // 내구도 단계마다 금이 한 줄씩 늘어난다 (온전함 → 다 깨지기 직전)
        ice = new Sprite[Rules.IceHp];
        for (int i = 0; i < ice.Length; i++) ice[i] = MakeIceSprite(i);
        icons[ItemType.Row] = MakeIcon(ItemType.Row);
        icons[ItemType.Col] = MakeIcon(ItemType.Col);
        icons[ItemType.Diag] = MakeIcon(ItemType.Diag);
        icons[ItemType.Bomb5] = MakeIcon(ItemType.Bomb5);
        icons[ItemType.ColorClear] = MakeIcon(ItemType.ColorClear);

        // 판: 직각 모서리. 얇은 잉크 테두리 + 반투명 안쪽만 두어 배경과 이어지게 한다.
        var center = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f, 1);
        MakePanel("frame_ink", center, Board.W + 0.78f, Board.H + 0.78f, BoardInk, -5);
        MakePanel("frame_grid", center, Board.W + 0.60f, Board.H + 0.60f,
                  new Color(EmptyColor.r, EmptyColor.g, EmptyColor.b, 0.55f), -4);

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
            rg.transform.localScale = Vector3.one;   // 칸 크기 — 이웃 고스트와 겹치지 않는다
            var rsr = rg.AddComponent<SpriteRenderer>();
            rsr.sprite = ring;
            rsr.sortingOrder = 5;
            rsr.enabled = false;
            ghostRing[i] = rsr;

            var go = new GameObject("ghost_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.98f;   // 밑 타일(0.92)보다는 크되 칸은 안 넘는다
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tile;
            sr.sortingOrder = 6;
            sr.enabled = false;
            ghost[i] = sr;
        }

        BuildParticlePool();
        BuildRingPool();
    }

    void MakePanel(string name, Vector3 center, float w, float h, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = new Vector3(w, h, 1);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = panel;
        sr.color = c;
        sr.sortingOrder = order;
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

    void BuildRingPool()
    {
        shock = MakeShockSprite();
        rTr = new Transform[MaxRings];
        rSr = new SpriteRenderer[MaxRings];
        rLife = new float[MaxRings]; rMax = new float[MaxRings];
        rFrom = new float[MaxRings]; rTo = new float[MaxRings];
        var root = new GameObject("rings").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < MaxRings; i++)
        {
            var go = new GameObject("ring" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = shock;
            sr.sortingOrder = 9;          // 파티클(8)보다 위
            sr.enabled = false;
            rTr[i] = go.transform;
            rSr[i] = sr;
        }
    }

    /// <summary>착지 지점에서 확 퍼졌다가 사라지는 충격파.</summary>
    void SpawnShockwave(float x, float y, Color col, float strength)
    {
        int idx = -1;
        for (int i = 0; i < MaxRings; i++)
            if (!rSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        rLife[idx] = 0f;
        rMax[idx] = 0.20f;
        rFrom[idx] = 0.35f;
        rTo[idx] = Mathf.Lerp(1.9f, 3.2f, strength);
        rTr[idx].localPosition = new Vector3(x, y, -1.5f);
        rTr[idx].localScale = Vector3.one * rFrom[idx];
        var c = Color.Lerp(col, Color.white, 0.65f);
        c.a = 0.95f;
        rSr[idx].color = c;
        rSr[idx].enabled = true;
        liveRings++;
    }

    void UpdateRings(float dt)
    {
        if (liveRings <= 0) return;
        for (int i = 0; i < MaxRings; i++)
        {
            if (!rSr[i].enabled) continue;
            rLife[i] += dt;
            float k = rLife[i] / rMax[i];
            if (k >= 1f) { rSr[i].enabled = false; liveRings--; continue; }
            // 반경은 초반에 확 퍼지고, 알파는 급격히 빠진다
            float e = 1f - Mathf.Pow(1f - k, 3f);
            rTr[i].localScale = Vector3.one * Mathf.Lerp(rFrom[i], rTo[i], e);
            var c = rSr[i].color;
            c.a = 0.95f * (1f - k) * (1f - k);
            rSr[i].color = c;
        }
    }

    public void SetVisible(bool v) { gameObject.SetActive(v); }

    /// <summary>칸 하나를 그린다. 아이템 아이콘까지 여기서 함께 다룬다 —
    /// 얼음은 아래 색 칸 + 구멍 뚫린 콘크리트 덮개 두 겹이라 두 레이어를 같이 정해야 한다.</summary>
    void PaintTile(int x, int y, int c, int hp, ItemType item, Color[] palette)
    {
        var sr = tiles[x, y];
        var ov = overlays[x, y];

        if (c == Board.Ice)
        {
            int stage = Mathf.Clamp(Rules.IceHp - hp, 0, ice.Length - 1);

            // 아래층: 마지막 단계에서만 조각 틈으로 색이 비친다. 그 전에는 배경색이라
            // 얼음 주위에 유채색 테두리가 남지 않는다.
            sr.sprite = tile;
            sr.color = stage >= 2
                ? Color.Lerp(UnderColor(x, y, palette), Color.white, 0.22f)
                : EmptyColor;
            sr.transform.localScale = Vector3.one * IceStyle.Scale;

            // 위층: 얼음 본체. 색은 스프라이트에 구워져 있으므로 틴트는 흰색.
            ov.enabled = true;
            ov.sprite = ice[stage];
            ov.color = Color.white;
            ov.transform.localScale = Vector3.one * IceStyle.Scale;
            return;
        }

        sr.sprite = tile;
        sr.color = c == Board.Empty ? EmptyColor : palette[c];
        sr.transform.localScale = Vector3.one * TileScale;

        ov.enabled = item != ItemType.None;
        if (item != ItemType.None)
        {
            ov.sprite = icons[item];
            ov.transform.localScale = Vector3.one * 0.8f;   // 아이콘은 작게
        }
    }

    /// <summary>얼음 아래 깔린 색. 좌표 해시라 판이 바뀌어도 같은 칸은 같은 색으로 남는다.
    /// 규칙상 의미는 없고 손상 단계를 읽히게 하는 표시다.</summary>
    static Color UnderColor(int x, int y, Color[] palette)
    {
        int h = (x * 73856093) ^ (y * 19349663);
        return palette[Mathf.Abs(h) % palette.Length];
    }

    /// <summary>보드 최종 상태를 즉시 반영 (색/아이템/위치·스케일 리셋)</summary>
    public void Refresh(Board b, Color[] palette)
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                PaintTile(x, y, b.GetTile(x, y), b.GetIceHp(x, y), b.GetItem(x, y), palette);
                tiles[x, y].transform.localPosition = new Vector3(x, y, 0);
                overlays[x, y].transform.localPosition = new Vector3(x, y, -0.5f);
            }
    }

    /// <summary>연쇄 한 단계가 끝난 시점의 보드를 반영 (색/아이템만; 위치는 FallIn 이 잡는다).</summary>
    public void ApplyState(int[] t, ItemType[] it, int[] hp, Color[] palette)
    {
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int k = x * Board.H + y;
                PaintTile(x, y, t[k], hp[k], it[k], palette);
            }
    }

    /// <summary>충격 커브 — 부드럽게 수렴하지 않고, 튕겨 올라갔다가 몇 번 진동하며 멎는다.
    /// 부드러운 ease-out 은 '착지'가 아니라 '내려놓기'처럼 보인다.</summary>
    static float Impact(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float p = 0.30f;                       // 진동 주기 — 작을수록 날카롭다
        return 1f + Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - p * 0.25f) * (2f * Mathf.PI) / p);
    }

    /// <summary>착지 연출. 눌렸다가 튕겨 돌아오고, 바닥에서 먼지가 좌우로 퍼진다.
    /// 세게 떨어진 칸일수록 더 많이 눌리고 먼지도 많다.</summary>
    public IEnumerator LandCells(List<Point> pts, float dur)
    {
        if (pts == null || pts.Count == 0) yield break;

        var baseCols = new Color[pts.Count];
        var amps = new float[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            baseCols[i] = tiles[p.X, p.Y].color;
            float d;
            LastDrop.TryGetValue(p.X * Board.H + p.Y, out d);
            amps[i] = Mathf.Clamp(d / 6f, 0.25f, 1f);     // 낙하 거리 → 찌그러지는 정도
        }

        SpawnLandingImpact(pts, amps, baseCols);

        const float HoldSec = 0.035f;   // 눌린 채로 버티는 2프레임 — 여기서 '맞았다'가 읽힌다
        const float FlashSec = 0.033f;  // 화이트 플래시 1~2프레임

        float t = 0;
        while (t < dur + HoldSec)
        {
            t += Time.deltaTime;
            // 홀드 구간에서는 완전히 눌린 상태를 유지하고, 그 뒤에 튕겨 돌아온다
            float e = t <= HoldSec ? 0f : Impact(Mathf.Clamp01((t - HoldSec) / dur));
            bool flash = t <= FlashSec;

            for (int i = 0; i < pts.Count; i++)
            {
                var sr = tiles[pts[i].X, pts[i].Y];
                float a = 0.42f * amps[i];                 // 최대 42% 눌림 — 이전 30% 보다 깊게
                float sx = Mathf.Lerp(1f + a * 0.7f, 1f, e);
                float sy = Mathf.Lerp(1f - a, 1f, e);
                sr.transform.localScale = new Vector3(TileScale * sx, TileScale * sy, 1f);
                sr.transform.localPosition = new Vector3(pts[i].X, pts[i].Y - (1f - e) * a * 0.40f, 0);
                // 첫 1~2프레임만 흰색으로 때리고 바로 원색으로 떨어뜨린다
                sr.color = flash ? Color.white : baseCols[i];
            }
            yield return null;
        }
        for (int i = 0; i < pts.Count; i++)
        {
            var sr = tiles[pts[i].X, pts[i].Y];
            sr.color = baseCols[i];
            sr.transform.localScale = Vector3.one * TileScale;
            sr.transform.localPosition = new Vector3(pts[i].X, pts[i].Y, 0);
        }
    }

    /// <summary>착지 충격: 먼지 + 충격파 링. 열마다 가장 아래 칸에서만 터뜨려 예산을 아낀다.</summary>
    void SpawnLandingImpact(List<Point> pts, float[] amps, Color[] cols)
    {
        var lowest = new Dictionary<int, int>();   // 열 → pts 인덱스
        for (int i = 0; i < pts.Count; i++)
        {
            int c = pts[i].X, cur;
            if (!lowest.TryGetValue(c, out cur) || pts[i].Y < pts[cur].Y) lowest[c] = i;
        }
        // 세게 떨어진 열부터 링을 준다 (풀이 12개라 전부 주면 약한 것에 밀린다)
        var order = new List<int>(lowest.Values);
        order.Sort((a, b) => amps[b].CompareTo(amps[a]));

        for (int n = 0; n < order.Count; n++)
        {
            int i = order[n];
            int dust = Mathf.RoundToInt(Mathf.Lerp(6f, 10f, amps[i]));
            for (int k = 0; k < dust; k++)
                SpawnDust(pts[i].X, pts[i].Y - 0.42f, cols[i], amps[i]);
            if (n < 5 && amps[i] > 0.3f)
                SpawnShockwave(pts[i].X, pts[i].Y, cols[i], amps[i]);
        }
    }

    /// <summary>좌우로 퍼지는 짧은 먼지. 파괴 버스트와 같은 풀을 쓴다.</summary>
    void SpawnDust(float x, float y, Color col, float strength)
    {
        int idx = -1;
        for (int i = 0; i < MaxParts; i++)
            if (!pSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        // 수평에 가까운 각도로만 뿌린다 — 바닥을 스치는 느낌
        float ang = (Random.value < 0.5f ? 0f : Mathf.PI) + Random.Range(-0.5f, 0.5f);
        float spd = Random.Range(1.4f, 3.4f) * (0.6f + strength);
        pVel[idx] = new Vector2(Mathf.Cos(ang), Mathf.Abs(Mathf.Sin(ang)) * 0.55f) * spd;
        pLife[idx] = 0f;
        pMax[idx] = Random.Range(0.20f, 0.30f);
        pSpin[idx] = Random.Range(-220f, 220f);
        pRot[idx] = Random.value * 360f;
        pSize[idx] = Random.Range(0.09f, 0.17f);

        var c = Color.Lerp(col, Color.white, 0.5f);
        c.a = 1f;
        pSr[idx].color = c;
        pTr[idx].localPosition = new Vector3(x + Random.Range(-0.35f, 0.35f), y, -1.2f);
        pTr[idx].localScale = Vector3.one * pSize[idx];
        pTr[idx].localRotation = Quaternion.Euler(0, 0, pRot[idx]);
        pSr[idx].enabled = true;
        liveParts++;
    }

    /// <summary>스탬프 연출. 세 박자로 끊는다:
    ///   ① 들어올림 — 커진 채로 보드 위에 떠오른다 (예비 동작)
    ///   ② 내려찍기 — 가속해서 꽂힌다 (여기서 onImpact)
    ///   ③ 복원 — 눌린 상태로 잠깐 버티다 튕겨 돌아온다
    /// 예비 동작이 없으면 그냥 '나타났다'로 보이고 타격감이 안 산다.</summary>
    public IEnumerator StampCells(List<Point> cells, Color c, float total, System.Action onImpact)
    {
        // 전체 시간을 네 박자로 나눈다 (기본 0.34초 기준). 테스트는 total 을 줄여 빨리 돌린다.
        float LiftSec = total * 0.25f, SlamSec = total * 0.16f,
              HoldSec = total * 0.12f, BackSec = total * 0.47f;
        const float LiftY = 0.34f, LiftScale = 1.26f;
        const float SquashX = 1.30f, SquashY = 0.72f;

        foreach (var p in cells)
        {
            var sr = tiles[p.X, p.Y];
            sr.sprite = tile;
            sr.color = c;
            sr.sortingOrder = 4;              // 들어올린 동안 이웃 타일 위에 뜬다
        }

        // ① 들어올림 — 빠르게 떠올랐다가 정점에서 살짝 머문다
        float t = 0;
        while (t < LiftSec)
        {
            t += Time.deltaTime;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / LiftSec), 3f);
            float sc = Mathf.Lerp(1f, LiftScale, e);
            foreach (var p in cells) Put(p, sc, sc, Mathf.Lerp(0f, LiftY, e));
            yield return null;
        }

        // ② 내려찍기 — 등가속으로 떨어져 마지막 프레임이 가장 빠르다
        t = 0;
        while (t < SlamSec)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / SlamSec);
            float e = k * k;                                  // 가속
            float sx = Mathf.Lerp(LiftScale, SquashX, e);
            float sy = Mathf.Lerp(LiftScale, SquashY, e);
            foreach (var p in cells) Put(p, sx, sy, Mathf.Lerp(LiftY, -0.06f, e));
            yield return null;
        }

        // 임팩트 — 흰 플래시 + 먼지 + 링, 그리고 바깥에서 셰이크/사운드
        foreach (var p in cells) tiles[p.X, p.Y].color = Color.white;
        SpawnStampImpact(cells, c);
        if (onImpact != null) onImpact();

        // ③ 눌린 채로 버틴다 (여기서 '맞았다'가 읽힌다)
        t = 0;
        while (t < HoldSec)
        {
            t += Time.deltaTime;
            foreach (var p in cells) Put(p, SquashX, SquashY, -0.06f);
            yield return null;
        }
        foreach (var p in cells) tiles[p.X, p.Y].color = c;

        // ④ 튕겨 복원
        t = 0;
        while (t < BackSec)
        {
            t += Time.deltaTime;
            float e = Impact(Mathf.Clamp01(t / BackSec));
            float sx = Mathf.Lerp(SquashX, 1f, e);
            float sy = Mathf.Lerp(SquashY, 1f, e);
            foreach (var p in cells) Put(p, sx, sy, Mathf.Lerp(-0.06f, 0f, e));
            yield return null;
        }

        foreach (var p in cells)
        {
            var sr = tiles[p.X, p.Y];
            sr.sortingOrder = 0;
            sr.transform.localScale = Vector3.one * TileScale;
            sr.transform.localPosition = new Vector3(p.X, p.Y, 0);
        }
    }

    void Put(Point p, float sx, float sy, float dy)
    {
        var tr = tiles[p.X, p.Y].transform;
        tr.localScale = new Vector3(TileScale * sx, TileScale * sy, 1f);
        tr.localPosition = new Vector3(p.X, p.Y + dy, 0);
    }

    /// <summary>내려찍은 자리의 먼지와 충격파. 조각의 아래쪽 테두리에서만 터뜨린다.</summary>
    void SpawnStampImpact(List<Point> cells, Color c)
    {
        var lowest = new Dictionary<int, int>();
        for (int i = 0; i < cells.Count; i++)
        {
            int col = cells[i].X, cur;
            if (!lowest.TryGetValue(col, out cur) || cells[i].Y < cells[cur].Y) lowest[col] = i;
        }
        int rings = 0;
        foreach (var kv in lowest)
        {
            var p = cells[kv.Value];
            for (int k = 0; k < 9; k++) SpawnDust(p.X, p.Y - 0.42f, c, 1f);
            if (rings++ < 3) SpawnShockwave(p.X, p.Y, c, 1f);
        }
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
        float dtr = Time.deltaTime;
        UpdateRings(dtr);
        if (liveParts <= 0) return;
        float dt = dtr;
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

    /// <summary>이번 낙하에서 칸마다 떨어진 거리. 착지 연출의 세기로 쓴다.</summary>
    public readonly Dictionary<int, float> LastDrop = new Dictionary<int, float>();
    /// <summary>이번 낙하에서 가장 멀리 떨어진 거리.</summary>
    public float LastMaxDrop { get; private set; }

    /// <summary>stagger = 위쪽 칸이 늦게 떨어지는 간격. 연쇄 단계 사이에는 짧게 줘서 늘어지지 않게 한다.</summary>
    public IEnumerator FallIn(bool[,] changed, float dur, float stagger)
    {
        LastDrop.Clear();
        LastMaxDrop = 0f;

        // 열마다 자기 위로 바뀐 칸이 몇 개인지 세어 낙하 거리로 삼는다.
        // 예전에는 전부 3.5칸 고정이라 한 칸 내려온 블록과 열 전체가 무너진 블록이 똑같이 보였다.
        var xs = new List<int>(); var ys = new List<int>();
        var delays = new List<float>(); var drops = new List<float>();
        for (int x = 0; x < Board.W; x++)
        {
            int run = 0;
            for (int y = Board.H - 1; y >= 0; y--)
            {
                if (!changed[x, y]) { run = 0; continue; }
                run++;
                float d = Mathf.Min(1.2f + run * 0.55f, 6f);
                xs.Add(x); ys.Add(y);
                delays.Add((Board.H - 1 - y) * stagger);
                drops.Add(d);
                LastDrop[x * Board.H + y] = d;
                if (d > LastMaxDrop) LastMaxDrop = d;
            }
        }
        if (xs.Count == 0) yield break;

        float maxDelay = (Board.H - 1) * stagger;
        float t = 0, total = dur + maxDelay;
        while (t < total)
        {
            t += Time.deltaTime;
            for (int i = 0; i < xs.Count; i++)
            {
                float k = Mathf.Clamp01((t - delays[i]) / dur);
                k = 1 - (1 - k) * (1 - k); // ease-out
                float off = (1 - k) * drops[i];
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
        float scale = Mathf.Lerp(0.93f, 1.0f, k);   // 1.0 을 넘지 않게
        var c = ghostRingColor;
        c.a = Mathf.Lerp(0.55f, 1f, k);
        for (int i = 0; i < ghostCount && i < ghostRing.Length; i++)
        {
            if (!ghostRing[i].enabled) continue;
            ghostRing[i].transform.localScale = Vector3.one * scale;
            ghostRing[i].color = c;
        }
    }

    // 직각 사각 — 보드 판용
    static Sprite MakePanelSprite()
    {
        const int S = 8;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Point };
        var px = new Color[S * S];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    const float RimPx = 2.8f;      // 테두리 두께 (32px 스프라이트 기준)
    const float RimDark = 0.80f;   // 테두리 명도 배수 — 1 에 가까울수록 옅다

    // 둥근 모서리 + 위가 밝은 세로 그라데이션 + 같은 색 진한 테두리 (SpriteRenderer.color로 틴트)
    // GameUI 의 배경 블록도 같은 모양을 쓴다.
    public static Sprite MakeTileSprite()
    {
        const int S = 32; const float r = 11f;   // 한 변의 34% — 레퍼런스와 같은 둥글기
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
                float g = Mathf.Lerp(0.88f, 1.0f, fy / (S - 1)); // 밝은 판이라 음영을 약하게
                // 상단 하이라이트 밴드
                if (fy > S - 6) g = Mathf.Min(1f, g + 0.06f);

                // 테두리: 경계 쪽 명도만 낮춘다. 틴트가 곱해지므로 자동으로
                // '그 블록 색의 조금 진한 톤' 이 된다 — 색을 따로 계산할 필요가 없다.
                float edge = r - dist;                                  // 0 = 경계, 안쪽일수록 큼
                float rim = Mathf.Clamp01((RimPx - edge) / 1.3f);
                g *= Mathf.Lerp(1f, RimDark, rim);
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

    // 얼음 블록. 색·형태 상수는 IceStyle 에 모아 두었다.
    // 스프라이트에 실제 색을 구워 넣고 SpriteRenderer 는 흰색으로 둔다 — 틴트가 섞이면
    // 지정한 냉색이 그대로 나오지 않는다.
    //
    // stage 0 = 온전한 얼음 / 1 = 쩍 갈라져 조각이 벌어진 얼음
    // 균열과 도트를 함께 쓴다. 작은 화면(≈22px)에서는 균열이 안 읽히고 도트만 남기 때문이다.
    static Sprite MakeIceSprite(int stage)
    {
        const int S = 48;
        float r = S * IceStyle.RoundFrac;
        float line = S * IceStyle.LineFrac;
        float crack = S * IceStyle.CrackFrac;
        float split = S * IceStyle.SplitFrac;
        float dotR = S * IceStyle.DotRFrac;
        int dots = Rules.IceHp - stage;
        Color body = IceStyle.BodyFor(stage);

        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;

                // 각진 실루엣 — 색 타일보다 모서리가 훨씬 덜 둥글다
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                Color c = body;

                // 결정면 — 대각선 띠 두 줄이 빛을 받아 얼음처럼 보이게 한다
                float diag = (fx + fy) / (2f * S);
                if (diag > 0.16f && diag < 0.30f) c = IceStyle.Light;
                if (diag > 0.54f && diag < 0.62f) c = Color.Lerp(c, IceStyle.Light, 0.65f);
                float anti = (fx - fy) / (float)S;
                if (anti > 0.30f && anti < 0.40f) c = Color.Lerp(c, IceStyle.Shadow, 0.35f);

                // 아래쪽 그늘
                c = Color.Lerp(c, IceStyle.Shadow, Mathf.Clamp01((0.30f - fy / S) * 1.6f));

                // 외곽선 — 색 타일에는 없는 신호라 테두리 자체가 구분 단서가 된다
                float edge = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(S - fx, S - fy));
                if (edge <= line) c = IceStyle.Outline;

                // 균열 / 조각 분리
                float w1 = Mathf.Sin(fy * 0.34f) * S * 0.09f;
                float d1 = Mathf.Abs(fx - (S * 0.44f + w1));
                if (stage == 0)
                {
                    // 온전해도 실금 하나는 넣어 얼음 결처럼 보이게 한다
                    if (d1 < crack * 0.55f) c = Color.Lerp(c, IceStyle.Outline, 0.35f);
                }
                else
                {
                    float w2 = Mathf.Sin(fx * 0.30f + 1.4f) * S * 0.08f;
                    float d2 = Mathf.Abs(fy - (S * 0.62f + w2));
                    float d = Mathf.Min(d1, d2);
                    if (d < split) a = 0f;                                       // 벌어진 틈 — 아래 색이 비친다
                    else if (d < split + line) c = IceStyle.Outline;             // 갈라진 단면
                }

                px[y * S + x] = new Color(c.r, c.g, c.b, a);
            }

        // 남은 내구도 도트 — 균열이 안 보이는 크기에서도 이건 읽힌다
        float cy = S * 0.235f;
        for (int i = 0; i < dots; i++)
        {
            float cx = S * 0.5f + (i - (dots - 1) * 0.5f) * (dotR * 2.45f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    int k = y * S + x;
                    if (d < dotR + 1.4f && d >= dotR && px[k].a > 0f)
                        px[k] = new Color(IceStyle.Light.r, IceStyle.Light.g, IceStyle.Light.b, px[k].a);
                    if (d < dotR)
                        px[k] = new Color(IceStyle.Dot.r, IceStyle.Dot.g, IceStyle.Dot.b, 1f);
                }
        }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    static float Frac(float v) { return v - Mathf.Floor(v); }

    // 충격파용 원형 링 (안쪽이 비어 있다)
    static Sprite MakeShockSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        float c = (S - 1) / 2f, outer = c - 1f, thick = 5.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - (outer - thick));
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
        tex.SetPixels(px);
        tex.Apply();
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
