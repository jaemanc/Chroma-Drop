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
    static readonly Color BoardFrame = Palette.Hex(0xEEE8D9);   // 테두리 — 옅은 크림 (참고 UI 와 같은 톤)
    static readonly Color BoardShadow = new Color(0.212f, 0.294f, 0.275f, 0.30f);   // 부드러운 그림자

    // 화면 폭 390px ↔ 월드 16유닛 이므로 1px = 0.041 유닛
    const float Px = 16f / 390f;
    const float TileScale = 0.96f;   // 칸 대비 블록 크기 — 1.0 이면 칸을 꽉 채운다
    // 목표 칸은 '블록에서 빛이 새어나오는' 것으로 표시한다.
    // 블록 모양 그대로 빛이 차 있고, 그 빛이 칸 밖으로 부옇게 번진다.
    const float MarkScale = 1.7f;   // 빛이 번져 나가는 범위 (칸 단위)

    static readonly Color MarkWhite = new Color(1f, 1f, 1f, 0.95f);               // 별빛 가루의 밝은 쪽
    static readonly Color MarkGold = new Color(1f, 0.85f, 0.30f, 0.95f);          // 별빛 가루의 노란 쪽

    static readonly Color MarkDim = new Color(0.07f, 0.09f, 0.18f);               // 빛이 잦아든 쪽 (판 네이비)
    static readonly Color MarkDoneColor = new Color(0.72f, 0.76f, 0.80f, 0.16f);  // 깬 칸 — 흔적만 남긴다

    SpriteRenderer[,] tiles;
    SpriteRenderer[,] overlays;   // 아이템 아이콘
    // 참고 아트(chroma_drop_game_board.html)에서 잘라낸 블록 그림.
    // 있으면 이걸 그대로 쓰고, 없으면 아래 절차 생성 스프라이트로 돌아간다.
    Sprite[] jellyArt;            // 팔레트 색 순서와 같은 4장 (보드 밖에서 쓰는 대표 얼굴)
    Sprite[][] faceArt;           // 색마다 표정 여러 장 — 칸마다 다른 얼굴이 뜬다
    Sprite[] starArt;             // 목표 칸 — 같은 색의 별 모양 블록
    Sprite[] blinkArt, winkArt;   // 표정 변화 — 눈 감기 / 윙크
    int[,] cellColor;             // 칸마다 지금 무슨 색인지 (표정 스프라이트를 고를 때 쓴다)
    Sprite[] brickArt;            // 표정 있는 벽돌 2종 — 칸마다 무작위로 하나
    Sprite[] arrowHArt, arrowVArt;  // 가로/세로 화살표 블록 — 팔레트 색마다 한 장
    bool useArt;
    float tileDraw = TileScale;   // 아트는 여백을 품고 있어 조금 크게 그린다

    SpriteRenderer[,] faces;      // 광택+표정 오버레이 — 색 타일 위에 공통으로 얹는다
    Sprite faceSprite;            // 광택 + 표정
    Sprite glossSprite;           // 광택만 — 목표 칸(별) 은 표정이 없다
    SpriteRenderer[] ghostGloss;  // 들고 있는 조각 위의 광택+표정
    SpriteRenderer[][] trayGloss; // 트레이 조각 위의 광택+표정
    SpriteRenderer[,] marks;      // 좌표 목표 — 블록 가장자리에서 바깥으로 번지는 빛
    SpriteRenderer[,] markFills;  // 그 블록 위를 덮는 그라데이션 — 칸 자체가 물들어 보이게
    readonly List<Point> markList = new List<Point>();    // 아직 깨야 하는 칸
    Sprite markGlow, markFill;

    // 목표 칸에서 피어오르는 별빛 가루. 파괴 버스트와 풀을 나눠 쓴다 —
    // 버스트가 풀을 가득 채운 순간에 반짝임이 통째로 멈추면 안 된다.
    const int MaxSparks = 600;
    Sprite star;
    Transform[] kTr;
    SpriteRenderer[] kSr;
    Vector2[] kVel;
    float[] kLife, kMax, kSpin, kRot, kSize, kPhase;
    int liveSparks;
    float sparkTimer;
    SpriteRenderer[] ghost;
    SpriteRenderer[] ghostRing;   // 놓일 자리 윤곽 — 타일 색과 무관하게 위치를 읽히게 한다
    SpriteRenderer[] carryShadow; // 들고 있는 조각 아래 그림자
    int ghostCount;               // 현재 표시 중인 고스트 칸 수 (펄스용)
    Color ghostRingColor;
    Sprite bomb;                  // 폭탄 조각 아이콘
    int obstacleMaxHp = Rules.ObstacleHp;   // 이 판의 방해블록 내구도

    /// <summary>이번 판의 방해블록 내구도. 손상 단계를 이 값에 맞춰 환산한다.</summary>
    public void SetObstacleMaxHp(int hp) { obstacleMaxHp = Mathf.Max(1, hp); }
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
    public GameObject popFx;      // 블록이 터질 때 이펙트 (GameManager 가 씬에서 연결한 것을 넘긴다)
    const int MaxRings = 12;
    Sprite shock;                 // 원형 링
    Transform[] rTr;
    SpriteRenderer[] rSr;
    float[] rLife, rMax, rFrom, rTo;
    int liveRings;

    // ---- 피버 타임: 판 뒤 붉은 후광 + 블록 위 옅은 붉은 빛 ----
    SpriteRenderer feverHalo, feverWash;
    bool feverOn;

    public void Build()
    {
        if (built) return;
        built = true;

        LoadTileArt();
        currentSkin = Wallet.Skin;
        tile = MakeTileSprite(currentSkin);
        faceSprite = MakeOverlaySprite(true);
        glossSprite = MakeOverlaySprite(false);
        markGlow = MakeMarkGlowSprite();
        markFill = MakeStarSprite();   // 목표 칸 중앙에 별이 반짝인다 (참고: "가운데 별 = 반짝이 블록")
        ring = MakeRingSprite();
        soft = MakeSoftSprite();
        // 내구도 단계마다 금이 한 줄씩 늘어난다 (온전함 → 다 깨지기 직전)
        obstacle = new Sprite[ObstacleStyle.Stages];
        for (int i = 0; i < obstacle.Length; i++) obstacle[i] = MakeObstacleSprite(i);
        // 가로·세로 아이템은 참고 아트의 화살표 그림을 그대로 쓴다
        icons[ItemType.Row] = arrowHArt != null ? arrowHArt[0] : MakeIcon(ItemType.Row);
        icons[ItemType.Col] = arrowVArt != null ? arrowVArt[0] : MakeIcon(ItemType.Col);
        icons[ItemType.Diag] = MakeIcon(ItemType.Diag);
        // 폭탄도 참고 아트를 그대로 쓴다 (없으면 절차 생성으로 돌아간다)
        var bombArt = LoadArt("items/bomb");
        bomb = bombArt != null ? bombArt : MakeBombSprite();
        icons[ItemType.Bomb5] = bomb;   // 판 위 폭탄 칸과 손에 든 폭탄이 같은 그림이다

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
        MakePanel("shadow", center + new Vector3(0, -6f * Px, 0), border, border, BoardShadow, -7, radius);
        MakePanel("border", center, border, border, BoardFrame, -6, radius);
        MakePanel("surface", center, surface, surface, BoardCream, -5, radius - 5f * Px);
        MakePanel("grid", center, inner, inner, EmptyColor, -4, radius - 9f * Px);

        cellColor = new int[Board.W, Board.H];
        tiles = new SpriteRenderer[Board.W, Board.H];
        overlays = new SpriteRenderer[Board.W, Board.H];
        faces = new SpriteRenderer[Board.W, Board.H];
        marks = new SpriteRenderer[Board.W, Board.H];
        markFills = new SpriteRenderer[Board.W, Board.H];
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                // 목표 칸 중앙의 별. 칸을 살짝 넘칠 만큼 크게 그려야 "반짝이는 특별한 칸" 이 확 읽힌다.
                var fg = new GameObject("mf_" + x + "_" + y);
                fg.transform.SetParent(transform, false);
                fg.transform.localPosition = new Vector3(x, y, -0.1f);
                fg.transform.localScale = Vector3.one * TileScale * 1.35f;
                var fsr = fg.AddComponent<SpriteRenderer>();
                fsr.sprite = markFill;
                fsr.sortingOrder = 2;       // 표정(1) 위, 아이템 아이콘(3) 아래
                fsr.enabled = false;
                markFills[x, y] = fsr;

                // 가장자리 반짝임 — 블록보다 크고, 아이콘 위까지 올라온다
                var mg = new GameObject("m_" + x + "_" + y);
                mg.transform.SetParent(transform, false);
                mg.transform.localPosition = new Vector3(x, y, -0.2f);
                mg.transform.localScale = Vector3.one * MarkScale;
                var msr = mg.AddComponent<SpriteRenderer>();
                msr.sprite = markGlow;
                msr.sortingOrder = 4;       // 타일·표정·아이템 위, 범위 예고(5) 아래
                msr.enabled = false;
                marks[x, y] = msr;

                var go = new GameObject("t_" + x + "_" + y);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, y, TileZ(y));
                go.transform.localScale = Vector3.one * tileDraw;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tile;
                tiles[x, y] = sr;

                // 표정(눈·입·볼) — 타일의 자식으로 붙인다. 그래야 낙하·스탬프 스쿼시 때
                // 타일과 같이 움직인다(따로 위치를 갱신할 필요가 없다). 목표 칸이 되면
                // 별(markFill)이 위에서 덮어써서 참고 UI 처럼 "특별 블록엔 표정이 없다" 가 재현된다.
                var fcg = new GameObject("face_" + x + "_" + y);
                fcg.transform.SetParent(go.transform, false);
                fcg.transform.localPosition = new Vector3(0, 0, -0.05f);
                fcg.transform.localScale = Vector3.one;
                var fcsr = fcg.AddComponent<SpriteRenderer>();
                fcsr.sprite = faceSprite;
                fcsr.sortingOrder = 1;
                fcsr.enabled = false;
                faces[x, y] = fcsr;

                var og = new GameObject("i_" + x + "_" + y);
                og.transform.SetParent(transform, false);
                og.transform.localPosition = new Vector3(x, y, -0.5f);
                og.transform.localScale = Vector3.one * 0.8f;
                var osr = og.AddComponent<SpriteRenderer>();
                osr.sortingOrder = 3;
                osr.enabled = false;
                overlays[x, y] = osr;
            }

        ghost = new SpriteRenderer[8]; // 최대 조각 5칸 + 여유
        ghostRing = new SpriteRenderer[8];
        ghostGloss = new SpriteRenderer[8];
        ghostX = new int[8]; ghostY = new int[8]; ghostBase = new Color[8];
        carryShadow = new SpriteRenderer[8];
        for (int i = 0; i < ghost.Length; i++)
        {
            var rg = new GameObject("ghostring_" + i);
            rg.transform.SetParent(transform, false);
            rg.transform.localScale = Vector3.one;   // 칸 크기 — 이웃 고스트와 겹치지 않는다
            var rsr = rg.AddComponent<SpriteRenderer>();
            rsr.sprite = ring;
            rsr.sortingOrder = 6;
            rsr.enabled = false;
            ghostRing[i] = rsr;

            var sg = new GameObject("carryshadow_" + i);
            sg.transform.SetParent(transform, false);
            var ssr = sg.AddComponent<SpriteRenderer>();
            ssr.sprite = tile;
            ssr.sortingOrder = 6;
            ssr.enabled = false;
            carryShadow[i] = ssr;

            var go = new GameObject("ghost_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one;   // 밑 타일보다 크되 칸은 안 넘는다
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = tile;
            sr.sortingOrder = 7;
            sr.enabled = false;
            ghost[i] = sr;

            // 들고 있는 조각도 보드 블록과 같은 광택·표정을 얹는다 — 자식이라 같이 움직인다
            var gg = new GameObject("ghostgloss_" + i);
            gg.transform.SetParent(go.transform, false);
            gg.transform.localPosition = new Vector3(0, 0, -0.05f);
            var gsr = gg.AddComponent<SpriteRenderer>();
            gsr.sprite = faceSprite;
            gsr.sortingOrder = 8;
            gsr.enabled = false;
            ghostGloss[i] = gsr;
        }

        // 폭발 범위 미리보기 — 고스트(6,7)보다 아래, 타일 위
        var blastSprite = MakePanelSprite(0.3f);
        blast = new SpriteRenderer[96];   // 대각선·행/열 아이템까지 덮을 만큼
        for (int i = 0; i < blast.Length; i++)
        {
            var bg = new GameObject("blast_" + i);
            bg.transform.SetParent(transform, false);
            
            var bsr = bg.AddComponent<SpriteRenderer>();
            bsr.sprite = blastSprite;
            bsr.sortingOrder = 5;
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
        sr.enabled = framePanelsOn;
        framePanels.RemoveAll(x => x == null);   // 판을 다시 지을 때 지워진 것들
        framePanels.Add(sr);
    }

    bool framePanelsOn = true;
    readonly List<SpriteRenderer> framePanels = new List<SpriteRenderer>();

    /// <summary>보드 판(헤일로·그림자·테두리·서피스·바닥)을 그릴지. 플레이 아트에 판이 그려져 있으면 끈다.</summary>
    public void SetFramePanels(bool on)
    {
        framePanelsOn = on;
        foreach (var sr in framePanels) if (sr != null) sr.enabled = on;
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
            sr.sortingOrder = 10;
            sr.enabled = false;
            pTr[i] = go.transform;
            pSr[i] = sr;
        }
    }

    void BuildSparklePool()
    {
        star = MakeSparkSprite();
        kTr = new Transform[MaxSparks];
        kSr = new SpriteRenderer[MaxSparks];
        kVel = new Vector2[MaxSparks];
        kLife = new float[MaxSparks];
        kMax = new float[MaxSparks];
        kSpin = new float[MaxSparks];
        kRot = new float[MaxSparks];
        kSize = new float[MaxSparks];
        kPhase = new float[MaxSparks];

        var root = new GameObject("sparkles").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < MaxSparks; i++)
        {
            var go = new GameObject("k" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = star;
            sr.sortingOrder = 9;      // 블록·고스트 위, 파괴 파티클(10) 아래
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
            sr.sortingOrder = 11;          // 파티클(10)보다 위
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

    /// <summary>피버 타임 동안 판이 붉게 달아오른다. 켜고 끄기만 하고, 맥박은 Update 가 준다.</summary>
    public void SetFever(bool on)
    {
        feverOn = on;
        if (feverHalo == null)
        {
            var center = new Vector3((Board.W - 1) / 2f, (Board.H - 1) / 2f, 0f);
            feverHalo = MakeFeverPanel("feverHalo", center, 1.4f, -3);   // 판 뒤로 번지는 후광
            feverWash = MakeFeverPanel("feverWash", center, 0.1f, 4);    // 블록 위, 들고 있는 조각(6) 아래
        }
        feverHalo.enabled = feverWash.enabled = on;
    }

    SpriteRenderer MakeFeverPanel(string name, Vector3 center, float pad, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = center;
        go.transform.localScale = new Vector3(Board.W + pad, Board.H + pad, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = MakePanelSprite(0.06f);
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    public void SetVisible(bool v) { gameObject.SetActive(v); }

    /// <summary>참고 아트에서 잘라낸 특수 블록 그림을 읽는다. 없는 그림은
    /// 절차 생성 스프라이트로 돌아간다 — 아트가 빠져도 게임은 돈다.
    /// 색 블록 그림은 판마다 세트를 골라 UseTheme 이 읽는다.</summary>
    void LoadTileArt()
    {
        const int n = 4;   // 색 블록 수 — 블록 세트 한 벌의 장 수와 같다
        starArt = new Sprite[n];
        blinkArt = new Sprite[n];
        winkArt = new Sprite[n];
        for (int i = 0; i < starArt.Length; i++)
        {
            starArt[i] = LoadArt("tiles/star_" + i);
            blinkArt[i] = LoadArt("tiles/blink_" + i);
            winkArt[i] = LoadArt("tiles/wink_" + i);
        }

        brickArt = new Sprite[2];
        for (int i = 0; i < 2; i++) brickArt[i] = LoadArt("tiles/brick_" + i);

        // 화살표 블록도 칸의 색을 따른다 — 팔레트 색마다 한 장씩 구워 뒀다
        arrowHArt = LoadArtSet("tiles/arrow_h", n);
        arrowVArt = LoadArtSet("tiles/arrow_v", n);
    }

    /// <summary>테마를 적용한다: 테마의 블록 그림 한 벌을 판의 색 블록 그림으로 쓴다.
    /// 넣은 순서와 상관없이 파일 이름순이 팔레트 색 순서다 — BlockTheme.Colors 와 순서가 맞아야 한다.
    /// 그림이 연결되지 않았으면 지금 그림을 그대로 둔다 — 한 벌 빠졌다고 판이 안 그려지면 안 된다.</summary>
    public void UseTheme(BlockTheme theme)
    {
        var texs = theme.Blocks != null ? System.Array.FindAll(theme.Blocks, t => t != null) : new Texture2D[0];
        if (texs.Length == 0) return;
        System.Array.Sort(texs, (a, b) => string.CompareOrdinal(a.name, b.name));

        jellyArt = new Sprite[texs.Length];
        faceArt = new Sprite[texs.Length][];
        for (int i = 0; i < texs.Length; i++)
        {
            jellyArt[i] = ToSprite(texs[i]);
            faceArt[i] = new[] { jellyArt[i] };
        }
        tileDraw = theme.BlockScale;      // 칸 대비 그림 크기 — 그림 여백이 세트마다 달라 BlockTheme 이 정한다
        if (useArt) return;

        useArt = true;
        bandageStages = new Sprite[ObstacleStyle.Stages];
        whiteArt = MakeWhiteTileSprite();
    }

    /// <summary>색마다 한 장씩인 그림 묶음. 색깔별 파일이 없으면 색 없는 한 장으로 전부 채운다.</summary>
    static Sprite[] LoadArtSet(string path, int n)
    {
        var set = new Sprite[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            set[i] = LoadArt(path + "_" + i);
            if (set[i] != null) any = true;
        }
        if (any)
        {
            for (int i = 0; i < n; i++) if (set[i] == null) set[i] = set[0];
            return set;
        }
        var one = LoadArt(path);
        if (one == null) return null;
        for (int i = 0; i < n; i++) set[i] = one;
        return set;
    }

    static Sprite LoadArt(string path)
    {
        var t = Resources.Load<Texture2D>(path);
        return t == null ? null : ToSprite(t);
    }

    static Sprite ToSprite(Texture2D t)
    {
        // 긴 변을 1칸(1 유닛)에 맞춘다. 가로 기준으로 맞추면 세로가 긴 그림이
        // 칸을 넘겨 위아래로 눌린 것처럼 보인다.
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f),
                             Mathf.Max(t.width, t.height));
    }

    /// <summary>아랫줄이 윗줄을 덮도록 줄마다 깊이를 조금씩 준다.
    /// 칸보다 큰 그림은 위 칸으로 넘치는데, 정렬이 같으면 위 칸 블록이
    /// 그 위에 그려져 갓이 잘려 보인다. 떨어지는 블록도 도착할 줄의 깊이를 받으므로
    /// 지나치는 윗줄들 위로 온전히 보인다.</summary>
    static float TileZ(int y) { return y * 0.02f; }

    /// <summary>떨어지거나 착지 중인 블록의 깊이. 멈춰 있는 모든 칸(0 ~ 0.2)보다 앞이라
    /// 지나가는 칸에 파묻히지 않는다. 칸보다 큰 그림이 떨어질 때
    /// 아래 칸에 잘려 보이던 문제가 여기서 생겼다. 움직이는 것들끼리는 원래 줄 순서를 지킨다.</summary>
    static float MovingZ(int y) { return -0.30f + y * 0.002f; }

    /// <summary>칸마다 늘 같은 변형을 고른다. 매번 다시 뽑으면 그릴 때마다 그림이 바뀐다.</summary>
    static Sprite Variant(Sprite[] set, int x, int y)
    {
        if (set == null) return null;
        int h = (x * 73856093) ^ (y * 19349663);
        return set[Mathf.Abs(h) % set.Length];
    }

    /// <summary>색 인덱스에 해당하는 블록 그림. 아트가 없으면 절차 생성 타일.</summary>
    Sprite TileArtFor(int c)
    {
        if (!useArt || c < 0) return tile;
        return jellyArt[c % jellyArt.Length];
    }

    /// <summary>이 칸에 그릴 블록 그림. 목표 칸은 같은 색의 별 모양 블록이 된다 —
    /// 예전처럼 블록 위에 큰 별을 얹으면 칸 밖으로 삐져나온다.</summary>
    Sprite TileArtFor(int c, bool marked)
    {
        if (!useArt || c < 0) return tile;
        if (marked && starArt != null && starArt[c % starArt.Length] != null)
            return starArt[c % starArt.Length];
        return jellyArt[c % jellyArt.Length];
    }

    /// <summary>판 위의 칸에 그릴 그림. 같은 색이라도 칸마다 표정이 다르다.
    /// 좌표 해시로 고르므로 다시 그려도 같은 칸은 같은 얼굴이다.</summary>
    Sprite TileArtFor(int c, bool marked, int x, int y)
    {
        if (!useArt || c < 0) return tile;
        if (marked && starArt != null && starArt[c % starArt.Length] != null)
            return starArt[c % starArt.Length];
        var set = faceArt != null ? faceArt[c % faceArt.Length] : null;
        return set != null && set.Length > 0 ? Variant(set, x, y) : jellyArt[c % jellyArt.Length];
    }

    Sprite[] bandageStages;
    Sprite whiteArt;              // 아트 타일과 같은 실루엣의 흰 판 (파괴·타격 플래시용)

    /// <summary>아트 블록과 같은 방석 실루엣의 흰 판.</summary>
    static Sprite MakeWhiteTileSprite()
    {
        const int S = 96;
        const float Fill = 0.91f;      // 아트에서 블록이 차지하는 비율
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (((x + 0.5f) / S) * 2f - 1f) / Fill;
                float v = (((y + 0.5f) / S) * 2f - 1f) / Fill;
                px[y * S + x] = new Color(1, 1, 1, Mathf.Clamp01((1f - JellyDist(u, v)) * S * 0.20f));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>벽돌 위에 얹는 금. 아트 자체는 온전한 그림 한 장이라
    /// 손상 단계는 이 금으로 보여준다 (0 단계면 아무것도 안 그린다).</summary>
    void SetBandage(SpriteRenderer ov, int stage)
    {
        if (stage <= 0) { ov.enabled = false; return; }
        if (bandageStages[stage] == null) bandageStages[stage] = MakeBandageSprite(stage);
        ov.enabled = true;
        ov.sprite = bandageStages[stage];
        ov.color = Color.white;
        ov.transform.localScale = Vector3.one * tileDraw;
    }

    /// <summary>손상 단계별 금. 단계가 오를수록 굵고 여러 갈래가 된다.</summary>
    /// <summary>맞을수록 십자 밴드를 하나씩 더 붙인다. 처음 깨지면 하나, 더 깨지면 둘.
    /// 금이 가는 것보다 이 게임의 말랑한 톤에 맞다. 젤리 블록과 따로 놀지 않게
    /// 반투명 유백색 + 위쪽 광택으로 같은 재질처럼 보이게 그린다.</summary>
    static Sprite MakeBandageSprite(int stage)
    {
        const int S = 128;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];

        // (중심x, 중심y, 각도, 긴 쪽 길이) — 십자의 짧은 쪽은 이 길이의 0.62배
        float[,] cross = {
            { 0.52f, 0.56f,  16f, 0.60f },
            { 0.36f, 0.33f, -28f, 0.50f },
        };
        int n = stage <= (ObstacleStyle.Stages - 1) / 2 ? 1 : 2;
        n = Mathf.Clamp(n, 1, cross.GetLength(0));

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f) / S, ny = (y + 0.5f) / S;
                float u = nx * 2f - 1f, v = ny * 2f - 1f;
                if (JellyDist(u, v) > 0.94f) { px[y * S + x] = new Color(0, 0, 0, 0); continue; }

                Color acc = new Color(0, 0, 0, 0);
                for (int k = 0; k < n; k++)
                {
                    float cx = cross[k, 0], cy = cross[k, 1];
                    float rad = cross[k, 2] * Mathf.Deg2Rad, len = cross[k, 3];
                    float dx = nx - cx, dy = ny - cy;
                    float lx = dx * Mathf.Cos(rad) + dy * Mathf.Sin(rad);
                    float ly = -dx * Mathf.Sin(rad) + dy * Mathf.Cos(rad);

                    // 십자 — 긴 띠와 짧은 띠를 겹친다. 둘 중 더 안쪽인 쪽으로 음영을 잡는다
                    var a1 = Strip(lx, ly, len, StripHalf);
                    var a2 = Strip(ly, lx, len * 0.62f, StripHalf);
                    float a = Mathf.Max(a1.x, a2.x);
                    if (a <= 0f) continue;
                    float across = a1.x >= a2.x ? ly : lx;      // 띠를 가로지르는 좌표 — 광택·그늘용

                    // 젤리처럼: 위쪽은 흰 광택, 아래쪽은 살짝 그늘, 전체는 반투명
                    float t = Mathf.Clamp01(across / StripHalf * 0.5f + 0.5f);
                    Color c = Color.Lerp(BandGloss, BandShade, t);
                    c.a = a * Mathf.Lerp(0.94f, 0.80f, t);
                    acc = Over(c, acc);
                }
                px[y * S + x] = acc;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    const float StripHalf = 0.062f;                                   // 띠 두께의 절반
    static readonly Color BandGloss = new Color(1.00f, 0.99f, 0.96f); // 위쪽 광택
    static readonly Color BandShade = new Color(0.90f, 0.83f, 0.76f); // 아래쪽 그늘

    /// <summary>끝이 둥근 띠. x 는 커버리지, y 는 안 쓴다 (Vector2 로 묶어 두 번 재지 않게).</summary>
    static Vector2 Strip(float lx, float ly, float len, float halfW)
    {
        float ex = Mathf.Max(Mathf.Abs(lx) - (len * 0.5f - halfW), 0f);
        float d = Mathf.Sqrt(ex * ex + ly * ly);
        return new Vector2(Mathf.Clamp01((halfW - d) * 128f * 0.6f), 0f);
    }

    /// <summary>위에 얹기 — 반창고가 겹칠 때 쓴다.</summary>
    static Color Over(Color top, Color bottom)
    {
        float a = top.a + bottom.a * (1f - top.a);
        if (a <= 0f) return new Color(0, 0, 0, 0);
        return new Color((top.r * top.a + bottom.r * bottom.a * (1f - top.a)) / a,
                         (top.g * top.a + bottom.g * bottom.a * (1f - top.a)) / a,
                         (top.b * top.a + bottom.b * bottom.a * (1f - top.a)) / a, a);
    }

    /// <summary>칸 하나를 그린다. 아이템 아이콘까지 여기서 함께 다룬다 —
    /// 콘크리트는 아래 색 칸 + 구멍 뚫린 콘크리트 덮개 두 겹이라 두 레이어를 같이 정해야 한다.</summary>
    void PaintTile(int x, int y, int c, int hp, ItemType item, Color[] palette)
    {
        var sr = tiles[x, y];
        var ov = overlays[x, y];
        var fc = faces[x, y];
        cellColor[x, y] = c;

        // 심지가 타야 할 폭탄 칸을 갱신한다
        for (int i = fuseCells.Count - 1; i >= 0; i--)
            if (fuseCells[i].X == x && fuseCells[i].Y == y) fuseCells.RemoveAt(i);
        if (item == ItemType.Bomb5) fuseCells.Add(new Point(x, y));

        if (c == Board.Obstacle)
        {
            fc.enabled = false;
            // 내구도는 스테이지마다 다르다. 상수로 계산하면 손상이 안 보인 채로
            // 한 방에 사라지는 것처럼 보인다 — 이 판의 최대 내구도를 기준으로 환산한다.
            int stage = ObstacleStyle.StageFor(hp, obstacleMaxHp);

            if (useArt && brickArt[0] != null)
            {
                sr.sprite = Variant(brickArt, x, y);
                sr.color = Color.white;
                sr.transform.localScale = Vector3.one * tileDraw;
                SetBandage(ov, stage);
                return;
            }

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
            ov.color = Color.white;
            ov.transform.localScale = Vector3.one * ObstacleStyle.Scale;
            return;
        }

        // 가로·세로 폭탄은 참고 아트의 화살표 블록을 통째로 쓴다 (글리프를 얹지 않는다)
        Sprite[] arrowSet = null;
        if (useArt && item == ItemType.Row) arrowSet = arrowHArt;
        else if (useArt && item == ItemType.Col) arrowSet = arrowVArt;
        Sprite arrowTile = arrowSet != null && c >= 0 ? arrowSet[c % arrowSet.Length] : null;
        if (arrowTile != null)
        {
            sr.sprite = arrowTile;
            sr.color = Color.white;
            sr.transform.localScale = Vector3.one * tileDraw;
            fc.enabled = false;
            ov.enabled = false;
            return;
        }

        sr.sprite = TileArtFor(c, marks[x, y].enabled, x, y);
        // 아트에는 색이 이미 들어 있다. 절차 생성 타일일 때만 팔레트 색을 곱한다.
        sr.color = useArt ? Color.white : (c == Board.Empty ? EmptyColor : palette[c]);
        if (useArt && c == Board.Empty) sr.color = new Color(1, 1, 1, 0);
        sr.transform.localScale = Vector3.one * tileDraw;

        // 아트에는 광택·표정이 이미 그려져 있으므로 오버레이를 얹지 않는다.
        // 절차 생성 타일일 때만 광택을 얹고, 목표 칸(별)에서는 표정을 뺀다.
        fc.enabled = !useArt && c != Board.Empty;
        if (fc.enabled)
        {
            fc.sprite = marks[x, y].enabled ? glossSprite : faceSprite;
            fc.color = Color.white;
        }

        ov.enabled = item != ItemType.None;
        if (item != ItemType.None)
        {
            ov.sprite = icons[item];
            bool artArrow = useArt && (item == ItemType.Row || item == ItemType.Col);
            ov.transform.localScale = Vector3.one * (artArrow ? tileDraw : 0.8f);
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
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                int t = b.GetTile(x, y);
                PaintTile(x, y, t, b.GetObstacleHp(x, y), b.GetItem(x, y), palette);
                tiles[x, y].transform.localPosition = new Vector3(x, y, TileZ(y));
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
                markFills[x, y].enabled = on && !useArt;   // 아트에서는 블록 자체가 별이다
                if (faces != null) faces[x, y].sprite = on ? glossSprite : faceSprite;
                if (on) markList.Add(new Point(x, y));
            }
        PulseMarks();
    }

    /// <summary>그 칸 타일의 Transform (테스트용).</summary>
    public Transform TileTransformForTest(int x, int y) { return tiles[x, y].transform; }

    /// <summary>그 칸에 지금 그려진 그림 (테스트용).</summary>
    public Sprite TileSpriteForTest(int x, int y) { return tiles[x, y].sprite; }

    /// <summary>벽돌 그림인가 (테스트용).</summary>
    public bool IsBrickSprite(Sprite sp) { return Has(brickArt, sp); }

    static bool Has(Sprite[] set, Sprite sp)
    {
        if (set == null || sp == null) return false;
        foreach (var t in set) if (t == sp) return true;
        return false;
    }

    /// <summary>아트 타일을 쓰고 있는가 (테스트용).</summary>
    public bool UsingArt { get { return useArt; } }

    /// <summary>그 색의 화살표 블록 그림 (테스트용).</summary>
    public Sprite ArrowArtForTest(ItemType it, int color)
    {
        var set = it == ItemType.Row ? arrowHArt : arrowVArt;
        return set == null || color < 0 ? null : set[color % set.Length];
    }

    /// <summary>목표 칸 하나를 깼다. 지우지 않고 흐리게 남긴다 — 어디를 깼는지 보이게.</summary>
    public void ClearMark(int x, int y)
    {
        if (marks == null || !marks[x, y].enabled) return;
        for (int i = 0; i < markList.Count; i++)
            if (markList[i].X == x && markList[i].Y == y) { markList.RemoveAt(i); break; }

        // 새어나오는 빛은 끈다. 켜 둔 채 색만 바꾸면 칸 전체가 계속 물들어 있어
        // 아직 깨야 하는 칸처럼 보인다.
        marks[x, y].enabled = false;
        faces[x, y].sprite = faceSprite;   // 별이 빠졌으니 다시 표정이 있는 보통 블록이다

        // 흔적은 칸 안쪽에만, 맥동 없이 옅게 남긴다 — 어디를 이미 깼는지는 보여야 한다.
        markFills[x, y].enabled = !useArt;
        markFills[x, y].color = MarkDoneColor;
    }

    /// <summary>남은 목표 칸의 가장자리가 흰빛으로 숨쉬듯 반짝인다.
    /// 무지개색으로 도는 대신 흰 반짝이 가루(SpawnSparkle)가 반짝임을 맡는다.
    /// 칸마다 위상을 어긋나게 줘서 칸 하나하나가 따로 읽힌다.</summary>
    void PulseMarks()
    {
        float t = Time.time;

        for (int i = 0; i < markList.Count; i++)
        {
            int x = markList[i].X, y = markList[i].Y;
            // 가로·세로로 다른 보폭이라 이웃 칸끼리 색이 겹치지 않는다
            float phase = Frac(x * 0.29f + y * 0.47f);

            float blink = 0.5f + 0.5f * Mathf.Sin((t + phase * 1.7f) * 4f);
            blink = blink * blink * (3f - 2f * blink);   // 양 끝에 머무는 시간을 늘린다

            // 흰 빛이 세면 블록이 하얗게 묻힌다 — 아주 옅게만 두고 반짝임은 별가루가 맡는다
            var c = Color.Lerp(MarkGold, Color.white, blink * 0.35f);
            c.a = 0.05f + 0.13f * blink;
            marks[x, y].color = c;

            // 별은 제 색(노랑)을 그대로 두고 밝기만 숨쉰다 — 틴트로 색을 덮으면 별색이 죽는다.
            var lit = Color.Lerp(new Color(0.90f, 0.88f, 0.82f), Color.white, blink);
            lit.a = 0.88f + 0.12f * blink;
            markFills[x, y].color = lit;
        }
    }

    /// <summary>남은 목표 칸에서 별빛 가루가 피어오른다. 한 번에 하나씩만 띄워
    /// 칸이 많아도 화면이 번잡해지지 않게 한다.</summary>
    void UpdateSparkles(float dt)
    {
        if (kSr == null || (liveSparks <= 0 && markList.Count == 0)) return;

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

            kTr[i].localPosition = pos;
            kRot[i] += kSpin[i] * dt;
            kTr[i].localRotation = Quaternion.Euler(0, 0, kRot[i]);
            kSr[i].color = c;
        }

        int spots = markList.Count;
        if (spots == 0) return;

        // 자리가 많을수록 자주 튄다. 고정하면 무늬가 클 때 너무 뜸하다.
        sparkTimer -= dt;
        if (sparkTimer > 0f) return;
        // 하한은 한 프레임(60fps)이다. 이보다 짧게 잡아 봐야 한 프레임에 한 번밖에 못 튄다.
        // 무지개색 테두리를 뺀 대신 흰 가루를 더 자주 띄운다.
        sparkTimer = Mathf.Max(0.016f, 0.020f / spots);

        // 한 자리에서 여러 개가 같이 튀어야 '가루' 로 보인다. 하나씩이면 점이 하나 뜨는 것뿐이다.
        var at = markList[Random.Range(0, spots)];
        int n = Random.Range(13, 20);
        for (int i = 0; i < n; i++) SpawnSparkle(at);
    }

    void SpawnSparkle(Point at)
    {
        int idx = -1;
        for (int i = 0; i < MaxSparks; i++)
            if (!kSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        float ang = Random.value * Mathf.PI * 2f;
        // 별똥별처럼 튀어나가되 너무 멀리 가면 판이 산만해진다 — 예전 범위의 절반으로 잡았다
        kVel[idx] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.85f, 1.9f);
        kLife[idx] = 0f;
        kMax[idx] = Random.Range(0.9f, 1.6f);       // 오래 날아야 칸 밖까지 퍼진다
        kSpin[idx] = Random.Range(-160f, 160f);
        kRot[idx] = Random.value * 360f;
        kPhase[idx] = Random.value * 10f;           // 흔들림·깜빡임을 저마다 다른 박자로
        // 큰 별 몇에 작은 가루 여럿 — 크기가 고르면 가루가 아니라 도형이 흩어지는 것처럼 보인다
        kSize[idx] = Random.value < 0.30f ? Random.Range(0.34f, 0.52f) : Random.Range(0.14f, 0.26f);

        kSr[idx].sprite = star;
        // 하얗게 — 금빛은 살짝 섞는 정도로만 남긴다 (무지개색 대신 흰 가루가 반짝임을 맡는다)
        var c = Color.Lerp(MarkWhite, MarkGold, Random.value * 0.5f);
        c.a = 0f;                                   // 첫 프레임부터 커지며 나타난다
        kSr[idx].color = c;
        // 칸 한가운데보다 가장자리 쪽에서 더 잘 튀게 — 테두리에서도 반짝임이 보이도록.
        float edgeAng = Random.value * Mathf.PI * 2f;
        float edgeR = Random.Range(0.32f, 0.50f);
        kTr[idx].localPosition = new Vector3(at.X + Mathf.Cos(edgeAng) * edgeR,
                                             at.Y + Mathf.Sin(edgeAng) * edgeR, -1.3f);
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
                sr.transform.localScale = new Vector3(tileDraw * sx, tileDraw * sy, 1f);
                sr.transform.localPosition = new Vector3(pts[i].X, pts[i].Y - (1f - e) * a * 0.40f, MovingZ(pts[i].Y));
                // 첫 1~2프레임만 흰색으로 때리고 바로 원색으로 떨어뜨린다
                sr.color = flash ? Color.white : baseCols[i];
            }
            yield return null;
        }
        for (int i = 0; i < pts.Count; i++)
        {
            var sr = tiles[pts[i].X, pts[i].Y];
            sr.color = baseCols[i];
            sr.transform.localScale = Vector3.one * tileDraw;
            sr.transform.localPosition = new Vector3(pts[i].X, pts[i].Y, TileZ(pts[i].Y));
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
    public IEnumerator StampCells(List<Point> cells, Color c, int colorIndex, float total, System.Action onImpact)
    {
        // 전체 시간을 네 박자로 나눈다 (기본 0.34초 기준). 테스트는 total 을 줄여 빨리 돌린다.
        float LiftSec = total * 0.25f, SlamSec = total * 0.16f,
              HoldSec = total * 0.12f, BackSec = total * 0.47f;
        const float LiftY = 0.34f, LiftScale = 1.26f;
        const float SquashX = 1.30f, SquashY = 0.72f;

        foreach (var p in cells)
        {
            var sr = tiles[p.X, p.Y];
            sr.sprite = useArt
                ? (winkArt != null && winkArt[colorIndex % winkArt.Length] != null
                   ? winkArt[colorIndex % winkArt.Length] : TileArtFor(colorIndex))
                : tile;
            sr.color = useArt ? Color.white : c;
            sr.sortingOrder = 5;              // 들어올린 동안 이웃 타일 위에 뜬다

            // 절차 생성 타일에서는 광택·표정도 몸통과 같이 올린다. 안 올리면 몸통(5)이
            // 자기 오버레이(1)를 덮어 내려놓는 동안 납작한 색 덩어리로 뭉개져 보인다.
            var fc = faces[p.X, p.Y];
            fc.enabled = !useArt;
            if (fc.enabled)
            {
                fc.sprite = marks[p.X, p.Y].enabled ? glossSprite : faceSprite;
                fc.color = Color.white;
                fc.sortingOrder = 6;
            }
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
        foreach (var p in cells)
        {
            var sr = tiles[p.X, p.Y];
            if (useArt) sr.sprite = whiteArt;   // 아트는 흰색을 곱해도 그대로라 실루엣으로 바꿔 번쩍인다
            sr.color = Color.white;
        }
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
        foreach (var p in cells)
        {
            var sr = tiles[p.X, p.Y];
            if (useArt) sr.sprite = TileArtFor(colorIndex);
            sr.color = useArt ? Color.white : c;
        }

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
            faces[p.X, p.Y].sortingOrder = 1;
            sr.transform.localScale = Vector3.one * tileDraw;
            sr.transform.localPosition = new Vector3(p.X, p.Y, TileZ(p.Y));
        }
    }

    void Put(Point p, float sx, float sy, float dy)
    {
        var tr = tiles[p.X, p.Y].transform;
        tr.localScale = new Vector3(tileDraw * sx, tileDraw * sy, 1f);
        tr.localPosition = new Vector3(p.X, p.Y + dy, MovingZ(p.Y));   // 들어올린 동안도 앞으로
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
            var sr = tiles[p.X, p.Y];
            if (useArt) sr.sprite = whiteArt;   // 아트 위에는 색을 곱해도 안 보인다 — 실루엣으로 번쩍인다
            sr.color = c;
            sr.transform.localScale = Vector3.one * tileDraw * 1.05f;   // 칸을 꽉 채우는 그림도 옆 칸을 덮지 않게 그림 크기 기준
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
            if (popFx != null)
            {
                 var fx = Instantiate(popFx, transform);
                fx.transform.localPosition = new Vector3(pts[i].X, pts[i].Y, -1.2f);
                var main = fx.GetComponent<ParticleSystem>().main;
                main.startColor = col;   
            }

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

    // ---------- 살아있는 느낌: 눈 깜박임 + 잔잔한 흔들림 ----------
    //
    // 판이 멈춰 있을 때만 움직인다. 낙하·스탬프 연출은 같은 transform 을 쓰므로
    // 그때 같이 건드리면 서로 싸운다.

    bool idleOn;
    float blinkTimer, shiverTimer;
    readonly List<Point> blinking = new List<Point>();
    readonly List<float> blinkLeft = new List<float>();
    int shiverX = -1, shiverY = -1;
    float shiverLeft;

    /// <summary>판이 놀고 있는가. 연출 중에는 꺼서 자세를 건드리지 않는다.</summary>
    public void SetIdle(bool on)
    {
        if (idleOn == on) return;
        idleOn = on;
        if (!on) ClearIdlePose();
    }

    void ClearIdlePose()
    {
        if (tiles == null) return;
        for (int i = blinking.Count - 1; i >= 0; i--) StopBlink(i);
        if (shiverX >= 0) { tiles[shiverX, shiverY].transform.localRotation = Quaternion.identity; shiverX = -1; }
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                var tr = tiles[x, y].transform;
                tr.localScale = Vector3.one * tileDraw;
                tr.localRotation = Quaternion.identity;
            }
    }

    void StopBlink(int i)
    {
        var p = blinking[i];
        int c = cellColor[p.X, p.Y];
        if (c >= 0) tiles[p.X, p.Y].sprite = TileArtFor(c, marks[p.X, p.Y].enabled, p.X, p.Y);
        blinking.RemoveAt(i); blinkLeft.RemoveAt(i);
    }

    /// <summary>이 칸이 표정을 지을 수 있는 보통 색 블록인가 (별·벽돌은 제외).</summary>
    bool CanEmote(int x, int y)
    {
        int c = cellColor[x, y];
        return useArt && c >= 0 && blinkArt != null && blinkArt[c % blinkArt.Length] != null
               && !marks[x, y].enabled;
    }

    // ---- 폭탄 심지 ----
    //
    // 판에 놓인 폭탄 칸을 기억해 두고 심지 끝에서 계속 불똥이 튀게 한다.
    // 가만히 있는 폭탄은 그냥 그림이지만, 타고 있으면 곧 터진다는 게 읽힌다.

    readonly List<Point> fuseCells = new List<Point>();
    float fuseTimer;

    void UpdateFuse(float dt)
    {
        if (fuseCells.Count == 0) return;
        fuseTimer -= dt;
        if (fuseTimer > 0f) return;
        fuseTimer = 0.045f;
        SpawnFuseSpark(fuseCells[Random.Range(0, fuseCells.Count)]);
    }

    void SpawnFuseSpark(Point at)
    {
        int idx = -1;
        for (int i = 0; i < MaxSparks; i++)
            if (!kSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        kVel[idx] = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(0.6f, 1.4f));
        kLife[idx] = 0f;
        kMax[idx] = Random.Range(0.16f, 0.32f);     // 짧게 지지직
        kSpin[idx] = Random.Range(-320f, 320f);
        kRot[idx] = Random.value * 360f;
        kPhase[idx] = Random.value * 10f;
        kSize[idx] = Random.Range(0.09f, 0.17f);
        kSr[idx].sprite = star;
        var c = Color.Lerp(new Color(1f, 0.92f, 0.55f), new Color(1f, 0.52f, 0.18f), Random.value);
        c.a = 0f;
        kSr[idx].color = c;
        // 심지는 그림의 오른쪽 위에 있다
        kTr[idx].localPosition = new Vector3(at.X + 0.22f + Random.Range(-0.03f, 0.03f),
                                             at.Y + 0.40f + Random.Range(-0.03f, 0.03f), -1.4f);
        kTr[idx].localScale = Vector3.zero;
        kTr[idx].localRotation = Quaternion.Euler(0, 0, kRot[idx]);
        kSr[idx].enabled = true;
        liveSparks++;
    }

    void UpdateIdle(float dt)
    {
        if (!idleOn || tiles == null) return;

        // 숨쉬듯 아주 조금 커졌다 작아진다 — 칸마다 박자를 어긋나게 준다
        float t = Time.time;
        for (int x = 0; x < Board.W; x++)
            for (int y = 0; y < Board.H; y++)
            {
                float phase = (x * 0.7f + y * 1.3f);
                float s = 1f + 0.016f * Mathf.Sin(t * 1.7f + phase);
                tiles[x, y].transform.localScale = Vector3.one * tileDraw * s;
            }

        // 눈 깜박임 — 가끔 아무 블록이나 하나
        for (int i = blinking.Count - 1; i >= 0; i--)
        {
            blinkLeft[i] -= dt;
            if (blinkLeft[i] <= 0f) StopBlink(i);
        }
        blinkTimer -= dt;
        if (blinkTimer <= 0f)
        {
            blinkTimer = Random.Range(0.35f, 0.9f);
            int x = Random.Range(0, Board.W), y = Random.Range(0, Board.H);
            if (CanEmote(x, y) && blinking.Count < 6)
            {
                // 눈 감기와 윙크를 번갈아 — 한 가지만 쓰면 표정이 바뀐 줄 모른다
                int c = cellColor[x, y];
                var set = (Random.value < 0.5f && winkArt != null && winkArt[c % winkArt.Length] != null)
                          ? winkArt : blinkArt;
                tiles[x, y].sprite = set[c % set.Length];
                blinking.Add(new Point(x, y));
                blinkLeft.Add(Random.Range(0.35f, 0.65f));   // 짧으면 눈에 안 띈다
            }
        }

        // 부르르 떨기 — 가끔 한 칸이 좌우로 살짝 흔들린다
        if (shiverX >= 0)
        {
            shiverLeft -= dt;
            if (shiverLeft <= 0f)
            {
                tiles[shiverX, shiverY].transform.localRotation = Quaternion.identity;
                shiverX = -1;
            }
            else
            {
                float k = shiverLeft / ShiverTime;
                float ang = Mathf.Sin(shiverLeft * 46f) * 7f * k;   // 점점 잦아든다
                tiles[shiverX, shiverY].transform.localRotation = Quaternion.Euler(0, 0, ang);
            }
        }
        else
        {
            shiverTimer -= dt;
            if (shiverTimer <= 0f)
            {
                shiverTimer = Random.Range(1.2f, 3.0f);
                int x = Random.Range(0, Board.W), y = Random.Range(0, Board.H);
                if (cellColor[x, y] >= 0) { shiverX = x; shiverY = y; shiverLeft = ShiverTime; }
            }
        }
    }

    const float ShiverTime = 0.45f;

    void Update()
    {
        PulseGhost();
        PulseMarks();
        UpdateIdle(Time.deltaTime);
        UpdateFuse(Time.deltaTime);
        float dtr = Time.deltaTime;
        UpdateSparkles(dtr);
        UpdateRings(dtr);
        if (feverOn)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);   // 실제 시간 — 피버 동안은 게임 시간이 빨라진다
            feverHalo.color = new Color(1f, 0.35f, 0.2f, Mathf.Lerp(0.35f, 0.65f, k));
            feverWash.color = new Color(1f, 0.55f, 0.45f, Mathf.Lerp(0.08f, 0.20f, k));
        }
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
        bool moving = Mathf.Abs(off) > 0.001f;
        sr.transform.localPosition = new Vector3(x, wy, moving ? MovingZ(y) : TileZ(y));
        sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * a);
        // 광택·표정은 자식이라 위치는 따라오지만 알파는 따로 맞춰야 판 밖에서 얼굴만 떠 있지 않는다
        if (faces[x, y].enabled) faces[x, y].color = new Color(1f, 1f, 1f, a);

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
                ghostGloss[i].enabled = false;
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
            carryShadow[i].sprite = TileArtFor(p.Color);
            carryShadow[i].transform.localPosition = new Vector3(px + ShadowOffX, py + ShadowOffY, -1.5f);
            carryShadow[i].transform.localScale = Vector3.one * CarryScale * 0.96f;
            carryShadow[i].color = ShadowColor;

            ghost[i].enabled = true;
            // 손에 든 블록은 윙크한다 — 지금 쓰는 조각이라는 표시도 된다
            ghost[i].sprite = useArt && winkArt != null && winkArt[p.Color % winkArt.Length] != null
                            ? winkArt[p.Color % winkArt.Length] : TileArtFor(p.Color);
            ghost[i].transform.localPosition = new Vector3(px, py + CarryLift, -2f);
            ghost[i].transform.localScale = Vector3.one * CarryScale;
            // 아트에는 색이 들어 있으므로 흰색으로 두고, 못 놓는 자리만 붉게 죽인다
            ghostBase[i] = can
                ? (useArt ? Color.white : new Color(pieceColor.r, pieceColor.g, pieceColor.b, 1f))
                : (useArt ? new Color(1f, 0.45f, 0.45f, 0.9f) : new Color(0.55f, 0.30f, 0.30f, 0.85f));
            ghost[i].color = ghostBase[i];

            // 절차 생성 타일일 때만 광택·표정을 따로 얹는다 (아트에는 이미 그려져 있다)
            ghostGloss[i].enabled = !useArt;
            ghostGloss[i].color = new Color(1f, 1f, 1f, ghostBase[i].a);
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
    Vector2[] trayAt;                        // 아트 자리로 옮긴 슬롯 중심 (null 이면 기본 자리)
    Vector2[] trayBox;                       // 그 자리에서 조각이 들어가야 하는 상자 (칸 단위)

    /// <summary>트레이를 플레이 아트의 자리에 맞춘다. 조각은 상자 안에 들어가게 줄여 그린다.
    /// 자리가 바뀌었으면 true — 호출한 쪽이 다시 그려야 한다.</summary>
    public bool SetTrayLayout(Vector2 cur, Vector2 curBox, Vector2 next, Vector2 nextBox)
    {
        if (trayAt != null && (trayAt[0] - cur).sqrMagnitude < 1e-6f && (trayAt[1] - next).sqrMagnitude < 1e-6f
            && (trayBox[0] - curBox).sqrMagnitude < 1e-6f && (trayBox[1] - nextBox).sqrMagnitude < 1e-6f) return false;
        trayAt = new[] { cur, next };
        trayBox = new[] { curBox, nextBox };
        if (trayPad != null) foreach (var p in trayPad) if (p != null) p.enabled = false;   // 받침은 아트에 있다
        return true;
    }

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
        trayGloss = new SpriteRenderer[TraySlots][];

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
            psr.enabled = trayAt == null;
            trayPad[i] = psr;

            trayCells[i] = new SpriteRenderer[5];   // 조각은 최대 5칸
            trayGloss[i] = new SpriteRenderer[5];
            for (int k = 0; k < trayCells[i].Length; k++)
            {
                var go = new GameObject("tray_" + i + "_" + k);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tile;
                sr.sortingOrder = 3;
                sr.enabled = false;
                trayCells[i][k] = sr;

                var tg = new GameObject("traygloss_" + i + "_" + k);
                tg.transform.SetParent(go.transform, false);
                tg.transform.localPosition = new Vector3(0, 0, -0.05f);
                var tsr = tg.AddComponent<SpriteRenderer>();
                tsr.sprite = faceSprite;
                tsr.sortingOrder = 4;
                tsr.enabled = false;
                trayGloss[i][k] = tsr;
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
        var gloss = trayGloss[i];
        bool isNext = i != CurrentSlot;
        if (piece == null)
        {
            foreach (var sr in cells) sr.enabled = false;
            foreach (var sr in gloss) sr.enabled = false;
            return;
        }

        var center = trayAt != null ? trayAt[i] : TraySlotCenter(i);
        float cx, cy;
        PieceCenter(piece, out cx, out cy);
        // 지금 블록은 조금 크게, 다음 블록은 작고 흐리게 — 무엇을 쓰는 중인지 갈린다
        float lift = selected && trayAt == null ? 0.18f : 0f;
        float scale = isNext ? TrayCell * 0.78f : TrayCell * (selected ? 1.12f : 1.0f);
        if (trayAt != null)
        {
            // 아트 자리에서는 조각 전체가 상자 안에 들어가야 한다 — 조각 크기로 나눠 맞춘다
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var c in piece.Cells)
            {
                if (c.X < minX) minX = c.X; if (c.X > maxX) maxX = c.X;
                if (c.Y < minY) minY = c.Y; if (c.Y > maxY) maxY = c.Y;
            }
            float bw = maxX - minX + 1, bh = maxY - minY + 1;
            scale = Mathf.Min(trayBox[i].x / bw, trayBox[i].y / bh, scale);
        }

        for (int k = 0; k < cells.Length; k++)
        {
            if (k >= piece.Cells.Count) { cells[k].enabled = false; gloss[k].enabled = false; continue; }
            var cell = piece.Cells[k];
            cells[k].enabled = true;
            cells[k].sprite = piece.Color >= 0 ? TileArtFor(piece.Color) : tile;
            cells[k].transform.localPosition = new Vector3(
                center.x + (cell.X - cx) * scale,
                center.y + (cell.Y - cy) * scale + lift, 0.5f);
            cells[k].transform.localScale = Vector3.one * scale * 0.94f;
            float alpha = dimmed ? 0.35f : (isNext ? 0.55f : 1f);
            cells[k].color = useArt ? new Color(1f, 1f, 1f, alpha)
                                    : new Color(color.r, color.g, color.b, alpha);
            gloss[k].enabled = !useArt;
            gloss[k].color = new Color(1f, 1f, 1f, alpha);
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
        foreach (var g in ghostGloss) g.enabled = false;   // 폭탄 아이콘 위에 광택·표정을 얹지 않는다

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

    // ---- 해머·무지개 조준 ----
    //
    // 조각을 들고 있는 게 아니라 이미 놓인 칸을 고르는 상태다. 어떤 칸이 사라질지
    // 범위 표시(blast)로 보여주고, 해머는 내리치는 시늉을 같이 띄운다.

    SpriteRenderer aimSr;         // 손에 든 아이템 — 해머 또는 무지개 구슬
    Sprite hammerHeld, rainbowHeld;
    float hammerSwing;

    static readonly Color AimHammer = new Color(1f, 0.86f, 0.55f);
    static readonly Color AimRainbow = new Color(0.95f, 0.72f, 1f);

    /// <summary>조준 중인 칸을 표시한다. ok 가 false 면 못 고르는 칸이라 아무것도 안 뜬다.
    /// 어떤 칸이 사라질지(blast)와 무엇을 들고 있는지(그림)를 같이 보여준다.</summary>
    public void ShowAim(bool hammer, int tx, int ty, bool ok, List<Point> cells)
    {
        if (!ok) { HideAim(); return; }
        ShowRange(cells, hammer ? AimHammer : AimRainbow);

        if (aimSr == null)
        {
            var go = new GameObject("aimitem");
            go.transform.SetParent(transform, false);
            aimSr = go.AddComponent<SpriteRenderer>();
            aimSr.sortingOrder = 9;
            hammerHeld = LoadArt("items/hammer");
            rainbowHeld = LoadArt("items/rainbow");
        }
        var art = hammer ? hammerHeld : rainbowHeld;
        if (art == null) { aimSr.enabled = false; return; }

        aimSr.enabled = true;
        aimSr.sprite = art;
        aimSr.color = Color.white;

        if (hammer)
        {
            // 망치가 왔다갔다 내리친다 — 지금 어떤 칸을 때릴지 눈으로 붙잡아 준다
            hammerSwing += Time.deltaTime * 7f;
            float k = Mathf.Abs(Mathf.Sin(hammerSwing));
            aimSr.transform.localPosition = new Vector3(tx + 0.42f, ty + 0.62f - 0.22f * k, -1.6f);
            aimSr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(38f, -14f, k));
            aimSr.transform.localScale = Vector3.one * 0.95f;
        }
        else
        {
            // 무지개 구슬은 고른 칸 위에서 살짝 떠서 흔들린다
            hammerSwing += Time.deltaTime * 3.2f;
            float k = Mathf.Sin(hammerSwing);
            aimSr.transform.localPosition = new Vector3(tx, ty + 0.52f + 0.06f * k, -1.6f);
            aimSr.transform.localRotation = Quaternion.Euler(0, 0, k * 9f);
            aimSr.transform.localScale = Vector3.one * (0.92f + 0.05f * Mathf.Abs(k));
        }
    }

    public void HideAim()
    {
        blastCount = 0;
        if (blast != null) foreach (var b in blast) if (b != null) b.enabled = false;
        if (aimSr != null) aimSr.enabled = false;
    }

    /// <summary>무지개 — 지워지는 칸마다 무지갯빛 가루를 뿌린다.</summary>
    public void RainbowBurst(List<Point> cells)
    {
        if (cells == null) return;
        foreach (var p in cells)
            for (int i = 0; i < 10; i++) SpawnRainbowSpark(p);
    }

    void SpawnRainbowSpark(Point at)
    {
        int idx = -1;
        for (int i = 0; i < MaxSparks; i++)
            if (!kSr[i].enabled) { idx = i; break; }
        if (idx < 0) return;

        float ang = Random.value * Mathf.PI * 2f;
        kVel[idx] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(1.1f, 2.4f);
        kLife[idx] = 0f;
        kMax[idx] = Random.Range(0.6f, 1.1f);
        kSpin[idx] = Random.Range(-220f, 220f);
        kRot[idx] = Random.value * 360f;
        kPhase[idx] = Random.value * 10f;
        kSize[idx] = Random.Range(0.16f, 0.36f);
        kSr[idx].sprite = star;
        var c = Palette.HslToRgb(Random.value, 0.85, 0.62);   // 가루마다 다른 무지개색
        c.a = 0f;
        kSr[idx].color = c;
        float r = Random.Range(0.05f, 0.34f);
        kTr[idx].localPosition = new Vector3(at.X + Mathf.Cos(ang) * r, at.Y + Mathf.Sin(ang) * r, -1.3f);
        kTr[idx].localScale = Vector3.zero;
        kTr[idx].localRotation = Quaternion.Euler(0, 0, kRot[idx]);
        kSr[idx].enabled = true;
        liveSparks++;
    }

    public void HideGhost()
    {
        if (ghost == null) return;
        ghostCount = 0;
        blastCount = 0;
        if (blast != null) foreach (var b in blast) if (b != null) b.enabled = false;
        foreach (var g in ghost) if (g != null) { g.enabled = false; g.transform.localScale = Vector3.one; }
        if (ghostGloss != null) foreach (var g in ghostGloss) if (g != null) g.enabled = false;
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

    // 크리스탈 글로우: 보석과 같은 컷이지만 전체적으로 더 밝고, 한쪽 면에서 별빛처럼
    // 반짝이는 작은 하이라이트가 하나 더 얹힌다 — 참고 이미지의 "화사하고 반짝이는" 느낌.
    static Sprite MakeCrystalSprite()
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

                float u = (fx - c) / c, v = (fy - c) / c;
                float g;
                if (Mathf.Abs(u) < Mathf.Abs(v))
                    g = v > 0 ? 1.0f : 0.80f;
                else
                    g = u < 0 ? 0.96f : 0.88f;

                float table = Mathf.Clamp01(1f - (Mathf.Abs(u) + Mathf.Abs(v)) / 0.72f);
                g = Mathf.Lerp(g, 1f, 0.62f * table);

                float cut = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) * 9f);
                g *= Mathf.Lerp(1f, 0.92f, cut);

                // 별빛 하이라이트 — 왼쪽 위 면 한 자리에서 반짝인다 (아스트로이드 별 모양)
                float sxn = (fx - S * 0.30f) / (S * 0.16f), syn = (fy - S * 0.72f) / (S * 0.16f);
                float sv = Mathf.Sqrt(Mathf.Abs(sxn)) + Mathf.Sqrt(Mathf.Abs(syn));
                float sparkle = Mathf.Clamp01((1f - sv) * 2.4f);
                g = Mathf.Lerp(g, 1f, sparkle);

                float edge = r - dist;
                g *= Mathf.Lerp(1f, RimDark, Mathf.Clamp01((RimPx - edge) / 1.3f));
                px[y * S + x] = new Color(g, g, g, a);
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    // 주얼 클래식: 같은 컷에 명암 대비를 깊게 주고 테두리(베젤)를 더 진하게 둘러
    // 고급스럽고 세련된 느낌을 낸다 — 화사한 크리스탈과 대비되는 차분한 쪽.
    static Sprite MakeJewelSprite()
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

                float u = (fx - c) / c, v = (fy - c) / c;
                float g;
                if (Mathf.Abs(u) < Mathf.Abs(v))
                    g = v > 0 ? 1.0f : 0.45f;         // 위 면은 밝게, 아래 면은 훨씬 깊게
                else
                    g = u < 0 ? 0.82f : 0.56f;

                float table = Mathf.Clamp01(1f - (Mathf.Abs(u) + Mathf.Abs(v)) / 0.60f);
                g = Mathf.Lerp(g, 1f, 0.30f * table);   // 중앙 하이라이트는 작고 또렷하게

                float cut = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) * 11f);
                g *= Mathf.Lerp(1f, 0.68f, cut);        // 컷 선을 더 진하게 그어 면을 또렷이 가른다

                float edge = r - dist;
                g *= Mathf.Lerp(1f, RimDark * 0.7f, Mathf.Clamp01((RimPx - edge) / 1.1f));  // 베젤을 더 진하게
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

    // example.html .star 의 clip-path 좌표 그대로 (CSS 기준: 위가 0, 아래가 1).
    static readonly float[] StarPoly = {
        0.50f, 0.00f,  0.61f, 0.36f,  1.00f, 0.38f,  0.69f, 0.60f,  0.80f, 1.00f,
        0.50f, 0.76f,  0.20f, 1.00f,  0.31f, 0.60f,  0.00f, 0.38f,  0.39f, 0.36f
    };

    /// <summary>다각형 안인가 (even-odd). 좌표는 0~1, CSS 처럼 위가 0.</summary>
    static bool InPoly(float[] poly, float x, float y)
    {
        bool inside = false;
        int n = poly.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i * 2], yi = poly[i * 2 + 1];
            float xj = poly[j * 2], yj = poly[j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>목표 칸의 별 — example.html .star 를 그대로 옮겼다.
    /// 노란 별(#fff4a3) 몸통 + 왼쪽 위 흰 점(::after) + 아래쪽 옅은 금빛 그림자.</summary>
    static Sprite MakeStarSprite()
    {
        const int S = 64, SS = 3;          // SS x SS 슈퍼샘플링으로 뾰족한 끝을 매끄럽게
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];

        var body = Palette.Hex(0xFFF4A3);
        var shade = Palette.Hex(0xF0D060);          // 아래쪽에 살짝 지는 그늘 — 납작해 보이지 않게
        var glow = new Color(0.82f, 0.59f, 0.16f, 0.30f);   // drop-shadow(0 2px 3px)

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cov = 0f, covShadow = 0f;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float nx = (x + (sx + 0.5f) / SS) / S;
                        float ny = 1f - (y + (sy + 0.5f) / SS) / S;   // CSS 는 위가 0
                        if (InPoly(StarPoly, nx, ny)) cov += 1f;
                        if (InPoly(StarPoly, nx, ny - 0.045f)) covShadow += 1f;   // 2px 아래로
                    }
                cov /= SS * SS; covShadow /= SS * SS;

                float fx = (x + 0.5f) / S, fy = 1f - (y + 0.5f) / S;
                // 아래로 갈수록 살짝 진해진다
                Color c = Color.Lerp(shade, body, Mathf.Clamp01(fy * 0.6f + 0.45f));

                // ::after — 왼쪽 위 흰 점 (left 18%, top 14%, 25% 크기)
                float hd = Mathf.Sqrt(Mathf.Pow(fx - 0.305f, 2) + Mathf.Pow(fy - 0.735f, 2)) / 0.115f;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01((1f - hd) * 3f));

                c.a = cov;
                if (cov < 1f)   // 별 바깥으로 새어나오는 금빛 그림자
                {
                    float g = Mathf.Clamp01(covShadow - cov) * glow.a;
                    if (g > 0f) c = Color.Lerp(new Color(glow.r, glow.g, glow.b, g), c, cov);
                }
                px[y * S + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>네 갈래 별빛 가루. 가운데 심지가 밝고 네 방향으로 뾰족하게 뻗는다.
    /// 별 모양은 아스트로이드(√|x| + √|y| ≤ 1) 라 갈래 사이가 오목하게 파인다 —
    /// 그래야 동그란 먼지가 아니라 '반짝임' 으로 읽힌다.</summary>
    static Sprite MakeSparkSprite()
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
            case TileSkin.Crystal: return MakeCrystalSprite();
            case TileSkin.Jewel: return MakeJewelSprite();
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

    // ---- 젤리 블록 공통 형태 ----
    //
    // 참고 이미지의 블록은 모서리를 둥글린 사각형이 아니라 '눌린 방석(superellipse)' 이다.
    // 가운데가 볼록하고 가장자리로 갈수록 급히 떨어지는 그 형태라야 말랑해 보인다.

    const float JellyN = 3.4f;      // 클수록 사각형, 작을수록 원

    /// <summary>방석 형태의 중심거리. 1 이 경계다.</summary>
    static float JellyDist(float u, float v)
    {
        return Mathf.Pow(Mathf.Pow(Mathf.Abs(u), JellyN) + Mathf.Pow(Mathf.Abs(v), JellyN), 1f / JellyN);
    }

    /// <summary>젤리 몸통. 팔레트 색이 곱해지므로 여기서는 '얼마나 밝은가' 만 정한다 —
    /// 가운데가 볼록한 반구 조명 + 가장자리로 갈수록 진해지는 테두리로 말랑한 덩어리를 만든다.
    /// 흰 광택은 색에 안 묻도록 MakeOverlaySprite 가 따로 얹는다.</summary>
    static Sprite MakeGlossySprite()
    {
        const int S = 96;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];

        // 왼쪽 위에서 들어오는 빛
        Vector3 L = new Vector3(-0.42f, 0.58f, 0.70f).normalized;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = ((x + 0.5f) / S) * 2f - 1f;
                float v = ((y + 0.5f) / S) * 2f - 1f;
                float d = JellyDist(u, v);

                float a = Mathf.Clamp01((1f - d) * S * 0.22f);   // 경계 안티에일리어싱
                if (a <= 0f) { px[y * S + x] = new Color(0, 0, 0, 0); continue; }

                // 반구 높이 — 가운데가 볼록하고 가장자리에서 0
                float h = Mathf.Sqrt(Mathf.Max(0f, 1f - d * d));
                var n = new Vector3(u * 0.85f, v * 0.85f, h + 0.35f).normalized;
                float diff = Mathf.Clamp01(Vector3.Dot(n, L));

                float g = 0.60f + 0.52f * diff;

                // 가장자리는 색이 진해진다 (젤리 안쪽이 두꺼워 보이는 부분)
                float edge = Mathf.Clamp01((d - 0.78f) / 0.22f);
                g *= Mathf.Lerp(1f, 0.74f, edge * edge);

                // 아래쪽으로 빛이 통과한 듯 살짝 되비친다 — 완전히 어두워지면 젤리가 아니라 돌이 된다
                float below = Mathf.Clamp01((-v - 0.15f) / 0.85f);
                g = Mathf.Lerp(g, g + 0.10f, below * (1f - edge));

                px[y * S + x] = new Color(g, g, g, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>블록 위에 얹는 진짜 색 레이어. 팔레트 색에 곱해지지 않으므로 흰색은 흰색으로 나온다.
    /// 참고 이미지의 큰 흰 광택 + 작은 반사점 + 표정(눈·볼·입)을 담는다.
    /// withFace 가 false 면 광택만 — 목표 칸(별)에는 표정을 안 그린다.</summary>
    static Sprite MakeOverlaySprite(bool withFace)
    {
        const int S = 96;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];

        var eyeCol = Palette.Hex(0x2B3252);
        var mouthCol = Palette.Hex(0x39405F);
        var cheekCol = new Color(1f, 0.55f, 0.66f, 0.55f);

        // 눈·볼·입 자리 (0~1, 위가 0)
        const float eyeCx = 0.325f, eyeCy = 0.545f, eyeRx = 0.050f, eyeRy = 0.070f;
        const float cheekCx = 0.205f, cheekCy = 0.655f, cheekRx = 0.072f, cheekRy = 0.042f;
        const float mouthHalf = 0.085f, mouthBase = 0.605f, mouthAmp = 0.055f, mouthThick = 0.020f;

        // 큰 광택: 왼쪽 위에 비스듬히 누운 타원 (rotate -22°)
        const float gcx = 0.335f, gcy = 0.255f, grx = 0.225f, gry = 0.115f;
        float cs = Mathf.Cos(-22f * Mathf.Deg2Rad), sn = Mathf.Sin(-22f * Mathf.Deg2Rad);

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f) / S;
                float ny = 1f - (y + 0.5f) / S;          // 위가 0 인 좌표
                float u = nx * 2f - 1f, v = (1f - ny) * 2f - 1f;
                float mask = Mathf.Clamp01((1f - JellyDist(u, -v)) * S * 0.22f);
                if (mask <= 0f) { px[y * S + x] = new Color(0, 0, 0, 0); continue; }

                float white = 0f;

                // 큰 흰 광택 — 가운데는 꽉 찬 흰색, 경계만 살짝 부드럽게
                float ox = nx - gcx, oy = ny - gcy;
                float rx = (ox * cs - oy * sn) / grx, ry = (ox * sn + oy * cs) / gry;
                float gd = Mathf.Sqrt(rx * rx + ry * ry);
                white = Mathf.Max(white, Mathf.Clamp01((1f - gd) * 3.2f) * 0.92f);

                // 위쪽 가장자리를 따라 도는 옅은 빛
                float topRim = Mathf.Clamp01((ny - 0.72f) / 0.28f);
                white = Mathf.Max(white, topRim * topRim * 0.22f * Mathf.Clamp01(1f - gd * 0.4f));

                // 오른쪽 아래 작은 반사점
                float pdx = nx - 0.775f, pdy = ny - 0.735f;
                float pd = Mathf.Sqrt(pdx * pdx + pdy * pdy) / 0.075f;
                white = Mathf.Max(white, Mathf.Clamp01((1f - pd) * 3f) * 0.42f);

                Color c = new Color(1f, 1f, 1f, white);

                if (withFace)
                {
                    // 볼 — 눈보다 바깥, 살짝 아래
                    float k1 = Mathf.Pow((nx - cheekCx) / cheekRx, 2) + Mathf.Pow((ny - cheekCy) / cheekRy, 2);
                    float k2 = Mathf.Pow((nx - (1f - cheekCx)) / cheekRx, 2) + Mathf.Pow((ny - cheekCy) / cheekRy, 2);
                    float cheek = Mathf.Clamp01((1f - Mathf.Sqrt(Mathf.Min(k1, k2))) * 4f);
                    if (cheek > 0f) c = Blend(c, new Color(cheekCol.r, cheekCol.g, cheekCol.b, cheekCol.a * cheek));

                    // 눈
                    float e1 = Mathf.Pow((nx - eyeCx) / eyeRx, 2) + Mathf.Pow((ny - eyeCy) / eyeRy, 2);
                    float e2 = Mathf.Pow((nx - (1f - eyeCx)) / eyeRx, 2) + Mathf.Pow((ny - eyeCy) / eyeRy, 2);
                    float eye = Mathf.Clamp01((1f - Mathf.Sqrt(Mathf.Min(e1, e2))) * 6f);
                    if (eye > 0f) c = Blend(c, new Color(eyeCol.r, eyeCol.g, eyeCol.b, eye));

                    // 입 — 가운데가 처지고 양끝이 올라가는 짧은 곡선
                    float mdx = nx - 0.5f;
                    if (Mathf.Abs(mdx) <= mouthHalf)
                    {
                        float t = mdx / mouthHalf;
                        float curve = mouthBase - mouthAmp * t * t;
                        float m = Mathf.Clamp01((mouthThick - Mathf.Abs(ny - curve)) * S * 0.10f);
                        if (m > 0f) c = Blend(c, new Color(mouthCol.r, mouthCol.g, mouthCol.b, m));
                    }
                }

                c.a *= mask;
                px[y * S + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    /// <summary>위에 얹기 (알파 합성).</summary>
    static Color Blend(Color under, Color over)
    {
        float a = over.a + under.a * (1f - over.a);
        if (a <= 0.0001f) return new Color(0, 0, 0, 0);
        return new Color((over.r * over.a + under.r * under.a * (1f - over.a)) / a,
                         (over.g * over.a + under.g * under.a * (1f - over.a)) / a,
                         (over.b * over.a + under.b * under.a * (1f - over.a)) / a, a);
    }

    /// <summary>상점 미리보기처럼 보드 밖에서 블록을 그릴 때 쓰는 광택+표정 오버레이.</summary>
    public static Sprite MakeTileOverlaySprite() { return MakeOverlaySprite(true); }

    // 둥근 사각 테두리 (고스트 위치 표시)
    /// <summary>폭탄 아이콘 — example.html 의 .bomb-icon: 보라 구슬(radial-gradient) + 뚜껑 + 불꽃.
    /// 보드 위 폭탄 조각 고스트와 HUD 폭탄 버튼이 같이 쓴다.</summary>
    public static Sprite MakeBombSprite()
    {
        const int S = 48;
        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear };
        var px = new Color[S * S];
        var core = Palette.Hex(0xB58CFF);
        var mid = Palette.Hex(0x6733BF);
        var deep = Palette.Hex(0x381B80);
        var ring = Palette.Hex(0x43247D);
        var cap = Palette.Hex(0x5E4535);
        var spark = Palette.Hex(0xFFD34F);

        float cx = S * 0.5f, cy = S * 0.46f, r = S * 0.40f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = fx - cx, dy = fy - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = new Color(0, 0, 0, 0);
                if (d <= r)
                {
                    // radial-gradient(circle at 32% 25%, core 9%, mid 35%, deep 100%)
                    float hdx = fx - (cx - r * 0.36f), hdy = fy - (cy + r * 0.50f);
                    float t = Mathf.Clamp01(Mathf.Sqrt(hdx * hdx + hdy * hdy) / (r * 1.6f));
                    c = t < 0.35f ? Color.Lerp(core, mid, Mathf.Clamp01((t - 0.09f) / 0.26f))
                                  : Color.Lerp(mid, deep, (t - 0.35f) / 0.65f);
                    if (d > r - 2.2f) c = Color.Lerp(c, ring, (d - (r - 2.2f)) / 2.2f);   // border 3px
                    c.a = Mathf.Clamp01(r - d + 0.5f);
                }
                // ::before 뚜껑 — 오른쪽 위에 걸친 짧은 갈색 호
                float capDy = fy - (cy + r * 1.05f);
                if (capDy > -3f && capDy < 4f && Mathf.Abs(fx - (cx + r * 0.62f)) < 6f) c = cap;
                // ::after 불꽃 — 노란 점 + 글로우
                float sd = Mathf.Sqrt(Mathf.Pow(fx - (cx + r * 0.78f), 2) + Mathf.Pow(fy - (cy + r * 1.35f), 2));
                if (sd <= r * 0.22f) c = Color.Lerp(spark, Color.white, Mathf.Clamp01(1f - sd / (r * 0.22f)) * 0.5f);

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

    /// <summary>포근한 벽돌 타일. 젤리 블록과 같은 방석 실루엣에 둥근 벽돌 두 켜를 얹어
    /// '깰 수 있는 덩어리' 로 읽히게 한다. stage 는 0(온전) ~ Stages-1(곧 부서짐).</summary>
    static Sprite MakeObstacleSprite(int stage)
    {
        const int S = 96;
        float dmg = ObstacleStyle.Stages <= 1 ? 0f : stage / (float)(ObstacleStyle.Stages - 1);

        var deep = ObstacleStyle.Shadow;      // 벽돌 사이 줄눈 (진한 갈색)
        var face = ObstacleStyle.Brick;       // 벽돌 몸통
        var lit = ObstacleStyle.Light;        // 벽돌 윗면
        var pale = ObstacleStyle.BrickPale;   // 깨질수록 흰기가 돈다

        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];

        // 가로 2장 x 세로 3켜, 켜마다 반 장씩 어긋난다
        const int Rows = 3;
        const float BrickW = 0.5f, Round = 0.16f, Gap = 0.055f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f) / S, ny = (y + 0.5f) / S;
                float u = nx * 2f - 1f, v = ny * 2f - 1f;
                float d = JellyDist(u, v);
                float a = Mathf.Clamp01((1f - d) * S * 0.22f);
                if (a <= 0f) { px[y * S + x] = new Color(0, 0, 0, 0); continue; }

                // 줄눈 색을 깔고 그 위에 벽돌을 얹는다
                Color c = deep;

                int row = Mathf.Clamp((int)(ny * Rows), 0, Rows - 1);
                float inRow = ny * Rows - row;                        // 0(아래) ~ 1(위)
                float shift = (row % 2) * BrickW * 0.5f;
                float bx = Frac((nx + shift) / BrickW);               // 벽돌 안 가로 위치

                // 벽돌 한 장을 둥근 사각으로 — 가장자리 Gap 만큼은 줄눈이 보인다
                float ex = Mathf.Min(bx, 1f - bx) * BrickW;           // 좌우 여백(전체 폭 기준)
                float ey = Mathf.Min(inRow, 1f - inRow) / Rows;
                float inset = Mathf.Min(ex, ey) - Gap * 0.5f;
                float corner = Mathf.Clamp01(inset / Round);
                float brick = Mathf.Clamp01(corner * S * 0.10f);

                if (brick > 0f)
                {
                    // 위가 밝고 아래가 어둡다 — 한 장씩 두께가 보인다
                    Color body = Color.Lerp(face, pale, dmg * 0.45f);
                    Color bc = Color.Lerp(Color.Lerp(body, ObstacleStyle.Shadow, 0.30f),
                                          Color.Lerp(body, lit, 0.55f), inRow);
                    // 윗면 하이라이트
                    if (inRow > 0.72f) bc = Color.Lerp(bc, lit, (inRow - 0.72f) / 0.28f * 0.55f);
                    c = Color.Lerp(c, bc, brick);
                }

                // 전체 둥근 덩어리 음영 — 가장자리로 갈수록 어둡게
                float edge = Mathf.Clamp01((d - 0.72f) / 0.28f);
                c = Color.Lerp(c, Color.Lerp(c, ObstacleStyle.Outline, 0.55f), edge * edge);
                // 왼쪽 위에서 오는 빛
                c = Color.Lerp(c, Color.Lerp(c, Color.white, 0.30f),
                               Mathf.Clamp01((-u * 0.5f + v * 0.5f)) * 0.5f * (1f - edge));

                // 손상: 금이 갈라진다
                if (dmg > 0f)
                {
                    float crack = Mathf.Abs((nx - 0.5f) * 0.9f + (ny - 0.5f) * 0.6f
                                            + Mathf.Sin(ny * 9f) * 0.05f);
                    if (crack < 0.020f * dmg) c = Color.Lerp(c, ObstacleStyle.Outline, 0.85f);
                    if (dmg > 0.55f)
                    {
                        float crack2 = Mathf.Abs((nx - 0.45f) * 0.7f - (ny - 0.55f) * 0.9f
                                                 + Mathf.Sin(nx * 11f) * 0.04f);
                        if (crack2 < 0.018f * dmg) c = Color.Lerp(c, ObstacleStyle.Outline, 0.8f);
                    }
                }

                c.a = a;
                px[y * S + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }

    static float Frac(float v) { return v - Mathf.Floor(v); }

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

    /// <summary>점에서 선분까지의 거리. 둥근 끝(캡슐) 글리프를 그리는 데 쓴다.</summary>
    static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float wx = px - ax, wy = py - ay;
        float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
        float dx = wx - vx * t, dy = wy - vy * t;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>아이템 글리프 안인가. 좌표는 -1~1, 전부 끝이 둥근 굵은 획이다.</summary>
    static bool InGlyph(ItemType t, float nx, float ny)
    {
        const float Bar = 0.17f, Tip = 0.14f;
        switch (t)
        {
            case ItemType.Row:      // ←→ 가로 화살표
                return SegDist(nx, ny, -0.40f, 0f, 0.40f, 0f) <= Bar
                    || SegDist(nx, ny, 0.82f, 0f, 0.48f, 0.33f) <= Tip
                    || SegDist(nx, ny, 0.82f, 0f, 0.48f, -0.33f) <= Tip
                    || SegDist(nx, ny, -0.82f, 0f, -0.48f, 0.33f) <= Tip
                    || SegDist(nx, ny, -0.82f, 0f, -0.48f, -0.33f) <= Tip;
            case ItemType.Col:      // ↑↓ 세로 화살표 (가로를 90도 돌린 것)
                return InGlyph(ItemType.Row, ny, nx);
            case ItemType.Diag:     // ✕ 대각선 두 획
                return SegDist(nx, ny, -0.58f, -0.58f, 0.58f, 0.58f) <= Bar
                    || SegDist(nx, ny, -0.58f, 0.58f, 0.58f, -0.58f) <= Bar;
        }
        return false;
    }

    // 아이템 아이콘: 어두운 원판 없이, 흰 획 + 짙은 외곽선만으로 대비를 만든다 (캐주얼 스티커 느낌).
    static Sprite MakeIcon(ItemType t)
    {
        const int S = 48, SS = 3;
        const float OutR = 3.0f;                    // 외곽선 두께(px)
        var ink = new Color(0.08f, 0.09f, 0.17f);

        var mask = new float[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cov = 0f;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float nx = (x + (sx + 0.5f) / SS) / S * 2f - 1f;
                        float ny = (y + (sy + 0.5f) / SS) / S * 2f - 1f;
                        if (InGlyph(t, nx, ny)) cov += 1f;
                    }
                mask[y * S + x] = cov / (SS * SS);
            }

        var px = new Color[S * S];
        int rad = Mathf.CeilToInt(OutR);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float m = mask[y * S + x];

                // 획을 OutR 만큼 부풀린 것에서 획 자신을 뺀 고리 = 외곽선
                float dil = 0f;
                for (int oy = -rad; oy <= rad && dil < 1f; oy++)
                    for (int ox = -rad; ox <= rad; ox++)
                    {
                        if (ox * ox + oy * oy > OutR * OutR) continue;
                        int gx = x + ox, gy = y + oy;
                        if (gx < 0 || gy < 0 || gx >= S || gy >= S) continue;
                        float v = mask[gy * S + gx];
                        if (v > dil) { dil = v; if (dil >= 1f) break; }
                    }
                float outline = Mathf.Clamp01(dil - m) * 0.55f;

                // 흰 획을 외곽선 위에 얹는다 (일반 알파 합성)
                float a = m + outline * (1f - m);
                Color c = a <= 0.001f
                    ? new Color(0, 0, 0, 0)
                    : new Color((1f * m + ink.r * outline * (1f - m)) / a,
                                (1f * m + ink.g * outline * (1f - m)) / a,
                                (1f * m + ink.b * outline * (1f - m)) / a, a);
                px[y * S + x] = c;
            }

        var tex = new Texture2D(S, S) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
    }
}
