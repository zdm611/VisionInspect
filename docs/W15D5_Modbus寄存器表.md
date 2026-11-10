# Modbus TCP 寄存器表（虚拟 PLC ↔ 上位机）

- 协议：Modbus TCP，端口 **1502**（502 需管理员权限，避坑用 1502），Unit ID = 1
- 寄存器类型：保持寄存器（Holding Register，功能码 3 读 / 6 写）

| 地址 | 方向 | 名称 | 格式 | 说明 |
|---|---|---|---|---|
| HR0 | 上位机 → PLC | 判定结果 | 1 = OK，0 = NG | 每次判定后写入 |
| HR1 | 上位机 → PLC | 测量值 | mm × 100（2498 = 24.98mm） | 整数传输，避免浮点字节序问题 |
| HR2 | 上位机 → PLC | 数据有效 | 1 = 有效 | 结果写入后置 1 |
| HR3 | 上位机 → PLC | 样本计数 | 累加 | 便于 PLC 侧确认新数据 |
| HR10 | PLC → 上位机 | 触发信号 | 1 = 请求测量 | 上位机 1Hz 轮询并显示 |

## 验收方式

- D5：上位机连接后写 HR0/HR1 → **虚拟 PLC 控制台打印出写入值** = 读写成功
- D6：改变公差使判定 OK↔NG → **PLC 控制台看到判定值变化** = 闭环

## 文件

- 虚拟 PLC：`tools/virtual_plc.py`（pymodbus）
- C# 客户端：`src/VisionInspect.Hardware/ModbusClient.cs`（纯 socket 实现，零第三方依赖）
