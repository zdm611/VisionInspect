using System;
using System.IO;

namespace VisionInspect.Business
{
    /// <summary>
    /// 日志系统：线程安全地写入 logs/app_YYYYMMDD.log（持久化到磁盘）。
    /// 级别：INFO / WARN / ERROR。日志失败不再抛出（避免二次崩溃）。
    /// </summary>
    public static class AppLog
    {
        private static readonly object _lock = new object();

        public static string LogDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        public static string CurrentFile => Path.Combine(LogDir, "app_" + DateTime.Now.ToString("yyyyMMdd") + ".log");

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogDir);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
                    if (ex != null)
                        line += " | " + ex.GetType().Name + ": " + ex.Message;
                    File.AppendAllText(CurrentFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // 日志系统自身失败时静默——不能因日志而崩溃
            }
        }
    }
}
