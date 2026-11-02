using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;
using Orientation = System.Windows.Forms.Orientation;

namespace MemoryAnalyzer
{
    // ==========================================
    // [데이터 모델] GC 부하 최소화를 위한 불변 Struct
    // ==========================================
    public struct MemoryLogEntry
    {
        public DateTime Timestamp { get; }
        public double CommitMB { get; }
        public double RamMB { get; }
        public double CSharpMB { get; }
        public double CppMB { get; }
        public int GdiCount { get; }
        public int UserCount { get; }
        public int ThreadCount { get; }
        public int HandleCount { get; }

        public MemoryLogEntry(DateTime timestamp, double commit, double ram, double csharp, double cpp,
                              int gdi, int user, int thread, int handle)
        {
            Timestamp = timestamp;
            CommitMB = commit;
            RamMB = ram;
            CSharpMB = csharp;
            CppMB = cpp;
            GdiCount = gdi;
            UserCount = user;
            ThreadCount = thread;
            HandleCount = handle;
        }
    }

    // ==========================================
    // [인터페이스 추상화] 다중 파일 배열(string[]) 지원으로 변경
    // ==========================================
    public interface IMemoryLogParser
    {
        Task<List<MemoryLogEntry>> ParseLogsAsync(string[] filePaths, Action<string, WinColor> logAction, IProgress<int> progress, CancellationToken token);
    }

    // ==========================================
    // 1. 비즈니스 로직 (대용량 다중 파일 스트리밍 파서)
    // ==========================================
    public class MemoryLogParserCore : IMemoryLogParser
    {
        public async Task<List<MemoryLogEntry>> ParseLogsAsync(string[] filePaths, Action<string, WinColor> logAction,
                                                               IProgress<int> progress, CancellationToken token)
        {
            if (filePaths == null || filePaths.Length == 0) return new List<MemoryLogEntry>();

            // [핵심 1] 시간순 병합을 위해 파일 이름 기반으로 정렬 (예: 2026-09-02, 2026-09-03 순서 보장)
            var sortedFiles = filePaths.OrderBy(f => f).ToArray();

            // [핵심 2] LOH (Large Object Heap) 파편화 방지를 위해 1,000,000건(수일치) 규모 사전 할당
            var results = new List<MemoryLogEntry>(1000000);

            logAction?.Invoke($"총 {sortedFiles.Length}개의 다중 로그 파일 분석을 시작합니다...", WinColor.WhiteSmoke);

            long totalBytesAllFiles = sortedFiles.Sum(f => new FileInfo(f).Length);
            long processedBytes = 0;

            foreach (string filePath in sortedFiles)
            {
                if (!File.Exists(filePath)) continue;

                logAction?.Invoke($"Reading: {Path.GetFileName(filePath)}", WinColor.Gray);

                // [핵심 3] 파일 이름에서 기준 날짜(Base Date) 추출 (예: 2026-09-02_LOT3_Debug.log)
                DateTime baseDate = ExtractDateFromFileName(Path.GetFileName(filePath));
                TimeSpan prevTime = TimeSpan.Zero;
                int dayOffset = 0;

                long currentFileBytes = new FileInfo(filePath).Length;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous))
                using (var reader = new StreamReader(fs))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        token.ThrowIfCancellationRequested();

                        if (!line.Contains("[Mem]") || !line.Contains("[Res]")) continue;

                        try
                        {
                            if (TimeSpan.TryParse(line.Substring(1, 12), out TimeSpan currentTime))
                            {
                                // 단일 파일 내 자정 롤오버 보정
                                if (currentTime < prevTime && (prevTime.TotalHours - currentTime.TotalHours) > 12)
                                    dayOffset++;
                                prevTime = currentTime;
                            }

                            DateTime actualTime = baseDate.AddDays(dayOffset).Add(currentTime);

                            double commit = ExtractDouble(line, "Commit: ", "MB");
                            double ram = ExtractDouble(line, "RAM: ", "MB");
                            double csharp = ExtractDouble(line, "C#: ", "MB");
                            double cpp = ExtractDouble(line, "C++: ", "MB");

                            int gdi = (int)ExtractDouble(line, "GDI: ", ",");
                            int user = (int)ExtractDouble(line, "User: ", ",");
                            int thread = (int)ExtractDouble(line, "Thread: ", ",");
                            int handle = (int)ExtractDouble(line, "Handle: ", "");

                            results.Add(new MemoryLogEntry(actualTime, commit, ram, csharp, cpp, gdi, user, thread, handle));
                        }
                        catch
                        {
                            continue;
                        }

                        // 통합 프로그레스 업데이트
                        if (results.Count % 10000 == 0)
                        {
                            int percent = (int)(((processedBytes + fs.Position) / (double)totalBytesAllFiles) * 100);
                            progress?.Report(percent);
                        }
                    }
                }
                processedBytes += currentFileBytes;
            }

            // [핵심 4] 다중 파일을 엮었으므로, 최종적으로 Timestamp 기준 완벽한 오름차순 정렬 보장 (SignalXY 필수 요건)
            logAction?.Invoke("데이터 시계열 정렬(Sorting) 중...", WinColor.Orange);
            results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            progress?.Report(100);
            logAction?.Invoke($"분석 완료! 총 {results.Count:N0}건의 누적 데이터가 로드되었습니다.", WinColor.LimeGreen);

            return results;
        }

        // 파일명에서 "2026-09-02" 또는 "20260902" 형태의 기준 날짜 추출
        private DateTime ExtractDateFromFileName(string fileName)
        {
            var match = Regex.Match(fileName, @"(\d{4})-(\d{2})-(\d{2})");
            if (match.Success) return new DateTime(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));

            match = Regex.Match(fileName, @"(\d{4})(\d{2})(\d{2})");
            if (match.Success) return new DateTime(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));

            // 날짜 포맷이 없으면 파일이 파싱되는 현재 날짜 반환 (Fallback)
            return DateTime.Today;
        }

        private double ExtractDouble(string source, string key, string endMarker)
        {
            int startIndex = source.IndexOf(key);
            if (startIndex == -1) return 0;

            startIndex += key.Length;
            int endIndex = string.IsNullOrEmpty(endMarker) ? source.Length : source.IndexOf(endMarker, startIndex);
            if (endIndex == -1) endIndex = source.Length;

            string valStr = source.Substring(startIndex, endIndex - startIndex).Trim();
            if (double.TryParse(valStr, out double result)) return result;
            return 0;
        }
    }

    // ==========================================
    // 2. UI 로직 (MainForm) 
    // ==========================================
    public class MainForm : Form
    {
        private TextBox _txtLogFile;
        private Button _btnBrowse;
        private Button _btnAnalyze;
        private RichTextBox _rtbLog;
        private ProgressBar _progressBar;

        private DataGridView _grid;
        private FormsPlot _mainPlot;
        private FlowLayoutPanel _buttonPanel;

        private string[] _selectedFiles = new string[0]; // 다중 선택된 파일 경로 배열
        private List<MemoryLogEntry> _cachedData = null;
        private string _currentSelectedMetric = "Commit (MB)";

        private readonly IMemoryLogParser _parserCore;
        private CancellationTokenSource _cts;
        private bool _isRunning = false;

        public MainForm()
        {
            _parserCore = new MemoryLogParserCore();
            InitializeDarkUI();
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Log Files (*.log;*.txt)|*.log;*.txt|All Files (*.*)|*.*", Title = "Vision System 다중 로그 파일 선택", Multiselect = true }) // 🌟 다중 선택 허용
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _selectedFiles = ofd.FileNames;
                    _txtLogFile.Text = $"{_selectedFiles.Length}개의 파일이 선택되었습니다.";
                    AppendLog($"{_selectedFiles.Length}개의 파일 스캔 준비 완료.", WinColor.LightSkyBlue);
                }
            }
        }

        private async void BtnAnalyze_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                if (_selectedFiles == null || _selectedFiles.Length == 0)
                {
                    MessageBox.Show("분석할 로그 파일을 1개 이상 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _isRunning = true;
                ToggleUiState();
                _rtbLog.Clear();
                _grid.DataSource = null;

                _cts = new CancellationTokenSource();
                var progressIndicator = new Progress<int>(p => _progressBar.Value = Math.Min(100, Math.Max(0, p)));

                try
                {
                    _cachedData = await _parserCore.ParseLogsAsync(_selectedFiles, AppendLog, progressIndicator, _cts.Token);

                    if (_cachedData.Count > 0)
                    {
                        RenderSingleMetricChart(_currentSelectedMetric);
                        SafeGridBinding(_cachedData);
                    }
                    else
                    {
                        AppendLog("추출된 데이터가 없습니다. 로그 포맷을 확인하세요.", WinColor.Orange);
                    }
                }
                catch (OperationCanceledException) { AppendLog("분석이 취소되었습니다.", WinColor.Yellow); }
                catch (Exception ex) { AppendLog($"[FATAL] 시스템 오류: {ex.Message}", WinColor.Tomato); }
                finally
                {
                    _isRunning = false;
                    ToggleUiState();
                }
            }
            else
            {
                _cts?.Cancel();
            }
        }

        // 🌟 [방어적 코딩] DataGridView 폭주 방지
        private void SafeGridBinding(List<MemoryLogEntry> data)
        {
            int maxGridRows = 100000; // 그리드는 최대 10만 줄까지만 바인딩 허용
            if (data.Count > maxGridRows)
            {
                AppendLog($"[주의] 데이터가 너무 큽니다({data.Count:N0}건). UI 응답성(Hang) 방지를 위해 DataGridView에는 최근 {maxGridRows:N0}건만 표시되며, 차트(Chart)는 전체 데이터를 정상 렌더링합니다.", WinColor.Orange);
                _grid.DataSource = data.Skip(data.Count - maxGridRows).ToList();
            }
            else
            {
                _grid.DataSource = data;
            }
        }

        private void RenderSingleMetricChart(string metricName)
        {
            if (_cachedData == null || _cachedData.Count == 0) return;

            _currentSelectedMetric = metricName;
            AppendLog($"[{metricName}] 대용량 차트 렌더링 중...", WinColor.LightSkyBlue);

            double[] xs = _cachedData.Select(x => x.Timestamp.ToOADate()).ToArray();
            double[] ys = null;
            ScottPlot.Color plotColor = ScottPlot.Colors.White;

            switch (metricName)
            {
                case "Commit (MB)": ys = _cachedData.Select(x => x.CommitMB).ToArray(); plotColor = ScottPlot.Colors.DeepSkyBlue; break;
                case "Physical RAM (MB)": ys = _cachedData.Select(x => x.RamMB).ToArray(); plotColor = ScottPlot.Colors.LimeGreen; break;
                case "C++ Unmanaged (MB)": ys = _cachedData.Select(x => x.CppMB).ToArray(); plotColor = ScottPlot.Colors.Magenta; break;
                case "C# Managed (MB)": ys = _cachedData.Select(x => x.CSharpMB).ToArray(); plotColor = ScottPlot.Colors.Orange; break;
                case "GDI Objects (Count)": ys = _cachedData.Select(x => (double)x.GdiCount).ToArray(); plotColor = ScottPlot.Colors.Gold; break;
                case "USER Objects (Count)": ys = _cachedData.Select(x => (double)x.UserCount).ToArray(); plotColor = ScottPlot.Colors.Cyan; break;
                case "Active Threads (Count)": ys = _cachedData.Select(x => (double)x.ThreadCount).ToArray(); plotColor = ScottPlot.Colors.Yellow; break;
                case "System Handles (Count)": ys = _cachedData.Select(x => (double)x.HandleCount).ToArray(); plotColor = ScottPlot.Colors.Tomato; break;
            }

            if (ys == null) return;

            var plt = _mainPlot.Plot;
            plt.Clear();

            plt.FigureBackground.Color = ScottPlot.Colors.Black;
            plt.DataBackground.Color = ScottPlot.Colors.Black;
            plt.Axes.Color(ScottPlot.Colors.White);
            plt.Axes.DateTimeTicksBottom();

            // 🌟 SignalXY가 수백만 개의 데이터를 1픽셀 단위로 완벽하게 병합 렌더링
            var sig = plt.Add.SignalXY(xs, ys);
            sig.Color = plotColor;

            plt.Title($"Vision System Multi-Day Profile: {metricName}", size: 16);
            plt.Axes.Left.Label.Text = metricName;
            plt.Axes.Left.Label.ForeColor = plotColor;
            plt.Axes.Left.TickLabelStyle.ForeColor = plotColor;
            plt.Axes.Bottom.Label.Text = "Timeline (Days / HH:mm:ss)";

            var anno = plt.Add.Annotation(metricName);
            anno.LabelStyle.FontSize = 14;
            anno.LabelStyle.Bold = true;
            anno.LabelStyle.ForeColor = ScottPlot.Colors.White;
            anno.LabelStyle.BackgroundColor = plotColor.WithAlpha(120);
            anno.LabelStyle.BorderColor = plotColor;
            anno.LabelStyle.BorderWidth = 1;
            anno.Alignment = ScottPlot.Alignment.UpperLeft;
            anno.OffsetX = 15;
            anno.OffsetY = 15;

            var ch = plt.Add.Crosshair(0, 0);
            ch.LineColor = plotColor.WithAlpha(150);
            ch.IsVisible = false;

            _mainPlot.MouseMove -= MainPlot_MouseMove;
            _mainPlot.MouseMove += MainPlot_MouseMove;
            _mainPlot.MouseLeave -= MainPlot_MouseLeave;
            _mainPlot.MouseLeave += MainPlot_MouseLeave;

            void MainPlot_MouseMove(object s, MouseEventArgs e)
            {
                ScottPlot.Pixel mousePixel = new ScottPlot.Pixel(e.X, e.Y);
                ScottPlot.Coordinates mouseLocation = plt.GetCoordinates(mousePixel);

                ch.X = mouseLocation.X;
                ch.Y = mouseLocation.Y;
                ch.IsVisible = true;

                // 다일(Multi-day) 데이터이므로 yyyy-MM-dd 날짜 포맷까지 표시
                string timeStr = DateTime.FromOADate(mouseLocation.X).ToString("yyyy-MM-dd HH:mm:ss.fff");
                anno.Text = $"{metricName}\nTime: {timeStr} ➔ Value: {mouseLocation.Y:N2}";

                _mainPlot.Refresh();
            }

            void MainPlot_MouseLeave(object s, EventArgs e)
            {
                ch.IsVisible = false;
                anno.Text = metricName;
                _mainPlot.Refresh();
            }

            plt.Axes.AutoScale();
            _mainPlot.Refresh();
            AppendLog($"[{metricName}] 대형 차트 렌더링 완료.", WinColor.LimeGreen);
        }

        private void AppendLog(string text, WinColor color)
        {
            if (InvokeRequired) { Invoke(new Action(() => AppendLog(text, color))); return; }
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            _rtbLog.ScrollToCaret();
        }

        private void ToggleUiState()
        {
            if (InvokeRequired) { Invoke(new Action(ToggleUiState)); return; }
            _txtLogFile.Enabled = false; // 항상 읽기 전용
            _btnBrowse.Enabled = !_isRunning;

            if (_isRunning)
            {
                _btnAnalyze.Text = "STOP ANALYSIS";
                _btnAnalyze.BackColor = WinColor.FromArgb(204, 74, 74);
            }
            else
            {
                _btnAnalyze.Text = "START MULTI-DAY ANALYSIS";
                _btnAnalyze.BackColor = WinColor.FromArgb(0, 122, 204);
            }
        }

        private void InitializeDarkUI()
        {
            this.Text = "Vision Multi-Day Leak Analyzer Pro (Interactive Mode)";
            this.Size = new Size(1850, 1020);
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 130 };
            this.Controls.Add(pnlTop);

            var lblFile = new WinLabel { Text = "Log Files:", Location = new Point(20, 24), Size = new Size(90, 20) };
            _txtLogFile = new TextBox { Location = new Point(110, 20), Size = new Size(1100, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White, ReadOnly = true };
            _btnBrowse = new Button { Text = "BROWSE", Location = new Point(1220, 19), Size = new Size(90, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnBrowse.FlatAppearance.BorderSize = 0;
            _btnBrowse.Click += BtnBrowse_Click;

            _btnAnalyze = new Button { Text = "START MULTI-DAY ANALYSIS", Location = new Point(1320, 19), Size = new Size(220, 25), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 10F, FontStyle.Bold) };
            _btnAnalyze.FlatAppearance.BorderSize = 0;
            _btnAnalyze.Click += BtnAnalyze_Click;

            _progressBar = new ProgressBar { Location = new Point(20, 55), Size = new Size(1520, 10), Style = ProgressBarStyle.Continuous };
            _rtbLog = new RichTextBox { Location = new Point(20, 70), Size = new Size(1520, 50), BackColor = WinColor.FromArgb(18, 18, 18), ForeColor = WinColor.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };

            pnlTop.Controls.AddRange(new Control[] { lblFile, _txtLogFile, _btnBrowse, _btnAnalyze, _progressBar, _rtbLog });

            var splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 450 };
            this.Controls.Add(splitMain);
            splitMain.BringToFront();

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = WinColor.FromArgb(45, 45, 48),
                ForeColor = WinColor.Black,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };
            splitMain.Panel1.Controls.Add(_grid);

            var splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 200 };
            splitMain.Panel2.Controls.Add(splitRight);

            _buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = WinColor.FromArgb(35, 35, 38),
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(10)
            };
            splitRight.Panel1.Controls.Add(_buttonPanel);

            string[] metrics = {
                "Commit (MB)",
                "Physical RAM (MB)",
                "C++ Unmanaged (MB)",
                "C# Managed (MB)",
                "GDI Objects (Count)",
                "USER Objects (Count)",
                "Active Threads (Count)",
                "System Handles (Count)"
            };

            foreach (var metric in metrics)
            {
                var btn = new Button
                {
                    Text = metric,
                    Size = new Size(175, 45),
                    BackColor = WinColor.FromArgb(62, 62, 66),
                    ForeColor = WinColor.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Consolas", 9.5F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 0, 8)
                };
                btn.FlatAppearance.BorderSize = 0;

                string targetMetric = metric;
                btn.Click += (s, e) =>
                {
                    foreach (Control c in _buttonPanel.Controls)
                    {
                        if (c is Button b) b.BackColor = WinColor.FromArgb(62, 62, 66);
                    }
                    btn.BackColor = WinColor.FromArgb(0, 122, 204);

                    RenderSingleMetricChart(targetMetric);
                };

                _buttonPanel.Controls.Add(btn);
            }

            if (_buttonPanel.Controls.Count > 0 && _buttonPanel.Controls[0] is Button firstBtn)
            {
                firstBtn.BackColor = WinColor.FromArgb(0, 122, 204);
            }

            _mainPlot = new FormsPlot { Dock = DockStyle.Fill };
            _mainPlot.Plot.FigureBackground.Color = ScottPlot.Colors.Black;
            _mainPlot.Plot.DataBackground.Color = ScottPlot.Colors.Black;
            _mainPlot.Plot.Axes.Color(ScottPlot.Colors.White);

            splitRight.Panel2.Controls.Add(_mainPlot);
        }
    }
}