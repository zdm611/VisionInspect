using System;
using VisionInspect.Algorithm;

namespace VisionInspect.Business
{
    /// <summary>处理结果对象：一帧图像 + 匹配坐标 + 外径/间距测量（后续挂 OK-NG/更多测量项）。</summary>
    public sealed class InspectResult
    {
        public HalconDotNet.HObject Frame { get; set; }
        public MatchResult[] Matches { get; set; }
        public EdgePair Diameter { get; set; }
        public EdgePair Spacing { get; set; }
        /// <summary>本次处理耗时（匹配+测量，ms）——压测节拍 CT。</summary>
        public double ProcessMs { get; set; }
        public DateTime Time { get; set; }
        public bool HasMatch => Matches != null && Matches.Length > 0;
        public bool HasDiameter => Diameter != null && Diameter.EdgeCount >= 2;
        public bool HasSpacing => Spacing != null && Spacing.EdgeCount >= 2;
    }
}

