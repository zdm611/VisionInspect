# -*- coding: utf-8 -*-
"""W15-D5/D6 虚拟 PLC：Modbus TCP 从站（pymodbus）。

寄存器表见 docs/W15D5_Modbus寄存器表.md：
  HR0 判定结果(1=OK/0=NG) | HR1 测量值(mm*100) | HR2 数据有效 | HR3 样本计数 | HR10 触发信号

用法：python tools/virtual_plc.py [端口，默认 1502]
验收证据：上位机写入后，本控制台打印出 OK/NG 变化。
"""
import sys
import time
import threading

from pymodbus.datastore import ModbusSequentialDataBlock, ModbusDeviceContext, ModbusServerContext
from pymodbus.server import ModbusTcpServer
from pymodbus.client import ModbusTcpClient
import asyncio

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 1502
block = ModbusSequentialDataBlock(1, [0] * 32)
context = ModbusServerContext(devices=ModbusDeviceContext(hr=block), single=True)


def monitor():
    """通过 Modbus 协议读自己的寄存器并打印判定变化 —— 真 PLC 就是这么'看'数据的。"""
    client = ModbusTcpClient("127.0.0.1", port=PORT)
    client.connect()
    last = None
    while True:
        try:
            rr = client.read_holding_registers(0, count=11, device_id=1)
            if rr.isError():
                time.sleep(0.5)
                continue
            vals = rr.registers
        except Exception:
            time.sleep(0.5)
            continue
        ok_ng, mm100, valid, count = vals[0], vals[1], vals[2], vals[3]
        trigger = vals[10]
        state = (ok_ng, mm100, valid, trigger)
        if valid == 1 and state != last:
            verdict = "OK" if ok_ng == 1 else "NG"
            print("[%s] PLC 收到 <- 判定=%s  测量=%.2fmm  样本#=%d  触发位=%d"
                  % (time.strftime("%H:%M:%S"), verdict, mm100 / 100.0, count, trigger), flush=True)
            last = state
        elif trigger == 1 and (last is None or last[3] != 1):
            print("[%s] PLC 发出触发信号 -> 上位机" % time.strftime("%H:%M:%S"), flush=True)
            last = state
        time.sleep(0.5)


t = threading.Thread(target=monitor, daemon=True)
t.start()


async def run_server():
    """pymodbus 3.16：服务器须在事件循环内创建（构造函数会取 running loop）。"""
    server = ModbusTcpServer(context=context, address=("127.0.0.1", PORT))
    print("[虚拟PLC] Modbus TCP 从站启动: 127.0.0.1:%d (Unit ID=1)" % PORT, flush=True)
    print("[虚拟PLC] 寄存器: HR0=判定 HR1=mm*100 HR2=有效 HR3=计数 HR10=触发", flush=True)
    await server.serve_forever()


asyncio.run(run_server())
