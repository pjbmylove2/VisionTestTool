using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace MachineVision.Algorithm.Comparator
{
    // ==========================================
    // [인터페이스 추상화] 향후 Halcon, Cognex 엔진 교체를 대비한 설계
    // ==========================================
    public interface IImageComparator
    {
        Task CompareAndSaveAsync(string refImagePath, string targetImagePath, string outputDir, CancellationToken token);
    }

    // ==========================================
    // 1. 비즈니스 로직 (OpenCVSharp 기반 마스킹 비교 엔진)
    // ==========================================
    public class OpenCvMaskComparator : IImageComparator
    {
        public async Task CompareAndSaveAsync(string refImagePath, string targetImagePath, string outputDir, CancellationToken token)
        {
            if (!File.Exists(refImagePath)) throw new FileNotFoundException($"Reference 이미지를 찾을 수 없습니다: {refImagePath}");
            if (!File.Exists(targetImagePath)) throw new FileNotFoundException($"Target 이미지를 찾을 수 없습니다: {targetImagePath}");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string originalExtension = Path.GetExtension(targetImagePath);
            if (string.IsNullOrEmpty(originalExtension)) originalExtension = ".bmp";

            string outputFileName = $"{Path.GetFileNameWithoutExtension(targetImagePath)}_Filtered{originalExtension}";
            string outputPath = Path.Combine(outputDir, outputFileName);

            // [핵심 1] UI 스레드 데드락 방지 및 비전 연산 스레드 분리
            await Task.Run(() =>
            {
                // [핵심 2] Mat 객체는 Unmanaged 자원이므로 반드시 using으로 감싸서 즉각 해제 (Zero-Leak)
                using (Mat matRef = Cv2.ImRead(refImagePath, ImreadModes.Grayscale))
                using (Mat matTargetGray = Cv2.ImRead(targetImagePath, ImreadModes.Grayscale))
                using (Mat matTargetColor = Cv2.ImRead(targetImagePath, ImreadModes.Color))
                {
                    token.ThrowIfCancellationRequested();

                    // [방어적 코딩] 카메라 트리거 지연으로 인한 해상도 불일치 예외 처리
                    if (matRef.Size() != matTargetColor.Size())
                    {
                        throw new InvalidOperationException($"이미지 해상도 불일치. Ref: {matRef.Width}x{matRef.Height}, Target: {matTargetColor.Width}x{matTargetColor.Height}");
                    }

                    using (Mat mask = new Mat())
                    using (Mat maskInv = new Mat())
                    using (Mat resultMat = new Mat())
                    using (Mat whiteBackground = new Mat(matTargetColor.Size(), matTargetColor.Type(), Scalar.White))
                    {
                        // 1. Reference 이미지(A)에서 문자 영역 추출을 위한 이진화 (Binarization)
                        Cv2.Threshold(matRef, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

                        // 2. 결과 버퍼 초기화 (배경 흰색)
                        whiteBackground.CopyTo(resultMat);

                        // 3. Target 이미지(B)에 Reference 마스크(A) 투영
                        matTargetColor.CopyTo(resultMat, mask);

                        token.ThrowIfCancellationRequested();

                        // 4. 저장
                        bool isSaved = Cv2.ImWrite(outputPath, resultMat);
                        if (!isSaved)
                        {
                            throw new IOException($"이미지 저장에 실패했습니다. 경로 또는 포맷을 확인하세요: {outputPath}");
                        }
                    }
                }
            }, token).ConfigureAwait(false);
        }
    }

    // ==========================================
    // 2. UI 로직 (MainForm)
    // ==========================================
    public class MainForm : Form
    {
        private TextBox _txtRefImage;
        private TextBox _txtTargetImage;
        private TextBox _txtOutputDir;
        private Button _btnBrowseRef;
        private Button _btnBrowseTarget;
        private Button _btnCompare;
        private RichTextBox _rtbLog;

        private readonly IImageComparator _comparatorCore;
        private CancellationTokenSource _cts;

        private string _lastOpenedDirectory = string.Empty;

        public MainForm()
        {
            _comparatorCore = new OpenCvMaskComparator();
            InitializeUI();
        }

        private void BtnBrowseRef_Click(object sender, EventArgs e)
        {
            SelectImageFile(_txtRefImage, "Select Image A (Reference)");
        }

        private void BtnBrowseTarget_Click(object sender, EventArgs e)
        {
            SelectImageFile(_txtTargetImage, "Select Image B (Target)");
        }

        private void SelectImageFile(TextBox targetTextBox, string title)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = title;
                ofd.Filter = "Image Files (*.bmp;*.jpg;*.jpeg;*.png)|*.bmp;*.jpg;*.jpeg;*.png|All Files (*.*)|*.*";

                if (!string.IsNullOrEmpty(_lastOpenedDirectory) && Directory.Exists(_lastOpenedDirectory))
                {
                    ofd.InitialDirectory = _lastOpenedDirectory;
                }

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    targetTextBox.Text = ofd.FileName;
                    _lastOpenedDirectory = Path.GetDirectoryName(ofd.FileName);
                    AppendLog($"파일 선택됨: {Path.GetFileName(ofd.FileName)}", Color.LightGray);
                }
            }
        }

        private async void BtnCompare_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtRefImage.Text) || string.IsNullOrWhiteSpace(_txtTargetImage.Text))
            {
                MessageBox.Show("A이미지와 B이미지 경로를 모두 입력하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnCompare.Enabled = false;
            _rtbLog.Clear();
            AppendLog("이미지 비교 연산을 시작합니다...", Color.LightSkyBlue);

            _cts = new CancellationTokenSource();

            try
            {
                await _comparatorCore.CompareAndSaveAsync(
                    _txtRefImage.Text,
                    _txtTargetImage.Text,
                    _txtOutputDir.Text,
                    _cts.Token
                );

                AppendLog("연산 및 저장이 성공적으로 완료되었습니다.", Color.LimeGreen);
            }
            catch (OperationCanceledException)
            {
                AppendLog("사용자에 의해 연산이 취소되었습니다.", Color.Yellow);
            }
            // 🌟 [핵심 방어] P/Invoke C++ DLL 매핑 실패 에러 인터셉트
            catch (TypeInitializationException ex)
            {
                AppendLog($"[DLL 누락 오류] OpenCV 코어 엔진을 로드할 수 없습니다.\n해결책: NuGet에서 'OpenCvSharp4.runtime.win' 패키지를 설치하거나, PC에 'VC++ 2015-2022 재배포 가능 패키지'가 설치되어 있는지 확인하세요.\n상세: {ex.InnerException?.Message}", Color.Tomato);
            }
            catch (DllNotFoundException ex)
            {
                AppendLog($"[DLL 누락 오류] 필수 C++ 라이브러리를 찾을 수 없습니다.\n해결책: 'OpenCvSharp4.runtime.win' 패키지 설치 필요.\n상세: {ex.Message}", Color.Tomato);
            }
            catch (Exception ex)
            {
                AppendLog($"[FATAL ERROR] {ex.Message}", Color.Tomato);
            }
            finally
            {
                _btnCompare.Enabled = true;
                _cts?.Dispose();
            }
        }

        private void AppendLog(string text, Color color)
        {
            if (InvokeRequired) { Invoke(new Action(() => AppendLog(text, color))); return; }
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}\n");
            _rtbLog.ScrollToCaret();
        }

        private void InitializeUI()
        {
            this.Text = "Vision Pattern Comparator Pro";
            this.Size = new Size(700, 450);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            var lblRef = new Label { Text = "Image A (Ref):", Location = new Point(20, yPos + 4), Size = new Size(130, 20) };
            _txtRefImage = new TextBox { Location = new Point(150, yPos), Size = new Size(410, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnBrowseRef = new Button { Text = "BROWSE", Location = new Point(570, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnBrowseRef.FlatAppearance.BorderSize = 0;
            _btnBrowseRef.Click += BtnBrowseRef_Click;
            this.Controls.Add(lblRef); this.Controls.Add(_txtRefImage); this.Controls.Add(_btnBrowseRef);

            yPos += 40;

            var lblTarget = new Label { Text = "Image B (Target):", Location = new Point(20, yPos + 4), Size = new Size(140, 20) };
            _txtTargetImage = new TextBox { Location = new Point(150, yPos), Size = new Size(410, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            _btnBrowseTarget = new Button { Text = "BROWSE", Location = new Point(570, yPos - 1), Size = new Size(80, 25), BackColor = Color.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnBrowseTarget.FlatAppearance.BorderSize = 0;
            _btnBrowseTarget.Click += BtnBrowseTarget_Click;
            this.Controls.Add(lblTarget); this.Controls.Add(_txtTargetImage); this.Controls.Add(_btnBrowseTarget);

            yPos += 40;

            var lblOut = new Label { Text = "Output Dir:", Location = new Point(20, yPos + 4), Size = new Size(130, 20) };
            _txtOutputDir = new TextBox { Text = @"C:\Vision\Output", Location = new Point(150, yPos), Size = new Size(500, 23), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White };
            this.Controls.Add(lblOut); this.Controls.Add(_txtOutputDir);

            yPos += 45;

            _btnCompare = new Button { Text = "COMPARE AND EXTRACT", Location = new Point(20, yPos), Size = new Size(630, 40), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 11F, FontStyle.Bold) };
            _btnCompare.FlatAppearance.BorderSize = 0;
            _btnCompare.Click += BtnCompare_Click;
            this.Controls.Add(_btnCompare);

            yPos += 60;

            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(630, 180), BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }
}