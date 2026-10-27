using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

// 화면 바깥쪽 풍경만 살짝 섞어 넓은 배경과의 직선 경계를 없앤다.
[RequireComponent(typeof(Image))]
public class HomeArtEdgeBlend : BaseMeshEffect
{
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive()) return;
        var image = (Image)graphic;
        if (image.sprite == null) return;
        Rect rect = image.GetPixelAdjustedRect();
        Vector4 uv = DataUtility.GetOuterUV(image.sprite);
        float[] stops = { 0, .035f, .965f, 1 };
        mesh.Clear();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                var vertex = UIVertex.simpleVert;
                vertex.position = new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, stops[x]),
                    Mathf.Lerp(rect.yMin, rect.yMax, stops[y]));
                vertex.uv0 = new Vector2(Mathf.Lerp(uv.x, uv.z, stops[x]), Mathf.Lerp(uv.y, uv.w, stops[y]));
                Color color = image.color;
                if (x == 0 || x == 3 || y == 0 || y == 3) color.a = 0;
                vertex.color = color;
                mesh.AddVert(vertex);
            }
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                int i = y * 4 + x;
                mesh.AddTriangle(i, i + 4, i + 5);
                mesh.AddTriangle(i, i + 5, i + 1);
            }
    }
}
