// Render.cs — 셀 다각형 배치. 셀 크기를 상수로 두지 않고 바운딩박스에서 유도한다.
// 터치 판정은 point-in-polygon 이다. 사각 히트박스를 쓰지 않는다.

namespace ChromaDrop.Engine
{
    public struct Extent
    {
        public double MinX, MinY, MaxX, MaxY;
        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
    }

    public static class Render
    {
        /// <summary>권장 최소 터치 크기(pt).</summary>
        public const double MinTouchPt = 44.0;

        public static Extent Bounds(Topology t)
        {
            var r = new Extent { MinX = double.MaxValue, MinY = double.MaxValue,
                               MaxX = double.MinValue, MaxY = double.MinValue };
            foreach (var c in t.Cells)
                foreach (var p in c.Poly)
                {
                    if (p.X < r.MinX) r.MinX = p.X;
                    if (p.Y < r.MinY) r.MinY = p.Y;
                    if (p.X > r.MaxX) r.MaxX = p.X;
                    if (p.Y > r.MaxY) r.MaxY = p.Y;
                }
            return r;
        }

        /// <summary>보드 영역에 맞춰 스케일·센터링한 다각형.</summary>
        public static Vec2[][] Layout(Topology t, double areaW, double areaH,
                                      out double scale, out double cellPt)
        {
            var b = Bounds(t);
            double sx = b.Width > 0 ? areaW / b.Width : 1;
            double sy = b.Height > 0 ? areaH / b.Height : 1;
            scale = sx < sy ? sx : sy;

            double offX = (areaW - b.Width * scale) * 0.5;
            double offY = (areaH - b.Height * scale) * 0.5;

            var outp = new Vec2[t.Count][];
            double minSpan = double.MaxValue;
            for (int i = 0; i < t.Count; i++)
            {
                var src = t.Cells[i].Poly;
                var dst = new Vec2[src.Length];
                double lo = double.MaxValue, hi = double.MinValue;
                for (int j = 0; j < src.Length; j++)
                {
                    dst[j] = new Vec2((src[j].X - b.MinX) * scale + offX,
                                      (src[j].Y - b.MinY) * scale + offY);
                    if (dst[j].X < lo) lo = dst[j].X;
                    if (dst[j].X > hi) hi = dst[j].X;
                }
                if (hi - lo < minSpan) minSpan = hi - lo;
                outp[i] = dst;
            }
            cellPt = minSpan;
            return outp;
        }

        /// <summary>터치 크기가 기준에 못 미치면 경고 문자열, 아니면 null.</summary>
        public static string TouchWarning(double cellPt)
        {
            return cellPt < MinTouchPt
                 ? "셀 터치 크기 " + cellPt.ToString("0.0") + "pt < " + MinTouchPt + "pt"
                 : null;
        }

        /// <summary>교차수 판정. 경계는 포함하지 않는다.</summary>
        public static bool PointInPolygon(Vec2[] poly, Vec2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                bool crosses = (poly[i].Y > p.Y) != (poly[j].Y > p.Y);
                if (!crosses) continue;
                double t = (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y);
                double x = poly[i].X + t * (poly[j].X - poly[i].X);
                if (p.X < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>이 점이 어느 셀인가. 없으면 -1.</summary>
        public static int HitTest(Vec2[][] layout, Vec2 p)
        {
            for (int i = 0; i < layout.Length; i++)
                if (PointInPolygon(layout[i], p)) return i;
            return -1;
        }
    }
}
