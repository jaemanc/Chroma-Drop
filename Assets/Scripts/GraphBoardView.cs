// GraphBoardView.cs — 그래프 보드 렌더링.
// 셀 다각형을 메시로 만들어 배치한다. 셀 크기를 상수로 두지 않고 보드 영역에서 유도한다.
// 터치 판정은 point-in-polygon 이다 (사각 히트박스 아님).

using System.Collections;
using System.Collections.Generic;
using ChromaDrop.Engine;
using UnityEngine;

public class GraphBoardView : MonoBehaviour
{
    public float CellInset = 0.90f;      // 셀 사이 간격 — 경계가 보이도록 살짝 줄인다

    Topology topo;
    Vec2[][] layout;                     // 루트 로컬 좌표계로 옮겨둔 다각형 (터치 판정에 그대로 쓴다)
    Transform root;
    MeshRenderer[] fills;
    MeshRenderer[] overlays;
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

        // 화면 중앙에 오도록 원점을 옮긴다
        var b = Render.Bounds(t);
        float offX = -(float)(b.Width * scale) * 0.5f;
        float offY = -(float)(b.Height * scale) * 0.5f;

        root = new GameObject("cells").transform;
        root.SetParent(transform, false);

        mat = new Material(Shader.Find("Sprites/Default"));

        fills = new MeshRenderer[t.Count];
        overlays = new MeshRenderer[t.Count];

        // 다각형을 루트 로컬 좌표로 옮겨 저장한다. 그리기와 터치 판정이 같은 값을 쓴다.
        for (int i = 0; i < t.Count; i++)
            for (int j = 0; j < layout[i].Length; j++)
                layout[i][j] = new Vec2(layout[i][j].X + offX, layout[i][j].Y + offY);

        for (int i = 0; i < t.Count; i++)
        {
            var center = Centroid(layout[i]);
            fills[i] = MakeCell("cell_" + i, layout[i], center, 0);
            overlays[i] = MakeCell("ov_" + i, layout[i], center, 1);
            overlays[i].transform.localScale = Vector3.one * 0.55f;
            overlays[i].enabled = false;
        }
    }

    MeshRenderer MakeCell(string name, Vec2[] poly, Vec2 center, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3((float)center.X, (float)center.Y, 0);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;

        // 중심 기준 상대 좌표로 만들어야 셀마다 위치를 바꿔도 모양이 유지된다
        var verts = new Vector3[poly.Length + 1];
        verts[0] = Vector3.zero;
        for (int i = 0; i < poly.Length; i++)
            verts[i + 1] = new Vector3((float)(poly[i].X - center.X) * CellInset,
                                       (float)(poly[i].Y - center.Y) * CellInset, 0);

        var tris = new int[poly.Length * 3];
        for (int i = 0; i < poly.Length; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % poly.Length + 1;
        }

        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        mf.mesh = mesh;
        return mr;
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
            fills[i].enabled = true;

            if (v == CellState.Empty) { Paint(fills[i], EmptyColor); overlays[i].enabled = false; }
            else if (v == CellState.Locked) { Paint(fills[i], LockedColor); overlays[i].enabled = false; }
            else if (v == CellState.Brick)
            {
                Paint(fills[i], BrickColor);
                overlays[i].enabled = true;
                Paint(overlays[i], BrickCore(eng.BrickHp(i)));
            }
            else if (v == CellState.Frozen)
            {
                Paint(fills[i], FrozenColor);
                overlays[i].enabled = true;
                Paint(overlays[i], FrozenCore);
            }
            else
            {
                Paint(fills[i], ColorOf(v));
                overlays[i].enabled = false;
            }
        }
    }

    Color ColorOf(int index)
    {
        if (palette == null || index < 0 || index >= palette.Length) return Color.magenta;
        return palette[index];
    }

    static readonly Color EmptyColor = new Color(0.90f, 0.94f, 0.93f, 0.35f);
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

    /// <summary>강조 표시 — 조각 고스트나 아이템 범위 미리보기에 쓴다.</summary>
    public void Highlight(IList<int> cells, Color c)
    {
        if (overlays == null) return;
        if (cells == null) return;
        foreach (int id in cells)
        {
            if (id < 0 || id >= overlays.Length) continue;
            overlays[id].enabled = true;
            Paint(overlays[id], c);
        }
    }

    public void Clear()
    {
        if (root != null) DestroyImmediate(root.gameObject);
        root = null; fills = null; overlays = null; topo = null; layout = null;
    }
}
