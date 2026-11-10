using System;
using System.Collections.Concurrent;
using System.Threading;
using HalconDotNet;

namespace VisionInspect.Hardware
{
    /// <summary>
    /// 采集线程 + 有界队列（施工图数据流的前半段）：
    /// 采集线程持续抓帧送入 BlockingCollection，队列满时丢弃最旧帧（预览只需最新帧），
    /// 被丢弃的帧立即 Dispose，防止 HObject 堆积。
    /// 断线重连（W16-D3）：抓帧失败/占位画面 → 关闭源 → 每 2 秒重试打开 → 恢复后自动续跑。
    /// </summary>
    public sealed class FramePipeline : IDisposable
    {
        private readonly IFrameSource _source;
        private readonly BlockingCollection<HObject> _frames;
        private Thread _thread;
        private volatile bool _running;
        private long _captured;
        private long _dropped;

        public int QueueCapacity { get; }

        public BlockingCollection<HObject> Frames => _frames;

        public bool IsRunning => _running;

        public string LastError { get; private set; }

        /// <summary>连接状态变化：true=已连接/重连成功，false=断开（附原因）。</summary>
        public event Action<bool, string> ConnectionChanged;

        public long CapturedCount => Interlocked.Read(ref _captured);

        public long DroppedCount => Interlocked.Read(ref _dropped);

        public FramePipeline(IFrameSource source, int queueCapacity = 2)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            QueueCapacity = queueCapacity;
            _frames = new BlockingCollection<HObject>(queueCapacity);
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "capture-thread"
            };
            _thread.Start();
        }

        private void CaptureLoop()
        {
            bool wasDisconnected = false;
            while (_running)
            {
                // 1) 确保相机源打开（未开/断线后走重连）
                if (!_source.IsOpen)
                {
                    try
                    {
                        _source.Open();
                        LastError = null;
                        if (wasDisconnected)
                        {
                            wasDisconnected = false;
                            ConnectionChanged?.Invoke(true, "相机重连成功");
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = "相机断开，自动重连中…（" + ex.Message + "）";
                        if (!wasDisconnected)
                        {
                            wasDisconnected = true;
                            ConnectionChanged?.Invoke(false, LastError);
                        }
                        Thread.Sleep(2000);
                        continue;
                    }
                }

                // 2) 抓帧
                try
                {
                    HObject frame = _source.Grab();
                    Interlocked.Increment(ref _captured);

                    if (!_frames.TryAdd(frame))
                    {
                        while (_frames.TryTake(out HObject stale))
                        {
                            stale.Dispose();
                            Interlocked.Increment(ref _dropped);
                        }
                        _frames.Add(frame);
                    }
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    try { _source.Close(); } catch { /* 关闭失败按断开处理 */ }
                    if (!wasDisconnected)
                    {
                        wasDisconnected = true;
                        ConnectionChanged?.Invoke(false, "相机断开（" + ex.Message + "），自动重连中…");
                    }
                    Thread.Sleep(2000);
                }
            }
        }

        /// <summary>取出队列中当前最新的帧，丢弃其余（调用方负责 Dispose 返回值）。</summary>
        public HObject TakeLatest()
        {
            HObject latest = null;
            while (_frames.TryTake(out HObject frame))
            {
                latest?.Dispose();
                latest = frame;
            }
            return latest;
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(1500);
            _thread = null;
            while (_frames.TryTake(out HObject frame)) frame.Dispose();
        }

        public void Dispose()
        {
            Stop();
            _source.Dispose();
        }
    }
}
