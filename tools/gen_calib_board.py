"""生成 W15 九点标定板（A4 可打印 PDF，300 DPI，精确毫米几何）。

设计：3x3 圆点阵，间距 30.0mm，左上角点加大作方位锚点，含 30mm 比例尺文字。
打印要求：100% 实际大小（不缩放），打印后用卡尺验证点距。
"""
import math
from PIL import Image, ImageDraw, ImageFont

DPI = 300
MM = DPI / 25.4  # px per mm
A4_W, A4_H = 210, 297  # mm

img = Image.new("L", (int(A4_W * MM), int(A4_H * MM)), 255)
d = ImageDraw.Draw(img)

spacing_mm = 30.0
dot_mm = 5.0
anchor_mm = 8.0
cx, cy = A4_W / 2, A4_H / 2  # 网格中心

def mm2px(v):
    return v * MM

for r in range(3):
    for c in range(3):
        x = cx + (c - 1) * spacing_mm
        y = cy + (r - 1) * spacing_mm
        dia = anchor_mm if (r == 0 and c == 0) else dot_mm
        rad = dia / 2
        d.ellipse([mm2px(x - rad), mm2px(y - rad), mm2px(x + rad), mm2px(y + rad)], fill=0)

# 边框（便于对齐与防歪斜检查）
m = 25
d.rectangle([mm2px(m), mm2px(m), mm2px(A4_W - m), mm2px(A4_H - m)], outline=0, width=3)

# 文字说明
def draw_text(x, y, text, size_mm=4):
    # 用默认字体按像素画；PIL 字体大小以 px 计
    try:
        font = ImageFont.truetype("arial.ttf", int(size_mm * MM))
    except Exception:
        font = ImageFont.load_default()
    d.text((mm2px(x), mm2px(y)), text, fill=0, font=font)

draw_text(30, 32, "W15 CALIBRATION BOARD - 9 points", 5)
draw_text(30, 40, "Grid spacing = 30.0 mm | corner dot = anchor", 3.5)
draw_text(30, A4_H - 40, "PRINT AT 100% / NO SCALING", 4)
draw_text(30, A4_H - 34, "verify spacing with caliper after printing", 3)

# 30mm 比例尺（右下）
bar_y = A4_H - 25
x0 = A4_W - 30 - 60
d.line([mm2px(x0), mm2px(bar_y), mm2px(x0 + 60), mm2px(bar_y)], fill=0, width=2)
d.line([mm2px(x0), mm2px(bar_y - 2), mm2px(x0), mm2px(bar_y + 2)], fill=0, width=2)
d.line([mm2px(x0 + 60), mm2px(bar_y - 2), mm2px(x0 + 60), mm2px(bar_y + 2)], fill=0, width=2)
draw_text(x0 + 22, bar_y + 3, "60 mm", 3)

out_pdf = r"C:\Users\zdm611\Desktop\work\VisionInspect\docs\W15D1_标定板_A4.pdf"
img.save(out_pdf, "PDF", resolution=DPI)
out_png = r"C:\Users\zdm611\Desktop\work\VisionInspect\docs\W15D1_标定板_A4.png"
img.save(out_png)
print("saved:", out_pdf, "and", out_png)
print("grid: 3x3, spacing 30.0mm, anchor top-left dot 8mm, others 5mm")
