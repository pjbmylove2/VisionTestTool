using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

// 🌟 [방어적 코딩] 명시적 별칭 선언 (네임스페이스 충돌 방지)
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Algorithm.Comparator
{
    // ==========================================
    // [인터페이스 추상화] 향후 엔진 교체를 대비한 설계
    // ==========================================
    public interface IImageComparator
    {
        Task CompareAndSaveAsync(string refImagePath, string targetImagePath, string outputDir,
                                 int diffThreshold, int dilateSize, CancellationToken token);
    }

    // ==========================================
    // 1. 비즈니스 로직 (순수 델타 비교 + 🌟트리플 마스크 추출 엔진)
    // ==========================================
    public class OpenCvDeltaComparator : IImageComparator
    {
        public async Task CompareAndSaveAsync(string refImagePath, string targetImagePath, string outputDir,
                                              int diffThreshold, int dilateSize, CancellationToken token)
        {
            if (!File.Exists(refImagePath)) throw new FileNotFoundException($"Reference 이미지를 찾을 수 없습니다: {refImagePath}");
            if (!File.Exists(targetImagePath)) throw new FileNotFoundException($"Target 이미지를 찾을 수 없습니다: {targetImagePath}");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string originalExtension = Path.GetExtension(targetImagePath);
            if (string.IsNullOrEmpty(originalExtension)) originalExtension = ".bmp";

            string baseFileName = Path.GetFileNameWithoutExtension(targetImagePath);

            // 🌟 3가지 출력 파일 경로 정의
            string outputRedPath = Path.Combine(outputDir, $"{baseFileName}_RedHighlight{originalExtension}");
            string outputBlackPath = Path.Combine(outputDir, $"{baseFileName}_DefectBlackMask{originalExtension}");
            string outputCoveredPath = Path.Combine(outputDir, $"{baseFileName}_DefectCovered{originalExtension}"); // 원본에서 불량만 도려낸 이미지

            await Task.Run(() =>
            {
                using (Mat matRefGray = Cv2.ImRead(refImagePath, ImreadModes.Grayscale))
                using (Mat matTargetGray = Cv2.ImRead(targetImagePath, ImreadModes.Grayscale))
                using (Mat matTargetColor = Cv2.ImRead(targetImagePath, ImreadModes.Color))
                {
                    token.ThrowIfCancellationRequested();

                    if (matRefGray.Size() != matTargetGray.Size())
                    {
                        throw new InvalidOperationException($"해상도 불일치. Ref: {matRefGray.Width}x{matRefGray.Height}, Target: {matTargetGray.Width}x{matTargetGray.Height}");
                    }

                    // 🌟 연산 버퍼 할당 (CoveredMat 추가)
                    using (Mat diffMat = new Mat())
                    using (Mat mask = new Mat())
                    using (Mat invertedMask = new Mat())
                    using (Mat coveredMat = new Mat())
                    {
                        // 1. 순수 픽셀 차이(Delta) 및 이진화
                        Cv2.Absdiff(matRefGray, matTargetGray, diffMat);
                        Cv2.Threshold(diffMat, mask, diffThreshold, 255, ThresholdTypes.Binary);

                        // 2. 육안 시인성 확보를 위한 팽창 (Dilation)
                        if (dilateSize > 1)
                        {
                            int kSize = (dilateSize % 2 == 0) ? dilateSize + 1 : dilateSize;
                            using (Mat dilateKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(kSize, kSize)))
                            {
                                Cv2.Dilate(mask, mask, dilateKernel);
                            }
                        }

                        token.ThrowIfCancellationRequested();

                        // 3. '다른 부분(Defect)'을 검은색(0)으로, '정상 부분'을 흰색(255)으로 반전 생성
                        Cv2.BitwiseNot(mask, invertedMask);

                        // 🌟 4. [신규 로직] 원본 B이미지에서 불량 부분만 까맣게 도려내기
                        // 주의: matTargetColor가 빨간색으로 오염되기 전에 먼저 연산해야 원본 컬러가 보존됨.
                        Cv2.BitwiseAnd(matTargetColor, matTargetColor, coveredMat, mask: invertedMask);

                        // 5. Target 이미지 위에 '다른 부분' 빨간색(BGR: 0,0,255) 마스킹 (육안 확인용 변이)
                        matTargetColor.SetTo(new Scalar(0, 0, 255), mask);

                        // 6. 트리플 파일 디스크 저장 (병목 대비)
                        bool isRedSaved = Cv2.ImWrite(outputRedPath, matTargetColor);
                        bool isBlackSaved = Cv2.ImWrite(outputBlackPath, invertedMask);
                        bool isCoveredSaved = Cv2.ImWrite(outputCoveredPath, coveredMat);

                        if (!isRedSaved || !isBlackSaved || !isCoveredSaved)
                        {
                            throw new IOException($"이미지 저장에 실패했습니다.\nRed: {isRedSaved}, Black: {isBlackSaved}, Covered: {isCoveredSaved}");
                        }
                    }
                }
            }, token).ConfigureAwait(false);
        }
    }

    // ==========================================
    // 2. UI 로직 (MainForm)
    // ==========================================
    public partial class MainForm : Form
    {
        private TextBox _txtRefImage;
        private TextBox _txtTargetImage;
        private TextBox _txtOutputDir;

        private NumericUpDown _numThreshold;
        private NumericUpDown _numDilate;

        private Button _btnBrowseRef;
        private Button _btnBrowseTarget;
        private Button _btnCompare;
        private RichTextBox _rtbLog;

        private readonly IImageComparator _comparatorCore;
        private CancellationTokenSource _cts;

        private string _lastOpenedDirectory = string.Empty;

        public MainForm()
        {
            _comparatorCore = new OpenCvDeltaComparator();
            InitializeUI();

            try
            {
                VisionEnvironmentValidator.ValidateOpenCvDependencies();
            }
            catch (Exception ex)
            {
                AppendLog($"[초기화 실패] {ex.Message}", WinColor.Tomato);
                MessageBox.Show(ex.Message, "DLL 로드 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                    AppendLog($"파일 선택됨: {Path.GetFileName(ofd.FileName)}", WinColor.LightGray);
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
            AppendLog($"차이 비교 및 트리플(Triple) 저장 모드 시작 (허용오차: {_numThreshold.Value}, 굵기: {_numDilate.Value})", WinColor.LightSkyBlue);

            _cts = new CancellationTokenSource();

            try
            {
                await _comparatorCore.CompareAndSaveAsync(
                    _txtRefImage.Text,
                    _txtTargetImage.Text,
                    _txtOutputDir.Text,
                    (int)_numThreshold.Value,
                    (int)_numDilate.Value,
                    _cts.Token
                );

                AppendLog("연산 및 3종 이미지 저장이 성공적으로 완료되었습니다.", WinColor.LimeGreen);
                AppendLog("▶ 1. [_RedHighlight] B이미지 위에 다른 부분만 빨간색 마스킹", WinColor.Yellow);
                AppendLog("▶ 2. [_DefectBlackMask] 정상은 '흰색', 불량은 '검은색'으로 반전 추출", WinColor.Yellow);
                AppendLog("▶ 3. [_DefectCovered] 원본 B이미지에서 불량 부분만 까맣게 도려냄(Masking)", WinColor.Yellow);
            }
            catch (OperationCanceledException)
            {
                AppendLog("사용자에 의해 연산이 취소되었습니다.", WinColor.Orange);
            }
            catch (Exception ex)
            {
                AppendLog($"[FATAL ERROR] {ex.Message}", WinColor.Tomato);
            }
            finally
            {
                _btnCompare.Enabled = true;
                _cts?.Dispose();
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

        private void InitializeUI()
        {
            this.Text = "Vision Pattern Comparator Pro (Triple Save Engine)";
            this.Size = new Size(700, 560);
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 9.75F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            int yPos = 20;

            var lblRef = new WinLabel { Text = "Image A (Ref):", Location = new Point(20, yPos + 4), Size = new Size(130, 20) };
            _txtRefImage = new TextBox { Location = new Point(150, yPos), Size = new Size(410, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };
            _btnBrowseRef = new Button { Text = "BROWSE", Location = new Point(570, yPos - 1), Size = new Size(80, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnBrowseRef.FlatAppearance.BorderSize = 0;
            _btnBrowseRef.Click += BtnBrowseRef_Click;
            this.Controls.Add(lblRef); this.Controls.Add(_txtRefImage); this.Controls.Add(_btnBrowseRef);

            yPos += 40;

            var lblTarget = new WinLabel { Text = "Image B (Target):", Location = new Point(20, yPos + 4), Size = new Size(140, 20) };
            _txtTargetImage = new TextBox { Location = new Point(150, yPos), Size = new Size(410, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };
            _btnBrowseTarget = new Button { Text = "BROWSE", Location = new Point(570, yPos - 1), Size = new Size(80, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnBrowseTarget.FlatAppearance.BorderSize = 0;
            _btnBrowseTarget.Click += BtnBrowseTarget_Click;
            this.Controls.Add(lblTarget); this.Controls.Add(_txtTargetImage); this.Controls.Add(_btnBrowseTarget);

            yPos += 40;

            var lblOut = new WinLabel { Text = "Output Dir:", Location = new Point(20, yPos + 4), Size = new Size(130, 20) };
            _txtOutputDir = new TextBox { Text = @"C:\Vision\Output", Location = new Point(150, yPos), Size = new Size(500, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };
            this.Controls.Add(lblOut); this.Controls.Add(_txtOutputDir);

            yPos += 45;

            var pnlParams = new Panel { Location = new Point(20, yPos), Size = new Size(630, 45), BackColor = WinColor.FromArgb(40, 40, 40), BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(pnlParams);

            var lblThresh = new WinLabel { Text = "차이민감도(0~255):", Location = new Point(10, 12), Size = new Size(160, 20), ForeColor = WinColor.LightSkyBlue };
            _numThreshold = new NumericUpDown { Location = new Point(170, 10), Size = new Size(60, 23), Minimum = 0, Maximum = 255, Value = 5, BackColor = WinColor.FromArgb(60, 60, 60), ForeColor = WinColor.White };

            var lblDilate = new WinLabel { Text = "표시굵기(Dilate):", Location = new Point(330, 12), Size = new Size(150, 20), ForeColor = WinColor.Tomato };
            _numDilate = new NumericUpDown { Location = new Point(480, 10), Size = new Size(60, 23), Minimum = 1, Maximum = 21, Value = 3, Increment = 2, BackColor = WinColor.FromArgb(60, 60, 60), ForeColor = WinColor.White };

            pnlParams.Controls.AddRange(new Control[] { lblThresh, _numThreshold, lblDilate, _numDilate });

            yPos += 60;

            _btnCompare = new Button { Text = "PURE COMPARE AND TRIPLE SAVE", Location = new Point(20, yPos), Size = new Size(630, 40), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 11F, FontStyle.Bold) };
            _btnCompare.FlatAppearance.BorderSize = 0;
            _btnCompare.Click += BtnCompare_Click;
            this.Controls.Add(_btnCompare);

            yPos += 55;

            _rtbLog = new RichTextBox { Location = new Point(20, yPos), Size = new Size(630, 160), BackColor = WinColor.FromArgb(18, 18, 18), ForeColor = WinColor.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };
            this.Controls.Add(_rtbLog);
        }
    }

    // ==========================================
    // [현장 유틸리티] 0x8007007E 방어용 부트스트래퍼
    // ==========================================
    public static class VisionEnvironmentValidator
    {
        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        public static void ValidateOpenCvDependencies()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string architecture = Environment.Is64BitProcess ? "x64" : "x86";
            string dllPath = Path.Combine(baseDir, "dll", architecture, "OpenCvSharpExtern.dll");

            if (!Environment.Is64BitProcess)
            {
                throw new InvalidOperationException("프로세스가 32비트(x86)입니다. 플랫폼을 'x64'로 변경하십시오.");
            }

            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException($"OpenCV DLL 누락: {dllPath}\n패키지 재설치 필요.");
            }

            IntPtr hModule = LoadLibrary(dllPath);
            if (hModule == IntPtr.Zero)
            {
                throw new TypeInitializationException("OpenCvSharpExtern.dll", new Exception(
                    "C++ 종속성 누락. 'VC++ 재배포 가능 패키지' 설치가 필요합니다."));
            }
        }
    }
}