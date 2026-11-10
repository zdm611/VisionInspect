using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace VisionInspect.Business
{
    /// <summary>
    /// 参数配置持久化（JSON）：模板/对比度/角度/标定/公差/Modbus 全参数存盘，
    /// 重启后自动恢复（W16-D2 验收项）。文件位置：程序目录 config.json。
    /// </summary>
    [DataContract]
    public class AppConfig
    {
        [DataMember(Name = "model_path")] public string ModelPath { get; set; } = "";
        [DataMember(Name = "min_contrast")] public double MinContrast { get; set; } = 20;
        [DataMember(Name = "angle_range")] public double AngleRange { get; set; } = 360;
        [DataMember(Name = "calib_path")] public string CalibPath { get; set; } = "";
        [DataMember(Name = "nominal_mm")] public double NominalMm { get; set; } = 25.0;
        [DataMember(Name = "tolerance_mm")] public double ToleranceMm { get; set; } = 0.10;
        [DataMember(Name = "modbus_host")] public string ModbusHost { get; set; } = "127.0.0.1";
        [DataMember(Name = "modbus_port")] public int ModbusPort { get; set; } = 1502;

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return new AppConfig();
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    return (AppConfig)serializer.ReadObject(fs);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("配置文件读取失败，使用默认值", ex);
                return new AppConfig();
            }
        }

        public void Save(string path)
        {
            try
            {
                using (var fs = File.Create(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    serializer.WriteObject(fs, this);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("配置文件保存失败", ex);
            }
        }
    }
}
