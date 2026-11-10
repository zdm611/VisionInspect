using System;

namespace VisionInspect.Algorithm
{
    /// <summary>形状匹配结果。</summary>
    public sealed class MatchResult
    {
        public double Row { get; set; }
        public double Column { get; set; }
        public double Angle { get; set; }
        public double Score { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Scale { get; set; }

        public override string ToString()
        {
            return string.Format("row={0:0.0} col={1:0.0} angle={2:0.000}rad score={3:0.000}",
                Row, Column, Angle, Score);
        }
    }
}
