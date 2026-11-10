using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HalconDotNet;

namespace VisionInspect.Algorithm
{
    /// <summary>
    /// HObject → Bitmap 转换，用于 PictureBox 显示。
    /// HALCON 图像为三平面（R/G/B）或单通道，Bitmap 需要交错 BGR。
    /// </summary>
    public static class HalconImageConverter
    {
        public static Bitmap ToBitmap(HObject image)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            HOperatorSet.CountChannels(image, out HTuple channels);
            HOperatorSet.GetImageType(image, out HTuple type);
            int width = w.I, height = h.I;

            if (channels.I == 3)
            {
                HOperatorSet.GetImagePointer3(image, out HTuple r, out HTuple g, out HTuple b,
                    out type, out w, out h);
                return PlanarToBitmap(new IntPtr(r.L), new IntPtr(g.L), new IntPtr(b.L), width, height);
            }

            HOperatorSet.GetImagePointer1(image, out HTuple ptr, out type, out w, out h);
            return GrayToBitmap(new IntPtr(ptr.L), width, height);
        }

        private static Bitmap PlanarToBitmap(IntPtr r, IntPtr g, IntPtr b, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[width * 3];
                for (int y = 0; y < height; y++)
                {
                    int plane = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        row[x * 3] = Marshal.ReadByte(b, plane + x);     // B
                        row[x * 3 + 1] = Marshal.ReadByte(g, plane + x); // G
                        row[x * 3 + 2] = Marshal.ReadByte(r, plane + x); // R
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        private static Bitmap GrayToBitmap(IntPtr gray, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            ColorPalette pal = bmp.Palette;
            for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                var row = new byte[width];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(IntPtr.Add(gray, y * width), row, 0, width);
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, width);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }
    }
}
