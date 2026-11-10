using System;
using System.Collections.Generic;
using System.IO;
using HalconDotNet;
using VisionInspect.Algorithm;

namespace VisionInspect.CalibTool
{
    /// <summary>
    /// 九点标定工具：识别标定板圆点 → 拟合仿射 → 输出 px/mm → 存配置。
    /// 用法：VisionInspect.CalibTool.exe [--spacing 30] [--out calib_config.json] 图1 [图2 ...]
    /// 多张图仅在同分辨率时合并拟合（不同分辨率尺度不同，各自独立拟合）。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e.GetType().Name + ": " + e.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            var images = new List<string>();
            double spacingMm = 30.0;
            string outPath = "calib_config.json";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--spacing" && i + 1 < args.Length) spacingMm = double.Parse(args[++i]);
                else if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
                else images.Add(args[i]);
            }
            if (images.Count == 0)
            {
                Console.WriteLine("用法：VisionInspect.CalibTool.exe [--spacing 30] [--out cfg.json] 图1 [图2 ...]");
                return 2;
            }

            HalconRuntime.Setup();

            var groups = new Dictionary<(int w, int h), List<CalibPoint>>();

            foreach (string file in images)
            {
                Console.WriteLine("== " + Path.GetFileName(file));
                try
                {
                HOperatorSet.ReadImage(out HObject img, file);
                HOperatorSet.GetImageSize(img, out HTuple w, out HTuple h);
                int iw = (int)w.ToDArr()[0], ih = (int)h.ToDArr()[0];
                List<CalibPoint> pts = NinePointCalibration.DetectBoard(img, spacingMm);
                img.Dispose();

                foreach (var p in pts)
                    Console.WriteLine(string.Format("  grid({0},{1})  px(col={2:0.0},row={3:0.0})",
                        p.GridC, p.GridR, p.ColPx, p.RowPx));

                CalibResult single = NinePointCalibration.Fit(pts);
                Console.WriteLine(string.Format("  单图({0}x{1}): px/mm={2:0.000}, 残差={3:0.00}px",
                    iw, ih, single.PxPerMm, single.ResidualPx));

                var key = (iw, ih);
                if (!groups.ContainsKey(key)) groups[key] = new List<CalibPoint>();
                groups[key].AddRange(pts);
                }
                catch (Exception e)
                {
                    Console.WriteLine("  跳过该图: " + e.Message);
                }
            }

            Console.WriteLine();
            int saved = 0;
            foreach (var kv in groups)
            {
                CalibResult result = NinePointCalibration.Fit(kv.Value);
                string tag = groups.Count > 1 ? "_" + kv.Key.w + "x" + kv.Key.h : "";
                string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".",
                    Path.GetFileNameWithoutExtension(outPath) + tag + Path.GetExtension(outPath));
                Console.WriteLine(string.Format("== 合并拟合 {0}x{1}（{2} 点）", kv.Key.w, kv.Key.h, result.PointCount));
                Console.WriteLine("  px/mm   = " + result.PxPerMm.ToString("0.000"));
                Console.WriteLine("  残差    = " + result.ResidualPx.ToString("0.00") + " px");
                Console.WriteLine("  仿射    = [" + string.Join(", ", Array.ConvertAll(result.Affine, v => v.ToString("0.000000"))) + "]");

                string cfg = "{\n"
                    + "  \"image_size\": \"" + kv.Key.w + "x" + kv.Key.h + "\",\n"
                    + "  \"px_per_mm\": " + result.PxPerMm.ToString("0.######") + ",\n"
                    + "  \"residual_px\": " + result.ResidualPx.ToString("0.######") + ",\n"
                    + "  \"point_count\": " + result.PointCount + ",\n"
                    + "  \"spacing_mm\": " + spacingMm.ToString("0.###") + ",\n"
                    + "  \"affine\": [" + string.Join(", ", Array.ConvertAll(result.Affine, v => v.ToString("0.######"))) + "]\n"
                    + "}\n";
                File.WriteAllText(path, cfg);
                Console.WriteLine("  配置已保存: " + path);
                saved++;
            }
            return saved > 0 ? 0 : 1;
        }
    }
}

