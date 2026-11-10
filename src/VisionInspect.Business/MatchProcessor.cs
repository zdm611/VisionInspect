using System;
using System.Collections.Concurrent;
using System.Threading;
using HalconDotNet;
using VisionInspect.Algorithm;

namespace VisionInspect.Business
{
    /// <summary>
    /// 处理线程（施工图数据流：队列 → 匹配 → 结果对象）。
    /// 消费采集队列的最新帧，跑形状匹配，产出 InspectResult 抛给 UI。
    /// 未训练模板时只透传图像（预览仍可用）。
    /// </summary>
    public sealed class MatchProcessor : IDisposable
    {
        private readonly BlockingCollection<HObject> _frames;
        private readonly ShapeMatcher _matcher;
        private readonly CaliperMeasurer _caliper;
        private readonly object _matchLock;
        private Thread _thread;
        private volatile bool _running;
        private volatile bool _paused;
        private long _processed;
        private long _matched;

        public event Action<InspectResult> ResultReady;

        public long ProcessedCount => Interlocked.Read(ref _processed);

        public long MatchedCount => Interlocked.Read(ref _matched);

        public bool IsRunning => _running;

        public MatchProcessor(BlockingCollection<HObject> frames, ShapeMatcher matcher, CaliperMeasurer caliper, object matchLock)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            _caliper = caliper ?? throw new ArgumentNullException(nameof(caliper));
            _matchLock = matchLock ?? throw new ArgumentNullException(nameof(matchLock));
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(ProcessLoop) { IsBackground = true, Name = "process-thread" };
            _thread.Start();
        }

        private void ProcessLoop()
        {
            while (_running)
            {
                HObject frame;
                try
                {
                    if (!_frames.TryTake(out frame, 100)) continue;
                }
                catch (Exception)
                {
                    break;
                }

                MatchResult[] matches = null;
                EdgePair diameter = null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                EdgePair spacing = null;
                if (_matcher.IsTrained && !_paused)
                {
                    try
                    {
                        lock (_matchLock)
                        {
                            matches = _matcher.Find(frame);
                            if (matches != null && matches.Length > 0)
                            {
                                diameter = _caliper.MeasureDiameter(frame, matches[0]);
                                spacing = _caliper.MeasureCenterSpacing(frame, matches[0]);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("处理线程匹配/测量异常", ex);
                        matches = null;
                    }
                }
                sw.Stop();

                Interlocked.Increment(ref _processed);
                if (matches != null && matches.Length > 0) Interlocked.Increment(ref _matched);

                try
                {
                    ResultReady?.Invoke(new InspectResult
                    {
                        Frame = frame,
                        Matches = matches,
                        Diameter = diameter,
                        Spacing = spacing,
                        ProcessMs = sw.Elapsed.TotalMilliseconds,
                        Time = DateTime.Now
                    });
                }
                catch (Exception)
                {
                    // 事件派发失败不能杀死处理线程
                    frame.Dispose();
                }
            }
        }

        /// <summary>暂停/恢复匹配（训练模板时用）。</summary>
        public void PauseMatching(bool pause)
        {
            _paused = pause;
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(1500);
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}


