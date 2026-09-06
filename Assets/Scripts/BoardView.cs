// BoardView.cs — 보드 렌더링/연출 계층.
// 스프라이트(타일/아이템 아이콘/파티클)는 전부 런타임 생성 — 외부 에셋 의존 없음.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ColorMatcher.Core;

public class BoardView : MonoBehaviour
{
    // 보드는 배경보다 확실히 밝고 불투명해야 한다. 반투명으로 두면 일러스트에 묻힌다.
    static readonly Color EmptyColor = Palette.Hex(0xEAF6F2);   // 타일 영역 배경
    static readonly Color BoardCream = Palette.Hex(0xFDFBF2);   // 보드 서피스
    static readonly Color BoardNavy  = Palette.Hex(0x1B2141);   // 테두리·그림자

    // 화면 폭 390px ↔ 월드 16유닛 이므로 1px = 0.041 유닛
    const float Px = 16f / 390f;
    const float TileScale = 0.96f;   // 칸 대비 블록 크기 — 1.0 이면 칸을 꽉 채운다
    // 목표 칸은 '블록에서 빛이 새어나오는' 것으로 표시한다.
    // 블록 모양 그대로 빛이 차 있고, 그 빛이 칸 밖으로 부옇게 번진다.
    const float MarkScale = 1.7f;   // 빛이 번져 나가는 범위 (칸 단위)

    static readonly Color MarkWhite = new Color(1f, 1f, 1f, 0.95f);               // 별빛 가루의 밝은 쪽
    static readonly Color MarkGold = new Color(1f, 0.85f, 0.30f, 0.95f);          // 별빛 가루의 노란 쪽

    // 가장자리 반짝임은 빨 → 노 → 초 → 파 를 돌고 다시 빨로 잇는다.
    static readonly Color[] MarkCycle = {
        new Color(1.00f, 0.30f, 0.32f),   // 빨
        new Color(1.00f, 0.85f, 0.25f),   // 노
        new Color(0.36f, 0.86f, 0.42f),   // 초
        new Color(0.34f, 0.62f, 1.00f),   // 파
    };
    static readonly Color SteelSpark = new Color(0.78f, 0.86f, 0.96f);            // 강철에서 튀는 차가운 빛
    static readonly Color MarkDim = new Color(0.07f, 0.09f, 0.18f);               // 빛이 잦아든 쪽 (판 네이비)
    static readonly Color MarkDoneColor = new Color(0.72f, 0.76f, 0.80f, 0.16f);  // 깬 칸 — 흔적만 남긴다

    // 오염 스테이지에서는 벽돌·강철이 오염으로 읽혀야 한다. 스프라이트는 그대로 두고 색만 물들인다.
    // 독을 뜻하는 색은 초록이다 — 보라로는 '오염' 이 안 읽힌다.
    static readonly Color RotColor = new Color(0.55f, 0.95f, 0.35f);        // 번진 오염 — 깰 수 있다
    static readonly Color RotSourceColor = new Color(0.20f, 0.55f, 0.18f);  // 오염 근원 — 못 깬다
    static readonly Color RotSmoke = new Color(0.45f, 0.90f, 0.35f);        // 피어오르는 독연기

    SpriteRenderer[,] tiles;
    SpriteRenderer[,] overlays;   // 아이템 아이콘
    SpriteRenderer[,] marks;      // 좌표 목표 — 블록 가장자리에서 바깥으로 번지는 빛
    SpriteRenderer[,] markFills;  // 그 블록 위를 덮는 그라데이션 — 칸 자체가 물들어 보이게
    readonly List<Point> markList = new List<Point>();    // 아직 깨야 하는 칸
    readonly List<Point> steelList = new List<Point>();   // 지금 판에 있는 강철 자리
    readonly List<Point> rotList = new List<Point>();     // 오염 칸 자리 (연기가 피어오른다)
    float smokeTimer;
    Sprite markGlow, markFill;

    // 목표 칸에서 피어오르는 별빛 가루. 파괴 버스트와 풀을 나눠 쓴다 —
    // 버스트가 풀을 가득 채운 순간에 반짝임이 통째로 멈추면 안 된다.
    const int MaxSparks = 600;
    Sprite star;
    Transform[] kTr;
    SpriteRenderer[] kSr;
    Vector2[] kVel;
    float[] kLife, kMax, kSpin, kRot, kSize, kPhase;
    bool[] kSmoke;                // 이 알갱이가 별빛인가 연기인가
    int liveSparks;
    float sparkTimer;
    bool rotStage;                // 오염 스테이지인가 — 벽돌·강철을 오염 색으로 물들인다
    SpriteRenderer[] ghost;
    SpriteRenderer[] ghostRing;   // 놓일 자리 윤곽 — 타일 색과 무관하게 위치를 읽히게 한다
    SpriteRenderer[] carryShadow; // 들고 있는 조각 아래 그림자
    int ghostCount;               // 현재 표시 중인 고스트 칸 수 (펄스용)
    Color ghostRingColor;
    Sprite bomb;                  // 폭탄 조각 아이콘
    Sprite[] steelStages;         // 강철 — 손상 단계별 (0 = 온전)
    int obstacleMaxHp = Rules.ObstacleHp;   // 이 판의 방해블록 내구도

    /// <summary>이번 판의 방해블록 내구도. 손상 단계를 이 값에 맞춰 환산한다.</summary>
    public void SetObstacleMaxHp(int hp) { obstacleMaxHp = Mathf.Max(1, hp); }

    int steelMaxHp = 5;   // 이 판의 강철 내구도. 손상 단계를 이 값에 맞춰 환산한다.

    /// <summary>이번 판의 강철 내구도.</summary>
    public void SetSteelMaxHp(int hp) { steelMaxHp = Mathf.Max(1, hp); }
    SpriteRenderer[] blast;       // 영향 범위 미리보기 (폭탄 5x5, 매칭 예고)
    int blastCount;
    Color blastColor;
    int[] ghostX, ghostY;         // 고스트 칸 좌표 — 사라질 자리인지 대조한다
    Color[] ghostBase;            // 깜빡이기 전 색
    readonly HashSet<int> doomed = new HashSet<int>();
    Sprite tile;                  // 현재 스킨의 타일 (타일/고스트 공용)
    TileSkin currentSkin = TileSkin.Glossy;
    Sprite ring;                  // 둥근 사각 테두리 (고스트)
    Sprite soft;                  // 파티클용 소프트 원
    Sprite[] obstacle;               // 내구도별 콘크리트 (금 0/1/2줄)
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

        currentSkin = Wallet.Skin;
        tile = MakeTileSprite(currentSkin);
        markGlow = MakeMarkGlowSprite();
        markFill = MakeMarkFillSprite();
        ring = MakeRingSprite();
        soft = MakeSoftSprite();
        // 내구도 단계마다 금이 한 줄씩 늘어난다 (온전함 → 다 깨지기 직전)
        obstacle = new Sprite[ObstacleStyle.Stages];
        for (int i = 0; i < obstacle.Length; i++) obstacle[i] = MakeObstacleSprite(i);
        icons[ItemType.Row] = MakeIcon(ItemType.Row);
        icons[ItemType.Col] = MakeIcon(ItemType.Col);
        icons[ItemType.Diag] = MakeIcon(ItemType.Diag);
        icons[ItemType.Bomb5] = MakeIcon(ItemType.Bomb5);

        // 보드 레이어 (뒤 → 앞):
        //   ① 흰색 20% 헤일로 — 보드 뒤 배경의 대비를 눌러 경계를 만든다
        //   ② 하드 그림자 (offset 6px, 블러 없음)  ③ 네이비 테두리 5px, radius 24px
        //   ④ 크림 서피스  ⑤ 타일 영역
        var center = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f, 1);
        float radius = 24f * Px;
        // 판 여백을 줄인 만큼 보드를 키울 수 있다 — 카메라 크기를 정하는 건 가로다.
        float inner = Board.H + 0.20f;
        float surface = inner + 0.19f;
        float border = surface + 2f * (5f * Px);

        MakePanel("halo", center, border + 1.6f, border + 1.6f, new Color(1, 1, 1, 0.20f), -8, radius * 1.6f);
        MakePanel("shadow", center + new Vector3(0, -6f * Px, 0), border, border, BoardNavy, -7, radius);
        MakePanel("border", center, border, border, BoardNavy, -6, radius);
        MakePanel("surface", center, surface, surface, BoardCream, -5, radius - 5f * Px);
        MakePanel("grid", center, inner, inner, EmptyColor, -4, radius - 9f * Px);

        tiles = new SpriteRenderer[Board.W, Board.H];
        overlays = new SpriteRenderer[Board.W, Board.H];
        marks = new SpriteRenderer[Board.W, Board.H];
        markFills = new SpriteRenderer[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                // 블록 위를 덮는 그라데이션. 블록 색을 지우지 않고 물들이는 정도로만 얹는다.
                var fg = new GameObject("mf_" + x + "_" + y);
                fg.transform.SetParent(transform, false);
                fg.transform.localPosition = new Vector3(x, y, -0.1f);
                fg.transform.localScale = Vector3.one * TileScale;
                var fsr = fg.AddComponent<SpriteRenderer>();
                fsr.sprite = markFill;
                fsr.sortingOrder = 1;       // 타일(0) 위, 아이템 아이콘(2) 아래
                fsr.enabled = false;
                markFills[x, y] = fsr;

                // 가장자리 반짝임 — 블록보다 크고, 아이콘 위까지 올라온다
                var mg = new GameObject("m_" + x + "_" + y);
                mg.transform.SetParent(transform, false);
                mg.transform.localPosition = new Vector3(x, y, -0.2f);
                mg.transform.localScale = Vector3.one * MarkScale;
                var msr = mg.AddComponent<SpriteRenderer>();
                msr.sprite = markGlow;
                msr.sortingOrder = 3;       // 타일(0)·아이템(2) 위, 범위 예고(4) 아래
                msr.enabled = false;
                marks[x, y] = msr;

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
        ghostX = new int[8]; ghostY = new int[8]; ghostBase = new Color[8];
        carryShadow = new SpriteRenderer[8];
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

            var sg = new GameObject("carryshadow_" + i);
            sg.transform.SetParent(transform, false);
            var ssr = sg.AddComponent<SpriteRenderer>();
            ssr.sprite = tile;
            ssr.sortingOrder = 5;
            ssr.enabled = false;
            carryShadow[i] = ssr;

            var go = new GameObject("ghost_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one;   // 밑 타일보다 크되 칸은 안 넘는다
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tile;
            sr.sortingOrder = 6;
            sr.enabled = false;
            ghost[i] = sr;
        }

        // 폭발 범위 미리보기 — 고스트(5,6)보다 아래, 타일 위
        bomb = MakeBombSprite();
        steelStages = new Sprite[ObstacleStyle.Stages];
        for (int i = 0; i < steelStages.Length; i++) steelStages[i] = MakeSteelSprite(i);
        var blastSprite = MakePanelSprite(0.3f);
        blast = new SpriteRenderer[96];   // 대각선·행/열 아이템까지 덮을 만큼
        for (int i = 0; i < blast.Length; i++)
        {
            var bg = new GameObject("blast_" + i);
            bg.transform.SetParent(transform, false);
            var bsr = bg.AddComponent<SpriteRenderer>();
            bsr.sprite = blastSprite;
            bsr.sortingOrder = 4;
            bsr.enabled = false;
            blast[i] = bsr;
        }

        BuildTray();
        BuildParticlePool();
        BuildSparklePool();
        BuildRingPool();
    }

    /// <summary>모서리 반경을 월드 단위로 지정한다. 스프라이트가 스케일되므로
    /// 판 크기에 대한 비율로 환산해 굽는다 — 그래야 레이어마다 반경이 같아 보인다.</summary>
    void MakePanel(string name, Vector3 center, float w, float h, Color c, int order, float radiusWorld)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = new Vector3(w, h, 1);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = MakePanelSprite(Mathf.Clamp01(radiusWorld / w));
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

    void BuildSparklePool()
    {
        star = MakeStarSprite();
        kTr = new Transform[MaxSparks];
        kSr = new SpriteRenderer[MaxSparks];
        kVel = new Vector2[MaxSparks];
        kLife = new float[MaxSparks];
        kMax = new float[MaxSparks];
        kSpin = new float[MaxSparks];
        kRot = new float[MaxSparks];
        kSize = new float[MaxSparks];
        kPhase = new float[MaxSparks];
        kSmoke = new bool[MaxSparks];

        var root = new GameObject("sparkles").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < MaxSparks; i++)
        {
            var go = new GameObject("k" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = star;
            sr.sortingOrder = 7;      // 블록·표시 위, 파괴 파티클(8) 아래
            sr.enabled = false;
            kTr[i] = go.transform;
            kSr[i] = sr;
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
    /// 콘크리트는 아래 색 칸 + 구멍 뚫린 콘크리트 덮개 두 겹이라 두 레이어를 같이 정해야 한다.</summary>
    void PaintTile(int x, int y, int c, int hp, ItemType item, Color[] palette)
    {
        var sr = tiles[x, y];
        var ov = overlays[x, y];

        if (c == Board.Steel)
        {
            // 내구도 0 인 강철(오염 근원)은 늘 온전한 모습이다 — 안 부서지니 상태도 없다.
            // 내구도를 준 강철은 맞을수록 금이 가고 모서리가 부스러진다.
            int st = hp > 0 ? ObstacleStyle.StageFor(hp, steelMaxHp) : 0;

            sr.sprite = tile;
            sr.color = EmptyColor;
            sr.transform.localScale = Vector3.one * ObstacleStyle.Scale;

            ov.enabled = true;
            ov.sprite = steelStages[st];
            ov.color = rotStage ? RotSourceColor : Color.white;
            ov.transform.localScale = Vector3.one * ObstacleStyle.Scale;
            return;
        }

        if (c == Board.Obstacle)
        {
            // 내구도는 스테이지마다 다르다. 상수로 계산하면 손상이 안 보인 채로
            // 한 방에 사라지는 것처럼 보인다 — 이 판의 최대 내구도를 기준으로 환산한다.
            int stage = ObstacleStyle.StageFor(hp, obstacleMaxHp);

            // 아래층: 마지막 단계에서만 조각 틈으로 색이 비친다. 그 전에는 배경색이라
            // 콘크리트 주위에 유채색 테두리가 남지 않는다.
            sr.sprite = tile;
            sr.color = stage >= 2
                ? Color.Lerp(UnderColor(x, y, palette), Color.white, 0.22f)
                : EmptyColor;
            sr.transform.localScale = Vector3.one * ObstacleStyle.Scale;

            // 위층: 콘크리트 본체. 색은 스프라이트에 구워져 있으므로 틴트는 흰색.
            ov.enabled = true;
            ov.sprite = obstacle[stage];
            ov.color = rotStage ? RotColor : Color.white;
            ov.transform.localScale = Vector3.one * ObstacleStyle.Scale;
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

    /// <summary>콘크리트 아래 깔린 색. 좌표 해시라 판이 바뀌어도 같은 칸은 같은 색으로 남는다.
    /// 규칙상 의미는 없고 손상 단계를 읽히게 하는 표시다.</summary>
    static Color UnderColor(int x, int y, Color[] palette)
    {
        int h = (x * 73856093) ^ (y * 19349663);
        return palette[Mathf.Abs(h) % palette.Length];
    }

    /// <summary>보드 최종 상태를 즉시 반영 (색/아이템/위치·스케일 리셋)</summary>
    public void Refresh(Board b, Color[] palette)
    {
        steelList.Clear();   // 강철은 중력으로 자리를 옮기므로 그릴 때마다 다시 모은다
        rotList.Clear();
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int t = b.GetTile(x, y);
                int hp = t == Board.Steel ? b.GetSteelHp(x, y) : b.GetObstacleHp(x, y);
                if (t == Board.Steel) steelList.Add(new Point(x, y));
                if (rotStage && (t == Board.Steel || t == Board.Obstacle)) rotList.Add(new Point(x, y));
                PaintTile(x, y, t, hp, b.GetItem(x, y), palette);
                tiles[x, y].transform.localPosition = new Vector3(x, y, 0);
                overlays[x, y].transform.localPosition = new Vector3(x, y, -0.5f);
            }
    }

    /// <summary>좌표 목표를 표시한다. null 이면 이 스테이지엔 목표 칸이 없다.</summary>
    public void SetMarks(bool[,] m)
    {
        if (marks == null) return;

        markList.Clear();
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                bool on = m != null && m[x, y];
                marks[x, y].enabled = on;
                markFills[x, y].enabled = on;
                if (on) markList.Add(new Point(x, y));
            }
        PulseMarks();
    }

    /// <summary>오염 스테이지인가. 이 판에서는 벽돌이 곧 오염이고, 못 깨는 칸은 오염 근원이다 —
    /// 그래서 칸마다 따로 표시할 필요 없이 판 전체를 한 번만 정해 주면 된다.</summary>
    public void SetPollutionStage(bool on) { rotStage = on; }

    /// <summary>목표 칸 하나를 깼다. 지우지 않고 흐리게 남긴다 — 어디를 깼는지 보이게.</summary>
    public void ClearMark(int x, int y)
    {
        if (marks == null || !marks[x, y].enabled) return;
        for (int i = 0; i < markList.Count; i++)
            if (markList[i].X == x && markList[i].Y == y) { markList.RemoveAt(i); break; }

        // 새어나오는 빛은 끈다. 켜 둔 채 색만 바꾸면 칸 전체가 계속 물들어 있어
        // 아직 깨야 하는 칸처럼 보인다.
        marks[x, y].enabled = false;

        // 흔적은 칸 안쪽에만, 맥동 없이 옅게 남긴다 — 어디를 이미 깼는지는 보여야 한다.
        markFills[x, y].enabled = true;
        markFills[x, y].color = MarkDoneColor;
    }

    /// <summary>남은 목표 칸의 가장자리를 빨·노·초·파 순으로 물들인다.
    /// 칸마다 위상을 어긋나게 줘서 칸 하나하나가 따로 읽힌다.</summary>
    void PulseMarks()
    {
        float t = Time.time;
        int n = MarkCycle.Length;

        for (int i = 0; i < markList.Count; i++)
        {
            int x = markList[i].X, y = markList[i].Y;
            // 가로·세로로 다른 보폭이라 이웃 칸끼리 색이 겹치지 않는다
            float phase = Frac(x * 0.29f + y * 0.47f);

            // 색 사이를 이어서 건너간다 — 뚝뚝 끊기면 반짝임이 아니라 점멸로 보인다
            float u = (Frac(t * 0.42f + phase)) * n;
            int a = (int)u % n;
            var c = Color.Lerp(MarkCycle[a], MarkCycle[(a + 1) % n], Frac(u));

            float blink = 0.5f + 0.5f * Mathf.Sin((t + phase * 1.7f) * 4f);
            blink = blink * blink * (3f - 2f * blink);   // 양 끝에 머무는 시간을 늘린다

            // 블록 밖으로 새어나오는 빛. 밝은 쪽에서 확 번지고 어두운 쪽에서는 거의 사라진다.
            c.a = 0.10f + 0.42f * blink;
            marks[x, y].color = c;

            // 블록 속은 밝아지기만 하지 않고 어두워지기도 한다 —
            // 한쪽으로만 움직이면 '조금 밝은 칸' 으로 보여서 눈에 안 띈다.
            // 색은 얹지 않는다. 색까지 칠하면 무슨 색 블록인지 못 읽는다.
            var lit = Color.Lerp(MarkDim, Color.white, blink);
            lit.a = 0.42f;
            markFills[x, y].color = lit;
        }
    }

    /// <summary>남은 목표 칸에서 별빛 가루가 피어오른다. 한 번에 하나씩만 띄워
    /// 칸이 많아도 화면이 번잡해지지 않게 한다.</summary>
    void UpdateSparkles(float dt)
    {
        if (kSr == null || (liveSparks <= 0 && markList.Count == 0 && steelList.Count == 0)) return;

        // 살아 있는 것부터 굴린다
        for (int i = 0; i < MaxSparks; i++)
        {
            if (!kSr[i].enabled) continue;
            kLife[i] += dt;
            float k = kLife[i] / kMax[i];
            if (k >= 1f) { kSr[i].enabled = false; liveSparks--; continue; }

            float t = Time.time + kPhase[i];
            var pos = kTr[i].localPosition;
            var c = kSr[i].color;

            if (kSmoke[i])
            {
                // 연기: 위로만 오르며 점점 커지고 옅어진다. 깜빡이지 않는다.
                kVel[i].y += 0.25f * dt;
                pos.x += (kVel[i].x + Mathf.Sin(t * 1.4f) * 0.35f) * dt;
                pos.y += kVel[i].y * dt;

                kTr[i].localScale = Vector3.one * kSize[i] * (0.4f + k * 1.3f);
                c.a = Mathf.Sin(k * Mathf.PI) * 0.5f;
            }
            else
            {
                // 별빛: 중력을 안 받는다. 퍼지면서 잦아들되 살짝 떠오르고, 좌우로 흔들린다.
                kVel[i] *= 0.985f;
                kVel[i].y += 0.45f * dt;
                pos.x += (kVel[i].x + Mathf.Sin(t * 3.2f) * 0.5f) * dt;
                pos.y += kVel[i].y * dt;

                // 확 나타났다가 오래 사그라든다 — 꺼지는 쪽이 길어야 '흩날린다' 로 읽힌다
                const float PopIn = 0.15f;
                float grow = k < PopIn ? k / PopIn : Mathf.Pow(1f - (k - PopIn) / (1f - PopIn), 0.7f);
                kTr[i].localScale = Vector3.one * kSize[i] * grow;
                c.a = grow * (0.62f + 0.38f * Mathf.Sin(t * 11f));   // 날리는 동안 저 혼자 깜빡인다
            }

            kTr[i].localPosition = pos;
            kRot[i] += kSpin[i] * dt;
            kTr[i].localRotation = Quaternion.Euler(0, 0, kRot[i]);
            kSr[i].color = c;
        }

        int steelSpots = rotStage ? 0 : steelList.Count;   // 오염 판의 강철은 연기가 맡는다
        int spots = markList.Count + steelSpots;
        if (spots == 0) return;

        // 자리가 많을수록 자주 튄다. 고정하면 무늬가 클 때 너무 뜸하다.
        sparkTimer -= dt;
        if (sparkTimer > 0f) return;
        // 하한은 한 프레임(60fps)이다. 이보다 짧게 잡아 봐야 한 프레임에 한 번밖에 못 튄다.
        sparkTimer = Mathf.Max(0.016f, 0.05f / spots);

        // 한 자리에서 여러 개가 같이 튀어야 '가루' 로 보인다. 하나씩이면 점이 하나 뜨는 것뿐이다.
        int pick = Random.Range(0, spots);
        bool onSteel = pick >= markList.Count;
        if (onSteel && steelSpots == 0) return;
        var at = onSteel ? steelList[pick - markList.Count] : markList[pick];

        // 강철은 목표 칸만큼 요란하면 안 된다 — 깨야 할 칸이 묻힌다
        int n = onSteel ? Random.Range(1, 3) : Random.Range(5, 9);
        for (int i = 0; i < n; i++) SpawnSparkle(at, onSteel);
    }

    /// <summary>오염 칸에서 초록 독연기가 천천히 피어오른다. 색만으로는 '깨야 하는 벽돌' 과
    /// 구분이 어렵다 — 움직이는 연기가 있어야 오염이라는 게 바로 읽힌다.</summary>
    void UpdateSmoke(float dt)
    {
        if (kSr == null || rotList.Count == 0) return;

        smokeTimer -= dt;
        if (smokeTimer > 0f) return;
        smokeTimer = Mathf.Max(0.05f, 0.35f / rotList.Count);

        var at = rotList[Random.Range(0, rotList.Count)];
        SpawnSmoke(at);
    }

    void SpawnSmoke(Point at)
    {
        int idx = -1;
        for (int i = 0; i < MaxSparks; i++)
            if (!kSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        // 옆으로는 거의 안 가고 위로만 오른다 — 연기는 퍼지는 게 아니라 오르는 것이다
        kVel[idx] = new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(0.5f, 1.0f));
        kLife[idx] = 0f;
        kMax[idx] = Random.Range(1.1f, 1.8f);
        kSpin[idx] = Random.Range(-25f, 25f);
        kRot[idx] = Random.value * 360f;
        kPhase[idx] = Random.value * 10f;
        kSize[idx] = Random.Range(0.45f, 0.75f);
        kSmoke[idx] = true;

        var c = RotSmoke;
        c.a = 0f;
        kSr[idx].sprite = soft;
        kSr[idx].color = c;
        kTr[idx].localPosition = new Vector3(at.X + Random.Range(-0.2f, 0.2f),
                                             at.Y + Random.Range(-0.2f, 0.2f), -1.25f);
        kTr[idx].localScale = Vector3.zero;
        kTr[idx].localRotation = Quaternion.Euler(0, 0, kRot[idx]);
        kSr[idx].enabled = true;
        liveSparks++;
    }

    void SpawnSparkle(Point at) { SpawnSparkle(at, false); }

    /// <summary>steel 이면 금속이 번쩍이는 정도로 작고 차갑게 튄다.</summary>
    void SpawnSparkle(Point at, bool steel)
    {
        int idx = -1;
        for (int i = 0; i < MaxSparks; i++)
            if (!kSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        float ang = Random.value * Mathf.PI * 2f;
        kVel[idx] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.9f, 2.4f);
        kLife[idx] = 0f;
        kMax[idx] = Random.Range(0.9f, 1.6f);       // 오래 날아야 칸 밖까지 퍼진다
        kSpin[idx] = Random.Range(-160f, 160f);
        kRot[idx] = Random.value * 360f;
        kPhase[idx] = Random.value * 10f;           // 흔들림·깜빡임을 저마다 다른 박자로
        // 큰 별 몇에 작은 가루 여럿 — 크기가 고르면 가루가 아니라 도형이 흩어지는 것처럼 보인다
        kSize[idx] = Random.value < 0.25f ? Random.Range(0.30f, 0.46f) : Random.Range(0.12f, 0.24f);

        if (steel) kSize[idx] *= 0.7f;
        kSmoke[idx] = false;
        kSr[idx].sprite = star;
        var c = steel ? Color.Lerp(Color.white, SteelSpark, Random.value)
                      : Color.Lerp(MarkWhite, MarkGold, Random.value);
        c.a = 0f;                                   // 첫 프레임부터 커지며 나타난다
        kSr[idx].color = c;
        kTr[idx].localPosition = new Vector3(at.X + Random.Range(-0.28f, 0.28f),
                                             at.Y + Random.Range(-0.28f, 0.28f), -1.3f);
        kTr[idx].localScale = Vector3.zero;
        kTr[idx].localRotation = Quaternion.Euler(0, 0, kRot[idx]);
        kSr[idx].enabled = true;
        liveSparks++;
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
        PulseMarks();
        float dtr = Time.deltaTime;
        UpdateSparkles(dtr);
        UpdateSmoke(dtr);
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
    /// <summary>이번 낙하에서 칸마다 떨어진 거리. 착지 연출의 세기로 쓴다.</summary>
    public readonly Dictionary<int, float> LastDrop = new Dictionary<int, float>();
    /// <summary>이번 낙하에서 가장 멀리 떨어진 거리.</summary>
    public float LastMaxDrop { get; private set; }

    /// <summary>칸마다 '실제로 떨어진 거리' 를 받아 그만큼만 움직인다.
    /// 거리를 추정하면 한 칸 미끄러진 블록과 새로 들어온 블록이 똑같이 보여
    /// 열 전체가 통째로 교체되는 것처럼 읽힌다.
    /// stagger = 아래쪽이 먼저 자리를 잡도록 위쪽 칸을 늦추는 간격.</summary>
    public IEnumerator FallIn(List<Point> cells, List<float> drops, float dur, float stagger)
    {
        LastDrop.Clear();
        LastMaxDrop = 0f;
        if (cells == null || cells.Count == 0) yield break;

        var delays = new List<float>(cells.Count);
        var baseColor = new List<Color>(cells.Count);
        float maxDelay = 0f;

        for (int i = 0; i < cells.Count; i++)
        {
            float d = drops[i];
            LastDrop[cells[i].X * Board.H + cells[i].Y] = d;
            if (d > LastMaxDrop) LastMaxDrop = d;
            // 아래쪽이 먼저 자리를 잡아야 위가 따라 내려오는 것으로 보인다
            float delay = (Board.H - 1 - cells[i].Y) * stagger;
            delays.Add(delay);
            if (delay > maxDelay) maxDelay = delay;
            baseColor.Add(tiles[cells[i].X, cells[i].Y].color);
        }

        // 낙하 거리가 길수록 오래 걸린다. 전부 같은 시간에 도착하면 물체 같지 않다.
        var times = new List<float>(cells.Count);
        float longest = 0f;
        for (int i = 0; i < cells.Count; i++)
        {
            float t = dur * Mathf.Sqrt(Mathf.Max(0.35f, drops[i]) / Mathf.Max(1f, LastMaxDrop));
            times.Add(t);
            if (delays[i] + t > longest) longest = delays[i] + t;
        }

        float clock = 0;
        while (clock < longest)
        {
            clock += Time.deltaTime;
            for (int i = 0; i < cells.Count; i++)
            {
                float k = Mathf.Clamp01((clock - delays[i]) / times[i]);
                // 중력 낙하: 처음엔 느리고 갈수록 빨라진다 (거리 ∝ t²)
                float off = drops[i] * (1f - k * k);
                Place(cells[i], off, baseColor[i]);
            }
            yield return null;
        }
        for (int i = 0; i < cells.Count; i++) Place(cells[i], 0f, baseColor[i]);
    }

    /// <summary>낙하 중인 칸을 옮겨 그린다. 보드 위쪽 밖으로 나간 부분은 서서히 사라진다 —
    /// 안 그러면 판 밖 허공에 타일이 떠 있는 것처럼 보인다.</summary>
    void Place(Point p, float off, Color baseCol)
    {
        int x = p.X, y = p.Y;
        float wy = y + off;

        // 맨 윗줄 위로 한 칸을 걸쳐 사라지게 한다
        float top = Board.H - 1 + 0.5f;
        float a = Mathf.Clamp01(1f - (wy - top));

        var sr = tiles[x, y];
        sr.transform.localPosition = new Vector3(x, wy, 0);
        sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * a);

        var ov = overlays[x, y];
        ov.transform.localPosition = new Vector3(x, wy, -0.5f);
        if (ov.enabled)
        {
            var oc = ov.color;
            ov.color = new Color(oc.r, oc.g, oc.b, a);
        }
    }


    /// <summary>조각을 '들고 다니는' 모습으로 그린다.
    ///
    ///   놓일 자리 : 스냅된 칸에 윤곽(ghostRing) — 어디에 떨어질지 읽힌다
    ///   손에 든 것 : 커서를 그대로 따라가며 살짝 들어올려진 채 그림자를 깔고 떠 있다
    ///
    /// 칸에 딱딱 붙여 그리면 들고 다니는 게 아니라 이미 놓인 것처럼 보인다.
    /// (fx, fy) 는 칸으로 반올림하기 전의 연속 좌표다.</summary>
    public void ShowGhost(Piece p, float fx, float fy, int ax, int ay, bool can, Color pieceColor)
    {
        ghostRingColor = can ? Color.white : new Color(1f, 0.35f, 0.35f);
        ghostCount = p.Cells.Count;

        // 조각의 한가운데를 커서에 맞춘다. 왼쪽 아래 칸 기준으로 잡으면 손에서 어긋난다.
        float cx = MaxCell(p, 0) * 0.5f, cy = MaxCell(p, 1) * 0.5f;

        for (int i = 0; i < ghost.Length; i++)
        {
            if (i >= p.Cells.Count)
            {
                ghost[i].enabled = false;
                ghostRing[i].enabled = false;
                carryShadow[i].enabled = false;
                continue;
            }

            // 1) 놓일 자리 — 스냅된 칸
            int gx = ax + p.Cells[i].X, gy = ay + p.Cells[i].Y;
            ghostX[i] = gx; ghostY[i] = gy;
            ghostRing[i].enabled = true;
            ghostRing[i].transform.localPosition = new Vector3(gx, gy, -1.05f);
            ghostRing[i].color = ghostRingColor;

            // 2) 들고 있는 조각 — 커서를 따라가는 연속 좌표
            float px = fx + p.Cells[i].X - cx;
            float py = fy + p.Cells[i].Y - cy;

            carryShadow[i].enabled = true;
            carryShadow[i].transform.localPosition = new Vector3(px + ShadowOffX, py + ShadowOffY, -1.5f);
            carryShadow[i].transform.localScale = Vector3.one * CarryScale * 0.96f;
            carryShadow[i].color = ShadowColor;

            ghost[i].enabled = true;
            ghost[i].sprite = tile;
            ghost[i].transform.localPosition = new Vector3(px, py + CarryLift, -2f);
            ghost[i].transform.localScale = Vector3.one * CarryScale;
            ghostBase[i] = can
                ? new Color(pieceColor.r, pieceColor.g, pieceColor.b, 1f)
                : new Color(0.55f, 0.30f, 0.30f, 0.85f);
            ghost[i].color = ghostBase[i];
        }
    }

    // ---------- 트레이 ----------
    //
    // 조각은 보드 아래 트레이에 놓인다. 손가락으로 집어 보드로 끌어다 놓는다.
    // 트레이도 보드와 같은 월드 좌표에 그린다 — 그래야 드래그가 한 좌표계에서 끝난다.

    // 0 = 지금 블록, 1 = 다음 블록. 다음 것은 미리보기라 고를 수 없다.
    public const int TraySlots = 2;
    public const int CurrentSlot = 0;
    public const float TrayY = -4.6f;        // 트레이 중심 (칸 단위). 보드와 사이를 띄운다
    public const float TrayCell = 0.60f;     // 트레이 안 칸 크기 — 블록보다 작게
    public const float TrayRadius = 1.55f;   // 슬롯 하나가 차지하는 반경

    SpriteRenderer[] trayPad;                // 슬롯 바닥
    SpriteRenderer[][] trayCells;            // 슬롯마다 조각 칸

    /// <summary>슬롯 i 의 중심 월드 좌표.</summary>
    public static Vector2 TraySlotCenter(int i)
    {
        float mid = (Board.W - 1) * 0.5f;
        return new Vector2(mid + (i == CurrentSlot ? -2.4f : 2.4f), TrayY);
    }

    void BuildTray()
    {
        trayPad = new SpriteRenderer[TraySlots];
        trayCells = new SpriteRenderer[TraySlots][];

        for (int i = 0; i < TraySlots; i++)
        {
            var c = TraySlotCenter(i);

            var pad = new GameObject("traypad_" + i);
            pad.transform.SetParent(transform, false);
            pad.transform.localPosition = new Vector3(c.x, c.y, 1f);
            pad.transform.localScale = Vector3.one * (TrayRadius * 2f);
            var psr = pad.AddComponent<SpriteRenderer>();
            psr.sprite = MakePanelSprite(0.22f);
            psr.color = TrayPadColor;
            psr.sortingOrder = -3;
            trayPad[i] = psr;

            trayCells[i] = new SpriteRenderer[5];   // 조각은 최대 5칸
            for (int k = 0; k < trayCells[i].Length; k++)
            {
                var go = new GameObject("tray_" + i + "_" + k);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tile;
                sr.sortingOrder = 3;
                sr.enabled = false;
                trayCells[i][k] = sr;
            }
        }
    }

    static readonly Color TrayPadColor = new Color(1f, 1f, 1f, 0.16f);

    /// <summary>트레이 슬롯을 그린다. piece 가 null 이면 빈 슬롯.</summary>
    public void SetTraySlot(int i, Piece piece, Color color, bool selected, bool dimmed)
    {
        if (trayCells == null || i < 0 || i >= TraySlots) return;

        trayPad[i].color = (i == CurrentSlot && selected)
            ? new Color(1f, 1f, 1f, 0.34f)
            : TrayPadColor;

        var cells = trayCells[i];
        bool isNext = i != CurrentSlot;
        if (piece == null)
        {
            foreach (var sr in cells) sr.enabled = false;
            return;
        }

        var center = TraySlotCenter(i);
        float cx, cy;
        PieceCenter(piece, out cx, out cy);
        // 지금 블록은 조금 크게, 다음 블록은 작고 흐리게 — 무엇을 쓰는 중인지 갈린다
        float lift = selected ? 0.18f : 0f;
        float scale = isNext ? TrayCell * 0.78f : TrayCell * (selected ? 1.12f : 1.0f);

        for (int k = 0; k < cells.Length; k++)
        {
            if (k >= piece.Cells.Count) { cells[k].enabled = false; continue; }
            var cell = piece.Cells[k];
            cells[k].enabled = true;
            cells[k].sprite = tile;
            cells[k].transform.localPosition = new Vector3(
                center.x + (cell.X - cx) * scale,
                center.y + (cell.Y - cy) * scale + lift, 0.5f);
            cells[k].transform.localScale = Vector3.one * scale * 0.94f;
            float alpha = dimmed ? 0.35f : (isNext ? 0.55f : 1f);
            cells[k].color = new Color(color.r, color.g, color.b, alpha);
        }
    }

    static void PieceCenter(Piece p, out float cx, out float cy)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in p.Cells)
        {
            if (c.X < minX) minX = c.X; if (c.X > maxX) maxX = c.X;
            if (c.Y < minY) minY = c.Y; if (c.Y > maxY) maxY = c.Y;
        }
        cx = (minX + maxX) * 0.5f;
        cy = (minY + maxY) * 0.5f;
    }

    /// <summary>이 월드 좌표가 어느 트레이 슬롯인가. 없으면 -1.</summary>
    public static int TrayHit(Vector2 world)
    {
        for (int i = 0; i < TraySlots; i++)
        {
            var c = TraySlotCenter(i);
            if (Mathf.Abs(world.x - c.x) <= TrayRadius && Mathf.Abs(world.y - c.y) <= TrayRadius)
                return i;
        }
        return -1;
    }

    // 들고 있는 조각: 살짝 크게, 살짝 위로, 아래에 그림자.
    const float CarryScale = 1.01f;
    const float CarryLift  = 0.22f;   // 들어올린 높이 (칸 단위)
    const float ShadowOffX = 0.07f;
    const float ShadowOffY = -0.16f;
    static readonly Color ShadowColor = new Color(0.06f, 0.07f, 0.12f, 0.34f);

    static int MaxCell(Piece p, int axis)
    {
        int m = 0;
        foreach (var c in p.Cells) { int v = axis == 0 ? c.X : c.Y; if (v > m) m = v; }
        return m;
    }

    /// <summary>폭탄 조각 고스트 — 던질 칸엔 폭탄 아이콘, 터질 범위는 붉게 미리 보여준다.</summary>
    public void ShowBombGhost(int ax, int ay, bool can, List<Point> range)
    {
        ghostRingColor = can ? Color.white : new Color(1f, 0.35f, 0.35f);
        ghostCount = 1;

        ghost[0].enabled = true;
        ghost[0].sprite = bomb;
        ghost[0].transform.localPosition = new Vector3(ax, ay, -1);
        ghost[0].color = can ? Color.white : new Color(0.55f, 0.55f, 0.6f, 0.85f);
        ghostRing[0].enabled = true;
        ghostRing[0].transform.localPosition = new Vector3(ax, ay, -1.05f);
        ghostRing[0].color = ghostRingColor;
        for (int i = 1; i < ghost.Length; i++) { ghost[i].enabled = false; ghostRing[i].enabled = false; }

        ShowRange(can ? range : null, BombRed);
    }

    static readonly Color BombRed = new Color(1f, 0.06f, 0.04f);
    static readonly Color MatchWhite = new Color(1f, 1f, 1f);

    /// <summary>지금 놓으면 사라질 칸을 흰색으로 예고한다.
    /// 손에 든 조각도 그 안에 들면 같이 반짝인다 — 고스트가 범위 표시를 덮기 때문이다.</summary>
    public void ShowMatchPreview(List<Point> cells)
    {
        ShowRange(cells, MatchWhite);

        doomed.Clear();
        if (cells != null) foreach (var c in cells) doomed.Add(c.X * 1000 + c.Y);

        float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 17f);   // 범위 표시와 같은 박자
        for (int i = 0; i < ghostCount && i < ghost.Length; i++)
        {
            if (!ghost[i].enabled) continue;
            ghost[i].color = doomed.Contains(ghostX[i] * 1000 + ghostY[i])
                ? Color.Lerp(ghostBase[i], Color.white, 0.20f + 0.80f * k)
                : ghostBase[i];
        }
    }

    void ShowRange(List<Point> cells, Color c)
    {
        blastColor = c;
        blastCount = 0;
        if (cells != null)
            for (int i = 0; i < cells.Count && i < blast.Length; i++)
            {
                blast[i].enabled = true;
                blast[i].transform.localPosition = new Vector3(cells[i].X, cells[i].Y, -0.9f);
                blastCount++;
            }
        for (int i = blastCount; i < blast.Length; i++) blast[i].enabled = false;
    }

    public void HideGhost()
    {
        if (ghost == null) return;
        ghostCount = 0;
        blastCount = 0;
        if (blast != null) foreach (var b in blast) if (b != null) b.enabled = false;
        foreach (var g in ghost) if (g != null) { g.enabled = false; g.transform.localScale = Vector3.one; }
        foreach (var g in ghostRing) if (g != null) g.enabled = false;
        if (carryShadow != null) foreach (var g in carryShadow) if (g != null) g.enabled = false;
    }

    // 고스트 테두리를 천천히 맥동시켜 배경 타일에 묻히지 않게 한다.
    void PulseGhost()
    {
        if (blastCount > 0)
        {
            // 빠르게 깜빡여서 '여기가 사라진다' 가 바로 읽히게 한다
            float ba = 0.66f + 0.24f * Mathf.Sin(Time.time * 17f);
            var bc = new Color(blastColor.r, blastColor.g, blastColor.b, ba);
            for (int i = 0; i < blastCount; i++) blast[i].color = bc;
        }
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

    // 보석: 각진 컷과 면(facet). 위쪽 면이 밝고 아래가 어두워 입체가 선다.
    static Sprite MakeGemSprite()
    {
        const int S = 32; const float r = 5f;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        float c = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - dist + 0.5f);

                // 네 모서리에서 중심으로 모이는 컷 — 대각선이 면의 경계가 된다
                float u = (fx - c) / c, v = (fy - c) / c;
                float g;
                if (Mathf.Abs(u) < Mathf.Abs(v))
                    g = v > 0 ? 1.0f : 0.68f;        // 위 면 / 아래 면
                else
                    g = u < 0 ? 0.90f : 0.78f;       // 좌 면 / 우 면

                // 중앙 테이블(평평한 윗면) — 살짝 밝게 띄운다
                float table = Mathf.Clamp01(1f - (Mathf.Abs(u) + Mathf.Abs(v)) / 0.72f);
                g = Mathf.Lerp(g, 1f, 0.45f * table);

                // 컷 선을 옅은 그늘로 그어 면을 나눈다
                float cut = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) * 9f);
                g *= Mathf.Lerp(1f, 0.86f, cut);

                float edge = r - dist;
                g *= Mathf.Lerp(1f, RimDark, Mathf.Clamp01((RimPx - edge) / 1.3f));
                px[y * S + x] = new Color(g, g, g, a);
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 크레파스: tile-crayon.png 참고. 삐뚤한 손그림 테두리 + 대각선 결.
    // 회색조라 팔레트 색이 그대로 곱해진다 — 바탕을 낮게 깔아야 결이 밝게 도드라진다.
    static Sprite MakeCrayonSprite()
    {
        const int S = 64; const float r = 15f;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;

                // 삐뚤빼뚤한 외곽 — 사인 두 개를 겹쳐 손으로 그은 흔들림을 만든다
                float wob = Mathf.Sin(fy * 0.30f + 0.7f) * 0.9f
                          + Mathf.Sin(fx * 0.37f + 2.1f) * 0.7f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy) + wob * 0.5f;
                float edge = r - dist;                      // 안쪽일수록 크다
                float a = Mathf.Clamp01(edge + 0.5f);
                if (a <= 0f) { px[y * S + x] = Color.clear; continue; }

                // 대각선 크레파스 결 — 굵기·간격이 제각각이라야 손맛이 난다
                // 결 간격을 넓게 잡고, 저주파 변조로 굵기를 들쭉날쭉하게 만든다.
                // 간격이 촘촘하면 손그림이 아니라 줄무늬 천으로 보인다.
                float diag = (fx - fy) * 0.72f;
                float jitter = Mathf.Sin(diag * 0.07f + 0.9f) * 1.6f;
                float band = Mathf.Sin(diag * 0.26f + jitter) * 0.5f + 0.5f;
                float wide = Mathf.Sin(diag * 0.09f + 2.4f) * 0.5f + 0.5f;
                float grain = Frac(Mathf.Sin(x * 17.31f + y * 39.77f) * 8123.31f);

                // 바탕은 낮게, 결이 지나가는 자리만 밝게
                float g = 0.70f + 0.14f * wide + 0.05f * grain;
                float streak = Mathf.Pow(band, 5f);                 // 성기고 또렷한 밝은 줄
                g = Mathf.Lerp(g, 1.0f, 0.80f * streak);

                // 군데군데 덜 칠해진 자리 — 종이가 비친다
                if (grain > 0.90f) a *= 0.72f;

                // 손으로 두른 테두리. 끊기듯 진해져야 그린 느낌이 난다.
                float rimStrength = Mathf.Clamp01((4.0f - edge) / 3.0f);
                float rimGap = Frac(Mathf.Sin(fx * 0.9f + fy * 1.7f) * 431.7f);
                g *= Mathf.Lerp(1f, 0.42f + 0.14f * rimGap, rimStrength);

                px[y * S + x] = new Color(g, g, g, a);
            }

        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 보드 판용 둥근 사각. radiusFrac 은 한 변에 대한 모서리 반경 비율.
    static Sprite MakePanelSprite(float radiusFrac)
    {
        const int S = 256;
        float r = Mathf.Max(0.5f, S * radiusFrac);
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * S + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>블록 안에 차 있는 빛. 가운데가 가장 밝고 가장자리로 갈수록 옅어져
    /// 블록 색이 비친다 — 블록이 스스로 빛나는 것처럼 보이게 하는 층이다.</summary>
    static Sprite MakeMarkFillSprite()
    {
        const int S = 64;
        const float R = 12f;   // 모서리 반경(px) — 블록의 둥근 정도에 맞춘다
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(R - fx, fx - (S - R), 0f);
                float dy = Mathf.Max(R - fy, fy - (S - R), 0f);
                float inside = R - Mathf.Sqrt(dx * dx + dy * dy);
                if (inside <= 0f) { px[y * S + x] = new Color(0, 0, 0, 0); continue; }

                float nx = (fx - S * 0.5f) / (S * 0.5f);
                float ny = (fy - S * 0.5f) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny) * 0.85f);

                px[y * S + x] = new Color(1, 1, 1, a * a * Mathf.Clamp01(inside));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>블록 밖으로 새어나오는 빛. 블록 모양만큼은 꽉 차 있고, 그 경계에서부터
    /// 부옇게 번져 나가며 사라진다. 경계선이 보이면 '테두리' 로 읽히므로 안팎이 이어져야 한다.
    /// 색은 쓰는 쪽에서 틴트로 주므로 흰색으로 굽는다.</summary>
    static Sprite MakeMarkGlowSprite()
    {
        const int S = 96;
        // 스프라이트 한 변이 MarkScale 월드다. 그 안에서 블록이 차지하는 크기를 구한다.
        float half = S * 0.5f * (TileScale / MarkScale);
        const float R = 7f;                 // 블록 모서리 반경(px)
        float blur = S * 0.5f - half;       // 블록 경계에서 스프라이트 끝까지 = 번지는 폭

        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // 둥근 사각까지의 거리 (안쪽이 음수, 바깥이 양수)
                float qx = Mathf.Abs(x + 0.5f - S * 0.5f) - (half - R);
                float qy = Mathf.Abs(y + 0.5f - S * 0.5f) - (half - R);
                float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)
                                         + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                              + Mathf.Min(Mathf.Max(qx, qy), 0f) - R;

                // 블록 안은 꽉 차 있고, 밖으로는 완만하게 꺼진다
                float a = outside <= 0f ? 1f : Mathf.Pow(Mathf.Clamp01(1f - outside / blur), 2.2f);

                px[y * S + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>네 갈래 별빛 가루. 가운데 심지가 밝고 네 방향으로 뾰족하게 뻗는다.
    /// 별 모양은 아스트로이드(√|x| + √|y| ≤ 1) 라 갈래 사이가 오목하게 파인다 —
    /// 그래야 동그란 먼지가 아니라 '반짝임' 으로 읽힌다.</summary>
    static Sprite MakeStarSprite()
    {
        const int S = 48;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f) / S * 2f - 1f;
                float ny = (y + 0.5f) / S * 2f - 1f;

                float v = Mathf.Sqrt(Mathf.Abs(nx)) + Mathf.Sqrt(Mathf.Abs(ny));
                float rays = Mathf.Clamp01((1f - v) * 2.6f);          // 네 갈래
                float core = Mathf.Exp(-(nx * nx + ny * ny) * 26f);   // 가운데 심지

                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01(rays * 0.85f + core));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 타일 경계는 명확해야 한다. 밝은 판 위에서 옅은 테두리는 바로 사라진다.
    const float RimPx = 3.4f;      // 테두리 두께 (32px 스프라이트 기준 ≈ 11%)
    const float RimDark = 0.62f;   // 테두리 명도 배수 — 작을수록 진하다

    /// <summary>기본(광택) 스킨. 스킨을 지정하지 않는 곳에서 쓴다.</summary>
    public static Sprite MakeTileSprite() { return MakeTileSprite(TileSkin.Glossy); }

    /// <summary>스킨별 타일. 전부 회색조로 굽고 SpriteRenderer.color 로 색을 곱한다 —
    /// 그래서 어떤 팔레트를 써도 그 색의 재질이 된다.</summary>
    public static Sprite MakeTileSprite(TileSkin skin)
    {
        switch (skin)
        {
            case TileSkin.Gem: return MakeGemSprite();
            case TileSkin.Crayon: return MakeCrayonSprite();
            default: return MakeGlossySprite();
        }
    }

    /// <summary>현재 스킨으로 모든 타일 스프라이트를 갈아끼운다.</summary>
    public void ApplySkin(TileSkin skin)
    {
        if (!built || skin == currentSkin) return;
        currentSkin = skin;
        tile = MakeTileSprite(skin);
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
                if (tiles[x, y].sprite != null) tiles[x, y].sprite = tile;
        foreach (var g in ghost) g.sprite = tile;
    }

    static Sprite MakeGlossySprite()
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
                // 바탕: 아래가 어둡고 위가 밝다. 광택이 도드라지도록 여유를 남겨 둔다.
                float g = Mathf.Lerp(0.84f, 0.97f, fy / (S - 1));

                // 광택 ① 왼쪽 위에서 비스듬히 들어오는 넓은 띠
                float u = (fx + (S - fy)) / (2f * S);              // 0 = 좌상단
                float sheen = Mathf.Clamp01(1f - Mathf.Abs(u - 0.28f) / 0.17f);
                g = Mathf.Lerp(g, 1f, 0.60f * sheen * sheen);

                // 광택 ② 좌상단의 작은 반사점 — 유약 바른 느낌
                float sdx = fx - S * 0.30f, sdy = fy - S * 0.74f;
                float spec = Mathf.Clamp01(1f - Mathf.Sqrt(sdx * sdx + sdy * sdy) / (S * 0.125f));
                g = Mathf.Lerp(g, 1f, 0.95f * spec);

                // 아래쪽 반사광 — 바닥에서 살짝 되비친다
                float bounce = Mathf.Clamp01(1f - fy / (S * 0.22f));
                g = Mathf.Lerp(g, 0.92f, 0.45f * bounce);

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
    /// <summary>폭탄 아이콘 — 검은 구체 + 하이라이트 + 심지 + 불꽃.</summary>
    static Sprite MakeBombSprite()
    {
        const int S = 32;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];

        var body = new Color(0.14f, 0.15f, 0.20f);
        var edge = new Color(0.06f, 0.06f, 0.10f);
        var fuse = new Color(0.72f, 0.60f, 0.38f);
        var spark = new Color(1f, 0.78f, 0.25f);

        float cx = 15.5f, cy = 13.5f, r = 10.5f;   // 구체는 살짝 아래로 — 위에 심지 자리
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = new Color(0, 0, 0, 0);

                if (d <= r)
                {
                    // 왼쪽 위에서 빛이 온다
                    float lit = Mathf.Clamp01(1f - (dx * -0.5f + dy * 0.5f + 6f) / 16f);
                    c = Color.Lerp(edge, body, 0.45f + 0.55f * lit);
                    float hd = Mathf.Sqrt((x - 11.5f) * (x - 11.5f) + (y - 17.5f) * (y - 17.5f));
                    if (hd < 3.4f) c = Color.Lerp(c, new Color(0.72f, 0.76f, 0.86f), 0.62f * (1f - hd / 3.4f));
                    if (d > r - 1.4f) c = Color.Lerp(c, edge, (d - (r - 1.4f)) / 1.4f);
                    c.a = 1f;
                }
                // 심지: 구체 위에서 오른쪽으로 휘어 오른다
                float fx = 17.5f + 2.6f * Mathf.Sin((y - 23f) * 0.55f);
                if (y >= 22 && y <= 27 && Mathf.Abs(x - fx) <= 1.3f) c = fuse;
                // 불꽃
                float sd = Mathf.Sqrt((x - 20.5f) * (x - 20.5f) + (y - 28.5f) * (y - 28.5f));
                if (sd <= 3.2f) c = Color.Lerp(spark, new Color(1f, 0.95f, 0.75f), 1f - sd / 3.2f);

                px[y * S + x] = c;
            }

        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

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

    // 장애물 블록(콘크리트). 색·형태 상수는 ObstacleStyle 에 모아 두었다.
    // 스프라이트에 실제 색을 구워 넣고 SpriteRenderer 는 흰색으로 둔다 — 틴트가 섞이면
    // 지정한 무채색이 그대로 나오지 않는다.
    //
    // stage 0 = 온전 / 1 = 갈라져 조각이 벌어짐 (틈으로 아래 색이 비친다)
    /// <summary>벽돌 블록. 흰 줄눈이 그리는 격자가 '쌓아올린 것 = 부술 수 있다' 를 알린다.
    /// stage 는 0(온전) ~ ObstacleStyle.Stages-1(곧 부서짐). 한 대 맞을 때마다 반드시 달라진다.</summary>
    static Sprite MakeObstacleSprite(int stage)
    {
        const int S = 48;
        float r = S * ObstacleStyle.RoundFrac;
        float line = S * ObstacleStyle.LineFrac;
        float mortar = S * ObstacleStyle.MortarFrac;
        float dmg = ObstacleStyle.Stages <= 1 ? 0f : stage / (float)(ObstacleStyle.Stages - 1);
        Color body = ObstacleStyle.BodyFor(stage);

        // 벽돌 한 장 — 가로 3장, 세로 4켜. 잘아야 '쌓아올린 벽' 으로 읽힌다
        float bw = S / 3f, bh = S / 4f;

        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;

                // 거의 직각인 실루엣 — 둥근 색 타일과 형태부터 다르다
                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                // 켜마다 반 장씩 어긋나게 쌓는다
                int row = Mathf.Clamp((int)(fy / bh), 0, 3);
                float shift = (row % 2) * bw * 0.5f;
                float ux = fx + shift;
                int col = (int)(ux / bw);

                // 벽돌마다 흰기를 다르게 섞는다 — 부분적으로 하얀 벽돌이 섞인 벽
                float h = Frac(Mathf.Sin(row * 37.13f + col * 91.7f) * 43758.5453f);
                Color c = Color.Lerp(body, ObstacleStyle.BrickPale, h * 0.55f);

                // 벽돌 한 장 안의 세로 명암 — 위가 밝고 아래가 어둡다. 두께가 생긴다
                float inRow = (fy - row * bh) / bh;
                c = Color.Lerp(Color.Lerp(c, ObstacleStyle.Light, 0.22f),
                               Color.Lerp(c, ObstacleStyle.Shadow, 0.26f), inRow);

                // 면의 잔결
                float n = Frac(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f);
                c = Color.Lerp(c, n > 0.5f ? ObstacleStyle.Light : ObstacleStyle.Shadow,
                               0.13f * Mathf.Abs(n - 0.5f) * 2f);

                // 줄눈 — 가로 켜 사이, 세로 이음매
                float dRow = Mathf.Abs(fy - Mathf.Round(fy / bh) * bh);
                float dCol = Mathf.Abs(ux - col * bw);
                dCol = Mathf.Min(dCol, Mathf.Abs(ux - (col + 1) * bw));
                if (dRow < mortar || dCol < mortar)
                {
                    c = Color.Lerp(ObstacleStyle.Mortar, c, 0.14f);
                    // 줄눈 바로 아래는 그늘 — 벽돌이 앞으로 튀어나와 보인다
                    if (dRow < mortar && fy < Mathf.Round(fy / bh) * bh)
                        c = Color.Lerp(c, ObstacleStyle.Shadow, 0.30f);
                }

                // 베벨: 위/좌는 밝게, 아래/우는 어둡게 — 덩어리의 두께
                float edge = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(S - fx, S - fy));
                if (edge > line && edge < line + S * ObstacleStyle.BevelFrac)
                    c = Color.Lerp(c, (fy > S * 0.5f || fx < S * 0.5f)
                                      ? ObstacleStyle.Light : ObstacleStyle.Shadow, 0.50f);

                // 굵은 외곽선 — 색 타일에는 없는 신호
                if (edge <= line) c = ObstacleStyle.Outline;

                // 균열: 손상이 깊어질수록 굵어지고 갈래가 늘고 끝내 조각이 벌어진다
                if (stage > 0)
                {
                    float w1 = Mathf.Sin(fy * 0.32f) * S * 0.085f;
                    float d = Mathf.Abs(fx - (S * 0.44f + w1));

                    if (dmg > 0.3f)
                    {
                        float w2 = Mathf.Sin(fx * 0.28f + 1.5f) * S * 0.075f;
                        d = Mathf.Min(d, Mathf.Abs(fy - (S * 0.60f + w2)));
                    }
                    if (dmg > 0.7f)
                    {
                        float w3 = Mathf.Sin(fy * 0.41f + 2.6f) * S * 0.06f;
                        d = Mathf.Min(d, Mathf.Abs(fx - (S * 0.74f + w3)));
                    }

                    float crack = line * (0.28f + 0.62f * dmg);
                    float gap = S * 0.030f * Mathf.Max(0f, dmg - 0.75f) * 4f;
                    if (gap > 0f && d < gap) a = 0f;
                    else if (d < crack) c = Color.Lerp(c, ObstacleStyle.Outline, 0.60f + 0.40f * dmg);
                }
                else
                {
                    // 온전해도 실금 하나 — 완전히 매끈하면 플라스틱처럼 보인다
                    float w1 = Mathf.Sin(fy * 0.32f) * S * 0.085f;
                    if (Mathf.Abs(fx - (S * 0.44f + w1)) < line * 0.22f)
                        c = Color.Lerp(c, ObstacleStyle.Outline, 0.26f);
                }

                c.a = a;
                px[y * S + x] = c;
            }

        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    static float Frac(float v) { return v - Mathf.Floor(v); }

    /// <summary>강철. 벽돌(점토색)과 헷갈리지 않게 차가운 금속과 리벳으로 그린다.
    /// stage 는 손상 단계(0 = 온전 … Stages-1 = 곧 부서짐)다. 맞을수록
    /// 금이 갈라지고 모서리가 부스러져 작은 알갱이로 떨어져 나간다.</summary>
    static Sprite MakeSteelSprite(int stage)
    {
        const int S = 48;
        float r = S * ObstacleStyle.RoundFrac;
        float line = S * ObstacleStyle.LineFrac;

        // 0 → 1 로 갈수록 심하게 망가진다
        float dmg = ObstacleStyle.Stages <= 1 ? 0f : stage / (float)(ObstacleStyle.Stages - 1);

        var body    = new Color(0.36f, 0.40f, 0.47f);
        var light   = new Color(0.55f, 0.60f, 0.68f);
        var shadow  = new Color(0.22f, 0.25f, 0.31f);
        var outline = new Color(0.10f, 0.11f, 0.16f);
        var rivet   = new Color(0.70f, 0.75f, 0.82f);

        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;

                float dx = Mathf.Max(r - fx, fx - (S - r), 0f);
                float dy = Mathf.Max(r - fy, fy - (S - r), 0f);
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);

                // 위에서 아래로 떨어지는 금속 그라데이션 + 대각선 결
                float g = 1f - fy / S;
                Color c = Color.Lerp(light, shadow, g);
                float grain = Mathf.Sin((fx + fy) * 0.55f) * 0.5f + 0.5f;
                c = Color.Lerp(c, body, grain * 0.40f);

                // 사방 리벳 — 딱 봐도 박아놓은 판
                for (int ry = 0; ry < 2; ry++)
                    for (int rx = 0; rx < 2; rx++)
                    {
                        float cx = rx == 0 ? S * 0.24f : S * 0.76f;
                        float cy = ry == 0 ? S * 0.24f : S * 0.76f;
                        float rd = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                        if (rd < S * 0.06f) c = Color.Lerp(rivet, c, rd / (S * 0.06f));
                    }

                float edge = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(S - fx, S - fy));
                if (edge > line && edge < line + S * 0.12f)
                    c = Color.Lerp(c, (fy > S * 0.5f) ? light : shadow, 0.45f);
                if (edge <= line) c = outline;

                if (dmg > 0f)
                {
                    // ① 균열 — 대각으로 갈라진 금이 단계마다 한 줄씩 굵고 길어진다
                    float crack = Mathf.Abs(Frac((fx * 0.9f - fy * 1.35f) / S + 0.18f) - 0.5f) * S;
                    float wobble = Mathf.Sin(fy * 0.7f) * 1.3f;      // 자로 그은 듯한 직선을 피한다
                    if (crack + wobble < 1.1f + dmg * 2.2f) c = Color.Lerp(c, outline, 0.85f);
                    if (dmg > 0.45f)
                    {
                        float crack2 = Mathf.Abs(Frac((fx * 1.25f + fy * 0.8f) / S + 0.62f) - 0.5f) * S;
                        if (crack2 + Mathf.Cos(fx * 0.6f) < dmg * 1.9f) c = Color.Lerp(c, outline, 0.75f);
                    }

                    // ② 부스러짐 — 가장자리부터 알갱이로 떨어져 나가 구멍이 뚫린다
                    float bite = Frac(Mathf.Sin(fx * 12.9898f + fy * 78.233f) * 43758.55f);
                    float rim = Mathf.Clamp01(1f - edge / (S * 0.30f));   // 가장자리일수록 1
                    if (bite < dmg * 0.55f * rim * rim) a = 0f;

                    // ③ 남은 몸통도 빛이 죽는다 — 밝기만으로도 상태가 읽히게
                    c = Color.Lerp(c, shadow, dmg * 0.30f);
                }

                c.a = a;
                px[y * S + x] = c;
            }

        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

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
                }
                if (glyph) col = Color.white;
                px[y * S + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }
}
