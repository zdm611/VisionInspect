using System;
using System.IO;
using System.Linq;
using HalconDotNet;
using VisionInspect.Algorithm;

namespace VisionInspect.MatchTest
{
    /// <summary>
    /// 离线匹配测试：对 samples 目录的模板图训练形状模型，逐张匹配测试图并输出结果。
    /// 用法：VisionInspect.MatchTest.exe &lt;模板图&gt; &lt;测试图目录&gt;
    /// 验收标准：测试图全部找到。
    /// </summary>
    internal static class Program
    {
        private static void Log(string s)
        {
            Console.WriteLine(s);
            Console.Out.Flush();
        }

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (HalconException e)
            {
                Log("HALCON ERROR: " + e.Message);
                return 1;
            }
            catch (Exception e)
            {
                Log("EXCEPTION " + e.GetType().FullName + ": " + e.Message);
                return 2;
            }
        }

        private static int Run(string[] args)
        {
            Log("stage: enter");
            if (args.Length < 2)
            {
                Console.WriteLine("用法：VisionInspect.MatchTest.exe <模板图> <测试图目录>");
                return 2;
            }

            HalconRuntime.Setup();
            Log("stage: runtime setup OK, root=" + HalconRuntime.Root);

            string templatePath = Path.GetFullPath(args[0]);
            string testDir = Path.GetFullPath(args[1]);
            if (!File.Exists(templatePath)) { Console.WriteLine("模板图不存在：" + templatePath); return 2; }
            if (!Directory.Exists(testDir)) { Console.WriteLine("测试图目录不存在：" + testDir); return 2; }

            string[] tests = Directory.GetFiles(testDir)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.Equals(f, templatePath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Console.WriteLine("模板：" + templatePath);
            Console.WriteLine("测试图：" + tests.Length + " 张");

            using (var matcher = new ShapeMatcher())
            {
                var caliper = new CaliperMeasurer();
                HOperatorSet.ReadImage(out HObject templateImage, templatePath);
                Log("stage: ReadImage OK");
                Console.WriteLine("正在训练形状模型（自动提取目标区域）…");
                matcher.Train(templateImage);
                Log("stage: Train OK");
                templateImage.Dispose();
                Console.WriteLine("模型训练完成");

                int found = 0;
                foreach (string file in tests)
                {
                    HOperatorSet.ReadImage(out HObject image, file);
                    MatchResult[] results = matcher.Find(image);

                    string name = Path.GetFileName(file);
                    if (results.Length > 0)
                    {
                        found++;
                        MatchResult best = results[0];
                        Console.WriteLine(string.Format("{0,-16} 找到  {1}", name, best));

                        double halfLen = Math.Max(best.Width, best.Height) / 2 * 1.15;
                        EdgePair pair = caliper.MeasureDiameter(image, best.Row, best.Column, best.Angle, halfLen);
                        if (pair.EdgeCount >= 2)
                            Console.WriteLine(string.Format("{0,-16} 测量  {1}（剖面边缘 {2} 个）", "", pair, pair.EdgeCount));
                        else
                            Console.WriteLine(string.Format("{0,-16} 测量  边缘不足（{1} 个）", "", pair.EdgeCount));
                    }
                    else
                    {
                        Console.WriteLine("{0,-16} 未找到", name);
                    }
                    image.Dispose();
                }

                Console.WriteLine();
                Console.WriteLine(string.Format("结果：{0}/{1} 找到", found, tests.Length));
                return found == tests.Length && tests.Length > 0 ? 0 : 1;
            }
        }
    }
}
