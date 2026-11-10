using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VisionInspect.Business
{
    /// <summary>重复性测试：收集 N 次测量值并计算均值/标准差/极差统计表。</summary>
    public sealed class RepeatabilityStats
    {
        private readonly List<double> _values = new List<double>();

        public int Target { get; }

        public RepeatabilityStats(int target = 30)
        {
            Target = target;
        }

        public int Count => _values.Count;

        public bool IsComplete => _values.Count >= Target;

        public IReadOnlyList<double> Values => _values;

        public void Add(double value)
        {
            if (_values.Count < Target) _values.Add(value);
        }

        public void Reset()
        {
            _values.Clear();
        }

        public double Mean => _values.Count == 0 ? 0 : _values.Average();

        /// <summary>样本标准差（n-1）。</summary>
        public double Std
        {
            get
            {
                if (_values.Count < 2) return 0;
                double mean = Mean;
                double sum = _values.Sum(v => (v - mean) * (v - mean));
                return Math.Sqrt(sum / (_values.Count - 1));
            }
        }

        public double Min => _values.Count == 0 ? 0 : _values.Min();

        public double Max => _values.Count == 0 ? 0 : _values.Max();

        public double Range => Max - Min;

        /// <summary>统计表文本（含逐次测量值）。</summary>
        public string Report(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine("测量时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("序号\t直径(px)");
            for (int i = 0; i < _values.Count; i++)
                sb.AppendLine((i + 1) + "\t" + _values[i].ToString("0.00"));
            sb.AppendLine();
            sb.AppendLine("== 统计表 ==");
            sb.AppendLine("样本数\t" + _values.Count);
            sb.AppendLine("均值\t" + Mean.ToString("0.000") + " px");
            sb.AppendLine("标准差\t" + Std.ToString("0.000") + " px");
            sb.AppendLine("最小值\t" + Min.ToString("0.00") + " px");
            sb.AppendLine("最大值\t" + Max.ToString("0.00") + " px");
            sb.AppendLine("极差\t" + Range.ToString("0.00") + " px");
            sb.AppendLine("判定\t" + (Std < 0.5 ? "通过（标准差 < 0.5px）" : "未通过（标准差 ≥ 0.5px）"));
            return sb.ToString();
        }
    }
}
