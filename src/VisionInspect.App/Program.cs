using System;
using System.Windows.Forms;
using VisionInspect.Algorithm;
using VisionInspect.Business;

namespace VisionInspect.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 全局异常捕获：异常不弹窗崩溃，一律记录日志（W16-D1 验收项）
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLog.Error("UI 线程未处理异常（已拦截，程序继续运行）", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLog.Error("非 UI 线程未处理异常", e.ExceptionObject as Exception);
            };

            AppLog.Info("应用启动");
            try
            {
                HalconRuntime.Setup();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                AppLog.Info("应用退出");
            }
            catch (Exception ex)
            {
                AppLog.Error("启动/主循环致命异常", ex);
                throw;
            }
        }
    }
}
