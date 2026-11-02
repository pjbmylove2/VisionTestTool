using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Tool.DatasetExtractor
{
    // ==========================================
    // 1. 데이터 모델 및 Enum
    // ==========================================
    public enum SortDirection
    {
        Ascending,
        Descending
    }

    public enum ExtractionStage
    {
        Scanning,
        Sorting,
        Saving
    }

    public class ExtractionProgress
    {
        public ExtractionStage Stage { get; set; }
        public int ProcessedFiles { get; set; }
        public int TotalFiles { get; set; }
    }

    public class ExtractionSummary
    {
        public int TotalFilesCount { get; set; }
        public long TotalObjectsCount { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string OutputFilePath { get; set; }
    }

    internal class FileLabelData
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public Dictionary<int, int> ClassCounts { get; set; } = new Dictionary<int, int>();
        public int TotalObjects { get; set; }
    }

    // ==========================================
    // 2. 비즈니스 로직 (🚀 대용량 I/O 및 고속 파싱 엔진)
    // ==========================================
    public class DatasetExtractorCore
    {
        public async Task<ExtractionSummary> ExtractDatasetCsvAsync(
            string targetDirectory,
            string outputFilePath,
            SortDirection sortDir,
            IProgress<ExtractionProgress> progress,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                throw new DirectoryNotFoundException($"대상 폴더를 찾을 수 없습니다: {targetDirectory}");
            }

            var stopwatch = Stopwatch.StartNew();

            return await Task.Run(() =>
            {
                // 🌟 [최적화 1] 지연 평가(Lazy Evaluation)를 통한 메모리 파편화 방지
                IEnumerable<string> fileEnumerable;
                try
                {
                    fileEnumerable = Directory.EnumerateFiles(targetDirectory, "*.txt", SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InvalidOperationException($"폴더 접근 권한이 없습니다: {ex.Message}", ex);
                }

                var fileDataList = new List<FileLabelData>(10000);
                var allClassIds = new HashSet<int>();
                int scannedCount = 0;

                // 1차 스캔: 각 텍스트 파일 파싱 및 클래스 ID 집계
                foreach (string filePath in fileEnumerable)
                {
                    token.ThrowIfCancellationRequested();

                    var fileData = new FileLabelData
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath)
                    };

                    ParseLabelFile(filePath, fileData, allClassIds, token);
                    fileDataList.Add(fileData);
                    scannedCount++;

                    if (scannedCount % 1000 == 0)
                    {
                        progress?.Report(new ExtractionProgress
                        {
                            Stage = ExtractionStage.Scanning,
                            ProcessedFiles = scannedCount
                        });
                    }
                }

                if (fileDataList.Count == 0)
                {
                    return new ExtractionSummary
                    {
                        TotalFilesCount = 0,
                        TotalObjectsCount = 0,
                        ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                        OutputFilePath = outputFilePath
                    };
                }

                // 2차 정렬: 파일명 기준 오름차순/내림차순 정렬
                progress?.Report(new ExtractionProgress
                {
                    Stage = ExtractionStage.Sorting,
                    ProcessedFiles = fileDataList.Count,
                    TotalFiles = fileDataList.Count
                });

                if (sortDir == SortDirection.Ascending)
                {
                    fileDataList.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.FileName, b.FileName));
                }
                else
                {
                    fileDataList.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(b.FileName, a.FileName));
                }

                // 클래스 ID 목록 정렬 (클래스 0, 1, 2, ...)
                var sortedClassIds = allClassIds.OrderBy(id => id).ToList();

                // 3차 저장: 원자적 쓰기(Atomic Write)를 위한 임시 파일(.tmp) 사용
                string tempFilePath = $"{outputFilePath}.{Guid.NewGuid():N}.tmp";
                long totalObjectsSum = 0;

                try
                {
                    progress?.Report(new ExtractionProgress
                    {
                        Stage = ExtractionStage.Saving,
                        ProcessedFiles = 0,
                        TotalFiles = fileDataList.Count
                    });

                    using (var fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.SequentialScan))
                    using (var writer = new StreamWriter(fs, new UTF8Encoding(true)))
                    {
                        // CSV 헤더 작성: 파일명,전체개수,클래스_0,클래스_1,...
                        writer.Write("파일명,전체개수");
                        foreach (int classId in sortedClassIds)
                        {
                            writer.Write($",클래스_{classId}");
                        }
                        writer.WriteLine();

                        for (int i = 0; i < fileDataList.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();

                            var data = fileDataList[i];
                            totalObjectsSum += data.TotalObjects;

                            writer.Write(EscapeCsv(data.FileName));
                            writer.Write(',');
                            writer.Write(data.TotalObjects.ToString(CultureInfo.InvariantCulture));

                            foreach (int classId in sortedClassIds)
                            {
                                data.ClassCounts.TryGetValue(classId, out int classCount);
                                writer.Write(',');
                                writer.Write(classCount.ToString(CultureInfo.InvariantCulture));
                            }
                            writer.WriteLine();

                            if ((i + 1) % 1000 == 0 || i + 1 == fileDataList.Count)
                            {
                                progress?.Report(new ExtractionProgress
                                {
                                    Stage = ExtractionStage.Saving,
                                    ProcessedFiles = i + 1,
                                    TotalFiles = fileDataList.Count
                                });
                            }
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    if (File.Exists(outputFilePath))
                    {
                        File.Delete(outputFilePath);
                    }
                    File.Move(tempFilePath, outputFilePath);
                }
                finally
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }

                stopwatch.Stop();

                return new ExtractionSummary
                {
                    TotalFilesCount = fileDataList.Count,
                    TotalObjectsCount = totalObjectsSum,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    OutputFilePath = outputFilePath
                };
            }, token);
        }

        private static void ParseLabelFile(string filePath, FileLabelData fileData, HashSet<int> allClassIds, CancellationToken token)
        {
            using (var reader = new StreamReader(filePath, Encoding.UTF8, true, 4096))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // 🌟 [최적화 2] Split() 할당을 방지하기 위한 고속 인덱스 파싱
                    int start = 0;
                    while (start < line.Length && char.IsWhiteSpace(line[start]))
                    {
                        start++;
                    }

                    if (start >= line.Length) continue;

                    int end = start;
                    while (end < line.Length && !char.IsWhiteSpace(line[end]))
                    {
                        end++;
                    }

                    string firstToken = line.Substring(start, end - start);
                    if (int.TryParse(firstToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
                    {
                        if (fileData.ClassCounts.ContainsKey(classId))
                        {
                            fileData.ClassCounts[classId]++;
                        }
                        else
                        {
                            fileData.ClassCounts[classId] = 1;
                        }

                        fileData.TotalObjects++;
                        allClassIds.Add(classId);
                    }
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }

    // ==========================================
    // 3. UI 로직 (MainForm)
    // ==========================================
    public partial class MainForm : Form
    {
        private TextBox _txtTargetFolder;
        private Button _btnSelectFolder;
        private RadioButton _rdoAscending;
        private RadioButton _rdoDescending;
        private Button _btnExtract;
        private ProgressBar _progressBar;
        private WinLabel _lblStatus;
        private RichTextBox _rtbLog;

        private readonly DatasetExtractorCore _extractorCore;
        private CancellationTokenSource _cts;
        private bool _isProcessing;

        public MainForm()
        {
            _extractorCore = new DatasetExtractorCore();
            InitializeDarkUI();
        }

        private void BtnSelectFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "YOLO 라벨 TXT 파일이 있는 폴더를 선택하세요.";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _txtTargetFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private async void BtnExtract_Click(object sender, EventArgs e)
        {
            if (_isProcessing)
            {
                AppendLog("작업 취소를 요청합니다...", WinColor.Yellow);
                _cts?.Cancel();
                _btnExtract.Enabled = false;
                return;
            }

            string targetDirectory = _txtTargetFolder.Text.Trim();

            if (!Directory.Exists(targetDirectory))
            {
                MessageBox.Show("유효한 대상 폴더를 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outputFilePath = Path.Combine(desktopPath, $"DatasetSummary_{timeStamp}.csv");

            SortDirection sortDir = _rdoAscending.Checked ? SortDirection.Ascending : SortDirection.Descending;

            _cts = new CancellationTokenSource();
            _isProcessing = true;
            ToggleUiState();

            _progressBar.Style = ProgressBarStyle.Marquee;
            _lblStatus.Text = "상태: 라벨 TXT 파일 스캔 중...";
            _rtbLog.Clear();

            AppendLog($"[{sortDir}] 데이터셋 클래스 개수 집계 및 CSV 추출을 시작합니다.", WinColor.LimeGreen);

            var progress = new Progress<ExtractionProgress>(value =>
            {
                if (!_isProcessing) return;

                if (value.Stage == ExtractionStage.Scanning)
                {
                    _lblStatus.Text = $"상태: TXT 파일 스캔 중... ({value.ProcessedFiles:N0}개 분석)";
                }
                else if (value.Stage == ExtractionStage.Sorting)
                {
                    _lblStatus.Text = "상태: 파일명 정렬 중...";
                    _progressBar.Style = ProgressBarStyle.Marquee;
                }
                else if (value.Stage == ExtractionStage.Saving)
                {
                    if (_progressBar.Style != ProgressBarStyle.Continuous)
                    {
                        _progressBar.Style = ProgressBarStyle.Continuous;
                        _progressBar.Value = 0;
                    }

                    int percent = value.TotalFiles == 0 ? 100 : (int)((long)value.ProcessedFiles * 100 / value.TotalFiles);
                    _progressBar.Value = Math.Max(0, Math.Min(100, percent));
                    _lblStatus.Text = $"상태: CSV 저장 중... ({value.ProcessedFiles:N0}/{value.TotalFiles:N0})";
                }
            });

            try
            {
                ExtractionSummary summary = await _extractorCore.ExtractDatasetCsvAsync(
                    targetDirectory,
                    outputFilePath,
                    sortDir,
                    progress,
                    _cts.Token);

                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = 100;
                _lblStatus.Text = $"상태: 완료 ({summary.TotalFilesCount:N0}개 파일)";

                AppendLog($"✔ 완료! TXT 파일 {summary.TotalFilesCount:N0}개, 총 객체" + "{summary.TotalObjectsCount:N0개를 집계했습니다.", WinColor.Cyan);
                AppendLog($"⏱ 소요 시간: {summary.ElapsedMilliseconds:N0} ms", WinColor.LightGray);
                AppendLog($"📁 CSV 저장 위치: {summary.OutputFilePath}", WinColor.Yellow);

                try
                {
                    Process.Start(new ProcessStartInfo(summary.OutputFilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    AppendLog($"CSV 파일 자동 열기 실패: {ex.Message}", WinColor.Yellow);
                }
            }
            catch (OperationCanceledException)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _lblStatus.Text = "상태: 작업 취소됨";
                AppendLog("작업이 사용자에 의해 취소되었습니다.", WinColor.Tomato);
            }
            catch (Exception ex)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _lblStatus.Text = "상태: 오류 발생";
                AppendLog($"[오류] {ex.Message}", WinColor.Tomato);
            }
            finally
            {
                _isProcessing = false;
                _cts?.Dispose();
                _cts = null;
                ToggleUiState();
            }
        }

        private void ToggleUiState()
        {
            if (InvokeRequired) { Invoke(new Action(ToggleUiState)); return; }

            _txtTargetFolder.Enabled = !_isProcessing;
            _btnSelectFolder.Enabled = !_isProcessing;
            _rdoAscending.Enabled = !_isProcessing;
            _rdoDescending.Enabled = !_isProcessing;
            _btnExtract.Enabled = true;

            if (_isProcessing)
            {
                _btnExtract.Text = "CANCEL (작업 취소)";
                _btnExtract.BackColor = WinColor.FromArgb(204, 74, 74);
            }
            else
            {
                _btnExtract.Text = "EXTRACT CSV (추출 시작)";
                _btnExtract.BackColor = WinColor.FromArgb(0, 122, 204);
            }
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

        private void InitializeDarkUI()
        {
            this.Text = "Vision FA Dataset Class Counter Pro";
            this.Size = new Size(600, 450);
            this.MinimumSize = new Size(600, 450);
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            var lblFolder = new WinLabel { Text = "Target Dir:", Location = new Point(20, yPos + 4), Size = new Size(100, 20), ForeColor = WinColor.LightSkyBlue };
            _txtTargetFolder = new TextBox { Location = new Point(120, yPos), Size = new Size(350, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };
            _btnSelectFolder = new Button { Text = "BROWSE", Location = new Point(480, yPos - 1), Size = new Size(80, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectFolder.FlatAppearance.BorderSize = 0;
            _btnSelectFolder.Click += BtnSelectFolder_Click;

            this.Controls.Add(lblFolder); this.Controls.Add(_txtTargetFolder); this.Controls.Add(_btnSelectFolder);

            yPos += 45;

            var lblSort = new WinLabel { Text = "Sort Order:", Location = new Point(20, yPos + 2), Size = new Size(100, 20), ForeColor = WinColor.LightGreen };
            _rdoAscending = new RadioButton { Text = "오름차순 (A ➔ Z)", Location = new Point(120, yPos), Size = new Size(160, 20), Checked = true, ForeColor = WinColor.White };
            _rdoDescending = new RadioButton { Text = "내림차순 (Z ➔ A)", Location = new Point(290, yPos), Size = new Size(160, 20), ForeColor = WinColor.White };

            this.Controls.Add(lblSort); this.Controls.Add(_rdoAscending); this.Controls.Add(_rdoDescending);

            yPos += 45;

            _btnExtract = new Button { Text = "EXTRACT CSV (추출 시작)", Location = new Point(20, yPos), Size = new Size(540, 45), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 12F, FontStyle.Bold) };
            _btnExtract.FlatAppearance.BorderSize = 0;
            _btnExtract.Click += BtnExtract_Click;
            this.Controls.Add(_btnExtract);

            yPos += 60;

            _lblStatus = new WinLabel { Text = "상태: 대기 중...", Location = new Point(20, yPos), Size = new Size(540, 20), ForeColor = WinColor.Cyan };
            this.Controls.Add(_lblStatus);

            yPos += 25;

            _progressBar = new ProgressBar { Location = new Point(20, yPos), Size = new Size(540, 10), Style = ProgressBarStyle.Continuous };
            this.Controls.Add(_progressBar);

            yPos += 25;

            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(540, 140), BackColor = WinColor.FromArgb(18, 18, 18), ForeColor = WinColor.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }
}