using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ookii.Dialogs.WinForms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Tool.Simulator
{
    // ==========================================
    // [인터페이스 추상화]
    // ==========================================
    public interface IFileSimulator : IDisposable
    {
        long TotalCopiedCount { get; }
        long TotalErrorCount { get; }
        Task StartSimulationAsync(string sourceFolder, string rootOutputFolder, int intervalMs,
                                   int minBuffer, int maxBuffer, bool simulateMidnight, bool syncCameras,
                                   bool simpleRawCopy, // 🌟 신규 옵션 추가
                                   Action<string, WinColor> logAction, CancellationToken token);
        void ResetCount();
    }

    // ==========================================
    // 1. Zero-Allocation 파일명 템플릿 제어기
    // ==========================================
    internal class OptimizedFileTemplate
    {
        public string SourceFullPath { get; }
        private readonly char[] _nameBuffer;

        public OptimizedFileTemplate(string sourcePath)
        {
            SourceFullPath = sourcePath;
            string nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);

            string[] parts = nameNoExt.Split('_');
            string suffix = parts.Length >= 3
                ? string.Join("_", parts.Skip(parts.Length - 3)) + ext
                : nameNoExt + ext;

            string template = $"00000000_000000000_{suffix}";
            _nameBuffer = template.ToCharArray();
        }

        public string GenerateTimestampedFileName(DateTime now)
        {
            int year = now.Year, month = now.Month, day = now.Day;
            _nameBuffer[0] = (char)('0' + (year / 1000));
            _nameBuffer[1] = (char)('0' + ((year / 100) % 10));
            _nameBuffer[2] = (char)('0' + ((year / 10) % 10));
            _nameBuffer[3] = (char)('0' + (year % 10));
            _nameBuffer[4] = (char)('0' + (month / 10));
            _nameBuffer[5] = (char)('0' + (month % 10));
            _nameBuffer[6] = (char)('0' + (day / 10));
            _nameBuffer[7] = (char)('0' + (day % 10));

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

            return new string(_nameBuffer);
        }
    }

    // ==========================================
    // 2. 비즈니스 로직 (원시 복사 모드 추가)
    // ==========================================
    public class ContinuousCopyCore : IFileSimulator
    {
        private long _totalCopiedCount = 0;
        public long TotalCopiedCount => Interlocked.Read(ref _totalCopiedCount);

        private long _totalErrorCount = 0;
        public long TotalErrorCount => Interlocked.Read(ref _totalErrorCount);

        private int _currentBufferCount = 0;
        private FileSystemWatcher _fileWatcher;
        private bool _isDisposed = false;

        public async Task StartSimulationAsync(string sourceFolder, string rootOutputFolder, int intervalMs,
                                               int minBuffer, int maxBuffer, bool simulateMidnight, bool syncCameras,
                                               bool simpleRawCopy, // 🌟 신규 옵션 수신
                                               Action<string, WinColor> logAction, CancellationToken token)
        {
            if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException("원본 폴더를 찾을 수 없습니다.");
            if (!Directory.Exists(rootOutputFolder)) Directory.CreateDirectory(rootOutputFolder);

            string[] supportedExts = { ".bmp", ".jpg", ".jpeg", ".png" };
            var rawFiles = Directory.GetFiles(sourceFolder)
                                    .Where(f => supportedExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                    .OrderBy(f => f) // 이름순 정렬
                                    .ToList();

            if (rawFiles.Count == 0) throw new FileNotFoundException("원본 폴더에 이미지 파일이 없습니다.");

            // 🌟 [핵심 분기] 단순 원본 복사 모드일 경우 기존의 24/7 로직을 전부 건너뜀
            if (simpleRawCopy)
            {
                logAction?.Invoke($"[단순 복사 모드] 총 {rawFiles.Count}개의 파일을 원본 이름 그대로 복사합니다.", WinColor.LimeGreen);

                foreach (var file in rawFiles)
                {
                    token.ThrowIfCancellationRequested(); // 중간 취소 지원

                    string destFile = Path.Combine(rootOutputFolder, Path.GetFileName(file));
                    try
                    {
                        File.Copy(file, destFile, true); // 덮어쓰기 허용
                        Interlocked.Increment(ref _totalCopiedCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _totalErrorCount);
                        logAction?.Invoke($"[ERROR] 복사 실패: {ex.Message}", WinColor.Tomato);
                    }

                    // 지정된 Interval 값만큼 스피드 조절 (Throttle)
                    if (intervalMs > 0)
                    {
                        await Task.Delay(intervalMs, token).ConfigureAwait(false);
                    }
                }

                logAction?.Invoke($"[완료] {rawFiles.Count}개 파일 복사가 완료되어 자동 종료합니다.", WinColor.Cyan);
                return; // 루프를 빠져나가면 UI가 자동으로 정지(Stopped) 상태로 복귀함
            }

            // --- 이 아래는 기존의 24/7 무한 루프 모드 (simpleRawCopy == false) ---
            List<List<OptimizedFileTemplate>> groupedTemplates = rawFiles
                .GroupBy(f => syncCameras ? GetGroupKey(f) : f)
                .Select(g => g.Select(file => new OptimizedFileTemplate(file)).ToList())
                .ToList();

            InitializeWatcher(rootOutputFolder);

            string modeMsg = syncCameras ? $"ON ({groupedTemplates.Count}개 그룹)" : "OFF (개별 파일)";
            logAction?.Invoke($"[연속 가동 모드] 버퍼 제어 (Max:{maxBuffer}, Min:{minBuffer}) | 카메라 동기화: {modeMsg}", WinColor.LimeGreen);

            int groupIndex = 0;
            string currentDailyFolderPath = string.Empty;
            bool simulateNextDayToggle = false;
            long iterationCounter = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    iterationCounter++;
                    DateTime now = DateTime.Now;

                    if (simulateMidnight && (iterationCounter % 50 == 0))
                    {
                        simulateNextDayToggle = !simulateNextDayToggle;
                        logAction?.Invoke($"[엣지 케이스 주입] 자정 폴더 교체 모사 (Target: {(simulateNextDayToggle ? now.AddDays(1).ToString("yyyyMMdd") : now.ToString("yyyyMMdd"))})", WinColor.Gold);
                    }

                    DateTime effectiveDate = (simulateMidnight && simulateNextDayToggle) ? now.AddDays(1) : now;
                    string currentDatePart = effectiveDate.ToString("yyyyMMdd");
                    string targetDailyFolder = Path.Combine(rootOutputFolder, currentDatePart);

                    if (currentDailyFolderPath != targetDailyFolder)
                    {
                        if (!Directory.Exists(targetDailyFolder))
                        {
                            Directory.CreateDirectory(targetDailyFolder);
                            logAction?.Invoke($"[디렉토리 생성] 일일 폴더 갱신: {currentDatePart}", WinColor.Yellow);
                        }
                        currentDailyFolderPath = targetDailyFolder;
                        SyncBufferCountHard(currentDailyFolderPath);
                    }

                    if (iterationCounter % 100 == 0) SyncBufferCountHard(currentDailyFolderPath);

                    int currentCount = Volatile.Read(ref _currentBufferCount);
                    if (currentCount >= maxBuffer)
                    {
                        logAction?.Invoke($"[PAUSE] 버퍼 폭주 방어! (현재: {currentCount} >= Max: {maxBuffer}). OCR 소비 대기...", WinColor.Orange);

                        while (Volatile.Read(ref _currentBufferCount) > minBuffer)
                        {
                            token.ThrowIfCancellationRequested();
                            await Task.Delay(10, token).ConfigureAwait(false);

                            if (Environment.TickCount % 1000 < 20) SyncBufferCountHard(currentDailyFolderPath);
                        }
                        logAction?.Invoke($"[RESUME] 버퍼 안정화 완료. 복사 재개.", WinColor.LimeGreen);
                    }

                    List<OptimizedFileTemplate> currentGroup = groupedTemplates[groupIndex];
                    int successfulCopiesInGroup = 0;

                    foreach (var template in currentGroup)
                    {
                        string newFileName = template.GenerateTimestampedFileName(effectiveDate);
                        string destFile = Path.Combine(currentDailyFolderPath, newFileName);

                        if (File.Exists(destFile))
                        {
                            await Task.Delay(1, token).ConfigureAwait(false);
                            continue;
                        }

                        File.Copy(template.SourceFullPath, destFile, true);
                        successfulCopiesInGroup++;
                        Interlocked.Increment(ref _totalCopiedCount);
                    }

                    Interlocked.Add(ref _currentBufferCount, successfulCopiesInGroup);
                    groupIndex = (groupIndex + 1) % groupedTemplates.Count;
                }
                catch (IOException ex) when ((uint)ex.HResult == 0x80070020)
                {
                    Interlocked.Increment(ref _totalErrorCount);
                    logAction?.Invoke($"[WARNING] 파일 공유 위반(Sharing Violation): {ex.Message}", WinColor.Tomato);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _totalErrorCount);
                    logAction?.Invoke($"[ERROR] 파일 I/O 예외: {ex.Message}", WinColor.Tomato);
                }

                if (intervalMs > 0) await Task.Delay(intervalMs, token).ConfigureAwait(false);
                else await Task.Yield();
            }
        }

        private string GetGroupKey(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            int lastIdx = name.LastIndexOf('_');
            if (lastIdx > 0) return name.Substring(0, lastIdx);
            return name;
        }

        private void SyncBufferCountHard(string folderPath)
        {
            try
            {
                int actualFileCount = Directory.EnumerateFiles(folderPath).Count();
                Interlocked.Exchange(ref _currentBufferCount, actualFileCount);
            }
            catch { }
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
            Interlocked.Exchange(ref _totalErrorCount, 0);
            Interlocked.Exchange(ref _currentBufferCount, 0);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _fileWatcher?.Dispose();
                _isDisposed = true;
            }
        }
    }

    // ==========================================
    // 3. UI 로직 (MainForm) 
    // ==========================================
    public partial class MainForm : Form
    {
        private TextBox _txtSourceFolder;
        private TextBox _txtOutputFolder;
        private NumericUpDown _numInterval;
        private NumericUpDown _numMinBuffer;
        private NumericUpDown _numMaxBuffer;
        private CheckBox _chkSimulateMidnight;
        private CheckBox _chkSyncCameras;
        private CheckBox _chkSimpleRawCopy; // 🌟 신규 옵션 UI

        private Button _btnSelectSource;
        private Button _btnSelectOutput;
        private Button _btnStartStop;
        private RichTextBox _rtbLog;
        private WinLabel _lblStatus;
        private System.Windows.Forms.Timer _uiUpdateTimer;

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

                if (!_chkSimpleRawCopy.Checked && _numMinBuffer.Value >= _numMaxBuffer.Value)
                {
                    MessageBox.Show("최소 유지 개수(Min)는 최대 허용 개수(Max)보다 반드시 작아야 합니다.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    // 🌟 비동기 연산 진입 (Task 완료 시 1회전 자동 종료 기능 지원)
                    await _simulatorCore.StartSimulationAsync(
                        _txtSourceFolder.Text,
                        _txtOutputFolder.Text,
                        (int)_numInterval.Value,
                        (int)_numMinBuffer.Value,
                        (int)_numMaxBuffer.Value,
                        _chkSimulateMidnight.Checked,
                        _chkSyncCameras.Checked,
                        _chkSimpleRawCopy.Checked, // 🌟 신규 파라미터 전달
                        AppendLog,
                        _cts.Token
                    );
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    AppendLog($"[FATAL] 시스템 오류: {ex.Message}", WinColor.Tomato);
                }
                finally
                {
                    // 🌟 1회전이 끝나서 Task가 종료되면 자동으로 UI 버튼이 원상 복구됨 (Auto-Reset)
                    _isRunning = false;
                    _uiUpdateTimer.Stop();
                    ToggleUiState();
                    AppendLog("=== 시뮬레이션 종료 ===", WinColor.Orange);
                }
            }
            else
            {
                AppendLog("정지 신호를 전송했습니다. 대기 중...", WinColor.Yellow);
                _cts?.Cancel();
            }
        }

        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            long copied = _simulatorCore.TotalCopiedCount;
            long errors = _simulatorCore.TotalErrorCount;
            _lblStatus.Text = $"누적 복사: {copied:N0} 회 | 발생 에러: {errors:N0} 건";
        }

        private void AppendLog(string text, WinColor color)
        {
            if (InvokeRequired) { Invoke(new Action(() => AppendLog(text, color))); return; }
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}\n");
            _rtbLog.ScrollToCaret();
        }

        // 🌟 신규 옵션 체크 시 관련 없는 UI 컨트롤 비활성화 처리
        private void ChkSimpleRawCopy_CheckedChanged(object sender, EventArgs e)
        {
            bool isRaw = _chkSimpleRawCopy.Checked;
            _numMinBuffer.Enabled = !isRaw && !_isRunning;
            _numMaxBuffer.Enabled = !isRaw && !_isRunning;
            _chkSimulateMidnight.Enabled = !isRaw && !_isRunning;
            _chkSyncCameras.Enabled = !isRaw && !_isRunning;

            if (isRaw) AppendLog("💡 [원본 복사 모드] 버퍼(Min/Max) 및 이름 변경을 무시하고 폴더 내 파일을 1회 복사 후 종료합니다.", WinColor.LightSkyBlue);
        }

        private void ToggleUiState()
        {
            if (InvokeRequired) { Invoke(new Action(ToggleUiState)); return; }

            _txtSourceFolder.Enabled = !_isRunning;
            _txtOutputFolder.Enabled = !_isRunning;
            _numInterval.Enabled = !_isRunning;

            // 단순 모드가 아닐 때만 버퍼 컨트롤 활성화
            bool isRaw = _chkSimpleRawCopy.Checked;
            _numMinBuffer.Enabled = !_isRunning && !isRaw;
            _numMaxBuffer.Enabled = !_isRunning && !isRaw;
            _chkSimulateMidnight.Enabled = !_isRunning && !isRaw;
            _chkSyncCameras.Enabled = !_isRunning && !isRaw;

            _chkSimpleRawCopy.Enabled = !_isRunning;
            _btnSelectSource.Enabled = !_isRunning;
            _btnSelectOutput.Enabled = !_isRunning;

            if (_isRunning)
            {
                _btnStartStop.Text = "STOP SIMULATION";
                _btnStartStop.BackColor = WinColor.FromArgb(204, 74, 74);
            }
            else
            {
                _btnStartStop.Text = "START CONTINUOUS SIMULATION";
                _btnStartStop.BackColor = WinColor.FromArgb(0, 122, 204);
            }
        }

        private void InitializeDarkUI()
        {
            this.Text = "24/7 OCR Image Simulator Pro [Hybrid Mode Edition]";
            this.Size = new Size(680, 640); // 🌟 신규 체크박스 공간 40px 확장
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            var lblSource = new WinLabel { Text = "Input Dir:", Location = new Point(20, yPos + 4), Size = new Size(110, 20) };
            _txtSourceFolder = new TextBox { Location = new Point(130, yPos), Size = new Size(420, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };
            _btnSelectSource = new Button { Text = "BROWSE", Location = new Point(560, yPos - 1), Size = new Size(80, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectSource.FlatAppearance.BorderSize = 0;
            _btnSelectSource.Click += BtnSelectSource_Click;
            this.Controls.Add(lblSource); this.Controls.Add(_txtSourceFolder); this.Controls.Add(_btnSelectSource);

            yPos += 40;

            var lblOutput = new WinLabel { Text = "Output Dir:", Location = new Point(20, yPos + 4), Size = new Size(110, 20), ForeColor = WinColor.LightGreen };
            _txtOutputFolder = new TextBox { Location = new Point(130, yPos), Size = new Size(420, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };
            _btnSelectOutput = new Button { Text = "BROWSE", Location = new Point(560, yPos - 1), Size = new Size(80, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectOutput.FlatAppearance.BorderSize = 0;
            _btnSelectOutput.Click += BtnSelectOutput_Click;
            this.Controls.Add(lblOutput); this.Controls.Add(_txtOutputFolder); this.Controls.Add(_btnSelectOutput);

            yPos += 45;

            var lblInterval = new WinLabel { Text = "Interval(ms):", Location = new Point(20, yPos + 4), Size = new Size(110, 20), ForeColor = WinColor.LightSkyBlue };
            _numInterval = new NumericUpDown { Location = new Point(130, yPos), Size = new Size(80, 23), Minimum = 0, Maximum = 60000, Value = 500, BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };

            var lblMin = new WinLabel { Text = "재개(Min):", Location = new Point(230, yPos + 4), Size = new Size(80, 20), ForeColor = WinColor.LightGreen };
            _numMinBuffer = new NumericUpDown { Location = new Point(310, yPos), Size = new Size(80, 23), Minimum = 1, Maximum = 10000, Value = 10, BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };

            var lblMax = new WinLabel { Text = "정지(Max):", Location = new Point(410, yPos + 4), Size = new Size(80, 20), ForeColor = WinColor.Tomato };
            _numMaxBuffer = new NumericUpDown { Location = new Point(490, yPos), Size = new Size(80, 23), Minimum = 5, Maximum = 10000, Value = 30, BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };

            this.Controls.Add(lblInterval); this.Controls.Add(_numInterval);
            this.Controls.Add(lblMin); this.Controls.Add(_numMinBuffer);
            this.Controls.Add(lblMax); this.Controls.Add(_numMaxBuffer);

            yPos += 45;

            _chkSimulateMidnight = new CheckBox
            {
                Text = "🔥 자정 날짜 폴더 스위칭(Rollover) 에러 유발 모사 활성화",
                Location = new Point(130, yPos),
                Size = new Size(500, 24),
                ForeColor = WinColor.Gold,
                Checked = true
            };
            this.Controls.Add(_chkSimulateMidnight);

            yPos += 30;

            _chkSyncCameras = new CheckBox
            {
                Text = "🎥 멀티 카메라 동기화 모드 (동일 ID 파일명 일괄 병렬 복사)",
                Location = new Point(130, yPos),
                Size = new Size(500, 24),
                ForeColor = WinColor.LightSkyBlue,
                Checked = true
            };
            this.Controls.Add(_chkSyncCameras);

            yPos += 30;

            // 🌟 신규: 단순 복사 모드 체크박스 추가
            _chkSimpleRawCopy = new CheckBox
            {
                Text = "📁 원본 파일 단순 복사 모드 (버퍼 제어 무시, Interval 간격 복사 후 자동 종료)",
                Location = new Point(130, yPos),
                Size = new Size(520, 24),
                ForeColor = WinColor.LimeGreen,
                Checked = false
            };
            _chkSimpleRawCopy.CheckedChanged += ChkSimpleRawCopy_CheckedChanged;
            this.Controls.Add(_chkSimpleRawCopy);

            yPos += 40;

            _btnStartStop = new Button { Text = "START CONTINUOUS SIMULATION", Location = new Point(20, yPos), Size = new Size(620, 50), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 12F, FontStyle.Bold) };
            _btnStartStop.FlatAppearance.BorderSize = 0;
            _btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(_btnStartStop);

            yPos += 65;

            _lblStatus = new WinLabel { Text = "누적 복사: 0 회 | 발생 에러: 0 건", Location = new Point(20, yPos), Size = new Size(620, 30), Font = new Font("Consolas", 12F, FontStyle.Bold), ForeColor = WinColor.Cyan, TextAlign = ContentAlignment.MiddleCenter };
            this.Controls.Add(_lblStatus);

            yPos += 35;

            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(620, 130), BackColor = WinColor.FromArgb(18, 18, 18), ForeColor = WinColor.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }
}