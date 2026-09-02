// GraphBoardView.cs — 그래프 보드 렌더링.
// 셀 다각형을 메시로 만들어 배치한다. 셀 크기를 상수로 두지 않고 보드 영역에서 유도한다.
// 터치 판정은 point-in-polygon 이다 (사각 히트박스 아님).

using System.Collections;
using System.Collections.Generic;
using ChromaDrop.Engine;
using UnityEngine;

public class GraphBoardView : MonoBehaviour
{
    public float CellInset = 0.97f;      // 셀 사이 간격 — 경계가 보이도록 살짝 줄인다
    const float FaceInset = 0.82f;       // 테두리 안쪽 면
    const float GlossInset = 0.52f;      // 광택 조각

    Topology topo;
    Vec2[][] layout;                     // 루트 로컬 좌표계로 옮겨둔 다각형 (터치 판정에 그대로 쓴다)
    Transform root;
    MeshRenderer[] rims;     // 같은 색 진한 테두리
    MeshRenderer[] fills;    // 블록 면
    MeshRenderer[] gloss;    // 위쪽 광택
    MeshRenderer[] overlays; // 장애물 속 표시
    MeshRenderer[] ghosts;   // 고스트·예고 (칸 전체를 덮는다)
    Color[] palette;

    Material mat;
    float cellSize;

    public bool Built { get { return topo != null; } }
    public float CellSize { get { return cellSize; } }

    /// <summary>토폴로지에 맞춰 셀을 짓는다. 판이 바뀌면 다시 부른다.</summary>
    public void Build(Topology t, float areaW, float areaH)
    {
        Clear();
        topo = t;

        double scale, cell;
        layout = Render.Layout(t, areaW, areaH, out scale, out cell);
        cellSize = (float)cell;

        // Layout 이 이미 areaW x areaH 안에서 가운데로 맞춰 놨다.
        // 여기서는 그 영역의 중심을 원점으로 옮기기만 한다 (두 번 밀면 판이 한쪽으로 쏠린다).
        float offX = -areaW * 0.5f;
        float offY = -areaH * 0.5f;

        root = new GameObject("cells").transform;
        root.SetParent(transform, false);

        mat = new Material(Shader.Find("Sprites/Default"));

        fills = new MeshRenderer[t.Count];
        overlays = new MeshRenderer[t.Count];

        // 다각형을 루트 로컬 좌표로 옮겨 저장한다. 그리기와 터치 판정이 같은 값을 쓴다.
        for (int i = 0; i < t.Count; i++)
            for (int j = 0; j < layout[i].Length; j++)
                layout[i][j] = new Vec2(layout[i][j].X + offX, layout[i][j].Y + offY);

        BuildPanel();

        rims = new MeshRenderer[t.Count];
        gloss = new MeshRenderer[t.Count];
        ghosts = new MeshRenderer[t.Count];

        for (int i = 0; i < t.Count; i++)
        {
            var center = Centroid(layout[i]);
            // 테두리 → 면 → 광택 순으로 쌓아 블록처럼 보이게 한다
            rims[i] = MakeCell("rim_" + i, layout[i], center, 0, CellInset);
            fills[i] = MakeCell("cell_" + i, layout[i], center, 1, CellInset * FaceInset);
            gloss[i] = MakeCell("gl_" + i, layout[i], center, 2, CellInset * GlossInset);
            gloss[i].transform.localPosition += new Vector3(0, (float)cell * 0.14f, 0);
            overlays[i] = MakeCell("ov_" + i, layout[i], center, 3, CellInset * 0.5f);
            overlays[i].enabled = false;
            ghosts[i] = MakeCell("gh_" + i, layout[i], center, 4, CellInset);
            ghosts[i].enabled = false;
        }
    }

    MeshRenderer MakeCell(string name, Vec2[] poly, Vec2 center, int order, float inset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3((float)center.X, (float)center.Y, -order * 0.01f);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;
        mf.mesh = RoundedMesh(poly, center, inset, CellCut);
        return mr;
    }

    /// <summary>보드 판. 배경 일러스트에 묻히지 않도록 그림자 → 테두리 → 바닥 순으로 깐다.</summary>
    void BuildPanel()
    {
        // 요청 영역이 아니라 셀이 실제로 차지한 범위에 맞춘다.
        // 토폴로지에 따라 가로·세로 중 한쪽만 꽉 차기 때문이다.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var poly in layout)
            foreach (var p in poly)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }

        float w = (float)(maxX - minX) + PanelPad * 2f;
        float h = (float)(maxY - minY) + PanelPad * 2f;
        panelX = (float)(minX + maxX) * 0.5f;
        panelY = (float)(minY + maxY) * 0.5f;

        Quad("halo", w + PanelBorder * 4f, h + PanelBorder * 4f, panelX, panelY, -4, HaloColor);
        Quad("shadow", w + PanelBorder * 2f, h + PanelBorder * 2f, panelX, panelY - PanelBorder * 1.6f, -3, ShadowColor);
        Quad("border", w + PanelBorder * 2f, h + PanelBorder * 2f, panelX, panelY, -2, BorderColor);
        Quad("surface", w, h, panelX, panelY, -1, SurfaceColor);
    }

    void Quad(string name, float w, float h, float x, float y, int order, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(x, y, -order * 0.01f + 0.1f);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;

        var poly = new[]
        {
            new Vec2(-w * 0.5, -h * 0.5), new Vec2(w * 0.5, -h * 0.5),
            new Vec2(w * 0.5, h * 0.5), new Vec2(-w * 0.5, h * 0.5),
        };
        mf.mesh = RoundedMesh(poly, new Vec2(0, 0), 1f, -PanelRadius);
        Paint(mr, c);
    }

    float panelX, panelY;
    const float PanelPad = 0.16f;      // 셀 바깥 여백
    const float PanelBorder = 0.16f;   // 테두리 두께

    static readonly Color SurfaceColor = new Color(0.99f, 0.98f, 0.95f, 0.96f);
    static readonly Color BorderColor = new Color(0.106f, 0.129f, 0.255f);
    static readonly Color ShadowColor = new Color(0.106f, 0.129f, 0.255f, 0.35f);
    static readonly Color HaloColor = new Color(1f, 1f, 1f, 0.22f);

    /// <summary>모서리를 깎은 다각형 메시. 각 꼭짓점을 두 점으로 나눠 둥글게 만든다.
    /// 스프라이트를 못 쓰는 대신 형태로 블록 느낌을 낸다.</summary>
    Mesh RoundedMesh(Vec2[] poly, Vec2 center, float inset, float cut)
    {
        int n = poly.Length;
        var pts = new System.Collections.Generic.List<Vector3>(n * 3);

        for (int i = 0; i < n; i++)
        {
            var prev = poly[(i - 1 + n) % n];
            var cur = poly[i];
            var next = poly[(i + 1) % n];

            // 꼭짓점에서 양옆으로 물러난 두 점 — 그 사이를 몇 조각으로 이어 둥글게
            // 변 길이에 비례해 깎되, 긴 변에서는 고정 폭을 넘지 않게 한다
            var a = Lerp(cur, prev, CutFor(cur, prev, cut));
            var b = Lerp(cur, next, CutFor(cur, next, cut));
            var mid = Lerp(a, b, 0.5f);
            var pull = new Vec2(mid.X + (cur.X - mid.X) * 0.45,
                                mid.Y + (cur.Y - mid.Y) * 0.45);

            pts.Add(Rel(a, center, inset));
            pts.Add(Rel(pull, center, inset));
            pts.Add(Rel(b, center, inset));
        }

        var verts = new Vector3[pts.Count + 1];
        verts[0] = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) verts[i + 1] = pts[i];

        var tris = new int[pts.Count * 3];
        for (int i = 0; i < pts.Count; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % pts.Count + 1;
        }

        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>칸의 모서리를 얼마나 깎을지 (변 길이 대비).</summary>
    const float CellCut = 0.26f;

    /// <summary>판의 모서리 반경 (월드 단위). 변 길이와 무관하게 일정해야 판처럼 보인다.</summary>
    const float PanelRadius = 0.42f;

    /// <summary>cut 이 양수면 비율, 음수면 그 절대값을 월드 거리로 본다.</summary>
    static double CutFor(Vec2 from, Vec2 to, float cut)
    {
        if (cut >= 0) return cut;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return 0;
        double t = -cut / len;
        return t > 0.5 ? 0.5 : t;
    }

    static Vec2 Lerp(Vec2 a, Vec2 b, double t)
    {
        return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    static Vector3 Rel(Vec2 p, Vec2 center, float inset)
    {
        return new Vector3((float)(p.X - center.X) * inset, (float)(p.Y - center.Y) * inset, 0);
    }

    static Vec2 Centroid(Vec2[] poly)
    {
        double x = 0, y = 0;
        foreach (var p in poly) { x += p.X; y += p.Y; }
        return new Vec2(x / poly.Length, y / poly.Length);
    }

    public void SetPalette(Color[] colors) { palette = colors; }

    public void SetVisible(bool on) { if (root != null) root.gameObject.SetActive(on); }

    /// <summary>엔진 상태를 그대로 그린다.</summary>
    public void Refresh(GameEngine eng)
    {
        if (fills == null) return;
        for (int i = 0; i < fills.Length && i < eng.Count; i++)
        {
            int v = eng.Get(i);
            bool solid = v != CellState.Empty;

            rims[i].enabled = solid;
            fills[i].enabled = true;
            gloss[i].enabled = solid;
            overlays[i].enabled = false;

            if (v == CellState.Empty)
            {
                Paint(fills[i], EmptyColor);
                gloss[i].enabled = false;
                continue;
            }

            Color face;
            if (v == CellState.Locked) face = LockedColor;
            else if (v == CellState.Brick) face = BrickColor;
            else if (v == CellState.Frozen) face = FrozenColor;
            else face = ColorOf(v);

            // 테두리는 같은 색을 진하게 — 블록마다 경계가 또렷해진다
            Paint(rims[i], Darken(face, RimDark));
            Paint(fills[i], face);
            Paint(gloss[i], new Color(1f, 1f, 1f, 0.18f));

            if (v == CellState.Brick)
            {
                overlays[i].enabled = true;
                Paint(overlays[i], BrickCore(eng.BrickHp(i)));
            }
            else if (v == CellState.Frozen)
            {
                overlays[i].enabled = true;
                Paint(overlays[i], FrozenCore);
            }
            else if (v == CellState.Locked)
            {
                overlays[i].enabled = true;
                Paint(overlays[i], LockedCore);
            }
        }
    }

    Color ColorOf(int index)
    {
        if (palette == null || index < 0 || index >= palette.Length) return Color.magenta;
        return palette[index];
    }

    const float RimDark = 0.72f;   // 테두리는 면보다 이만큼 어둡다

    static Color Darken(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, c.a); }

    static readonly Color EmptyColor = new Color(0.92f, 0.96f, 0.95f, 0.55f);
    static readonly Color LockedCore = new Color(0.72f, 0.76f, 0.82f, 0.9f);
    static readonly Color LockedColor = new Color(0.24f, 0.27f, 0.33f);
    static readonly Color BrickColor = new Color(0.66f, 0.47f, 0.42f);
    static readonly Color FrozenColor = new Color(0.62f, 0.82f, 0.90f);
    static readonly Color FrozenCore = new Color(0.90f, 0.97f, 1f, 0.85f);

    static Color BrickCore(int hp)
    {
        // 남은 내구도가 적을수록 밝다 — 크기가 작아도 상태가 읽힌다
        float t = Mathf.Clamp01(1f - hp / 4f);
        return Color.Lerp(new Color(0.52f, 0.34f, 0.30f), new Color(0.92f, 0.86f, 0.80f), t);
    }

    static void Paint(MeshRenderer mr, Color c)
    {
        var block = new MaterialPropertyBlock();
        mr.GetPropertyBlock(block);
        block.SetColor("_Color", c);
        mr.SetPropertyBlock(block);
    }

    /// <summary>월드 좌표가 어느 셀인가. 없으면 -1. 다각형 판정이라 육각·삼각에서도 정확하다.</summary>
    public int HitTest(Vector3 world)
    {
        if (layout == null || root == null) return -1;
        var local = root.InverseTransformPoint(world);
        return Render.HitTest(layout, new Vec2(local.x, local.y));
    }

    /// <summary>셀의 월드 좌표 중심 — 연출에 쓴다.</summary>
    public Vector3 CellCenter(int id)
    {
        if (layout == null || id < 0 || id >= layout.Length) return transform.position;
        var c = Centroid(layout[id]);
        return root.TransformPoint(new Vector3((float)c.X, (float)c.Y, 0));
    }

    // ---------- 강조 ----------
    // 고스트(조각이 놓일 자리)와 예고(그래서 사라질 칸)를 나눈다.
    // 예고는 깜빡여야 '여기가 없어진다' 가 바로 읽힌다.

    readonly List<int> ghostCells = new List<int>();
    readonly List<int> doomedCells = new List<int>();
    Color ghostColor = Color.white;
    Color doomedColor = Color.white;

    public void ShowGhost(IList<int> cells, Color c)
    {
        ghostCells.Clear();
        if (cells != null) ghostCells.AddRange(cells);
        ghostColor = c;
    }

    public void ShowDoomed(IList<int> cells, Color c)
    {
        doomedCells.Clear();
        if (cells != null) doomedCells.AddRange(cells);
        doomedColor = c;
    }

    public void ClearHighlight() { ghostCells.Clear(); doomedCells.Clear(); }

    void LateUpdate()
    {
        if (ghosts == null) return;

        for (int i = 0; i < ghosts.Length; i++) ghosts[i].enabled = false;

        // 사라질 칸은 흰색으로 빠르게 깜빡인다 (예전 예고와 같은 박자)
        float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 17f);
        var doom = Color.Lerp(new Color(doomedColor.r, doomedColor.g, doomedColor.b, 0.25f),
                              new Color(doomedColor.r, doomedColor.g, doomedColor.b, 0.92f), k);

        foreach (int id in doomedCells)
        {
            if (id < 0 || id >= ghosts.Length) continue;
            ghosts[id].enabled = true;
            Paint(ghosts[id], doom);
        }

        // 조각이 놓일 자리는 또렷하게 — 깜빡이지 않는다
        foreach (int id in ghostCells)
        {
            if (id < 0 || id >= ghosts.Length) continue;
            ghosts[id].enabled = true;
            Paint(ghosts[id], ghostColor);
        }
    }

    // ---------- 연출 ----------
    //
    // 스프라이트가 없으므로 크기와 색으로 표현한다.
    // 핵심은 '부드러움'이 아니라 순간적인 타격감이라, 이징을 급격한 곡선으로 쓴다.

    /// <summary>들어올렸다가 내려찍는다. onImpact 는 꽂히는 순간에 부른다.</summary>
    public IEnumerator StampCells(IList<int> cells, float total, System.Action onImpact)
    {
        float lift = total * 0.34f;     // 들어올림
        float slam = total * 0.16f;     // 내려찍기 — 짧을수록 세게 보인다
        float hold = total * 0.10f;     // 눌린 채 버팀
        float back = total - lift - slam - hold;

        // 1) 들어올림
        for (float t = 0; t < lift; t += Time.deltaTime)
        {
            float k = t / lift;
            k = 1f - (1f - k) * (1f - k);
            SetScale(cells, Mathf.Lerp(1f, LiftScale, k));
            yield return null;
        }
        // 2) 내려찍기 — 가속
        for (float t = 0; t < slam; t += Time.deltaTime)
        {
            float k = t / slam;
            SetScale(cells, Mathf.Lerp(LiftScale, SquashScale, k * k));
            yield return null;
        }
        if (onImpact != null) onImpact();
        // 3) 버팀
        SetScale(cells, SquashScale);
        yield return new WaitForSeconds(hold);
        // 4) 복원 — 감쇠 진동
        for (float t = 0; t < back; t += Time.deltaTime)
        {
            float k = t / back;
            float damp = Mathf.Exp(-6f * k) * Mathf.Cos(k * Mathf.PI * 3.2f);
            SetScale(cells, 1f + (SquashScale - 1f) * damp);
            yield return null;
        }
        SetScale(cells, 1f);
    }

    const float LiftScale = 1.24f;
    const float SquashScale = 0.82f;

    /// <summary>사라지기 직전 번쩍인다. 직접 터진 칸과 연계로 터진 칸을 색으로 나눈다.</summary>
    public IEnumerator FlashCells(IList<int> cells, Color flash, float dur)
    {
        if (cells == null || cells.Count == 0) yield break;
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float k = t / dur;
            foreach (int id in cells)
            {
                if (id < 0 || id >= fills.Length) continue;
                ghosts[id].enabled = true;
                Paint(ghosts[id], new Color(flash.r, flash.g, flash.b, 1f - k));
                float sc = 1f + 0.18f * (1f - k);
                fills[id].transform.localScale = Vector3.one * sc;
                rims[id].transform.localScale = Vector3.one * sc;
            }
            yield return null;
        }
        foreach (int id in cells)
        {
            if (id < 0 || id >= fills.Length) continue;
            ghosts[id].enabled = false;
            fills[id].transform.localScale = Vector3.one;
            rims[id].transform.localScale = Vector3.one;
        }
    }

    /// <summary>새로 내려온 칸이 튀어 들어온다.</summary>
    public IEnumerator DropIn(IList<int> cells, float dur)
    {
        if (cells == null || cells.Count == 0) yield break;
        for (float t = 0; t < dur; t += Time.deltaTime)
        {
            float k = t / dur;
            // 살짝 넘겼다가 돌아온다
            float sc = 1f + 0.35f * Mathf.Sin(k * Mathf.PI) - 0.35f * (1f - k);
            SetScale(cells, Mathf.Max(0.05f, sc));
            yield return null;
        }
        SetScale(cells, 1f);
    }

    void SetScale(IList<int> cells, float s)
    {
        if (cells == null || fills == null) return;
        foreach (int id in cells)
        {
            if (id < 0 || id >= fills.Length) continue;
            var v = Vector3.one * s;
            fills[id].transform.localScale = v;
            rims[id].transform.localScale = v;
            gloss[id].transform.localScale = v;
        }
    }

    public void Clear()
    {
        if (root != null) DestroyImmediate(root.gameObject);
        root = null; fills = null; overlays = null; rims = null; gloss = null; ghosts = null;
        topo = null; layout = null;
        ghostCells.Clear(); doomedCells.Clear();
    }
}
