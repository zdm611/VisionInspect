using System;
using System.Collections.Generic;

namespace VisionInspect.Business
{
    /// <summary>一条检测记录。</summary>
    public sealed class ResultRecord
    {
        public DateTime Time { get; set; }
        public bool Matched { get; set; }
        public double Score { get; set; }
        public double Row { get; set; }
        public double Column { get; set; }
        public double Diameter { get; set; }
        public double Spacing { get; set; }
        public double DiameterMm { get; set; }
        public string Verdict { get; set; } = "-";
    }

    /// <summary>结果历史：保留最近 N 条检测记录（默认 200），供列表界面翻查。</summary>
    public sealed class ResultHistory
    {
        private readonly List<ResultRecord> _items = new List<ResultRecord>();

        public int Capacity { get; }

        public ResultHistory(int capacity = 200)
        {
            Capacity = capacity;
        }

        public int Count => _items.Count;

        public void Add(ResultRecord record)
        {
            _items.Insert(0, record);
            while (_items.Count > Capacity)
                _items.RemoveAt(_items.Count - 1);
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>索引 0 = 最新一条。</summary>
        public ResultRecord this[int index] => _items[index];
    }
}
