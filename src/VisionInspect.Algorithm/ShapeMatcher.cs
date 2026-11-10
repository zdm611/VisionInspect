using System;
using System.Collections.Generic;
using System.Drawing;
using HalconDotNet;

namespace VisionInspect.Algorithm
{
    /// <summary>
    /// Halcon 形状匹配（定位）。
    /// 抗反光配置：ignore_local_polarity + 降采样搜索 + 提高对比度（金属件反光会翻转边缘极性）。
    /// 支持自动分割目标（干净背景）与手动 ROI（复杂背景）两种训练方式。
    /// </summary>
    public sealed class ShapeMatcher : IDisposable
    {
        private HTuple _modelId;
        private HTuple _numLevels;
        private double _modelWidth;
        private double _modelHeight;
        private bool _trained;

        /// <summary>搜索降采样系数：ignore_local_polarity 全分辨率太慢。</summary>
        private const double SearchZoom = 0.35;

        public double MinScore { get; set; } = 0.5;

        public int MaxMatches { get; set; } = 1;

        /// <summary>最小对比度（0-255）：训练时只保留高于此对比度的边缘。</summary>
        public double MinContrast { get; set; } = 20;

        /// <summary>角度搜索范围（度，0-360）。</summary>
        public double AngleRangeDeg { get; set; } = 360;

        public bool IsTrained => _trained;

        /// <summary>自动训练：分割前景目标（最大连通域）作为模型区域，适合干净背景。</summary>
        public void Train(HObject templateImage)
        {
            HOperatorSet.Rgb1ToGray(templateImage, out HObject gray);
            HOperatorSet.ZoomImageFactor(gray, out HObject zoomed, SearchZoom, SearchZoom, "constant");
            using (HObject region = SegmentLargestBlob(zoomed))
            {
                TrainCore(zoomed, region);
            }
            zoomed.Dispose();
            gray.Dispose();
        }

        /// <summary>ROI 训练：矩形区域整体作为模型区域，适合复杂背景（HDevelop 经典流程）。</summary>
        public void Train(HObject templateImage, Rectangle roi)
        {
            HOperatorSet.Rgb1ToGray(templateImage, out HObject gray);
            HOperatorSet.ZoomImageFactor(gray, out HObject zoomed, SearchZoom, SearchZoom, "constant");

            int r1 = (int)Math.Round(roi.Top * SearchZoom);
            int c1 = (int)Math.Round(roi.Left * SearchZoom);
            int r2 = (int)Math.Round(roi.Bottom * SearchZoom);
            int c2 = (int)Math.Round(roi.Right * SearchZoom);
            HOperatorSet.GenRectangle1(out HObject rect, r1, c1, r2, c2);

            TrainCore(zoomed, rect);
            rect.Dispose();
            zoomed.Dispose();
            gray.Dispose();
        }

        private void TrainCore(HObject zoomedGray, HObject modelRegion)
        {
            if (_trained)
            {
                HOperatorSet.ClearShapeModel(_modelId);
                _trained = false;
            }

            double half = AngleRangeDeg * Math.PI / 180.0 / 2.0;
            HOperatorSet.ReduceDomain(zoomedGray, modelRegion, out HObject modelImage);
            HOperatorSet.CreateScaledShapeModel(modelImage, "auto",
                new HTuple(-half), new HTuple(2 * half), "auto",
                0.5, 1.8, "auto",
                "auto", "ignore_local_polarity", MinContrast, "auto", out _modelId);
            HOperatorSet.GetShapeModelParams(_modelId, out _numLevels,
                out HTuple _, out HTuple _, out HTuple _, out HTuple _,
                out HTuple _, out HTuple _, out HTuple _, out HTuple _);

            // 尺寸取训练区域的包围盒（用户 ROI/分割区），不依赖轮廓——
            // 反光会削弱外缘导致轮廓包围盒缩到内部纹理（如硬币的"1"）
            HOperatorSet.SmallestRectangle1(modelRegion, out HTuple rr1, out HTuple cc1, out HTuple rr2, out HTuple cc2);
            _modelWidth = (cc2.ToDArr()[0] - cc1.ToDArr()[0]) / SearchZoom;
            _modelHeight = (rr2.ToDArr()[0] - rr1.ToDArr()[0]) / SearchZoom;
            modelImage.Dispose();

            _trained = true;
        }

        private void UpdateModelDims()
        {
            HOperatorSet.GetShapeModelContours(out HObject contours, _modelId, 1);
            // 多条轮廓时 smallest_rectangle1_xld 每条各返回一个包围盒，需汇总 min/max
            HOperatorSet.SmallestRectangle1Xld(contours, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
            double[] r1a = r1.ToDArr(), c1a = c1.ToDArr(), r2a = r2.ToDArr(), c2a = c2.ToDArr();
            if (c1a.Length == 0)
                throw new InvalidOperationException("模型轮廓为空，无法计算尺寸");
            _modelWidth = (System.Linq.Enumerable.Max(c2a) - System.Linq.Enumerable.Min(c1a)) / SearchZoom;
            _modelHeight = (System.Linq.Enumerable.Max(r2a) - System.Linq.Enumerable.Min(r1a)) / SearchZoom;
            contours.Dispose();
        }

        private static HObject SegmentLargestBlob(HObject gray)
        {
            HObject best = null;
            double bestScore = -1;

            foreach (string polarity in new[] { "dark", "light" })
            {
                HOperatorSet.BinaryThreshold(gray, out HObject region,
                    "max_separability", polarity, out HTuple _);
                HOperatorSet.Connection(region, out HObject conn);
                HOperatorSet.SelectShape(conn, out HObject selected, "area", "and", 500, 1e9);
                HOperatorSet.AreaCenter(selected, out HTuple area, out HTuple _, out HTuple _);

                if (area.Length == 0)
                {
                    region.Dispose();
                    conn.Dispose();
                    selected.Dispose();
                    continue;
                }

                double maxArea = System.Linq.Enumerable.Max(area.ToDArr());
                HOperatorSet.SelectShape(selected, out HObject largest, "area", "and", maxArea - 1, maxArea + 1);
                HOperatorSet.FillUp(largest, out HObject filled);
                HOperatorSet.ClosingCircle(filled, out HObject closed, 3.5);

                HOperatorSet.GetImageSize(gray, out HTuple w, out HTuple h);
                double frameArea = w.ToDArr()[0] * h.ToDArr()[0];
                double ratio = maxArea / frameArea;
                double score = (ratio > 0.01 && ratio < 0.85) ? maxArea : -1;

                region.Dispose();
                conn.Dispose();
                selected.Dispose();
                largest.Dispose();
                filled.Dispose();

                if (score > bestScore)
                {
                    bestScore = score;
                    best?.Dispose();
                    best = closed;
                }
                else
                {
                    closed.Dispose();
                }
            }

            if (best == null)
                throw new InvalidOperationException("模板图中未分割出目标区域（检查前景/背景对比）");
            return best;
        }

        /// <summary>在图像中查找目标，坐标为原图坐标系。</summary>
        public MatchResult[] Find(HObject image)
        {
            if (!_trained) throw new InvalidOperationException("模型尚未训练");

            double half = AngleRangeDeg * Math.PI / 180.0 / 2.0;
            HOperatorSet.ZoomImageFactor(image, out HObject zoomed, SearchZoom, SearchZoom, "constant");
            HOperatorSet.FindScaledShapeModel(zoomed, _modelId,
                new HTuple(-half), new HTuple(2 * half),
                0.5, 1.8,
                MinScore, MaxMatches, 0.5, "least_squares", _numLevels, 0.9,
                out HTuple rows, out HTuple cols, out HTuple angles, out HTuple scales, out HTuple scores);
            zoomed.Dispose();

            double[] rowArr = rows.ToDArr(), colArr = cols.ToDArr();
            double[] angArr = angles.ToDArr(), scaleArr = scales.ToDArr(), scoreArr = scores.ToDArr();
            var results = new List<MatchResult>();
            for (int i = 0; i < scoreArr.Length; i++)
            {
                results.Add(new MatchResult
                {
                    Row = rowArr[i] / SearchZoom,
                    Column = colArr[i] / SearchZoom,
                    Angle = angArr[i],
                    Score = scoreArr[i],
                    Scale = scaleArr[i],
                    Width = _modelWidth * scaleArr[i],
                    Height = _modelHeight * scaleArr[i]
                });
            }
            return results.ToArray();
        }

        public void SaveModel(string path)
        {
            if (!_trained) throw new InvalidOperationException("模型尚未训练");
            HOperatorSet.WriteShapeModel(_modelId, path);
        }

        public void LoadModel(string path)
        {
            HOperatorSet.ReadShapeModel(path, out _modelId);
            HOperatorSet.GetShapeModelParams(_modelId, out _numLevels,
                out HTuple _, out HTuple _, out HTuple _, out HTuple _,
                out HTuple _, out HTuple _, out HTuple _, out HTuple _);
            UpdateModelDims();
            _trained = true;
        }

        public void Dispose()
        {
            if (_trained)
            {
                HOperatorSet.ClearShapeModel(_modelId);
                _trained = false;
            }
        }
    }
}
