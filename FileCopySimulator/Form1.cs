using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ookii.Dialogs.WinForms;

namespace MachineVision.Tool.Simulator
{
    // ==========================================
    // [인터페이스 추상화] 장비 비즈니스 로직 결합도 최소화
    // ==========================================
    public interface IFileSimulator : IDisposable
    {
        long TotalCopiedCount { get; }
        Task StartSimulationAsync(string sourceFolder, string rootOutputFolder, int intervalMs,
                                  int minBuffer, int maxBuffer,
                                  Action<string, Color> logAction, CancellationToken token);
        void ResetCount();
    }

    // ==========================================
    // 1. Zero-Allocation 파일명 템플릿 제어기
    // ==========================================
    internal class OptimizedFileTemplate
    {
        public string SourceFullPath { get; }
        private readonly char[] _nameBuffer; // 메모리 재생성 없이 값만 덮어쓸 버퍼

        public OptimizedFileTemplate(string sourcePath)
        {
            SourceFullPath = sourcePath;
            string nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);

            // [안전 파싱] 원본 파일명에서 뒤의 3개 파트(원본유지_원본유지_cam0) 추출
            string[] parts = nameNoExt.Split('_');
            string suffix = parts.Length >= 3
                ? string.Join("_", parts.Skip(parts.Length - 3)) + ext
                : nameNoExt + ext;

            // 베이스 템플릿 구조: yyyyMMdd_HHmmssfff_suffix
            string template = $"00000000_000000000_{suffix}";
            _nameBuffer = template.ToCharArray();
        }

        // ToString() 없이 아스키(ASCII) 비트 연산만으로 날짜/시간을 덮어씀 (Zero-Allocation)
        public string GenerateTimestampedFileName(DateTime now)
        {
            // 1. Date (yyyyMMdd -> 인덱스 0~7)
            int year = now.Year, month = now.Month, day = now.Day;
            _nameBuffer[0] = (char)('0' + (year / 1000));
            _nameBuffer[1] = (char)('0' + ((year / 100) % 10));
            _nameBuffer[2] = (char)('0' + ((year / 10) % 10));
            _nameBuffer[3] = (char)('0' + (year % 10));
            _nameBuffer[4] = (char)('0' + (month / 10));
            _nameBuffer[5] = (char)('0' + (month % 10));
            _nameBuffer[6] = (char)('0' + (day / 10));
            _nameBuffer[7] = (char)('0' + (day % 10));

            // 2. Time (HHmmssfff -> 인덱스 9~17)
            int h = now.Hour, m = now.Minute, s = now.Second, ms = now.Millisecond;
            _nameBuffer[9] = (char)('0' + (h / 10));
            _nameBuffer[10] = (char)('0' + (h % 10));
            _nameBuffer[11] = (char)('0' + (m / 10));
            _nameBuffer[12] = (char)('0' + (m % 10));
            _nameBuffer[13] = (char)('0' + (s / 10));
            _nameBuffer[14] = (char)('0' + (s % 10));
            _nameBuffer[15] = (char)('0' + (ms / 100));
            _nameBuffer[16] = (char)('0' + ((ms / 10) % 10));
            _nameBuffer[17] = (char)('0' + (ms % 10));

            // .NET 파일 I/O를 위해 여기서 딱 한 번 string 할당
            return new string(_nameBuffer);
        }
    }

    // ==========================================
    // 2. 비즈니스 로직 (규격 준수 + 초고속 무한 복사 엔진)
    // ==========================================
    public class ContinuousCopyCore : IFileSimulator
    {
        private long _totalCopiedCount = 0;
        public long TotalCopiedCount => Interlocked.Read(ref _totalCopiedCount);

        private int _currentBufferCount = 0;
        private FileSystemWatcher _fileWatcher;
        private bool _isDisposed = false;

        public async Task StartSimulationAsync(string sourceFolder, string rootOutputFolder, int intervalMs,
                                               int minBuffer, int maxBuffer,
                                               Action<string, Color> logAction, CancellationToken token)
        {
            if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException("원본 폴더를 찾을 수 없습니다.");
            if (!Directory.Exists(rootOutputFolder)) Directory.CreateDirectory(rootOutputFolder);

            string[] supportedExts = { ".bmp", ".jpg", ".jpeg", ".png" };
            var rawFiles = Directory.GetFiles(sourceFolder)
                                    .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                    .ToList();

            if (rawFiles.Count == 0) throw new FileNotFoundException("원본 폴더에 이미지 파일이 없습니다.");

            // [핵심] 시작 시점에 모든 원본 파일을 최적화된 템플릿 메모리 구조로 변환
            List<OptimizedFileTemplate> fileTemplates = rawFiles
                .Select(f => new OptimizedFileTemplate(f))
                .ToList();

            InitializeWatcher(rootOutputFolder);

            logAction?.Invoke($"[규격 준수 고속 모드] 시뮬레이션 가동! (대상: {fileTemplates.Count}개, 주기: {(intervalMs == 0 ? "MAX SPEED" : intervalMs + "ms")})", Color.LimeGreen);

            int fileIndex = 0;
            bool isThrottled = false;
            string currentDailyFolderPath = string.Empty;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    string currentDatePart = now.ToString("yyyyMMdd");
                    string targetDailyFolder = Path.Combine(rootOutputFolder, currentDatePart);

                    // 자정 롤오버 및 폴더 스위칭
                    if (currentDailyFolderPath != targetDailyFolder)
                    {
                        if (!Directory.Exists(targetDailyFolder))
                        {
                            Directory.CreateDirectory(targetDailyFolder);
                            logAction?.Invoke($"[날짜 스위칭] 새로운 일일 폴더 생성: {currentDatePart}", Color.Yellow);
                        }

                        currentDailyFolderPath = targetDailyFolder;
                        Interlocked.Exchange(ref _currentBufferCount, Directory.EnumerateFiles(currentDailyFolderPath).Count());

                        if (isThrottled)
                        {
                            isThrottled = false;
                            logAction?.Invoke($"[RESUME] 새 폴더 전환으로 복사를 재개합니다.", Color.LimeGreen);
                        }
                    }

                    // 히스테리시스(버퍼) 제어 (디스크 I/O 없이 메모리로만 O(1) 연산)
                    int currentCount = Volatile.Read(ref _currentBufferCount);

                    if (isThrottled)
                    {
                        if (currentCount <= minBuffer)
                        {
                            isThrottled = false;
                            logAction?.Invoke($"[RESUME] 버퍼 안정화 ({currentCount}장). 복사를 재개합니다.", Color.LimeGreen);
                        }
                    }
                    else
                    {
                        if (currentCount >= maxBuffer)
                        {
                            isThrottled = true;
                            // 오차 보정 동기화
                            Interlocked.Exchange(ref _currentBufferCount, Directory.EnumerateFiles(currentDailyFolderPath).Count());
                            logAction?.Invoke($"[PAUSE] 버퍼 포화. OCR 처리를 대기합니다.", Color.Orange);
                        }
                    }

                    if (isThrottled)
                    {
                        await Task.Delay(10, token).ConfigureAwait(false);
                        continue;
                    }

                    // =========================================================================
                    // 🌟 [최종 병기] 스트링 조합(Concat) 제로, 규격(Naming) 100% 준수
                    // =========================================================================
                    OptimizedFileTemplate currentTemplate = fileTemplates[fileIndex];
                    string newFileName = currentTemplate.GenerateTimestampedFileName(now);
                    string destFile = Path.Combine(currentDailyFolderPath, newFileName);

                    // 방어적 코딩: MAX SPEED (0ms) 모드에서 1ms 안에 두 번 루프가 돌면 같은 파일명이 생성될 수 있음.
                    // 이 경우 파일을 덮어쓰면 카운트가 오르지 않아 기계가 멈춤(Starvation).
                    if (File.Exists(destFile))
                    {
                        // 클럭이 다음 1ms로 넘어갈 때까지 강제로 살짝 쉬고 '같은 파일(fileIndex)'로 재시도
                        await Task.Delay(1, token).ConfigureAwait(false);
                        continue;
                    }

                    // OS 레벨 쾌속 복사
                    File.Copy(currentTemplate.SourceFullPath, destFile, true);

                    Interlocked.Increment(ref _currentBufferCount);
                    Interlocked.Increment(ref _totalCopiedCount);

                    fileIndex = (fileIndex + 1) % fileTemplates.Count;
                }
                catch (IOException ex) when ((uint)ex.HResult == 0x80070020) // Sharing Violation
                {
                    logAction?.Invoke($"[WARNING] 파일 잠김(Sharing Violation): {ex.Message}", Color.Tomato);
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"[WARNING] I/O 지연: {ex.Message}", Color.Tomato);
                }

                if (intervalMs > 0)
                {
                    await Task.Delay(intervalMs, token).ConfigureAwait(false);
                }
                else
                {
                    // CPU 점유율 최적화를 위한 Context Yield
                    await Task.Yield();
                }
            }
        }

        private void InitializeWatcher(string rootPath)
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
            }

            _fileWatcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            _fileWatcher.Deleted += (s, e) => Interlocked.Decrement(ref _currentBufferCount);
            _fileWatcher.EnableRaisingEvents = true;
        }

        public void ResetCount()
        {
            Interlocked.Exchange(ref _totalCopiedCount, 0);
            Interlocked.Exchange(ref _currentBufferCount, 0);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                if (_fileWatcher != null)
                {
                    _fileWatcher.EnableRaisingEvents = false;
                    _fileWatcher.Dispose();
                }
                _isDisposed = true;
            }
        }
    }

    // ==========================================
    // 3. UI 로직 (MainForm) - 스레드 안전성 보장
    // ==========================================
    public class MainForm : Form
    {
        private TextBox _txtSourceFolder;
        private TextBox _txtOutputFolder;
        private NumericUpDown _numInterval;
        private NumericUpDown _numMinBuffer;
        private NumericUpDown _numMaxBuffer;
        private Button _btnSelectSource;
        private Button _btnSelectOutput;
        private Button _btnStartStop;
        private RichTextBox _rtbLog;
        private Label _lblStatus;
        private System.Windows.Forms.Timer _uiUpdateTimer;

        // 인터페이스로 참조하여 의존성 분리
        private readonly IFileSimulator _simulatorCore;
        private CancellationTokenSource _cts;
        private bool _isRunning = false;

        public MainForm()
        {
            _simulatorCore = new ContinuousCopyCore();
            InitializeDarkUI();

            _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            this.FormClosing += (s, e) => { _cts?.Cancel(); _simulatorCore?.Dispose(); };
        }

        private void BtnSelectSource_Click(object sender, EventArgs e)
        {
            using (var dialog = new VistaFolderBrowserDialog { Description = "Select Input Folder", UseDescriptionForTitle = true })
            {
                if (dialog.ShowDialog() == DialogResult.OK) _txtSourceFolder.Text = dialog.SelectedPath;
            }
        }

        private void BtnSelectOutput_Click(object sender, EventArgs e)
        {
            using (var dialog = new VistaFolderBrowserDialog { Description = "Select Output Folder", UseDescriptionForTitle = true })
            {
                if (dialog.ShowDialog() == DialogResult.OK) _txtOutputFolder.Text = dialog.SelectedPath;
            }
        }

        private async void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                if (string.IsNullOrWhiteSpace(_txtSourceFolder.Text) || string.IsNullOrWhiteSpace(_txtOutputFolder.Text))
                {
                    MessageBox.Show("폴더 경로를 모두 지정하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_numMinBuffer.Value >= _numMaxBuffer.Value)
                {
                    MessageBox.Show("최소 유지 개수(Min)는 최대 허용 개수(Max)보다 작아야 합니다.", "설정 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _isRunning = true;
                ToggleUiState();
                _simulatorCore.ResetCount();
                _rtbLog.Clear();

                _cts = new CancellationTokenSource();
                _uiUpdateTimer.Start();

                try
                {
                    await _simulatorCore.StartSimulationAsync(
                        _txtSourceFolder.Text,
                        _txtOutputFolder.Text,
                        (int)_numInterval.Value,
                        (int)_numMinBuffer.Value,
                        (int)_numMaxBuffer.Value,
                        AppendLog,
                        _cts.Token
                    );
                }
                catch (TaskCanceledException) { /* 정상 정지 시 무시 */ }
                catch (Exception ex)
                {
                    AppendLog($"[FATAL] 시스템 오류: {ex.Message}", Color.Tomato);
                }
                finally
                {
                    _isRunning = false;
                    _uiUpdateTimer.Stop();
                    ToggleUiState();
                    AppendLog("=== 시뮬레이션 정지됨 ===", Color.Orange);
                }
            }
            else
            {
                AppendLog("정지 신호를 전송했습니다. 대기 중...", Color.Yellow);
                _cts?.Cancel();
            }
        }

        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            long currentCount = _simulatorCore.TotalCopiedCount;
            _lblStatus.Text = $"현재 누적 복사 횟수: {currentCount:N0} 회";
        }

        private void AppendLog(string text, Color color)
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

            _txtSourceFolder.Enabled = !_isRunning;
            _txtOutputFolder.Enabled = !_isRunning;
            _numInterval.Enabled = !_isRunning;
            _numMinBuffer.Enabled = !_isRunning;
            _numMaxBuffer.Enabled = !_isRunning;
            _btnSelectSource.Enabled = !_isRunning;
            _btnSelectOutput.Enabled = !_isRunning;

            if (_isRunning)
            {
                _btnStartStop.Text = "STOP SIMULATION";
                _btnStartStop.BackColor = Color.FromArgb(204, 74, 74);
            }
            else
            {
                _btnStartStop.Text = "START CONTINUOUS SIMULATION";
                _btnStartStop.BackColor = Color.FromArgb(0, 122, 204);
            }
        }

        private void InitializeDarkUI()
        {
            this.Text = "24/7 OCR Image Simulator Pro";
            this.Size = new Size(680, 520);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            var lblSource = new Label { Text = "Input Dir:", Location = new Point(20, yPos + 4), Size = new Size(110, 20) };
            _txtSourceFolder = new TextBox { Location = new Point(130, yPos), Size = new Size(420, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnSelectSource = new Button { Text = "BROWSE", Location = new Point(560, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectSource.FlatAppearance.BorderSize = 0;
            _btnSelectSource.Click += BtnSelectSource_Click;
            this.Controls.Add(lblSource); this.Controls.Add(_txtSourceFolder); this.Controls.Add(_btnSelectSource);

            yPos += 40;

            var lblOutput = new Label { Text = "Output Dir:", Location = new Point(20, yPos + 4), Size = new Size(110, 20), ForeColor = Color.LightGreen };
            _txtOutputFolder = new TextBox { Location = new Point(130, yPos), Size = new Size(420, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnSelectOutput = new Button { Text = "BROWSE", Location = new Point(560, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectOutput.FlatAppearance.BorderSize = 0;
            _btnSelectOutput.Click += BtnSelectOutput_Click;
            this.Controls.Add(lblOutput); this.Controls.Add(_txtOutputFolder); this.Controls.Add(_btnSelectOutput);

            yPos += 45;

            var lblInterval = new Label { Text = "Interval(ms):", Location = new Point(20, yPos + 4), Size = new Size(110, 20), ForeColor = Color.LightSkyBlue };
            _numInterval = new NumericUpDown { Location = new Point(130, yPos), Size = new Size(80, 23), Minimum = 0, Maximum = 60000, Value = 0, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };

            var lblMin = new Label { Text = "재개(Min):", Location = new Point(230, yPos + 4), Size = new Size(80, 20), ForeColor = Color.LightGreen };
            _numMinBuffer = new NumericUpDown { Location = new Point(310, yPos), Size = new Size(80, 23), Minimum = 1, Maximum = 10000, Value = 350, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };

            var lblMax = new Label { Text = "정지(Max):", Location = new Point(410, yPos + 4), Size = new Size(80, 20), ForeColor = Color.Tomato };
            _numMaxBuffer = new NumericUpDown { Location = new Point(490, yPos), Size = new Size(80, 23), Minimum = 10, Maximum = 10000, Value = 450, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };

            this.Controls.Add(lblInterval); this.Controls.Add(_numInterval);
            this.Controls.Add(lblMin); this.Controls.Add(_numMinBuffer);
            this.Controls.Add(lblMax); this.Controls.Add(_numMaxBuffer);

            yPos += 45;

            _btnStartStop = new Button { Text = "START CONTINUOUS SIMULATION", Location = new Point(20, yPos), Size = new Size(620, 50), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 12F, FontStyle.Bold) };
            _btnStartStop.FlatAppearance.BorderSize = 0;
            _btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(_btnStartStop);

            yPos += 65;

            _lblStatus = new Label { Text = "현재 누적 복사 횟수: 0 회", Location = new Point(20, yPos), Size = new Size(620, 30), Font = new Font("Consolas", 14F, FontStyle.Bold), ForeColor = Color.Cyan, TextAlign = ContentAlignment.MiddleCenter };
            this.Controls.Add(_lblStatus);

            yPos += 35;

            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(620, 180), BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }
}