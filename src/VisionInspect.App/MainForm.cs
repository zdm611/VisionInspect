using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using HalconDotNet;
using VisionInspect.Algorithm;
using VisionInspect.Business;
using VisionInspect.Hardware;

namespace VisionInspect.App
{
    public class MainForm : Form
    {
        private readonly PictureBox _picture = new PictureBox();
        private readonly Button _captureBtn = new Button();
        private readonly Button _previewBtn = new Button();
        private readonly Button _trainBtn = new Button();
        private readonly Button _repeatBtn = new Button();
        private readonly Button _stressBtn = new Button();
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel();

        // 参数面板
        private readonly TextBox _modelPathBox = new TextBox();
        private readonly NumericUpDown _contrastInput = new NumericUpDown();
        private readonly NumericUpDown _angleInput = new NumericUpDown();
        private readonly TextBox _nominalInput = new TextBox();
        private readonly TextBox _tolInput = new TextBox();
        private readonly TextBox _calibPathBox = new TextBox();

        private readonly ShapeMatcher _matcher = new ShapeMatcher();
        private readonly CaliperMeasurer _caliper = new CaliperMeasurer();
        private readonly object _matchLock = new object();
        private CalibResult _calib;
        private ToleranceJudge _judge = new ToleranceJudge(25.0, 0.10);

        // Modbus 通讯
        private ModbusClient _modbus;
        private readonly TextBox _modbusHostBox = new TextBox();
        private readonly NumericUpDown _modbusPortInput = new NumericUpDown();
        private readonly Button _modbusConnectBtn = new Button();
        private int _sampleCount;
        private readonly System.Windows.Forms.Timer _triggerTimer = new System.Windows.Forms.Timer();

        private readonly RepeatabilityStats _repeatability = new RepeatabilityStats(30);
        private readonly RepeatabilityStats _repeatabilitySpacing = new RepeatabilityStats(30);
        private readonly ResultHistory _history = new ResultHistory(200);
        private readonly DataGridView _historyGrid = new DataGridView();
        private bool _collecting;
        private readonly StressTest _stress = new StressTest(2000);
        private bool _stressRunning;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private DateTime _lastLogTime = DateTime.MinValue;

        private IFrameSource _source;
        private FramePipeline _pipeline;
        private MatchProcessor _processor;

        private HObject _latestFrame;
        private Rectangle _roiImageRect;
        private Rectangle _roiControlRect;
        private bool _selecting;
        private Point _selStart;
        private bool _hasRoi;
        private string _userStatus = "";

        public MainForm()
        {
            Text = "零件尺寸测量与表面缺陷检测上位机";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(960, 600);

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 4) };
            _captureBtn.Text = "采集一帧";
            _captureBtn.Width = 110;
            _captureBtn.Height = 32;
            _captureBtn.Location = new Point(8, 8);
            _captureBtn.Click += OnCaptureClick;

            _previewBtn.Text = "开始预览";
            _previewBtn.Width = 110;
            _previewBtn.Height = 32;
            _previewBtn.Location = new Point(128, 8);
            _previewBtn.Click += OnPreviewClick;

            _trainBtn.Text = "训练模板";
            _trainBtn.Width = 110;
            _trainBtn.Height = 32;
            _trainBtn.Location = new Point(248, 8);
            _trainBtn.Enabled = false;
            _trainBtn.Click += OnTrainClick;

            _repeatBtn.Text = "重复性测试";
            _repeatBtn.Width = 110;
            _repeatBtn.Height = 32;
            _repeatBtn.Location = new Point(368, 8);
            _repeatBtn.Click += OnRepeatClick;

            _stressBtn.Text = "压测 2000";
            _stressBtn.Width = 110;
            _stressBtn.Height = 32;
            _stressBtn.Location = new Point(488, 8);
            _stressBtn.Click += OnStressClick;

            toolbar.Controls.Add(_captureBtn);
            toolbar.Controls.Add(_previewBtn);
            toolbar.Controls.Add(_trainBtn);
            toolbar.Controls.Add(_repeatBtn);
            toolbar.Controls.Add(_stressBtn);

            var paramPanel = BuildParamPanel();

            _picture.Dock = DockStyle.Fill;
            _picture.SizeMode = PictureBoxSizeMode.Zoom;
            _picture.BackColor = Color.Black;
            _picture.MouseDown += OnPictureMouseDown;
            _picture.MouseMove += OnPictureMouseMove;
            _picture.MouseUp += OnPictureMouseUp;
            _picture.Paint += OnPicturePaint;

            var historyPanel = new Panel { Dock = DockStyle.Bottom, Height = 200 };
            _historyGrid.Dock = DockStyle.Fill;
            _historyGrid.ReadOnly = true;
            _historyGrid.AllowUserToAddRows = false;
            _historyGrid.AllowUserToDeleteRows = false;
            _historyGrid.RowHeadersVisible = false;
            _historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _historyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _historyGrid.Columns.Add("time", "时间");
            _historyGrid.Columns.Add("status", "状态");
            _historyGrid.Columns.Add("score", "得分");
            _historyGrid.Columns.Add("dia", "外径(px)");
            _historyGrid.Columns.Add("diamm", "外径(mm)");
            _historyGrid.Columns.Add("verdict", "判定");
            _historyGrid.Columns.Add("spa", "间距(px)");
            historyPanel.Controls.Add(_historyGrid);

            var statusStrip = new StatusStrip();
            _status.Text = "就绪（开始预览后，在画面上拖框选目标，点「训练模板」）";
            statusStrip.Items.Add(_status);

            Controls.Add(_picture);
            Controls.Add(historyPanel);
            Controls.Add(paramPanel);
            Controls.Add(toolbar);
            Controls.Add(statusStrip);

            LoadConfig();
            TryLoadCalibration(_calibPathBox.Text);

            FormClosed += (s, e) =>
            {
                SaveConfig();
                _triggerTimer.Stop();
                _modbus?.Dispose();
                _processor?.Dispose();
                _pipeline?.Dispose();
                _matcher.Dispose();
                _latestFrame?.Dispose();
            };
        }

        private Panel BuildParamPanel()
        {
            var panel = new Panel { Dock = DockStyle.Right, Width = 280, Padding = new Padding(12) };

            var title = new Label { Text = "参数面板", Location = new Point(12, 14), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

            var pathLabel = new Label { Text = "模板路径", Location = new Point(12, 52), AutoSize = true };
            _modelPathBox.Location = new Point(12, 74);
            _modelPathBox.Width = 256;
            _modelPathBox.AccessibleName = "modelPath";
            _modelPathBox.TextChanged += (s, e) => SaveConfig();

            var browseBtn = new Button { Text = "浏览…", Location = new Point(12, 104), Width = 80, AccessibleName = "browse" };
            browseBtn.Click += OnBrowseModel;
            var loadBtn = new Button { Text = "加载模型", Location = new Point(100, 104), Width = 80, AccessibleName = "loadModel" };
            loadBtn.Click += OnLoadModel;
            var saveBtn = new Button { Text = "保存模型", Location = new Point(188, 104), Width = 80, AccessibleName = "saveModel" };
            saveBtn.Click += OnSaveModel;

            var contrastLabel = new Label { Text = "最小对比度 (0-255)", Location = new Point(12, 156), AutoSize = true };
            _contrastInput.Location = new Point(12, 178);
            _contrastInput.Width = 256;
            _contrastInput.Minimum = 1;
            _contrastInput.Maximum = 255;
            _contrastInput.Value = (decimal)_matcher.MinContrast;
            _contrastInput.AccessibleName = "minContrast";

            var angleLabel = new Label { Text = "角度范围 (度, 0-360)", Location = new Point(12, 218), AutoSize = true };
            _angleInput.Location = new Point(12, 240);
            _angleInput.Width = 256;
            _angleInput.Minimum = 1;
            _angleInput.Maximum = 360;
            _angleInput.Value = (decimal)_matcher.AngleRangeDeg;
            _angleInput.AccessibleName = "angleRange";

            var calibLabel = new Label { Text = "标定文件", Location = new Point(12, 292), AutoSize = true };
            _calibPathBox.Location = new Point(12, 314);
            _calibPathBox.Width = 256;
            _calibPathBox.AccessibleName = "calibPath";
            _calibPathBox.TextChanged += (s, e) => SaveConfig();
            _calibPathBox.Text = @"C:\Users\zdm611\Desktop\work\VisionInspect\docs\W15D2_标定配置_stand640x480.json";
            var loadCalibBtn = new Button { Text = "加载标定", Location = new Point(12, 344), Width = 110, AccessibleName = "loadCalib" };
            loadCalibBtn.Click += (s, e) => TryLoadCalibration(_calibPathBox.Text);

            var nominalLabel = new Label { Text = "标称值 (mm)", Location = new Point(12, 396), AutoSize = true };
            _nominalInput.Location = new Point(12, 418);
            _nominalInput.Width = 120;
            _nominalInput.AccessibleName = "nominal";
            _nominalInput.TextChanged += OnTolChanged;
            var tolLabel = new Label { Text = "公差 (±mm)", Location = new Point(148, 396), AutoSize = true };
            _tolInput.Location = new Point(148, 418);
            _tolInput.Width = 120;
            _tolInput.AccessibleName = "tolerance";
            _tolInput.TextChanged += OnTolChanged;

            var hint = new Label
            {
                Text = "改参数后立即重训生效\n（需已框选 ROI 并训练过）",
                Location = new Point(12, 462),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            // Modbus 通讯区
            var modbusLabel = new Label { Text = "Modbus TCP", Location = new Point(12, 510), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _modbusHostBox.Location = new Point(12, 532);
            _modbusHostBox.Width = 150;
            _modbusHostBox.Text = "127.0.0.1";
            _modbusHostBox.AccessibleName = "modbusHost";
            _modbusHostBox.TextChanged += (s, e) => SaveConfig();
            _modbusPortInput.Location = new Point(170, 532);
            _modbusPortInput.Width = 86;
            _modbusPortInput.Minimum = 1;
            _modbusPortInput.Maximum = 65535;
            _modbusPortInput.Value = 1502;
            _modbusPortInput.AccessibleName = "modbusPort";
            _modbusConnectBtn.Text = "连接 PLC";
            _modbusConnectBtn.Location = new Point(12, 562);
            _modbusConnectBtn.Width = 120;
            _modbusConnectBtn.AccessibleName = "modbusConnect";
            _modbusConnectBtn.Click += OnModbusConnect;

            _contrastInput.ValueChanged += OnParamChanged;
            _angleInput.ValueChanged += OnParamChanged;

            panel.Controls.AddRange(new Control[]
            {
                title, pathLabel, _modelPathBox, browseBtn, loadBtn, saveBtn,
                contrastLabel, _contrastInput, angleLabel, _angleInput,
                calibLabel, _calibPathBox, loadCalibBtn,
                nominalLabel, _nominalInput, tolLabel, _tolInput, hint,
                modbusLabel, _modbusHostBox, _modbusPortInput, _modbusConnectBtn
            });
            return panel;
        }

        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private bool _loading = true;  // 构造期抑制保存，初始化完成后放开

        private void LoadConfig()
        {
            _loading = true;
            try
            {
                AppConfig cfg = AppConfig.Load(_configPath);
                _modelPathBox.Text = cfg.ModelPath;
                _contrastInput.Value = (decimal)Math.Max((double)_contrastInput.Minimum, Math.Min((double)_contrastInput.Maximum, cfg.MinContrast));
                _angleInput.Value = (decimal)Math.Max((double)_angleInput.Minimum, Math.Min((double)_angleInput.Maximum, cfg.AngleRange));
                if (!string.IsNullOrEmpty(cfg.CalibPath)) _calibPathBox.Text = cfg.CalibPath;
                _nominalInput.Text = cfg.NominalMm.ToString("0.00");
                _tolInput.Text = cfg.ToleranceMm.ToString("0.00");
                _modbusHostBox.Text = cfg.ModbusHost;
                _modbusPortInput.Value = Math.Max(_modbusPortInput.Minimum, Math.Min(_modbusPortInput.Maximum, cfg.ModbusPort));
                _judge = new ToleranceJudge(cfg.NominalMm, cfg.ToleranceMm);
            }
            finally { _loading = false; }
            if (File.Exists(_configPath)) AppLog.Info("配置已加载: " + _configPath);
        }

        private void SaveConfig()
        {
            if (_loading) return;
            try
            {
                double nominal, tol;
                double.TryParse(_nominalInput.Text, out nominal);
                double.TryParse(_tolInput.Text, out tol);
                new AppConfig
                {
                    ModelPath = _modelPathBox.Text,
                    MinContrast = (double)_contrastInput.Value,
                    AngleRange = (double)_angleInput.Value,
                    CalibPath = _calibPathBox.Text,
                    NominalMm = nominal,
                    ToleranceMm = tol,
                    ModbusHost = _modbusHostBox.Text,
                    ModbusPort = (int)_modbusPortInput.Value
                }.Save(_configPath);
            }
            catch (Exception ex)
            {
                AppLog.Error("配置保存异常", ex);
            }
        }

        private void SetUserStatus(string text)
        {
            _userStatus = text;
            _status.Text = text;
        }

        // ---------- 标定与公差 ----------

        private void TryLoadCalibration(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    AppLog.Warn("标定文件不存在：" + path);
                    SetUserStatus("标定文件不存在：" + path);
                    return;
                }
                _calib = CalibResult.Load(path);
                SetUserStatus(string.Format("标定已加载：{0:0.000} px/mm，残差 {1:0.00}px", _calib.PxPerMm, _calib.ResidualPx));
            }
            catch (Exception ex)
            {
                _calib = null;
                AppLog.Error("标定加载失败", ex);
                SetUserStatus("标定加载失败：" + ex.Message);
            }
        }

        private void OnTolChanged(object sender, EventArgs e)
        {
            double nominal, tol;
            if (double.TryParse(_nominalInput.Text, out nominal) && double.TryParse(_tolInput.Text, out tol) && tol >= 0)
            {
                _judge = new ToleranceJudge(nominal, tol);
                _userStatus = string.Format("公差设定：{0:0.00}±{1:0.00}mm", nominal, tol);
            }
            SaveConfig();
        }

        // ---------- 参数面板事件 ----------

        private void OnParamChanged(object sender, EventArgs e)
        {
            _matcher.MinContrast = (double)_contrastInput.Value;
            _matcher.AngleRangeDeg = (double)_angleInput.Value;

            if (_hasRoi && _latestFrame != null)
            {
                if (Retrain())
                    _userStatus = string.Format("参数生效，已重训（对比度 {0:0}，角度 {1:0}°）",
                        _matcher.MinContrast, _matcher.AngleRangeDeg);
            }
            else
            {
                _userStatus = string.Format("参数已更新（对比度 {0:0}，角度 {1:0}°）",
                    _matcher.MinContrast, _matcher.AngleRangeDeg);
            }
            SaveConfig();
        }

        private void OnBrowseModel(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Halcon 模型 (*.shm)|*.shm|所有文件 (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _modelPathBox.Text = dlg.FileName;
            }
        }

        private void OnLoadModel(object sender, EventArgs e)
        {
            string path = _modelPathBox.Text.Trim();
            if (path.Length == 0) { SetUserStatus("请先填写模型路径"); return; }
            try
            {
                _processor?.PauseMatching(true);
                lock (_matchLock) { _matcher.LoadModel(path); }
                _processor?.PauseMatching(false);
                SetUserStatus("模型已加载：" + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetUserStatus("加载失败：" + ex.Message);
            }
        }

        private void OnSaveModel(object sender, EventArgs e)
        {
            string path = _modelPathBox.Text.Trim();
            if (path.Length == 0) { SetUserStatus("请先填写模型路径"); return; }
            try
            {
                lock (_matchLock) { _matcher.SaveModel(path); }
                SetUserStatus("模型已保存：" + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetUserStatus("保存失败：" + ex.Message);
            }
        }

        // ---------- Modbus 通讯 ----------

        private void OnModbusConnect(object sender, EventArgs e)
        {
            if (_modbus != null && _modbus.IsConnected)
            {
                _triggerTimer.Stop();
                _modbus.Disconnect();
                _modbus.Dispose();
                _modbus = null;
                _modbusConnectBtn.Text = "连接 PLC";
                AppLog.Info("Modbus 已断开");
                SetUserStatus("Modbus 已断开");
                return;
            }

            try
            {
                _modbus = new ModbusClient();
                _modbus.Connect(_modbusHostBox.Text.Trim(), (int)_modbusPortInput.Value);
                _modbusConnectBtn.Text = "断开 PLC";
                AppLog.Info("Modbus 已连接 " + _modbusHostBox.Text + ":" + _modbusPortInput.Value);
                SetUserStatus(string.Format("Modbus 已连接 {0}:{1}", _modbusHostBox.Text, _modbusPortInput.Value));

                // 启动触发轮询（1Hz 读 HR10）
                _triggerTimer.Interval = 1000;
                _triggerTimer.Tick += OnTriggerPoll;
                _triggerTimer.Start();
            }
            catch (Exception ex)
            {
                _modbus?.Dispose();
                _modbus = null;
                AppLog.Error("Modbus 连接失败", ex);
                SetUserStatus("Modbus 连接失败：" + ex.Message);
            }
        }

        private void OnTriggerPoll(object sender, EventArgs e)
        {
            if (_modbus == null || !_modbus.IsConnected) return;
            try
            {
                int[] regs = _modbus.ReadRegisters(10, 1);
                if (regs[0] == 1)
                    _userStatus = "PLC 触发信号收到（HR10=1）";
            }
            catch
            {
                _triggerTimer.Stop();
                _modbusConnectBtn.Text = "连接 PLC";
                _userStatus = "Modbus 连接已断开（触发轮询失败）";
            }
        }

        private void WriteResultToPlc(bool ok, double valueMm)
        {
            if (_modbus == null || !_modbus.IsConnected) return;
            try
            {
                _sampleCount++;
                _modbus.WriteRegister(0, ok ? 1 : 0);           // HR0 判定
                _modbus.WriteRegister(1, (int)Math.Round(valueMm * 100)); // HR1 测量值 mm×100
                _modbus.WriteRegister(2, 1);                     // HR2 数据有效
                _modbus.WriteRegister(3, _sampleCount);          // HR3 样本计数
            }
            catch (Exception ex)
            {
                _userStatus = "Modbus 写入失败：" + ex.Message;
            }
        }

        // ---------- 相机与预览 ----------

        private IFrameSource Source
        {
            get
            {
                if (_source == null || !_source.IsOpen)
                {
                    _source = new OpenCvCameraSource();
                    _source.Open();
                }
                return _source;
            }
        }

        private void OnCaptureClick(object sender, EventArgs e)
        {
            try
            {
                using (var image = Source.Grab())
                using (var bmp = HalconImageConverter.ToBitmap(image))
                {
                    var old = _picture.Image;
                    _picture.Image = bmp;
                    old?.Dispose();
                }
                SetUserStatus(string.Format("采集成功 {0:HH:mm:ss}", DateTime.Now));
            }
            catch (Exception ex)
            {
                AppLog.Error("单帧采集失败", ex);
                SetUserStatus("采集失败：" + ex.Message);
            }
        }

        private void OnPreviewClick(object sender, EventArgs e)
        {
            if (_pipeline != null && _pipeline.IsRunning)
            {
                _processor.Stop();
                _pipeline.Stop();
                _previewBtn.Text = "开始预览";
                _captureBtn.Enabled = true;
                SetUserStatus("预览已停止");
                return;
            }

            try
            {
                _pipeline = new FramePipeline(new OpenCvCameraSource());
                _pipeline.ConnectionChanged += OnConnectionChanged;
                _processor = new MatchProcessor(_pipeline.Frames, _matcher, _caliper, _matchLock);
                _processor.ResultReady += OnResultReady;
                _pipeline.Start();
                _processor.Start();
                _previewBtn.Text = "停止预览";
                _captureBtn.Enabled = false;
                AppLog.Info("预览已启动");
                SetUserStatus("预览中…");
            }
            catch (Exception ex)
            {
                AppLog.Error("预览启动失败", ex);
                SetUserStatus("预览启动失败：" + ex.Message);
            }
        }

        private void OnConnectionChanged(bool connected, string message)
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                if (connected) AppLog.Info(message); else AppLog.Warn(message);
                SetUserStatus(message);
            }));
        }

        private void OnResultReady(InspectResult result)
        {
            if (IsDisposed) return;
            BeginInvoke(new Action<InspectResult>(DisplayResult), result);
        }

        private void DisplayResult(InspectResult result)
        {
            try
            {
                HObject oldFrame = _latestFrame;
                _latestFrame = result.Frame;
                oldFrame?.Dispose();

                double diaMm = 0, spaMm = 0;
                bool hasCalib = _calib != null;
                if (hasCalib && result.HasDiameter)
                    diaMm = _calib.DistanceMm(result.Diameter.Col1, result.Diameter.Row1, result.Diameter.Col2, result.Diameter.Row2);
                if (hasCalib && result.HasSpacing)
                    spaMm = _calib.DistanceMm(result.Spacing.Col1, result.Spacing.Row1, result.Spacing.Col2, result.Spacing.Row2);

                var oldBmp = _picture.Image;
                Bitmap bmp = HalconImageConverter.ToBitmap(result.Frame);
                if (result.HasMatch)
                {
                    DrawMatches(bmp, result.Matches);
                }
                if (result.HasDiameter)
                {
                    string label = hasCalib
                        ? string.Format("{0:0.0}px/{1:0.00}mm", result.Diameter.DistancePx, diaMm)
                        : string.Format("{0:0.0}px", result.Diameter.DistancePx);
                    DrawMeasurement(bmp, result.Diameter, Color.Cyan, "", label);
                }
                if (result.HasSpacing)
                {
                    string label = hasCalib
                        ? string.Format("{0:0.0}px/{1:0.00}mm", result.Spacing.DistancePx, spaMm)
                        : string.Format("{0:0.0}px", result.Spacing.DistancePx);
                    DrawMeasurement(bmp, result.Spacing, Color.Orange, "距 ", label);
                }
                _picture.Image = bmp;
                oldBmp?.Dispose();

                _trainBtn.Enabled = _hasRoi;

                string matchText = _matcher.IsTrained
                    ? (result.HasMatch
                        ? string.Format("命中 score={0:0.00} @({1:0},{2:0})", result.Matches[0].Score, result.Matches[0].Column, result.Matches[0].Row)
                        : "未找到")
                    : "未训练模板";

                bool judged = false;
                bool ok = false;
                string measureText = "";
                if (result.HasDiameter && hasCalib)
                {
                    ok = _judge.Judge(diaMm);
                    judged = true;
                    measureText = string.Format(" | 外径 {0:0.00}mm [{1}]", diaMm, _judge.Verdict(diaMm));
                    if (result.HasSpacing)
                        measureText += string.Format(" 间距 {0:0.00}mm", spaMm);
                }
                else if (result.HasDiameter)
                {
                    measureText = string.Format(" | 直径 {0:0.0}px", result.Diameter.DistancePx);
                }
                _status.ForeColor = judged && !ok ? Color.Red : Color.Black;

                // 写判定结果到 PLC
                if (judged)
                    WriteResultToPlc(ok, diaMm);

                string collectText = _collecting
                    ? string.Format(" | 重复性采样 {0}/{1}", _repeatability.Count, _repeatability.Target)
                    : "";
                _status.Text = string.Format("预览中  处理 {0} 帧 | 命中 {1} | {2}{3}{4}{5}",
                    _processor.ProcessedCount, _processor.MatchedCount, matchText, measureText, collectText,
                    _userStatus.Length > 0 ? " | " + _userStatus : "");

                if (_collecting && result.HasDiameter && result.HasSpacing
                    && (DateTime.Now - _lastSampleTime).TotalMilliseconds >= 300)
                {
                    _lastSampleTime = DateTime.Now;
                    _repeatability.Add(result.Diameter.DistancePx);
                    _repeatabilitySpacing.Add(result.Spacing.DistancePx);
                    if (_repeatability.IsComplete)
                    {
                        _collecting = false;
                        FinishRepeatability();
                    }
                }

                // 压测采集（W16-D4）
                if (_stressRunning)
                {
                    _stress.AddSample(result.ProcessMs, System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);
                    if (_stress.IsComplete)
                    {
                        _stressRunning = false;
                        FinishStress();
                    }
                    else
                    {
                        _status.Text = string.Format("压测中… {0}/{1}  CT={2:0.0}ms", _stress.Count, _stress.Target, result.ProcessMs);
                        return;
                    }
                }

                // 历史列表：每秒记一条检测记录（上限 200 条，最新在上）
                if ((DateTime.Now - _lastLogTime).TotalSeconds >= 1.0)
                {
                    _lastLogTime = DateTime.Now;
                    var record = new ResultRecord
                    {
                        Time = DateTime.Now,
                        Matched = result.HasMatch,
                        Score = result.HasMatch ? result.Matches[0].Score : 0,
                        Column = result.HasMatch ? result.Matches[0].Column : 0,
                        Row = result.HasMatch ? result.Matches[0].Row : 0,
                        Diameter = result.HasDiameter ? result.Diameter.DistancePx : 0,
                        Spacing = result.HasSpacing ? result.Spacing.DistancePx : 0,
                        DiameterMm = diaMm,
                        Verdict = judged ? (ok ? "OK" : "NG") : "-"
                    };
                    _history.Add(record);
                    var gridRow = new DataGridViewRow();
                    gridRow.CreateCells(_historyGrid,
                        record.Time.ToString("HH:mm:ss"),
                        record.Matched ? "命中" : "未找到",
                        record.Matched ? record.Score.ToString("0.00") : "-",
                        record.Diameter > 0 ? record.Diameter.ToString("0.0") : "-",
                        record.DiameterMm > 0 ? record.DiameterMm.ToString("0.00") : "-",
                        record.Verdict,
                        record.Spacing > 0 ? record.Spacing.ToString("0.0") : "-");
                    _historyGrid.Rows.Insert(0, gridRow);
                    while (_historyGrid.Rows.Count > _history.Capacity)
                        _historyGrid.Rows.RemoveAt(_historyGrid.Rows.Count - 1);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("结果显示异常", ex);
                _status.Text = "显示异常：" + ex.Message;
            }
        }

        private static void DrawMatches(Bitmap bmp, MatchResult[] matches)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var m in matches)
                {
                    g.TranslateTransform((float)m.Column, (float)m.Row);
                    g.RotateTransform((float)(m.Angle * 180.0 / Math.PI));
                    using (var pen = new Pen(Color.Lime, 2))
                        g.DrawRectangle(pen, (float)(-m.Width / 2), (float)(-m.Height / 2), (float)m.Width, (float)m.Height);
                    g.ResetTransform();
                    g.DrawString(string.Format("{0:0.00}", m.Score), SystemFonts.DefaultFont, Brushes.Lime,
                        (float)m.Column + 6, (float)m.Row + 6);
                }
            }
        }

        /// <summary>画测量线：两个边缘点 + 连线 + 标注。</summary>
        private static void DrawMeasurement(Bitmap bmp, EdgePair diameter, Color color, string prefix, string label)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, 2))
                {
                    g.DrawLine(pen,
                        (float)diameter.Col1, (float)diameter.Row1,
                        (float)diameter.Col2, (float)diameter.Row2);
                    const float r = 5f;
                    g.DrawEllipse(pen, (float)diameter.Col1 - r, (float)diameter.Row1 - r, 2 * r, 2 * r);
                    g.DrawEllipse(pen, (float)diameter.Col2 - r, (float)diameter.Row2 - r, 2 * r, 2 * r);
                }
                float midX = (float)((diameter.Col1 + diameter.Col2) / 2);
                float midY = (float)((diameter.Row1 + diameter.Row2) / 2);
                using (var brush = new SolidBrush(color))
                {
                    g.DrawString(prefix + label, SystemFonts.DefaultFont, brush, midX + 8, midY - 18);
                }
            }
        }

        // ---------- 压测（W16-D4）----------

        private void OnStressClick(object sender, EventArgs e)
        {
            if (!_matcher.IsTrained)
            {
                SetUserStatus("请先训练模板");
                return;
            }
            _stress.Reset();
            _stressRunning = true;
            AppLog.Info("压测开始（目标 " + _stress.Target + " 次）");
            SetUserStatus("压测中… 0/" + _stress.Target);
        }

        private void FinishStress()
        {
            string report = _stress.Report();
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "W16D4_压测报告.txt");
            try { System.IO.File.WriteAllText(path, report); }
            catch (Exception ex) { AppLog.Error("压测报告保存失败", ex); }
            AppLog.Info(string.Format("压测完成：CT 平均{0:0.0}ms 最大{1:0.0}ms", _stress.AvgMs, _stress.MaxMs));
            MessageBox.Show(report, "压测报告", MessageBoxButtons.OK,
                _stress.MaxMs < 150 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            SetUserStatus(string.Format("压测完成：CT 平均 {0:0.0}ms / 最大 {1:0.0}ms", _stress.AvgMs, _stress.MaxMs));
        }

        // ---------- 重复性测试 ----------

        private void OnRepeatClick(object sender, EventArgs e)
        {
            if (!_matcher.IsTrained)
            {
                SetUserStatus("请先训练模板");
                return;
            }
            _repeatability.Reset();
            _repeatabilitySpacing.Reset();
            _collecting = true;
            _lastSampleTime = DateTime.MinValue;
            SetUserStatus("重复性测试采集中…（每 0.3 秒 1 次，共 30 次）");
        }

        private void FinishRepeatability()
        {
            string report = _repeatability.Report("W14-D5 重复性测试报告 — 外径（直径）")
                + "\n" + _repeatabilitySpacing.Report("间距");
            bool pass = _repeatability.Std < 0.5 && _repeatabilitySpacing.Std < 0.5;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "W14D5_重复性测试报告.txt");
            try
            {
                File.WriteAllText(path, report);
            }
            catch (Exception ex)
            {
                path = "（保存失败：" + ex.Message + "）";
            }
            string summary = string.Format(
                "两项重复性测试完成（各 {0} 次）\n\n外径  均值 {1:0.000}px  标准差 {2:0.000}px\n间距  均值 {3:0.000}px  标准差 {4:0.000}px\n\n判定：{5}\n\n完整统计表已保存：\n{6}",
                _repeatability.Count, _repeatability.Mean, _repeatability.Std,
                _repeatabilitySpacing.Mean, _repeatabilitySpacing.Std,
                pass ? "两项都稳定（std < 0.5px）" : "存在不达标项", path);
            MessageBox.Show(summary, "重复性测试统计表", MessageBoxButtons.OK,
                pass ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            SetUserStatus(string.Format("重复性测试完成：外径 std {0:0.000}px，间距 std {1:0.000}px",
                _repeatability.Std, _repeatabilitySpacing.Std));
        }

        // ---------- 训练 ----------

        private void OnTrainClick(object sender, EventArgs e)
        {
            if (_latestFrame == null || !_hasRoi) return;
            if (Retrain())
                SetUserStatus(string.Format("模板已训练（ROI {0}x{1}）", _roiImageRect.Width, _roiImageRect.Height));
        }

        private bool Retrain()
        {
            try
            {
                _processor?.PauseMatching(true);
                lock (_matchLock)
                {
                    _matcher.Train(_latestFrame, _roiImageRect);
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("模板训练失败", ex);
                SetUserStatus("训练失败：" + ex.Message);
                return false;
            }
            finally
            {
                _processor?.PauseMatching(false);
            }
        }

        // ---------- 框选 ----------

        private void OnPictureMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _latestFrame == null) return;
            _selecting = true;
            _selStart = e.Location;
            _roiControlRect = new Rectangle(e.Location, Size.Empty);
        }

        private void OnPictureMouseMove(object sender, MouseEventArgs e)
        {
            if (!_selecting) return;
            _roiControlRect = Rectangle.FromLTRB(
                Math.Min(_selStart.X, e.X), Math.Min(_selStart.Y, e.Y),
                Math.Max(_selStart.X, e.X), Math.Max(_selStart.Y, e.Y));
            _picture.Invalidate();
        }

        private void OnPictureMouseUp(object sender, MouseEventArgs e)
        {
            if (!_selecting) return;
            _selecting = false;
            if (_roiControlRect.Width < 10 || _roiControlRect.Height < 10)
            {
                _picture.Invalidate();
                return;
            }
            _roiImageRect = ControlToImage(_roiControlRect);
            _hasRoi = true;
            _trainBtn.Enabled = true;
            SetUserStatus(string.Format("已框选 ROI {0}x{1}", _roiImageRect.Width, _roiImageRect.Height));
            _picture.Invalidate();
        }

        private void OnPicturePaint(object sender, PaintEventArgs e)
        {
            if (!_selecting || _roiControlRect.IsEmpty) return;
            using (var pen = new Pen(Color.Yellow, 1) { DashStyle = DashStyle.Dash })
                e.Graphics.DrawRectangle(pen, _roiControlRect);
        }

        private Rectangle ControlToImage(Rectangle controlRect)
        {
            if (_latestFrame == null) return Rectangle.Empty;
            int imgW = ImageWidth, imgH = ImageHeight;
            if (imgW == 0 || imgH == 0) return Rectangle.Empty;

            float scale = Math.Min((float)_picture.ClientSize.Width / imgW, (float)_picture.ClientSize.Height / imgH);
            float offX = (_picture.ClientSize.Width - imgW * scale) / 2;
            float offY = (_picture.ClientSize.Height - imgH * scale) / 2;

            int c1 = Clamp((int)((controlRect.Left - offX) / scale), 0, imgW);
            int r1 = Clamp((int)((controlRect.Top - offY) / scale), 0, imgH);
            int c2 = Clamp((int)((controlRect.Right - offX) / scale), 0, imgW);
            int r2 = Clamp((int)((controlRect.Bottom - offY) / scale), 0, imgH);
            return Rectangle.FromLTRB(c1, r1, c2, r2);
        }

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        private int ImageWidth
        {
            get
            {
                HOperatorSet.GetImageSize(_latestFrame, out HTuple w, out HTuple _);
                return (int)w.ToDArr()[0];
            }
        }

        private int ImageHeight
        {
            get
            {
                HOperatorSet.GetImageSize(_latestFrame, out HTuple _, out HTuple h);
                return (int)h.ToDArr()[0];
            }
        }
    }
}


