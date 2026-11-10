using System;

namespace VisionInspect.Business
{
    /// <summary>公差判定：|测量值 - 标称值| ≤ 公差 → OK。</summary>
    public sealed class ToleranceJudge
    {
        public double Nominal { get; set; }
        public double Tolerance { get; set; }

        public ToleranceJudge(double nominal, double tolerance)
        {
            Nominal = nominal;
            Tolerance = tolerance;
        }

        public double Deviation(double measuredMm) => measuredMm - Nominal;

        public bool Judge(double measuredMm) => Math.Abs(Deviation(measuredMm)) <= Tolerance;

        public string Verdict(double measuredMm)
        {
            double dev = Deviation(measuredMm);
            return string.Format("{0}（{1}{2:0.00}mm）",
                Judge(measuredMm) ? "OK" : "NG",
                dev >= 0 ? "+" : "", dev);
        }
    }
}
