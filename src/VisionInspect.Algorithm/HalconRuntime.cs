using System;
using System.IO;

namespace VisionInspect.Algorithm
{
    /// <summary>
    /// HALCON 原生库运行时准备：halcondotnet 通过 P/Invoke 加载 halcon.dll，
    /// 需保证 %HALCONROOT%\bin\x64-win64 在 DLL 搜索路径内。
    /// </summary>
    public static class HalconRuntime
    {
        private static bool _ready;

        public static string Root { get; private set; }

        public static void Setup()
        {
            if (_ready) return;

            string root = Environment.GetEnvironmentVariable("HALCONROOT");
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                root = @"C:\Program Files\MVTec\HALCON-26.05-Progress";
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException("未找到 HALCON 安装目录：" + root);

            string nativeBin = Path.Combine(root, "bin", "x64-win64");
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.StartsWith(nativeBin, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", nativeBin + ";" + path);

            Root = root;
            _ready = true;
        }
    }
}
