using System;
using System.Collections.Generic;
using System.Text;

namespace VisionInspect.Business
{
    /// <summary>
    /// W16-D4 压测：收集 N 次测量的节拍（CT，处理耗时）与内存占用，产出压测报告。
    /// 验收：CT &lt; 150ms、内存无增长。
    /// </summary>
    public sealed class StressTest
    {
        private readonly List<double> _ct = new List<double>();
        private long _memStart;
        private long _memMax;
        private long _memEnd;

        public int Target { get; }

        public StressTest(int target = 2000)
        {
            Target = target;
        }

        public int Count => _ct.Count;

        public bool IsComplete => _ct.Count >= Target;

        public double AvgMs { get { double s = 0; foreach (var v in _ct) s += v; return _ct.Count == 0 ? 0 : s / _ct.Count; } }

        public double MaxMs { get { double m = 0; foreach (var v in _ct) if (v > m) m = v; return m; } }

        public double MinMs { get { double m = double.MaxValue; foreach (var v in _ct) if (v < m) m = v; return _ct.Count == 0 ? 0 : m; } }

        public void Reset()
        {
            _ct.Clear();
            _memStart = _memMax = _memEnd = 0;
        }

        public void AddSample(double ctMs, long workingSetBytes)
        {
            if (_ct.Count >= Target) return;
            if (_ct.Count == 0) _memStart = workingSetBytes;
            _ct.Add(ctMs);
            if (workingSetBytes > _memMax) _memMax = workingSetBytes;
            _memEnd = workingSetBytes;
        }

        public string Report()
        {
            bool ctPass = MaxMs < 150;
            double growthMb = (_memEnd - _memStart) / 1048576.0;
            bool memPass = growthMb < 20; // 增长 < 20MB 视为无泄漏（GC 波动容差）
            var sb = new StringBuilder();
            sb.AppendLine("W16-D4 压测报告 —— " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("样本数\t" + _ct.Count + " / " + Target);
            sb.AppendLine();
            sb.AppendLine("== 节拍（CT，单次处理耗时）==");
            sb.AppendLine("平均\t" + AvgMs.ToString("0.0") + " ms");
            sb.AppendLine("最小\t" + MinMs.ToString("0.0") + " ms");
            sb.AppendLine("最大\t" + MaxMs.ToString("0.0") + " ms");
            sb.AppendLine("判定\t" + (ctPass ? "通过（最大 CT < 150ms）" : "未通过（最大 CT ≥ 150ms）"));
            sb.AppendLine();
            sb.AppendLine("== 内存（工作集）==");
            sb.AppendLine("起始\t" + (_memStart / 1048576.0).ToString("0.0") + " MB");
            sb.AppendLine("峰值\t" + (_memMax / 1048576.0).ToString("0.0") + " MB");
            sb.AppendLine("结束\t" + (_memEnd / 1048576.0).ToString("0.0") + " MB");
            sb.AppendLine("增长\t" + growthMb.ToString("0.0") + " MB");
            sb.AppendLine("判定\t" + (memPass ? "通过（内存无增长）" : "未通过（增长 ≥ 20MB）"));
            return sb.ToString();
        }
    }
}
