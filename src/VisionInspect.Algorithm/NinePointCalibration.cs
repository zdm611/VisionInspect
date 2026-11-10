using System;
using System.Collections.Generic;
using System.Linq;
using HalconDotNet;

namespace VisionInspect.Algorithm
{
    /// <summary>标定板上的一点：像素坐标 + 网格坐标。</summary>
    public sealed class CalibPoint
    {
        public double ColPx { get; set; }
        public double RowPx { get; set; }
        public int GridC { get; set; }
        public int GridR { get; set; }
        public double SpacingMm { get; set; }
        public double XMm => GridC * SpacingMm;
        public double YMm => GridR * SpacingMm;
    }

    /// <summary>标定结果：px→mm 仿射变换 + 精度指标。</summary>
    public sealed class CalibResult
    {
        /// <summary>仿射矩阵 [a b tx; c d ty]：x_mm = a*col + b*row + tx，y_mm = c*col + d*row + ty。</summary>
        public double[] Affine { get; set; } = new double[6];
        public double PxPerMm { get; set; }
        public double ResidualPx { get; set; }
        public int PointCount { get; set; }

        public (double XMm, double YMm) PxToMm(double colPx, double rowPx)
        {
            double[] m = Affine;
            return (m[0] * colPx + m[1] * rowPx + m[2], m[3] * colPx + m[4] * rowPx + m[5]);
        }

        /// <summary>两个像素坐标之间的物理距离（mm）：端点各自变换后取距，吸收各向异性。</summary>
        public double DistanceMm(double col1, double row1, double col2, double row2)
        {
            var p1 = PxToMm(col1, row1);
            var p2 = PxToMm(col2, row2);
            return Math.Sqrt((p2.XMm - p1.XMm) * (p2.XMm - p1.XMm) + (p2.YMm - p1.YMm) * (p2.YMm - p1.YMm));
        }

        public void Save(string path)
        {
            string json = "{\n"
                + "  \"px_per_mm\": " + PxPerMm.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + ",\n"
                + "  \"residual_px\": " + ResidualPx.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + ",\n"
                + "  \"point_count\": " + PointCount + ",\n"
                + "  \"affine\": [" + string.Join(", ", Array.ConvertAll(Affine, v => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))) + "]\n"
                + "}\n";
            System.IO.File.WriteAllText(path, json);
        }

        public static CalibResult Load(string path)
        {
            string text = System.IO.File.ReadAllText(path);
            var result = new CalibResult();
            result.PxPerMm = ParseDouble(text, "px_per_mm");
            result.ResidualPx = ParseDouble(text, "residual_px");
            result.PointCount = (int)ParseDouble(text, "point_count");
            int idx = text.IndexOf("[", text.IndexOf("affine"));
            int end = text.IndexOf("]", idx);
            string[] parts = text.Substring(idx + 1, end - idx - 1).Split(',');
            for (int i = 0; i < 6; i++)
                result.Affine[i] = double.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            return result;
        }

        private static double ParseDouble(string text, string key)
        {
            int i = text.IndexOf(key);
            int colon = text.IndexOf(":", i);
            int start = colon + 1;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            int end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-' || text[end] == '+' || text[end] == 'e' || text[end] == 'E'))
                end++;
            return double.Parse(text.Substring(start, end - start).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 九点标定：识别标定板 3x3 圆点（左上锚点加大），与已知网格坐标配对，
    /// 最小二乘拟合仿射变换，输出 px/mm。
    /// </summary>
    public static class NinePointCalibration
    {
        /// <summary>检出 9 点并按锚点定序（锚点 = 最大圆点 = 网格 0,0）。失败抛异常并附诊断。</summary>
        public static List<CalibPoint> DetectBoard(HObject image, double spacingMm)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            double width = w.ToDArr()[0], height = h.ToDArr()[0];

            HOperatorSet.Rgb1ToGray(image, out HObject gray);
            // 自适应阈值：暗点在灰纸上，max_separability 易被亮区带偏；mean-1.5*std 稳定分割
            HOperatorSet.Intensity(gray, gray, out HTuple gMean, out HTuple gDev);
            double cut = gMean.ToDArr()[0] - 1.5 * gDev.ToDArr()[0];
            HOperatorSet.Threshold(gray, out HObject region, 0, cut);

            // 开运算断开"点与折痕/阴影"的细粘连；半径随分辨率自适应
            double openR = Math.Max(2.0, width / 150.0);
            HOperatorSet.OpeningCircle(region, out HObject opened, openR);

            HOperatorSet.Connection(opened, out HObject conn);
            double minArea = width * height / 20000.0;
            double maxArea = width * height / 50.0;
            HOperatorSet.SelectShape(conn, out HObject selected, "area", "and", minArea, maxArea);
            HOperatorSet.SelectShape(selected, out HObject round, "circularity", "and", 0.5, 1.0);

            HOperatorSet.AreaCenter(round, out HTuple areas, out HTuple rows, out HTuple cols);
            gray.Dispose();
            region.Dispose();
            opened.Dispose();

            int n = areas.Length;
            double[] areaArr = areas.ToDArr(), rArr = rows.ToDArr(), cArr = cols.ToDArr();

            // 面积一致性过滤：真点尺寸相近（锚点约 2.5 倍），阴影/碎片偏离大
            if (n > 2)
            {
                var sortedA = areaArr.OrderBy(x => x).ToArray();
                double med = sortedA[n / 2];
                var keep = Enumerable.Range(0, n).Where(i => areaArr[i] > med * 0.3 && areaArr[i] < med * 3.8).ToArray();
                if (keep.Length >= 3)
                {
                    areaArr = keep.Select(i => areaArr[i]).ToArray();
                    rArr = keep.Select(i => rArr[i]).ToArray();
                    cArr = keep.Select(i => cArr[i]).ToArray();
                    n = keep.Length;
                }
            }

            // 超出 9 个时保留面积最大的 9 个（噪声斑点通常更小）
            if (n > 9)
            {
                var order = Enumerable.Range(0, n).OrderByDescending(i => areaArr[i]).Take(9).ToArray();
                areaArr = order.Select(i => areaArr[i]).ToArray();
                rArr = order.Select(i => rArr[i]).ToArray();
                cArr = order.Select(i => cArr[i]).ToArray();
                n = 9;
            }
            if (n != 9)
                throw new InvalidOperationException(string.Format(
                    "应检出 9 个圆点，实际 {0} 个（面积: {1}）", n,
                    string.Join(",", areaArr.Select(a => a.ToString("0")))));

            // 锚点 = 面积最大的点
            int anchor = 0;
            for (int i = 1; i < n; i++)
                if (areaArr[i] > areaArr[anchor]) anchor = i;

            // 点距（像素）= 锚点到两个最近邻的平均距离
            var dists = new List<(int idx, double d)>();
            for (int i = 0; i < n; i++)
            {
                if (i == anchor) continue;
                double dr = rArr[i] - rArr[anchor], dc = cArr[i] - cArr[anchor];
                dists.Add((i, Math.Sqrt(dr * dr + dc * dc)));
            }
            var nearest = dists.OrderBy(t => t.d).Take(2).ToList();
            double spacingPx = (nearest[0].d + nearest[1].d) / 2;

            // 两轴单位向量（锚点的两个近邻定义网格方向）
            double e1r = (rArr[nearest[0].idx] - rArr[anchor]) / spacingPx;
            double e1c = (cArr[nearest[0].idx] - cArr[anchor]) / spacingPx;
            double e2r = (rArr[nearest[1].idx] - rArr[anchor]) / spacingPx;
            double e2c = (cArr[nearest[1].idx] - cArr[anchor]) / spacingPx;
            double det = e1r * e2c - e1c * e2r;
            if (Math.Abs(det) < 1e-6)
                throw new InvalidOperationException("标定板两个轴向退化（共线），无法定序");

            var points = new List<CalibPoint>();
            for (int i = 0; i < n; i++)
            {
                double dr = rArr[i] - rArr[anchor], dc = cArr[i] - cArr[anchor];
                double u = (dr * e2c - dc * e2r) / det;
                double v = (e1r * dc - e1c * dr) / det;
                points.Add(new CalibPoint
                {
                    ColPx = cArr[i],
                    RowPx = rArr[i],
                    GridC = (int)Math.Round(u / spacingPx),
                    GridR = (int)Math.Round(v / spacingPx),
                    SpacingMm = spacingMm
                });
            }
            return points;
        }

        /// <summary>最小二乘拟合仿射变换（≥3 点），输出 px/mm 与残差。</summary>
        public static CalibResult Fit(List<CalibPoint> points)
        {
            if (points.Count < 3)
                throw new ArgumentException("至少需要 3 个对应点");

            double[] mx = Solve3(points.Select(p => (p.ColPx, p.RowPx, p.XMm)).ToList());
            double[] my = Solve3(points.Select(p => (p.ColPx, p.RowPx, p.YMm)).ToList());

            double a = mx[0], b = mx[1], tx = mx[2];
            double c = my[0], d = my[1], ty = my[2];

            double s1 = Math.Sqrt(a * a + c * c);
            double s2 = Math.Sqrt(b * b + d * d);
            // s1/s2 = mm/px（仿射映 px→mm），px/mm = 两方向倒数的平均
            double pxPerMm = (s1 > 0 && s2 > 0) ? 0.5 * (1.0 / s1 + 1.0 / s2) : 0;

            double sumSq = 0;
            foreach (var p in points)
            {
                double xMm = a * p.ColPx + b * p.RowPx + tx;
                double yMm = c * p.ColPx + d * p.RowPx + ty;
                double dxMm = xMm - p.XMm, dyMm = yMm - p.YMm;
                sumSq += (dxMm * dxMm + dyMm * dyMm) * pxPerMm * pxPerMm;
            }

            return new CalibResult
            {
                Affine = new[] { a, b, tx, c, d, ty },
                PxPerMm = pxPerMm,
                ResidualPx = Math.Sqrt(sumSq / points.Count),
                PointCount = points.Count
            };
        }

        private static double[] Solve3(List<(double col, double row, double val)> pts)
        {
            double scc = 0, scr = 0, srr = 0, sc = 0, sr = 0, sv = 0, scv = 0, srv = 0;
            double n = pts.Count;
            foreach (var (col, row, val) in pts)
            {
                scc += col * col; scr += col * row; srr += row * row;
                sc += col; sr += row; sv += val;
                scv += col * val; srv += row * val;
            }
            double[,] m = { { scc, scr, sc }, { scr, srr, sr }, { sc, sr, n } };
            double[] rhs = { scv, srv, sv };
            return Solve3x3(m, rhs);
        }

        private static double[] Solve3x3(double[,] m, double[] rhs)
        {
            double det =
                m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
                m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
                m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            if (Math.Abs(det) < 1e-12)
                throw new InvalidOperationException("标定点共线，无法拟合仿射变换");
            double[] x = new double[3];
            for (int col = 0; col < 3; col++)
            {
                double[,] t = (double[,])m.Clone();
                for (int r = 0; r < 3; r++) t[r, col] = rhs[r];
                double d2 =
                    t[0, 0] * (t[1, 1] * t[2, 2] - t[1, 2] * t[2, 1]) -
                    t[0, 1] * (t[1, 0] * t[2, 2] - t[1, 2] * t[2, 0]) +
                    t[0, 2] * (t[1, 0] * t[2, 1] - t[1, 1] * t[2, 0]);
                x[col] = d2 / det;
            }
            return x;
        }
    }
}




