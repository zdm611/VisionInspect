# VisionInspect — 零件尺寸测量与表面缺陷检测上位机

单相机视觉质检上位机：相机拍照 → 形状匹配定位 → 卡尺测量 → 公差判定 → Modbus 写 PLC。
四周 MVP（W13–W16）交付，四层架构，全流程实测数据见下文。

## ① 这是什么

工业质检场景的替代方案：人工卡尺量尺寸 + 肉眼看缺陷 → 本系统用机器视觉自动完成
「定位 → 测量 → 判定 → 通知 PLC」的闭环。

```
采集线程（UVC/工业相机）→ BlockingCollection 队列 → 处理线程（匹配 → 测量 → 判定）
    → 结果对象 { OK/NG, 尺寸mm, 坐标 } → ① UI 实时画框画线  ② Modbus 写寄存器  ③ 日志/历史
```

| 层 | 项目 | 职责 |
|---|---|---|
| UI | `src/VisionInspect.App` | WinForm：实时画面、匹配框/测量线叠加、参数面板、历史列表 |
| 业务 | `src/VisionInspect.Business` | 处理线程、公差判定、重复性/压测统计、配置/日志 |
| 算法 | `src/VisionInspect.Algorithm` | Halcon 形状匹配、卡尺测量、九点标定（px→mm） |
| 硬件 | `src/VisionInspect.Hardware` | 相机采集（UVC，可换海康 MVS）、Modbus TCP 客户端、采集管线 |

## ② 怎么运行

**环境要求**
- Windows 10/11 + .NET Framework 4.8（VS2022 或 .NET SDK）
- HALCON 26.05（`C:\Program Files\MVTec\HALCON-26.05-Progress`，需有效 license）
- Python 3.11 + `pip install pymodbus`（仅虚拟 PLC 用）
- 相机：手机 DroidCam/IVCam（开发期）或海康工业相机（生产期，实现 `IFrameSource` 即可替换）

**构建与运行**
```powershell
dotnet build VisionInspect.sln
.\src\VisionInspect.App\bin\Debug\net48\VisionInspect.exe
```

**相机连接（DroidCam）**：手机装 DroidCam App → 与电脑同一 WiFi → 电脑装 DroidCam 客户端 → 连接后虚拟摄像头出现即就绪。

**虚拟 PLC（Modbus TCP:1502）**
```powershell
python tools\virtual_plc.py    # 控制台实时打印收到的 OK/NG
```

**上手三步**：① 点「开始预览」→ ② 在画面上拖框选目标 → ③ 点「训练模板」。随后测量值/判定实时显示，连接 Modbus 后写入 PLC。
参数面板可调：模板路径、最小对比度、角度范围、标定文件、标称值/公差、Modbus 地址——改动即生效、重启自动恢复（`config.json`）。

## ③ 方案与理由

| 决策 | 选择 | 理由 |
|---|---|---|
| 定位 | Halcon 形状匹配 | 工业现场标配；比深度学习更适小样本、可解释、快（本项目单帧 ~20ms） |
| 测量 | 卡尺（1D 边缘） | YOLO 检测框抖动 ±数px，测尺寸需亚像素级稳定；边缘提取才是计量学正解 |
| 抗反光 | `ignore_local_polarity` | 金属件反光翻转边缘极性，实测该配置得分 0.64→0.76 提升 |
| 标定 | 九点标定（打印板+仿射） | 低成本覆盖 px→mm；端点变换取距吸收各向异性 |
| 通讯 | Modbus TCP | 工业现场通用；先虚拟 PLC 闭环，后接真 PLC 零改动 |
| 架构 | 四层 + 队列管线 | 采集/处理解耦（BlockingCollection），换相机只动硬件层 |

## ④ 实测数据（全部有报告/日志佐证，见 docs/）

| 指标 | 实测 | 说明 |
|---|---|---|
| 离线匹配 | **20/20 全部找到** | 20 张手持样本图（W13-D4） |
| 测量重复性 | **std 0.042px** | 30 次连续测量（W14-D4） |
| 双项稳定性 | 外径 std 0.13px / 间距 std 0.03px | 外径+「1」字宽（W14-D5） |
| **精度**（vs 卡尺） | 典型 **0.06mm**，最大 0.14mm | 基准：游标卡尺 24.98mm（W15-D4 误差表） |
| **节拍 CT** | 平均 **21.3ms** / 最大 38.4ms | 2000 次压测 ≈ 47 次/秒（W16-D4） |
| 内存 | 2000 次压测**无增长**（-3.1MB） | HObject 全程 Dispose 纪律 |
| 在线稳定 | 30 分钟 42334 帧 **99.9% 命中** | 可拖动、无卡顿（W13-D5） |
| 断线自愈 | 多轮拔线自动重连 | 日志留痕（W16-D3） |

## ⑤ 已知限制

1. **精度天花板 ~0.05–0.1mm**：手机相机 + 打印标定板的组合，瓶颈是标定拟合残差（0.83px≈0.15mm）。达标路径：硬质标定板 / 垂直正拍 / 测量区约束 / 二次畸变标定（见 docs/W15D4_误差表.md）
2. **YOLO 缺陷检测未实现**：属"良好"档加分项，需要拍摄缺陷样本集训练；当前聚焦尺寸测量主线
3. **相机为手机 UVC**：卷帘快门+自动曝光，工业应用需换全局快门工业相机（实现 `IFrameSource` 即插）
4. **PLC 为虚拟从站**：寄存器表见 docs/W15D5_Modbus寄存器表.md，接真 PLC 仅需改 IP
5. **标定需随场景重做**：机位/高度变化后须重新标定（标定板照片留存 samples/calib/）

## 目录

```
VisionInspect/
├── src/            UI、业务、算法、硬件四层 + 离线测试工具
├── docs/           标定板、误差表、压测报告、笔记、寄存器表
├── samples/        测试图（20 张匹配样本 + 标定图）
├── tools/          虚拟 PLC、标定板生成脚本
└── README.md
```
