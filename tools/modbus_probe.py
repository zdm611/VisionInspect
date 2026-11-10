# -*- coding: utf-8 -*-
"""W15-D5 读写自检：用 pymodbus 客户端连虚拟 PLC，写 HR0/HR1/HR2/HR3/HR10 再读回。

验收（施工图 D5）：上位机连接后写 HR0/HR1 → 虚拟 PLC 控制台打印出写入值 = 读写成功。
用法：python tools/modbus_probe.py [端口，默认 1502]
"""
import sys
from pymodbus.client import ModbusTcpClient

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 1502
client = ModbusTcpClient("127.0.0.1", port=PORT)
if not client.connect():
    print("[探针] 连接失败 127.0.0.1:%d" % PORT)
    sys.exit(1)
print("[探针] 已连接 127.0.0.1:%d" % PORT)

# 写入：判定=OK(1)、测量=24.98mm→2498、有效=1、计数=1；触发位 HR10 置 1 再清 0
for addr, val in [(0, 1), (1, 2498), (2, 1), (3, 1), (10, 1)]:
    rr = client.write_register(addr, val)
    print("[探针] 写 HR%-2d = %-5d -> %s" % (addr, val, "OK" if rr.isError() is False else rr))

rr = client.read_holding_registers(0, 11)
if rr.isError():
    print("[探针] 读失败:", rr)
else:
    print("[探针] 读回 HR0..HR10 =", list(rr.registers))

client.write_register(10, 0)
client.close()
print("[探针] 完成")
