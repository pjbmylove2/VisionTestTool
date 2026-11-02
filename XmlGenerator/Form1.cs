using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ookii.Dialogs.WinForms;

namespace MachineVision.Tool.XmlGenerator
{
    // ==========================================
    // [인터페이스 추상화] 의존성 분리
    // ==========================================
    public interface IImageTagExtractor
    {
        Task ProcessAsync(string sourceFolder, string outputFile, Action<string, Color> logAction,
                          IProgress<int> progress, CancellationToken token);
    }

    // ==========================================
    // 1. 비즈니스 로직 (스트리밍 기반 안전한 파일 탐색기)
    // ==========================================
    public class ImageTagExtractorCore : IImageTagExtractor
    {
        // 읽기 전용 해시셋으로 검색 성능 O(1) 달성
        private readonly HashSet<string> _supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
        };

        public async Task ProcessAsync(string sourceFolder, string outputFile, Action<string, Color> logAction,
                                       IProgress<int> progress, CancellationToken token)
        {
            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException("원본 폴더를 찾을 수 없습니다.");

            logAction?.Invoke("하위 폴더를 포함하여 안전한 이미지 탐색을 시작합니다...", Color.WhiteSmoke);

            int totalCount = 0;

            // [핵심 1] FileStream과 StreamWriter를 연결하여 메모리 누수 없이 하드디스크에 다이렉트 기록
            using (var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            using (var writer = new StreamWriter(fs))
            {
                // [핵심 2] yield return을 통해 메모리에 배열을 만들지 않고 하나씩 스트리밍
                foreach (string file in SafeEnumerateFiles(sourceFolder))
                {
                    token.ThrowIfCancellationRequested(); // 사용자 정지 신호 감지

                    // 요구하신 정확한 XML 태그 형식으로 문자열 기록
                    string xmlLine = $"    <string>{file}</string>";
                    await writer.WriteLineAsync(xmlLine).ConfigureAwait(false);

                    totalCount++;

                    // UI 스레드 보호를 위한 스로틀링 (1000개마다 한 번만 UI 업데이트)
                    if (totalCount % 1000 == 0)
                    {
                        progress?.Report(totalCount);
                    }
                }
            }

            progress?.Report(totalCount); // 최종 개수 업데이트

            logAction?.Invoke($"\n=== [추출 완료] ===", Color.Gold);
            logAction?.Invoke($"- 총 발견 및 변환된 이미지: {totalCount:N0} 개", Color.LimeGreen);
            logAction?.Invoke($"- 저장 완료: {Path.GetFileName(outputFile)}", Color.LightGray);
        }

        // =========================================================================
        // 🌟 [방어적 코딩] 권한 잠금(UnauthorizedAccessException)을 우회하는 재귀 탐색기
        // =========================================================================
        private IEnumerable<string> SafeEnumerateFiles(string path)
        {
            IEnumerable<string> files = null;
            try
            {
                // 해당 폴더의 파일 목록 획득 시도
                files = Directory.EnumerateFiles(path);
            }
            catch
            {
                // 접근 권한이 없거나 잠긴 폴더는 무시하고 패스 (프로그램 뻗음 방지)
            }

            if (files != null)
            {
                foreach (var f in files)
                {
                    if (_supportedExts.Contains(Path.GetExtension(f)))
                        yield return f;
                }
            }

            IEnumerable<string> dirs = null;
            try
            {
                // 하위 폴더 목록 획득 시도
                dirs = Directory.EnumerateDirectories(path);
            }
            catch
            {
                // 권한 없는 폴더 무시
            }

            if (dirs != null)
            {
                foreach (var d in dirs)
                {
                    // 재귀 호출 (Recursive Call)
                    foreach (var f in SafeEnumerateFiles(d))
                        yield return f;
                }
            }
        }
    }

    // ==========================================
    // 2. UI 로직 (MainForm) - 스레드 안전성 보장
    // ==========================================
    public class MainForm : Form
    {
        private TextBox _txtSourceFolder;
        private TextBox _txtOutputFile;
        private Button _btnSelectSource;
        private Button _btnSelectOutput;
        private Button _btnStartStop;
        private RichTextBox _rtbLog;
        private Label _lblStatus;

        private readonly IImageTagExtractor _extractorCore;
        private CancellationTokenSource _cts;
        private bool _isRunning = false;

        public MainForm()
        {
            _extractorCore = new ImageTagExtractorCore();
            InitializeDarkUI();
            this.FormClosing += (s, e) => _cts?.Cancel();
        }

        private void BtnSelectSource_Click(object sender, EventArgs e)
        {
            using (var dialog = new VistaFolderBrowserDialog { Description = "Select Input Folder", UseDescriptionForTitle = true })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _txtSourceFolder.Text = dialog.SelectedPath;
                    // 자동으로 바탕화면에 Output.txt 경로 추천
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    _txtOutputFile.Text = Path.Combine(desktopPath, "Simulation_ImageList.txt");
                }
            }
        }

        private void BtnSelectOutput_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog { Filter = "Text Files (*.txt)|*.txt", Title = "Save Output Text File" })
            {
                if (sfd.ShowDialog() == DialogResult.OK) _txtOutputFile.Text = sfd.FileName;
            }
        }

        private async void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                if (string.IsNullOrWhiteSpace(_txtSourceFolder.Text) || string.IsNullOrWhiteSpace(_txtOutputFile.Text))
                {
                    MessageBox.Show("원본 폴더와 저장할 텍스트 파일 경로를 지정하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _isRunning = true;
                ToggleUiState();
                _rtbLog.Clear();
                _cts = new CancellationTokenSource();

                // IProgress<T>를 활용한 모던하고 안전한 UI 업데이트 (Invoke 불필요)
                var progressIndicator = new Progress<int>(count =>
                {
                    _lblStatus.Text = $"현재 추출된 파일: {count:N0} 개";
                });

                AppendLog("하위 폴더 탐색 및 XML 태그 추출을 시작합니다...", Color.Gold);

                try
                {
                    await _extractorCore.ProcessAsync(
                        _txtSourceFolder.Text,
                        _txtOutputFile.Text,
                        AppendLog,
                        progressIndicator,
                        _cts.Token
                    );
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AppendLog($"[FATAL] 시스템 오류: {ex.Message}", Color.Tomato);
                }
                finally
                {
                    _isRunning = false;
                    ToggleUiState();
                    if (_cts.IsCancellationRequested) AppendLog("=== 작업이 취소되었습니다 ===", Color.Orange);
                }
            }
            else
            {
                AppendLog("정지 신호를 전송했습니다. 대기 중...", Color.Yellow);
                _cts?.Cancel();
            }
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
            _txtOutputFile.Enabled = !_isRunning;
            _btnSelectSource.Enabled = !_isRunning;
            _btnSelectOutput.Enabled = !_isRunning;

            if (_isRunning)
            {
                _btnStartStop.Text = "STOP EXTRACTION";
                _btnStartStop.BackColor = Color.FromArgb(204, 74, 74);
            }
            else
            {
                _btnStartStop.Text = "START EXTRACTION";
                _btnStartStop.BackColor = Color.FromArgb(0, 122, 204);
            }
        }

        private void InitializeDarkUI()
        {
            this.Text = "Vision Path Tag Extractor Pro";
            this.Size = new Size(680, 420);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            var lblSource = new Label { Text = "Root Dir:", Location = new Point(20, yPos + 4), Size = new Size(100, 20) };
            _txtSourceFolder = new TextBox { Location = new Point(120, yPos), Size = new Size(430, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnSelectSource = new Button { Text = "BROWSE", Location = new Point(560, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectSource.FlatAppearance.BorderSize = 0;
            _btnSelectSource.Click += BtnSelectSource_Click;
            this.Controls.Add(lblSource); this.Controls.Add(_txtSourceFolder); this.Controls.Add(_btnSelectSource);

            yPos += 40;

            var lblOutput = new Label { Text = "Output TXT:", Location = new Point(20, yPos + 4), Size = new Size(100, 20), ForeColor = Color.LightGreen };
            _txtOutputFile = new TextBox { Location = new Point(120, yPos), Size = new Size(430, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnSelectOutput = new Button { Text = "BROWSE", Location = new Point(560, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectOutput.FlatAppearance.BorderSize = 0;
            _btnSelectOutput.Click += BtnSelectOutput_Click;
            this.Controls.Add(lblOutput); this.Controls.Add(_txtOutputFile); this.Controls.Add(_btnSelectOutput);

            yPos += 50;

            _btnStartStop = new Button { Text = "START EXTRACTION", Location = new Point(20, yPos), Size = new Size(620, 50), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 12F, FontStyle.Bold) };
            _btnStartStop.FlatAppearance.BorderSize = 0;
            _btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(_btnStartStop);

            yPos += 65;

            _lblStatus = new Label { Text = "현재 추출된 파일: 0 개", Location = new Point(20, yPos), Size = new Size(620, 30), Font = new Font("Consolas", 14F, FontStyle.Bold), ForeColor = Color.Cyan, TextAlign = ContentAlignment.MiddleCenter };
            this.Controls.Add(_lblStatus);

            yPos += 35;

            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(620, 120), BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }
}