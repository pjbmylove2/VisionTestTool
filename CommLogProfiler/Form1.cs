using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Tool.LogProfiler
{
    // ==========================================
    // 1. 데이터 모델 (🌟 NewOcrResult 추가)
    // ==========================================
    public enum EventCategory
    {
        Info,
        Warning,
        Error,
        Defect,
        NewOcrResult, // 🌟 [신규] 값이 변경된 최초의 신규 판독 시점
        OcrOk,        // [중복] 하트비트로 계속 날아가는 과거 데이터
        LotChange,
        SystemError
    }

    public class LogEventItem
    {
        public string Timestamp { get; set; }
        public string FileName { get; set; }
        public EventCategory Category { get; set; }
        public string EventName { get; set; }
        public string Description { get; set; }
        public string RawData { get; set; }
    }

    public class AnalysisSummary
    {
        public int TotalFilesProcessed { get; set; }
        public long TotalLinesScanned { get; set; }
        public int PlcErrorCount { get; set; }
        public int VisionErrorCount { get; set; }
        public int DefectCount { get; set; }
        public int NewOcrOkCount { get; set; }    // 🌟 진짜 검사한 신규 제품 개수
        public int DisconnectCount { get; set; }
        public int LotChangeCount { get; set; }
        public int SystemExceptionCount { get; set; }

        public long ElapsedMilliseconds { get; set; }
        public double PeakMemoryMB { get; set; }
    }

    public interface ILogAnalyzer
    {
        Task<(List<LogEventItem> Events, AnalysisSummary Summary)> AnalyzeLogsAsync(
            string[] filePaths, IProgress<int> progress, CancellationToken token);
    }

    // ==========================================
    // 2. 비즈니스 로직 (🚀 엣지 트리거 캡처 엔진 탑재)
    // ==========================================
    public class LogAnalyzerCore : ILogAnalyzer
    {
        public async Task<(List<LogEventItem> Events, AnalysisSummary Summary)> AnalyzeLogsAsync(
            string[] filePaths, IProgress<int> progress, CancellationToken token)
        {
            var events = new List<LogEventItem>(50000);
            var summary = new AnalysisSummary();

            bool prevPlcError = false;
            byte prevVisionCmd = 0;
            bool prevPlcConnected = true;
            bool prevApiConnected = true;
            bool prevCamError = false;
            bool prevShareError = false;
            bool prevContinuousNg = false;

            string prevLotId1 = null;
            string prevLotId2 = null;

            // 🌟 [핵심] 이전 Send 패킷의 최종 OCR 결과를 기억하는 캐싱 변수
            string prevOcrResultData = null;

            long totalSize = filePaths.Sum(f => new FileInfo(f).Length);
            long processedSize = 0;
            int lastReportedPercent = -1;

            long initialMemory = GC.GetTotalMemory(true);
            long peakMemory = initialMemory;

            var sw = Stopwatch.StartNew();

            await Task.Run(() =>
            {
                byte[] packetBuffer = new byte[256];

                foreach (var filePath in filePaths.OrderBy(f => f))
                {
                    token.ThrowIfCancellationRequested();
                    string fileName = Path.GetFileName(filePath);
                    summary.TotalFilesProcessed++;

                    events.Add(CreateEvent("00:00:00.000", fileName, EventCategory.Info, "파일 스캔 시작", $"분석 시작: {fileName}", ""));

                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1048576, FileOptions.SequentialScan))
                    using (var reader = new StreamReader(fs, Encoding.Default, true))
                    {
                        string line;
                        string currentTimestamp = "00:00:00.000";
                        LogEventItem pendingSystemError = null;

                        while ((line = reader.ReadLine()) != null)
                        {
                            summary.TotalLinesScanned++;
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            if (line.StartsWith("["))
                            {
                                currentTimestamp = ExtractTimestampFast(line);

                                if (pendingSystemError != null)
                                {
                                    events.Add(pendingSystemError);
                                    pendingSystemError = null;
                                }
                            }
                            else
                            {
                                if (line.StartsWith("System.") || line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (pendingSystemError == null)
                                    {
                                        pendingSystemError = CreateEvent(currentTimestamp, fileName, EventCategory.SystemError, "C# 시스템 예외 (Crash)", line, "");
                                        summary.SystemExceptionCount++;
                                    }
                                    else
                                    {
                                        pendingSystemError.Description += "\n" + line.Trim();
                                    }
                                    continue;
                                }
                                else if (pendingSystemError != null)
                                {
                                    pendingSystemError.Description += "\n" + line.Trim();
                                    continue;
                                }
                                continue;
                            }

                            int rxIdx = line.IndexOf("Received:", StringComparison.Ordinal);
                            int txIdx = rxIdx == -1 ? line.IndexOf("Send:", StringComparison.Ordinal) : -1;

                            if (rxIdx == -1 && txIdx == -1) continue;

                            bool isRx = rxIdx != -1;
                            bool isTx = txIdx != -1;
                            int dataStartIdx = isRx ? rxIdx + 9 : txIdx + 5;

                            bool isByteArray = false;
                            for (int i = dataStartIdx; i < line.Length; i++)
                            {
                                char c = line[i];
                                if (c == ' ' || c == '\t') continue;
                                if (c >= '0' && c <= '9') { isByteArray = true; break; }
                                break;
                            }

                            if (!isByteArray)
                            {
                                string textMsg = line.Substring(dataStartIdx).Trim();

                                if (textMsg.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    textMsg.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Error, "소켓/통신 에러", textMsg, line));
                                    summary.DisconnectCount++;
                                }
                                else if (textMsg.IndexOf("Disconnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         textMsg.IndexOf("Closed", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Warning, "소켓 단절/종료", textMsg, line));
                                    summary.DisconnectCount++;
                                }
                                else if (textMsg.IndexOf("State:", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Info, "소켓 상태 전이", textMsg, line));
                                }
                                continue;
                            }

                            try
                            {
                                int byteCount = ParseBytesZeroAllocation(line, dataStartIdx, packetBuffer);
                                if (byteCount == 0) continue;

                                if (isRx && byteCount >= 46)
                                {
                                    string currentLot1 = Encoding.ASCII.GetString(packetBuffer, 12, 10).Trim('\0', ' ');
                                    if (!string.IsNullOrWhiteSpace(currentLot1))
                                    {
                                        if (prevLotId1 == null)
                                        {
                                            prevLotId1 = currentLot1;
                                            events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.LotChange, "초기 LOT ID 1 인식", $"[LOT ID 1] {currentLot1}", GenerateRawString(packetBuffer, byteCount)));
                                        }
                                        else if (currentLot1 != prevLotId1)
                                        {
                                            events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.LotChange, "LOT ID 1 변경됨", $"기존: {prevLotId1} ➔ 신규: {currentLot1}", GenerateRawString(packetBuffer, byteCount)));
                                            prevLotId1 = currentLot1;
                                            summary.LotChangeCount++;
                                        }
                                    }

                                    if (byteCount >= 58)
                                    {
                                        string currentLot2 = Encoding.ASCII.GetString(packetBuffer, 46, 10).Trim('\0', ' ');
                                        if (!string.IsNullOrWhiteSpace(currentLot2))
                                        {
                                            if (prevLotId2 == null) prevLotId2 = currentLot2;
                                            else if (currentLot2 != prevLotId2)
                                            {
                                                events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.LotChange, "LOT ID 2 변경됨", $"기존: {prevLotId2} ➔ 신규: {currentLot2}", GenerateRawString(packetBuffer, byteCount)));
                                                prevLotId2 = currentLot2;
                                                summary.LotChangeCount++;
                                            }
                                        }
                                    }

                                    bool currentPlcError = (packetBuffer[4] & 0x80) != 0;
                                    if (currentPlcError && !prevPlcError)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Error, "설비 에러 발생", "PLC에서 에러 신호를 송신했습니다.", GenerateRawString(packetBuffer, byteCount)));
                                        summary.PlcErrorCount++;
                                    }
                                    else if (!currentPlcError && prevPlcError)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Info, "설비 에러 해제", "PLC 에러가 해제되었습니다.", GenerateRawString(packetBuffer, byteCount)));
                                    }
                                    prevPlcError = currentPlcError;

                                    byte currentCmd = (byte)(packetBuffer[5] & 0x03);
                                    if (currentCmd != prevVisionCmd)
                                    {
                                        string cmdStr = currentCmd == 1 ? "Start" : (currentCmd == 2 ? "Stop" : "None");
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Info, "비전 커맨드 변경", $"PLC 명령 변경 ➔ {cmdStr}", GenerateRawString(packetBuffer, byteCount)));
                                        prevVisionCmd = currentCmd;
                                    }

                                    bool currentPlcConn = (packetBuffer[7] & 0x01) != 0;
                                    bool currentApiConn = (packetBuffer[7] & 0x04) != 0;

                                    if (!currentPlcConn && prevPlcConnected)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Warning, "Machine PLC 연결 끊김", "설비와 ROLLMAP PLC 간 연결이 소실되었습니다.", GenerateRawString(packetBuffer, byteCount)));
                                        summary.DisconnectCount++;
                                    }
                                    if (!currentApiConn && prevApiConnected)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Warning, "API 연결 끊김", "API 연결이 소실되었습니다.", GenerateRawString(packetBuffer, byteCount)));
                                        summary.DisconnectCount++;
                                    }
                                    prevPlcConnected = currentPlcConn;
                                    prevApiConnected = currentApiConn;
                                }
                                else if (isTx && byteCount >= 30)
                                {
                                    ushort statusWord = BitConverter.ToUInt16(packetBuffer, 0);

                                    bool currentCamError = (statusWord & (1 << 9)) != 0;
                                    if (currentCamError && !prevCamError)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Error, "카메라 연결 끊김", "비전 PC에서 카메라 연결 소실을 보고했습니다.", GenerateRawString(packetBuffer, byteCount)));
                                        summary.VisionErrorCount++;
                                    }
                                    prevCamError = currentCamError;

                                    bool currentShareError = (statusWord & (1 << 10)) != 0;
                                    if (currentShareError && !prevShareError)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Error, "공유 폴더 끊김", "이미지 저장용 공유 폴더 연결이 소실되었습니다.", GenerateRawString(packetBuffer, byteCount)));
                                        summary.VisionErrorCount++;
                                    }
                                    prevShareError = currentShareError;

                                    // 🌟 [핵심 변경] 판독 결과의 상태 전이 검사 (New vs Heartbeat)
                                    ushort refPos = BitConverter.ToUInt16(packetBuffer, 10);
                                    ushort laneNo = BitConverter.ToUInt16(packetBuffer, 12);
                                    ushort resultCode = BitConverter.ToUInt16(packetBuffer, 14);

                                    // 3가지 데이터를 조합하여 고유한 "현재 판독 결과 서명(Signature)" 생성
                                    string currentOcrSignature = $"{refPos}_{laneNo}_{resultCode}";

                                    if (refPos == 0) // NG
                                    {
                                        if (resultCode > 0)
                                        {
                                            // NG는 연속으로 동일한 코드가 터질 수 있으므로 무조건 로깅
                                            events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Defect, "불량(NG) 검출", $"Defect Code: {resultCode}", GenerateRawString(packetBuffer, byteCount)));
                                            summary.DefectCount++;
                                            prevOcrResultData = currentOcrSignature; // NG도 마지막 상태로 기록
                                        }
                                    }
                                    else if (refPos == 1 || refPos == 2) // A Type or Y Type
                                    {
                                        string okStr = refPos == 1 ? $"A{laneNo}{resultCode:D2}" : $"Y{laneNo:D3}{resultCode}";

                                        if (currentOcrSignature != prevOcrResultData)
                                        {
                                            // 🌟 [최초 1회 엣지 트리거] 이전 결과와 다르다면, "새로운" 제품을 방금 검사완료한 것임
                                            events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.NewOcrResult, "🚀 [신규] OCR 정상 판독", $"판독 문자열: [{okStr}] (최초 보고)", GenerateRawString(packetBuffer, byteCount)));
                                            summary.NewOcrOkCount++;
                                            prevOcrResultData = currentOcrSignature;
                                        }
                                        else
                                        {
                                            // 🌟 [하트비트 중복 전송] 이전과 결과가 같다면, 하트비트에 실려가는 과거 데이터임
                                            events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.OcrOk, "정상 판독 유지 (HB)", $"판독 문자열: [{okStr}] (중복 전송)", GenerateRawString(packetBuffer, byteCount)));
                                        }
                                    }

                                    bool currentContNg = (packetBuffer[24] & 0x01) != 0;
                                    if (currentContNg && !prevContinuousNg)
                                    {
                                        events.Add(CreateEvent(currentTimestamp, fileName, EventCategory.Warning, "연속 불량 알람 발생", "설정된 연속 불량 횟수를 초과했습니다.", GenerateRawString(packetBuffer, byteCount)));
                                    }
                                    prevContinuousNg = currentContNg;
                                }
                            }
                            catch (Exception) { /* 무시 */ }

                            if (summary.TotalLinesScanned % 20000 == 0)
                            {
                                long currentMem = GC.GetTotalMemory(false);
                                if (currentMem > peakMemory) peakMemory = currentMem;
                            }
                        }

                        if (pendingSystemError != null)
                        {
                            events.Add(pendingSystemError);
                        }

                        processedSize += fs.Length;
                        int percentComplete = (int)((double)processedSize / totalSize * 100);
                        if (percentComplete != lastReportedPercent)
                        {
                            progress?.Report(percentComplete);
                            lastReportedPercent = percentComplete;
                        }
                    }
                }
            }, token).ConfigureAwait(false);

            sw.Stop();
            summary.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            summary.PeakMemoryMB = (peakMemory - initialMemory) / 1048576.0;
            if (summary.PeakMemoryMB < 0) summary.PeakMemoryMB = 0.1;

            return (events, summary);
        }

        private LogEventItem CreateEvent(string time, string file, EventCategory cat, string name, string desc, string raw)
        {
            return new LogEventItem { Timestamp = time, FileName = file, Category = cat, EventName = name, Description = desc, RawData = raw };
        }

        private string GenerateRawString(byte[] buffer, int count)
        {
            return string.Join(" ", buffer.Take(count));
        }

        private string ExtractTimestampFast(string line)
        {
            int endIdx = line.IndexOf(']');
            if (endIdx > 1) return line.Substring(1, endIdx - 1);
            return "00:00:00.000";
        }

        private int ParseBytesZeroAllocation(string line, int startIndex, byte[] buffer)
        {
            int count = 0;
            int currentVal = 0;
            bool inNumber = false;
            int len = line.Length;

            for (int i = startIndex; i < len; i++)
            {
                char c = line[i];
                if (c == '(') break;

                if (c >= '0' && c <= '9')
                {
                    currentVal = currentVal * 10 + (c - '0');
                    inNumber = true;
                }
                else if (inNumber)
                {
                    if (count < buffer.Length) buffer[count++] = (byte)currentVal;
                    currentVal = 0;
                    inNumber = false;
                }
            }

            if (inNumber && count < buffer.Length)
            {
                buffer[count++] = (byte)currentVal;
            }
            return count;
        }
    }

    // ==========================================
    // 3. UI 로직
    // ==========================================
    public class MainForm : Form
    {
        private Button _btnSelectFiles;
        private TextBox _txtFilePaths;
        private Button _btnAnalyze;
        private ProgressBar _progressBar;

        private WinLabel _lblTotalLines;
        private WinLabel _lblPerformance;
        private WinLabel _lblPlcErrors;
        private WinLabel _lblVisionErrors;
        private WinLabel _lblDefects;
        private WinLabel _lblOcrOkCount;
        private WinLabel _lblDisconnects;
        private WinLabel _lblLotChanges;
        private WinLabel _lblSystemExceptions;

        private Button _btnPrevPage;
        private Button _btnNextPage;
        private WinLabel _lblPageInfo;
        private WinLabel _lblGridStatus;

        private CheckBox _chkNewOcrOk;       // 🌟 신규 판독 (디폴트 켜짐)
        private CheckBox _chkOcrOk;          // 하트비트 중복 판독 (디폴트 꺼짐)
        private CheckBox _chkInfo;
        private CheckBox _chkWarning;
        private CheckBox _chkError;
        private CheckBox _chkDefect;
        private CheckBox _chkLotChange;
        private CheckBox _chkSystemError;

        private DataGridView _gridEvents;

        private readonly ILogAnalyzer _analyzerCore;
        private CancellationTokenSource _cts;

        private string[] _selectedFiles = new string[0];
        private List<LogEventItem> _allEvents = new List<LogEventItem>();

        private List<LogEventItem> _currentFilteredEvents = new List<LogEventItem>();
        private int _currentPage = 1;
        private int _totalPages = 1;
        private const int PageSize = 5000;

        public MainForm()
        {
            _analyzerCore = new LogAnalyzerCore();
            InitializeLargeUI();
        }

        private void BtnSelectFiles_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "분석할 로그 파일을 선택하세요 (다중 선택 가능)";
                ofd.Filter = "Log/Text Files (*.log;*.txt)|*.log;*.txt|All Files (*.*)|*.*";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _selectedFiles = ofd.FileNames;
                    _txtFilePaths.Text = _selectedFiles.Length == 1 ? Path.GetFileName(_selectedFiles[0]) : $"총 {_selectedFiles.Length}개의 로그 파일이 선택되었습니다.";
                }
            }
        }

        private async void BtnAnalyze_Click(object sender, EventArgs e)
        {
            if (_selectedFiles == null || _selectedFiles.Length == 0)
            {
                MessageBox.Show("분석할 로그 파일을 선택하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnAnalyze.Enabled = false;
            _btnSelectFiles.Enabled = false;
            _gridEvents.DataSource = null;
            _progressBar.Value = 0;

            _btnPrevPage.Enabled = false;
            _btnNextPage.Enabled = false;
            _lblPageInfo.Text = "Page: 1 / 1 (총 0건)";

            _cts = new CancellationTokenSource();
            var progress = new Progress<int>(p => _progressBar.Value = p);

            try
            {
                var result = await _analyzerCore.AnalyzeLogsAsync(_selectedFiles, progress, _cts.Token);
                _allEvents = result.Events;

                UpdateDashboard(result.Summary);
                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"분석 중 오류 발생: {ex.Message}", "FATAL ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnAnalyze.Enabled = true;
                _btnSelectFiles.Enabled = true;
                _cts?.Dispose();
            }
        }

        private void UpdateDashboard(AnalysisSummary summary)
        {
            double seconds = summary.ElapsedMilliseconds / 1000.0;
            _lblTotalLines.Text = $"스캔: {summary.TotalLinesScanned:N0} 줄 ({summary.TotalFilesProcessed} 파일)";
            _lblPerformance.Text = $"성능: {seconds:N2}초 소요 (RAM: {summary.PeakMemoryMB:N1} MB)";
            _lblLotChanges.Text = $"LOT 변경: {summary.LotChangeCount:N0} 회";

            _lblOcrOkCount.Text = $"정상(OK) 제품 수: {summary.NewOcrOkCount:N0} 개"; // 🌟 신규 판독 개수만 집계
            _lblDefects.Text = $"불량(NG) 검출: {summary.DefectCount:N0} 건";

            _lblPlcErrors.Text = $"설비 에러: {summary.PlcErrorCount:N0} 회";
            _lblVisionErrors.Text = $"비전 에러: {summary.VisionErrorCount:N0} 회";
            _lblDisconnects.Text = $"통신 단절: {summary.DisconnectCount:N0} 회";
            _lblSystemExceptions.Text = $"시스템 예외(Crash): {summary.SystemExceptionCount:N0} 건";
        }

        private void Filter_CheckedChanged(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_allEvents == null || _allEvents.Count == 0) return;

            _currentFilteredEvents = _allEvents.Where(ev =>
                (ev.Category == EventCategory.NewOcrResult && _chkNewOcrOk.Checked) || // 🌟 신규 판독
                (ev.Category == EventCategory.OcrOk && _chkOcrOk.Checked) ||           // 중복 하트비트 판독
                (ev.Category == EventCategory.Info && _chkInfo.Checked) ||
                (ev.Category == EventCategory.Warning && _chkWarning.Checked) ||
                (ev.Category == EventCategory.Error && _chkError.Checked) ||
                (ev.Category == EventCategory.Defect && _chkDefect.Checked) ||
                (ev.Category == EventCategory.LotChange && _chkLotChange.Checked) ||
                (ev.Category == EventCategory.SystemError && _chkSystemError.Checked)
            ).ToList();

            _currentPage = 1;
            _totalPages = Math.Max(1, (int)Math.Ceiling((double)_currentFilteredEvents.Count / PageSize));

            RenderCurrentPage();
        }

        private void RenderCurrentPage()
        {
            int totalFiltered = _currentFilteredEvents.Count;

            var pageData = _currentFilteredEvents.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();

            _lblPageInfo.Text = $"Page: {_currentPage} / {_totalPages} (총 {totalFiltered:N0}건)";
            _btnPrevPage.Enabled = _currentPage > 1;
            _btnNextPage.Enabled = _currentPage < _totalPages;

            _lblGridStatus.Visible = totalFiltered > PageSize;

            _gridEvents.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _gridEvents.DataSource = pageData;

            if (_gridEvents.Columns.Count > 0)
            {
                _gridEvents.Columns["Timestamp"].Width = 140;
                _gridEvents.Columns["FileName"].Width = 150;
                _gridEvents.Columns["Category"].Width = 120; // 🌟 카테고리 폭 확보
                _gridEvents.Columns["EventName"].Width = 250;
                _gridEvents.Columns["Description"].Width = 500;
                _gridEvents.Columns["Description"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                _gridEvents.Columns["RawData"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }

            _gridEvents.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            foreach (DataGridViewRow row in _gridEvents.Rows)
            {
                var cat = (EventCategory)row.Cells["Category"].Value;
                if (cat == EventCategory.Error) row.DefaultCellStyle.BackColor = WinColor.FromArgb(80, 20, 20);
                else if (cat == EventCategory.Warning) row.DefaultCellStyle.BackColor = WinColor.FromArgb(80, 60, 20);
                else if (cat == EventCategory.Defect) row.DefaultCellStyle.BackColor = WinColor.FromArgb(80, 20, 60);
                else if (cat == EventCategory.LotChange) row.DefaultCellStyle.BackColor = WinColor.FromArgb(20, 60, 80);
                else if (cat == EventCategory.SystemError) row.DefaultCellStyle.BackColor = WinColor.DarkRed;
                else if (cat == EventCategory.Info) row.DefaultCellStyle.BackColor = WinColor.FromArgb(20, 70, 30);

                // 🌟 색상 분리: 엣지 트리거(신규)는 형광 라임색, 하트비트(중복)는 칙칙한 녹색
                else if (cat == EventCategory.NewOcrResult)
                {
                    row.DefaultCellStyle.BackColor = WinColor.LimeGreen;
                    row.DefaultCellStyle.ForeColor = WinColor.Black; // 글자를 검정으로 하여 가독성 극대화
                }
                else if (cat == EventCategory.OcrOk)
                {
                    row.DefaultCellStyle.BackColor = WinColor.FromArgb(20, 50, 20);
                    row.DefaultCellStyle.ForeColor = WinColor.WhiteSmoke;
                }
            }
        }

        private void BtnPrevPage_Click(object sender, EventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                RenderCurrentPage();
            }
        }

        private void BtnNextPage_Click(object sender, EventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                RenderCurrentPage();
            }
        }

        private void InitializeLargeUI()
        {
            this.Text = "Vision FA Log Profiler Pro (Edge Trigger Isolation Edition)";
            this.Size = new Size(1600, 950);
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(10) };
            this.Controls.Add(pnlTop);

            var lblFile = new WinLabel { Text = "Log Files:", Location = new Point(10, 25), Size = new Size(100, 20) };
            _txtFilePaths = new TextBox { Location = new Point(110, 22), Size = new Size(600, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White, ReadOnly = true, Text = "BROWSE를 눌러 다중 로그 파일 선택" };
            _btnSelectFiles = new Button { Text = "BROWSE FILES", Location = new Point(720, 20), Size = new Size(120, 28), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            _btnSelectFiles.FlatAppearance.BorderSize = 0;
            _btnSelectFiles.Click += BtnSelectFiles_Click;

            _btnAnalyze = new Button { Text = "다중 로그 일괄 분석 (ANALYZE)", Location = new Point(850, 15), Size = new Size(300, 40), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 12F, FontStyle.Bold) };
            _btnAnalyze.FlatAppearance.BorderSize = 0;
            _btnAnalyze.Click += BtnAnalyze_Click;

            _progressBar = new ProgressBar { Location = new Point(10, 60), Size = new Size(1140, 10), Style = ProgressBarStyle.Continuous };

            pnlTop.Controls.AddRange(new Control[] { lblFile, _txtFilePaths, _btnSelectFiles, _btnAnalyze, _progressBar });

            var pnlDashboard = new Panel { Dock = DockStyle.Left, Width = 310, BackColor = WinColor.FromArgb(35, 35, 38), Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(pnlDashboard);

            int dashY = 20;
            var lblDashTitle = new WinLabel { Text = "[ 24H 요약 대시보드 ]", Location = new Point(10, dashY), Size = new Size(290, 30), Font = new Font("Consolas", 14F, FontStyle.Bold), ForeColor = WinColor.Gold, TextAlign = ContentAlignment.MiddleCenter }; dashY += 50;

            _lblTotalLines = new WinLabel { Text = "스캔 라인: 0 줄", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.LightGray }; dashY += 30;
            _lblPerformance = new WinLabel { Text = "성능: - 초 (RAM: 0 MB)", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Yellow, Font = new Font("Consolas", 10F, FontStyle.Bold) }; dashY += 40;

            _lblLotChanges = new WinLabel { Text = "LOT 변경: 0 회", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Cyan, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 35;
            // 🌟 텍스트를 "정상 제품 수"로 직관적으로 변경
            _lblOcrOkCount = new WinLabel { Text = "정상(OK) 제품 수: 0 개", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.LimeGreen, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 35;
            _lblDefects = new WinLabel { Text = "불량(NG) 검출: 0 건", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Magenta, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 35;
            _lblPlcErrors = new WinLabel { Text = "설비 에러: 0 회", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Tomato, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 35;
            _lblVisionErrors = new WinLabel { Text = "비전 에러: 0 회", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Tomato, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 35;
            _lblDisconnects = new WinLabel { Text = "통신 단절: 0 회", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Orange, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 35;
            _lblSystemExceptions = new WinLabel { Text = "시스템 예외(Crash): 0 건", Location = new Point(10, dashY), Size = new Size(290, 25), ForeColor = WinColor.Red, Font = new Font("Consolas", 12F, FontStyle.Bold) }; dashY += 50;

            var lblFilterTitle = new WinLabel { Text = "[ 이벤트 필터 ]", Location = new Point(10, dashY), Size = new Size(290, 30), Font = new Font("Consolas", 12F, FontStyle.Bold), ForeColor = WinColor.LightSkyBlue, TextAlign = ContentAlignment.MiddleCenter }; dashY += 35;

            _chkSystemError = new CheckBox { Text = "🧨 System Crash (코드 에러)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = true, ForeColor = WinColor.Red }; dashY += 30;
            _chkLotChange = new CheckBox { Text = "🔄 LOT Change (LOT 변경)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = true, ForeColor = WinColor.Cyan }; dashY += 30;
            _chkError = new CheckBox { Text = "🚨 Error (에러 발생)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = true, ForeColor = WinColor.Tomato }; dashY += 30;
            _chkDefect = new CheckBox { Text = "❌ Defect (불량 검출)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = false, ForeColor = WinColor.Magenta }; dashY += 30;
            _chkWarning = new CheckBox { Text = "⚠️ Warning (알람/단절)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = true, ForeColor = WinColor.Orange }; dashY += 30;
            _chkInfo = new CheckBox { Text = "ℹ️ Info (상태/커맨드 변경)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = true, ForeColor = WinColor.LightGray }; dashY += 30;

            // 🌟 신규 판독(형광색)은 디폴트 ON, 하트비트 중복(어두운색)은 디폴트 OFF
            _chkNewOcrOk = new CheckBox { Text = "🚀 New OCR (신규 판독 엣지)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = true, ForeColor = WinColor.LimeGreen }; dashY += 30;
            _chkOcrOk = new CheckBox { Text = "✅ Heartbeat (중복 판독 패킷)", Location = new Point(20, dashY), Size = new Size(270, 25), Checked = false, ForeColor = WinColor.DarkGreen };

            _chkSystemError.CheckedChanged += Filter_CheckedChanged;
            _chkLotChange.CheckedChanged += Filter_CheckedChanged;
            _chkError.CheckedChanged += Filter_CheckedChanged;
            _chkDefect.CheckedChanged += Filter_CheckedChanged;
            _chkWarning.CheckedChanged += Filter_CheckedChanged;
            _chkInfo.CheckedChanged += Filter_CheckedChanged;
            _chkNewOcrOk.CheckedChanged += Filter_CheckedChanged;
            _chkOcrOk.CheckedChanged += Filter_CheckedChanged;

            pnlDashboard.Controls.AddRange(new Control[] { lblDashTitle, _lblTotalLines, _lblPerformance, _lblLotChanges, _lblOcrOkCount, _lblDefects, _lblPlcErrors, _lblVisionErrors, _lblDisconnects, _lblSystemExceptions,
                                                           lblFilterTitle, _chkSystemError, _chkLotChange, _chkError, _chkDefect, _chkWarning, _chkInfo, _chkNewOcrOk, _chkOcrOk });

            Panel pnlMainContent = new Panel { Dock = DockStyle.Fill };
            this.Controls.Add(pnlMainContent);
            pnlMainContent.BringToFront();

            Panel pnlGridTop = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = WinColor.FromArgb(25, 25, 28), BorderStyle = BorderStyle.FixedSingle };

            _btnPrevPage = new Button { Text = "◀ 이전 5000건", Location = new Point(10, 5), Size = new Size(120, 28), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat, ForeColor = WinColor.White, Cursor = Cursors.Hand, Enabled = false };
            _btnPrevPage.FlatAppearance.BorderSize = 0;
            _btnPrevPage.Click += BtnPrevPage_Click;

            _lblPageInfo = new WinLabel { Text = "Page: 1 / 1 (총 0건)", Location = new Point(140, 9), Size = new Size(250, 20), ForeColor = WinColor.Yellow, Font = new Font("Consolas", 11F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };

            _btnNextPage = new Button { Text = "다음 5000건 ▶", Location = new Point(400, 5), Size = new Size(120, 28), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat, ForeColor = WinColor.White, Cursor = Cursors.Hand, Enabled = false };
            _btnNextPage.FlatAppearance.BorderSize = 0;
            _btnNextPage.Click += BtnNextPage_Click;

            _lblGridStatus = new WinLabel { Text = "⚠️ 화면 응답성 보호를 위해 페이징 처리되었습니다.", Location = new Point(540, 10), Size = new Size(600, 20), ForeColor = WinColor.Gray, Font = new Font("Consolas", 10F, FontStyle.Regular), Visible = false };

            pnlGridTop.Controls.AddRange(new Control[] { _btnPrevPage, _lblPageInfo, _btnNextPage, _lblGridStatus });
            pnlMainContent.Controls.Add(pnlGridTop);

            _gridEvents = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = WinColor.FromArgb(20, 20, 20),
                ForeColor = WinColor.WhiteSmoke,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
            };

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null, _gridEvents, new object[] { true });

            pnlMainContent.Controls.Add(_gridEvents);
            _gridEvents.BringToFront();
        }
    }
}