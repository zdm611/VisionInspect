using System;
using HalconDotNet;

namespace VisionInspect.Hardware
{
    /// <summary>
    /// 帧源抽象：当前实现为 UVC（手机/虚拟摄像头），
    /// 后续换海康工业相机时新增实现类，业务层不受影响。
    /// </summary>
    public interface IFrameSource : IDisposable
    {
        bool IsOpen { get; }

        void Open();

        HObject Grab();

        void Close();
    }
}
