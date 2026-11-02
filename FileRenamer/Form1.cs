using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ookii.Dialogs.WinForms; // 현업 표준 다이얼로그 라이브러리

namespace MachineVision.Tool.FileRenamer
{
    // ==========================================
    // 1. 비즈니스 로직 (순수 파일 복사 및 이름 변경기)
    // ==========================================
    public class FileRenamerCore
    {
        public async Task ProcessAsync(string sourceFolder, string outputFolder, string findText, string replaceText,
                                       Action<string, Color> logAction, Action<int> progressAction)
        {
            if (!Directory.Exists(sourceFolder)) throw new DirectoryNotFoundException("원본 폴더를 찾을 수 없습니다.");
            if (string.IsNullOrEmpty(findText)) throw new ArgumentException("찾을 문자열을 입력해야 합니다.");

            // Output 폴더가 없으면 자동 생성
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                logAction?.Invoke($"Output 폴더를 생성했습니다: {outputFolder}", Color.LightGray);
            }

            // 원본 폴더에서 조건(findText)에 맞는 파일만 필터링하여 가져옴 (LINQ 활용)
            var targetFiles = Directory.GetFiles(sourceFolder)
                                       .Where(f => Path.GetFileName(f).Contains(findText))
                                       .ToList();

            if (targetFiles.Count == 0)
            {
                logAction?.Invoke($"'{findText}' 문자열이 포함된 파일이 없습니다.", Color.Orange);
                return;
            }

            int totalFiles = targetFiles.Count;
            int processedCount = 0;
            int successCount = 0;

            logAction?.Invoke($"총 {totalFiles}개의 매칭 파일 복사 및 이름 변경 시작...", Color.WhiteSmoke);

            // 비동기 백그라운드 스레드에서 파일 I/O 수행 (UI 프리징 방지)
            await Task.Run(() =>
            {
                foreach (string sourceFile in targetFiles)
                {
                    string originalFileName = Path.GetFileName(sourceFile);
                    string newFileName = originalFileName.Replace(findText, replaceText);
                    string destFile = Path.Combine(outputFolder, newFileName);

                    try
                    {
                        // File.Copy는 OS 레벨에서 처리되므로 C# 관리 힙(LOH) 메모리를 소모하지 않음
                        // true 플래그를 주어 Output 폴더에 이미 파일이 있으면 덮어쓰기 허용
                        File.Copy(sourceFile, destFile, true);

                        successCount++;
                        logAction?.Invoke($"[OK] {originalFileName}  ->  {newFileName}", Color.LightGreen);
                    }
                    catch (Exception ex)
                    {
                        logAction?.Invoke($"[ERROR] 복사 실패 ({originalFileName}): {ex.Message}", Color.Tomato);
                    }

                    processedCount++;

                    // 진행률 업데이트 (UI 스레드 부하 방지를 위해 잦은 업데이트 스로틀링)
                    if (processedCount % 5 == 0 || processedCount == totalFiles)
                    {
                        progressAction?.Invoke((int)((processedCount / (double)totalFiles) * 100));
                    }
                }
            }).ConfigureAwait(false);

            progressAction?.Invoke(100);
            logAction?.Invoke($"\n=== [작업 완료] ===", Color.Gold);
            logAction?.Invoke($"- 처리된 파일: {successCount} / {totalFiles} 개", Color.LimeGreen);
        }
    }

    // ==========================================
    // 2. UI 로직 (MainForm)
    // ==========================================
    public class MainForm : Form
    {
        private TextBox _txtSourceFolder;
        private TextBox _txtOutputFolder;
        private TextBox _txtFindText;
        private TextBox _txtReplaceText;

        private Button _btnSelectSource;
        private Button _btnSelectOutput;
        private Button _btnStart;

        private RichTextBox _rtbLog;
        private ProgressBar _progressBar;

        private readonly FileRenamerCore _renamerCore;

        public MainForm()
        {
            _renamerCore = new FileRenamerCore();
            InitializeDarkUI();
        }

        private void BtnSelectSource_Click(object sender, EventArgs e)
        {
            using (VistaFolderBrowserDialog dialog = new VistaFolderBrowserDialog())
            {
                dialog.Description = "Select Source Folder";
                dialog.UseDescriptionForTitle = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _txtSourceFolder.Text = dialog.SelectedPath;
                    AppendLog($"원본 폴더 선택됨: {dialog.SelectedPath}", Color.LightGray);
                }
            }
        }

        private void BtnSelectOutput_Click(object sender, EventArgs e)
        {
            using (VistaFolderBrowserDialog dialog = new VistaFolderBrowserDialog())
            {
                dialog.Description = "Select Output Folder";
                dialog.UseDescriptionForTitle = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _txtOutputFolder.Text = dialog.SelectedPath;
                    AppendLog($"출력 폴더 선택됨: {dialog.SelectedPath}", Color.LightGray);
                }
            }
        }

        private async void BtnStart_Click(object sender, EventArgs e)
        {
            string sourceFolder = _txtSourceFolder.Text;
            string outputFolder = _txtOutputFolder.Text;
            string findText = _txtFindText.Text;
            string replaceText = _txtReplaceText.Text;

            if (string.IsNullOrWhiteSpace(sourceFolder) || string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show("원본 및 출력 폴더 경로를 모두 지정하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetUiState(false);
            _rtbLog.Clear();
            _progressBar.Value = 0;

            try
            {
                await _renamerCore.ProcessAsync(sourceFolder, outputFolder, findText, replaceText, AppendLog, UpdateProgress);
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] 시스템 예외 발생: {ex.Message}", Color.Tomato);
            }
            finally
            {
                SetUiState(true);
            }
        }

        // ==========================================
        // 스레드 안전 UI 업데이트 메서드
        // ==========================================
        private void AppendLog(string text, Color color)
        {
            if (InvokeRequired) { Invoke(new Action(() => AppendLog(text, color))); return; }
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            _rtbLog.ScrollToCaret();
        }

        private void UpdateProgress(int value)
        {
            if (InvokeRequired) { Invoke(new Action(() => UpdateProgress(value))); return; }
            _progressBar.Value = Math.Min(100, Math.Max(0, value));
        }

        private void SetUiState(bool isEnabled)
        {
            if (InvokeRequired) { Invoke(new Action(() => SetUiState(isEnabled))); return; }
            _txtSourceFolder.Enabled = isEnabled;
            _txtOutputFolder.Enabled = isEnabled;
            _txtFindText.Enabled = isEnabled;
            _txtReplaceText.Enabled = isEnabled;
            _btnSelectSource.Enabled = isEnabled;
            _btnSelectOutput.Enabled = isEnabled;
            _btnStart.Enabled = isEnabled;
        }

        // ==========================================
        // UI 유틸리티 및 다크 테마 구성
        // ==========================================
        private void InitializeDarkUI()
        {
            this.Text = "Vision Batch File Copy & Renamer Pro";
            this.Size = new Size(650, 520);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            // Source Folder UI
            var lblSource = new Label { Text = "Source Dir:", Location = new Point(20, yPos + 4), Size = new Size(110, 20) };
            _txtSourceFolder = new TextBox { Location = new Point(130, yPos), Size = new Size(390, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnSelectSource = new Button { Text = "BROWSE", Location = new Point(530, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectSource.FlatAppearance.BorderSize = 0;
            _btnSelectSource.Click += BtnSelectSource_Click;
            this.Controls.Add(lblSource); this.Controls.Add(_txtSourceFolder); this.Controls.Add(_btnSelectSource);

            yPos += 40;

            // Output Folder UI
            var lblOutput = new Label { Text = "Output Dir:", Location = new Point(20, yPos + 4), Size = new Size(110, 20) };
            _txtOutputFolder = new TextBox { Location = new Point(130, yPos), Size = new Size(390, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnSelectOutput = new Button { Text = "BROWSE", Location = new Point(530, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectOutput.FlatAppearance.BorderSize = 0;
            _btnSelectOutput.Click += BtnSelectOutput_Click;
            this.Controls.Add(lblOutput); this.Controls.Add(_txtOutputFolder); this.Controls.Add(_btnSelectOutput);

            yPos += 45;

            // Find Text UI
            var lblFind = new Label { Text = "Target String:", Location = new Point(20, yPos + 4), Size = new Size(130, 20), ForeColor = Color.LightCoral };
            _txtFindText = new TextBox { Location = new Point(150, yPos), Size = new Size(460, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            this.Controls.Add(lblFind); this.Controls.Add(_txtFindText);

            yPos += 35;

            // Replace Text UI
            var lblReplace = new Label { Text = "Replace With:", Location = new Point(20, yPos + 4), Size = new Size(130, 20), ForeColor = Color.LightGreen };
            _txtReplaceText = new TextBox { Location = new Point(150, yPos), Size = new Size(460, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            this.Controls.Add(lblReplace); this.Controls.Add(_txtReplaceText);

            yPos += 50;

            // Start Button
            _btnStart = new Button { Text = "COPY & RENAME FILES", Location = new Point(20, yPos), Size = new Size(590, 40), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 11F, FontStyle.Bold) };
            _btnStart.FlatAppearance.BorderSize = 0;
            _btnStart.Click += BtnStart_Click;
            this.Controls.Add(_btnStart);

            yPos += 55;

            // 🌟 [핵심 변경점] 모호한 참조 해결을 위해 명시적으로 System.Windows.Forms 지정
            _progressBar = new ProgressBar { Location = new Point(20, yPos), Size = new Size(590, 10), Style = System.Windows.Forms.ProgressBarStyle.Continuous };
            this.Controls.Add(_progressBar);

            yPos += 20;

            // Log TextBox
            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(590, 170), BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }
}