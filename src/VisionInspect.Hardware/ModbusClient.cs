using System;
using System.Net.Sockets;

namespace VisionInspect.Hardware
{
    /// <summary>
    /// 最小 Modbus TCP 客户端（功能码 3 读保持寄存器 / 6 写单寄存器），零第三方依赖。
    /// 协议：MBAP 头（事务2B + 协议2B=0 + 长度2B + 单元1B）+ PDU。
    /// </summary>
    public sealed class ModbusClient : IDisposable
    {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private ushort _transaction;
        private readonly object _ioLock = new object();

        public bool IsConnected => _tcp != null && _tcp.Connected;

        public byte UnitId { get; set; } = 1;

        public void Connect(string host, int port)
        {
            Disconnect();
            _tcp = new TcpClient();
            _tcp.Connect(host, port);
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = 2000;
            _stream.WriteTimeout = 2000;
        }

        public void Disconnect()
        {
            _stream?.Dispose();
            _tcp?.Dispose();
            _stream = null;
            _tcp = null;
        }

        /// <summary>功能码 6：写单个保持寄存器。</summary>
        public void WriteRegister(int address, int value)
        {
            lock (_ioLock)
            {
                ushort t = _transaction++;
                byte[] req = BuildFrame(t, 0x06, new byte[]
                {
                    (byte)(address >> 8), (byte)(address & 0xFF),
                    (byte)(value >> 8), (byte)(value & 0xFF)
                });
                _stream.Write(req, 0, req.Length);
                byte[] resp = ReadFrame();
                if (resp.Length < 4 || resp[1] != 0x06)
                    throw new InvalidOperationException("写寄存器响应异常");
            }
        }

        /// <summary>功能码 3：读保持寄存器。</summary>
        public int[] ReadRegisters(int address, int count)
        {
            lock (_ioLock)
            {
                ushort t = _transaction++;
                byte[] req = BuildFrame(t, 0x03, new byte[]
                {
                    (byte)(address >> 8), (byte)(address & 0xFF),
                    (byte)(count >> 8), (byte)(count & 0xFF)
                });
                _stream.Write(req, 0, req.Length);
                byte[] resp = ReadFrame();
                if (resp.Length < 3 || resp[1] != 0x03)
                    throw new InvalidOperationException("读寄存器响应异常");
                int byteCount = resp[2];
                var values = new int[count];
                for (int i = 0; i < count && 3 + i * 2 + 1 < resp.Length; i++)
                    values[i] = (resp[3 + i * 2] << 8) | resp[4 + i * 2];
                return values;
            }
        }

        private byte[] BuildFrame(ushort transaction, byte function, byte[] data)
        {
            // 长度字段 = 单元ID(1) + 功能码(1) + 数据
            ushort length = (ushort)(2 + data.Length);
            byte[] frame = new byte[6 + length];
            frame[0] = (byte)(transaction >> 8);
            frame[1] = (byte)(transaction & 0xFF);
            frame[2] = 0;
            frame[3] = 0;
            frame[4] = (byte)(length >> 8);
            frame[5] = (byte)(length & 0xFF);
            frame[6] = UnitId;
            frame[7] = function;
            Buffer.BlockCopy(data, 0, frame, 8, data.Length);
            return frame;
        }

        private byte[] ReadFrame()
        {
            // MBAP 头 6 字节（事务2+协议2+长度2），长度字段 = 单元ID+PDU 的字节数
            byte[] header = new byte[6];
            int read = 0;
            while (read < 6)
            {
                int n = _stream.Read(header, read, 6 - read);
                if (n <= 0) throw new InvalidOperationException("连接被关闭");
                read += n;
            }
            int len = (header[4] << 8) | header[5];
            byte[] body = new byte[len];
            read = 0;
            while (read < len)
            {
                int n = _stream.Read(body, read, len - read);
                if (n <= 0) throw new InvalidOperationException("连接被关闭");
                read += n;
            }
            return body; // body[0]=单元ID, body[1]=功能码, 之后是数据
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
