using System;
using HalconDotNet;
using OpenCvSharp;

namespace VisionInspect.Hardware
{
    /// <summary>
    /// UVC 相机帧源（OpenCvSharp / DirectShow）。
    /// 适用于手机 DroidCam/IVCam 虚拟摄像头与普通 USB 摄像头。
    /// 检出占位画面（无信号蓝/橙屏）时抛异常，交由上层重连逻辑处理。
    /// </summary>
    public sealed class OpenCvCameraSource : IFrameSource
    {
        private VideoCapture _capture;
        private readonly Mat _frame = new Mat();

        public bool IsOpen { get; private set; }

        /// <summary>设备号；-1 表示打开时自动扫描（跳过打不开和输出占位图的源）。</summary>
        public int DeviceIndex { get; }

        /// <summary>打开后丢弃的预热帧数（虚拟摄像头首帧为占位图）。</summary>
        public int WarmupFrames { get; set; } = 15;

        public OpenCvCameraSource(int deviceIndex = -1)
        {
            DeviceIndex = deviceIndex;
        }

        public void Open()
        {
            if (IsOpen) return;

            if (DeviceIndex >= 0)
            {
                _capture = OpenIndex(DeviceIndex);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        _capture = OpenIndex(i);
                        break;
                    }
                    catch (Exception)
                    {
                        _capture?.Dispose();
                        _capture = null;
                    }
                }
            }

            if (_capture == null)
                throw new InvalidOperationException("未找到可用的摄像头设备（DirectShow 0-4）");
            IsOpen = true;
        }

        private VideoCapture OpenIndex(int index)
        {
            var cap = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (!cap.IsOpened())
            {
                cap.Dispose();
                throw new InvalidOperationException("设备 " + index + " 打开失败");
            }

            for (int i = 0; i < WarmupFrames; i++)
            {
                if (!cap.Read(_frame) || _frame.Empty())
                {
                    cap.Dispose();
                    throw new InvalidOperationException("设备 " + index + " 读帧失败");
                }
            }
            if (IsPlaceholder(_frame))
            {
                cap.Dispose();
                throw new InvalidOperationException("设备 " + index + " 输出占位画面（无真实推流）");
            }
            return cap;
        }

        public HObject Grab()
        {
            if (!IsOpen || !_capture.Read(_frame) || _frame.Empty())
                throw new InvalidOperationException("抓帧失败，相机可能已断开");
            if (IsPlaceholder(_frame))
                throw new InvalidOperationException("相机输出占位画面（推流中断）");

            int width = _frame.Width, height = _frame.Height;
            byte[] bgr = new byte[width * height * 3];
            System.Runtime.InteropServices.Marshal.Copy(_frame.Data, bgr, 0, bgr.Length);

            byte[] r = new byte[width * height];
            byte[] g = new byte[width * height];
            byte[] b = new byte[width * height];
            for (int i = 0, j = 0; i < r.Length; i++, j += 3)
            {
                b[i] = bgr[j];
                g[i] = bgr[j + 1];
                r[i] = bgr[j + 2];
            }

            var hR = System.Runtime.InteropServices.GCHandle.Alloc(r, System.Runtime.InteropServices.GCHandleType.Pinned);
            var hG = System.Runtime.InteropServices.GCHandle.Alloc(g, System.Runtime.InteropServices.GCHandleType.Pinned);
            var hB = System.Runtime.InteropServices.GCHandle.Alloc(b, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                HOperatorSet.GenImage3(out HObject image, "byte", width, height,
                    hR.AddrOfPinnedObject(), hG.AddrOfPinnedObject(), hB.AddrOfPinnedObject());
                return image;
            }
            finally
            {
                hR.Free();
                hG.Free();
                hB.Free();
            }
        }

        /// <summary>占位图判定：无信号的蓝/橙屏是纯色，灰度方差极低。</summary>
        private static bool IsPlaceholder(Mat frame)
        {
            Cv2.MeanStdDev(frame, out Scalar _, out Scalar std);
            return std.Val0 < 2.0 && std.Val1 < 2.0 && std.Val2 < 2.0;
        }

        public void Close()
        {
            _capture?.Dispose();
            _capture = null;
            IsOpen = false;
        }

        public void Dispose()
        {
            Close();
            _frame?.Dispose();
        }
    }
}
