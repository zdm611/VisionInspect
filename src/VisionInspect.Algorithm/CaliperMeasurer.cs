using System;
using HalconDotNet;

namespace VisionInspect.Algorithm
{
    /// <summary>一次卡尺测量的结果。</summary>
    public sealed class EdgePair
    {
        public double Row1 { get; set; }
        public double Col1 { get; set; }
        public double Row2 { get; set; }
        public double Col2 { get; set; }
        public double DistancePx { get; set; }
        public int EdgeCount { get; set; }

        public override string ToString()
        {
            return string.Format("边1=({0:0.0},{1:0.0}) 边2=({2:0.0},{3:0.0}) 直径={4:0.0}px",
                Row1, Col1, Row2, Col2, DistancePx);
        }
    }

    /// <summary>
    /// 卡尺测量（1D 边缘）：沿指定方向取灰度剖面找边缘点。
    /// 三要素：ROI 方向（phi）、边缘极性（polarity）、阈值（threshold）。
    /// </summary>
    public sealed class CaliperMeasurer
    {
        /// <summary>边缘最小灰度幅值（0-255）。</summary>
        public double Threshold { get; set; } = 30;

        /// <summary>剖面高斯平滑系数。</summary>
        public double Sigma { get; set; } = 1.0;

        /// <summary>边缘极性："all" / "positive"（暗→亮）/ "negative"（亮→暗）。</summary>
        public string Polarity { get; set; } = "all";

        /// <summary>测量带半宽（沿短轴平均降噪）。</summary>
        public double BandHalfWidth { get; set; } = 6;

        /// <summary>外径：沿匹配位姿测直径，三条弦取最接近期望直径的一条，抗单侧边缘漏检。</summary>
        public EdgePair MeasureDiameter(HObject image, MatchResult match)
        {
            double outerR = Math.Max(match.Width, match.Height) / 2;
            return MeasureBestChord(image, match, outerR);
        }

        /// <summary>中心间距：中心特征区（如硬币"1"字宽）的边缘跨度，沿两个正交方向取较小值。</summary>
        public EdgePair MeasureCenterSpacing(HObject image, MatchResult match)
        {
            double outerR = Math.Max(match.Width, match.Height) / 2;
            double zoneRadius = outerR * 0.35;
            double halfLength = outerR * 0.6;

            EdgePair best = null;
            for (int k = 0; k < 2; k++)
            {
                double phi = match.Angle + k * Math.PI / 2.0;
                EdgePair pair = MeasureSpacing(image, match.Row, match.Column, phi, halfLength, zoneRadius);
                if (pair.EdgeCount >= 2 && (best == null || pair.DistancePx < best.DistancePx))
                    best = pair;
            }
            return best;
        }

        /// <summary>中心区（|s| &lt; zoneRadius）内首尾边缘的跨度 = 中心特征的宽度。</summary>
        private EdgePair MeasureSpacing(HObject image, double row, double col, double phi, double halfLength, double zoneRadius)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            HOperatorSet.GenMeasureRectangle2(row, col, phi, halfLength, BandHalfWidth,
                w.ToDArr()[0], h.ToDArr()[0], "nearest_neighbor", out HTuple handle);
            try
            {
                HOperatorSet.MeasurePos(image, handle, Sigma, Threshold,
                    Polarity, "all", out HTuple rows, out HTuple cols,
                    out HTuple amplitudes, out HTuple distances);

                double[] rArr = rows.ToDArr(), cArr = cols.ToDArr();
                double sin = Math.Sin(phi), cos = Math.Cos(phi);
                int i1 = -1, i2 = -1;
                double sMin = double.MaxValue, sMax = double.MinValue;
                for (int i = 0; i < rArr.Length; i++)
                {
                    double s = (rArr[i] - row) * sin + (cArr[i] - col) * cos;
                    if (Math.Abs(s) > zoneRadius) continue;
                    if (s < sMin) { sMin = s; i1 = i; }
                    if (s > sMax) { sMax = s; i2 = i; }
                }
                if (i1 < 0 || i1 == i2)
                    return new EdgePair { EdgeCount = 0 };

                var pair = new EdgePair
                {
                    Row1 = rArr[i1], Col1 = cArr[i1],
                    Row2 = rArr[i2], Col2 = cArr[i2],
                    EdgeCount = rArr.Length
                };
                pair.DistancePx = Math.Sqrt(
                    Math.Pow(pair.Row2 - pair.Row1, 2) + Math.Pow(pair.Col2 - pair.Col1, 2));
                return pair;
            }
            finally
            {
                HOperatorSet.CloseMeasure(handle);
            }
        }

        /// <summary>沿 3 个方向测弦，返回最接近期望直径（2×expectedRadius）的一条。</summary>
        private EdgePair MeasureBestChord(HObject image, MatchResult match, double expectedRadius)
        {
            double halfLength = Math.Max(match.Width, match.Height) / 2 * 1.3;
            EdgePair best = null;
            double bestErr = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                double phi = match.Angle + k * Math.PI / 3.0;
                EdgePair pair = MeasureDiameter(image, match.Row, match.Column, phi, halfLength, expectedRadius);
                if (pair.EdgeCount < 2) continue;
                double err = Math.Abs(pair.DistancePx - 2 * expectedRadius);
                if (err < bestErr)
                {
                    bestErr = err;
                    best = pair;
                }
            }
            return best ?? MeasureDiameter(image, match.Row, match.Column, match.Angle, halfLength, expectedRadius);
        }

        /// <summary>
        /// 沿 phi 方向的弦测直径。expectedRadius > 0 时选"最接近期望半径"的边缘对
        /// （抗单侧漏检/纹理干扰）；否则退化为剖面首尾两点。
        /// </summary>
        public EdgePair MeasureDiameter(HObject image, double row, double col, double phi, double halfLength, double expectedRadius = 0)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            double width = w.ToDArr()[0], height = h.ToDArr()[0];

            HOperatorSet.GenMeasureRectangle2(row, col, phi, halfLength, BandHalfWidth,
                width, height, "nearest_neighbor", out HTuple handle);
            try
            {
                HOperatorSet.MeasurePos(image, handle, Sigma, Threshold,
                    Polarity, "all", out HTuple rows, out HTuple cols,
                    out HTuple amplitudes, out HTuple distances);

                double[] rArr = rows.ToDArr(), cArr = cols.ToDArr();
                if (rArr.Length < 2)
                    return new EdgePair { EdgeCount = rArr.Length };

                int i1, i2;
                if (expectedRadius > 0)
                {
                    // 剖面轴方向 (sin phi, cos phi)，带符号投影距离；
                    // 圆心附近的纹理边缘（如硬币中央的"1"字笔画）直接排除
                    double sin = Math.Sin(phi), cos = Math.Cos(phi);
                    double minAbs = expectedRadius * 0.5;
                    i1 = -1; i2 = -1;
                    double bestNeg = double.MaxValue, bestPos = double.MaxValue;
                    for (int i = 0; i < rArr.Length; i++)
                    {
                        double s = (rArr[i] - row) * sin + (cArr[i] - col) * cos;
                        if (Math.Abs(s) < minAbs) continue;
                        if (s < 0 && Math.Abs(s + expectedRadius) < bestNeg)
                        {
                            bestNeg = Math.Abs(s + expectedRadius);
                            i1 = i;
                        }
                        else if (s >= 0 && Math.Abs(s - expectedRadius) < bestPos)
                        {
                            bestPos = Math.Abs(s - expectedRadius);
                            i2 = i;
                        }
                    }
                    if (i1 < 0 || i2 < 0)
                    {
                        i1 = 0;
                        i2 = rArr.Length - 1;
                    }
                }
                else
                {
                    i1 = 0;
                    i2 = rArr.Length - 1;
                }

                var pair = new EdgePair
                {
                    Row1 = rArr[i1], Col1 = cArr[i1],
                    Row2 = rArr[i2], Col2 = cArr[i2],
                    EdgeCount = rArr.Length
                };
                pair.DistancePx = Math.Sqrt(
                    Math.Pow(pair.Row2 - pair.Row1, 2) + Math.Pow(pair.Col2 - pair.Col1, 2));
                return pair;
            }
            finally
            {
                HOperatorSet.CloseMeasure(handle);
            }
        }
    }
}
