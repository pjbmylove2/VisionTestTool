using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Tool.PacketParser
{
    // ==========================================
    // 1. 데이터 모델 (Data Model)
    // ==========================================
    public class ParsedItem
    {
        public string ByteRange { get; set; }
        public string ItemName { get; set; }
        public string Value { get; set; }
        public string RawBytes { get; set; }
    }

    // ==========================================
    // 2. 비즈니스 로직 (패킷 파싱 엔진)
    // ==========================================
    public class PacketParserEngine
    {
        // 🌟 PLC -> OCR (Receive) : 48 Bytes (또는 58 Bytes)
        public List<ParsedItem> ParsePlcToOcr(byte[] data)
        {
            var results = new List<ParsedItem>();
            try
            {
                if (data.Length < 46) throw new ArgumentException($"데이터가 너무 짧습니다. Receive 패킷은 최소 48바이트 이상이어야 합니다. (입력: {data.Length} bytes)");

                // 1. Rewinder Encoder Count (Byte 0-3)
                uint rewinder = BitConverter.ToUInt32(data, 0);
                results.Add(new ParsedItem { ByteRange = "0~3", ItemName = "Rewinder Encoder Count", Value = rewinder.ToString("N0"), RawBytes = GetRaw(data, 0, 4) });

                // 2. Link Status (Byte 4)
                byte b4 = data[4];
                string runStatus = (b4 & 0x01) != 0 ? "Start" : "Stop";
                string errorStatus = (b4 & 0x80) != 0 ? "Error" : "Normal";
                results.Add(new ParsedItem { ByteRange = "4", ItemName = "Link Status (설비 상태)", Value = $"Run: {runStatus}, Err: {errorStatus}", RawBytes = GetRaw(data, 4, 1) });

                // 3. Vision Command (Byte 5)
                byte b5 = data[5];
                string visCmd = (b5 & 0x01) != 0 ? "Start" : ((b5 & 0x02) != 0 ? "Stop" : "None");
                results.Add(new ParsedItem { ByteRange = "5", ItemName = "Vision Command", Value = visCmd, RawBytes = GetRaw(data, 5, 1) });

                // 4. NG Tag Marking Position (Byte 6)
                byte b6 = data[6];
                string ngTag = "None";
                if ((b6 & 0x02) != 0) ngTag = "Left(Normal)";
                else if ((b6 & 0x04) != 0) ngTag = "Right(Reverse)";
                results.Add(new ParsedItem { ByteRange = "6", ItemName = "NG Tag Marking Position", Value = ngTag, RawBytes = GetRaw(data, 6, 1) });

                // 5. Link Status 2 (Byte 7)
                byte b7 = data[7];
                string mPlc = (b7 & 0x01) != 0 ? "Connected" : "Disconnected";
                string hb = (b7 & 0x02) != 0 ? "HB ON" : "HB OFF";
                string api = (b7 & 0x04) != 0 ? "Connected" : "Disconnected";
                results.Add(new ParsedItem { ByteRange = "7", ItemName = "Link Status (API 연결상태)", Value = $"M-PLC: {mPlc}, HB: {hb}, API: {api}", RawBytes = GetRaw(data, 7, 1) });

                // 6. Unwinder Encoder Count (Byte 8-11)
                uint unwinder = BitConverter.ToUInt32(data, 8);
                results.Add(new ParsedItem { ByteRange = "8~11", ItemName = "Unwinder Encoder Count", Value = unwinder.ToString("N0"), RawBytes = GetRaw(data, 8, 4) });

                // 7. LOT ID1 (Byte 12-21) - 10자리 문자열
                string lotId1 = Encoding.ASCII.GetString(data, 12, 10).Trim('\0', ' ');
                results.Add(new ParsedItem { ByteRange = "12~21", ItemName = "LOT ID 1", Value = string.IsNullOrWhiteSpace(lotId1) ? "(Empty)" : lotId1, RawBytes = GetRaw(data, 12, 10) });

                // 8. Model Info (Byte 22-41) - 20자리 문자열
                string modelInfo = Encoding.ASCII.GetString(data, 22, 20).Trim('\0', ' ');
                results.Add(new ParsedItem { ByteRange = "22~41", ItemName = "Model Info", Value = string.IsNullOrWhiteSpace(modelInfo) ? "(Empty)" : modelInfo, RawBytes = GetRaw(data, 22, 20) });

                // 9. Equipment Speed (Byte 42-45)
                uint speed = BitConverter.ToUInt32(data, 42);
                results.Add(new ParsedItem { ByteRange = "42~45", ItemName = "Equipment Speed", Value = $"{speed} M/Min", RawBytes = GetRaw(data, 42, 4) });

                // 10. LOT ID2 & Line End (가변 길이 처리 방어)
                if (data.Length >= 58)
                {
                    string lotId2 = Encoding.ASCII.GetString(data, 46, 10).Trim('\0', ' ');
                    results.Add(new ParsedItem { ByteRange = "46~55", ItemName = "LOT ID 2", Value = string.IsNullOrWhiteSpace(lotId2) ? "(Empty)" : lotId2, RawBytes = GetRaw(data, 46, 10) });
                    results.Add(new ParsedItem { ByteRange = "56~57", ItemName = "Line End (CR/LF)", Value = "CR LF", RawBytes = GetRaw(data, 56, 2) });
                }
                else
                {
                    // 48바이트인 경우 46~47이 Line End
                    results.Add(new ParsedItem { ByteRange = "46~47", ItemName = "Line End (CR/LF)", Value = "CR LF", RawBytes = GetRaw(data, 46, 2) });
                }
            }
            catch (Exception ex)
            {
                results.Add(new ParsedItem { ByteRange = "ERROR", ItemName = "Parsing Failure", Value = ex.Message, RawBytes = "N/A" });
            }
            return results;
        }

        // 🌟 OCR -> PLC (Send) : 30 Bytes
        public List<ParsedItem> ParseOcrToPlc(byte[] data)
        {
            var results = new List<ParsedItem>();
            try
            {
                if (data.Length < 30) throw new ArgumentException($"데이터가 너무 짧습니다. Send 패킷은 30바이트여야 합니다. (입력: {data.Length} bytes)");

                // 1. Inspection Status (Byte 0-1)
                ushort statusWord = BitConverter.ToUInt16(data, 0);
                List<string> statusFlags = new List<string>();
                if ((statusWord & (1 << 1)) != 0) statusFlags.Add("검사 중");
                if ((statusWord & (1 << 2)) != 0) statusFlags.Add("검사 정지 상태");
                if ((statusWord & (1 << 3)) != 0) statusFlags.Add("테스트 모드");
                if ((statusWord & (1 << 9)) != 0) statusFlags.Add("에러(카메라 끊김)");
                if ((statusWord & (1 << 10)) != 0) statusFlags.Add("에러(공유폴더 끊김)");

                string statusStr = statusFlags.Count > 0 ? string.Join(", ", statusFlags) : "대기 (None)";
                results.Add(new ParsedItem { ByteRange = "0~1", ItemName = "Inspection Status", Value = statusStr, RawBytes = GetRaw(data, 0, 2) });

                // 2. Line Position (Byte 2-3)
                ushort linePos = BitConverter.ToUInt16(data, 2);
                results.Add(new ParsedItem { ByteRange = "2~3", ItemName = "Line Position (유지부 위치)", Value = linePos.ToString("N0"), RawBytes = GetRaw(data, 2, 2) });

                // 3. Trigger Count (Byte 4-5)
                ushort triggerCnt = BitConverter.ToUInt16(data, 4);
                results.Add(new ParsedItem { ByteRange = "4~5", ItemName = "Trigger Count (비전 카운트)", Value = triggerCnt.ToString("N0"), RawBytes = GetRaw(data, 4, 2) });

                // 4. Rewinder Encoder Count (Byte 6-9)
                uint rewinder = BitConverter.ToUInt32(data, 6);
                results.Add(new ParsedItem { ByteRange = "6~9", ItemName = "Rewinder Encoder Count", Value = rewinder.ToString("N0"), RawBytes = GetRaw(data, 6, 4) });

                // 5. Ref. Pos (Byte 10-11)
                ushort refPos = BitConverter.ToUInt16(data, 10);
                string refPosStr = refPos == 0 ? "NG (0)" : (refPos == 1 ? "A (1)" : (refPos == 2 ? "Y (2)" : refPos.ToString()));
                results.Add(new ParsedItem { ByteRange = "10~11", ItemName = "Ref. Pos", Value = refPosStr, RawBytes = GetRaw(data, 10, 2) });

                // 6. Lane No (Byte 12-13)
                ushort laneNo = BitConverter.ToUInt16(data, 12);
                results.Add(new ParsedItem { ByteRange = "12~13", ItemName = "Lane No", Value = laneNo.ToString(), RawBytes = GetRaw(data, 12, 2) });

                // 7. OCR Result / Defect Code (Byte 14-15)
                ushort defCode = BitConverter.ToUInt16(data, 14);
                results.Add(new ParsedItem { ByteRange = "14~15", ItemName = "OCR Result / Defect Code", Value = defCode.ToString(), RawBytes = GetRaw(data, 14, 2) });

                // 8. Realtime BCD Pos (Byte 16-17)
                ushort bcdPos = BitConverter.ToUInt16(data, 16);
                results.Add(new ParsedItem { ByteRange = "16~17", ItemName = "Realtime BCD Pos", Value = bcdPos.ToString(), RawBytes = GetRaw(data, 16, 2) });

                // 9. Camera No (Byte 18-19)
                ushort camNo = BitConverter.ToUInt16(data, 18);
                string camStr = camNo.ToString();
                if (camNo == 1) camStr = "Middle (1)";
                else if (camNo == 2) camStr = "Upper (2) / Middle (2)";
                else if (camNo == 3) camStr = "Upper (3)";
                else if (camNo == 4) camStr = "Lower (4)";
                results.Add(new ParsedItem { ByteRange = "18~19", ItemName = "Camera No", Value = camStr, RawBytes = GetRaw(data, 18, 2) });

                // 10. Unwinder Encoder Count (Byte 20-23)
                uint unwinder = BitConverter.ToUInt32(data, 20);
                results.Add(new ParsedItem { ByteRange = "20~23", ItemName = "Unwinder Encoder Count", Value = unwinder.ToString("N0"), RawBytes = GetRaw(data, 20, 4) });

                // 11. Vision Status (Byte 24-25)
                ushort visStat = BitConverter.ToUInt16(data, 24);
                string continuousNg = (visStat & (1 << 0)) != 0 ? "연속 불량 ON" : "정상";
                string hb = (visStat & (1 << 4)) != 0 ? "HB ON" : "HB OFF";
                results.Add(new ParsedItem { ByteRange = "24~25", ItemName = "Vision Status", Value = $"NG: {continuousNg}, HB: {hb}", RawBytes = GetRaw(data, 24, 2) });

                // 12. BCD Pos (Byte 26-27)
                ushort bcdPosFinal = BitConverter.ToUInt16(data, 26);
                results.Add(new ParsedItem { ByteRange = "26~27", ItemName = "BCD Pos", Value = bcdPosFinal.ToString(), RawBytes = GetRaw(data, 26, 2) });

                // 13. Line End (Byte 28-29)
                results.Add(new ParsedItem { ByteRange = "28~29", ItemName = "Line End (CR/LF)", Value = "CR LF", RawBytes = GetRaw(data, 28, 2) });
            }
            catch (Exception ex)
            {
                results.Add(new ParsedItem { ByteRange = "ERROR", ItemName = "Parsing Failure", Value = ex.Message, RawBytes = "N/A" });
            }
            return results;
        }

        private string GetRaw(byte[] data, int start, int len)
        {
            if (start + len > data.Length) return "OOR";
            return string.Join(" ", data.Skip(start).Take(len).Select(b => b.ToString("D")));
        }

        public byte[] ExtractBytesFromString(string input)
        {
            int colonIdx = input.IndexOf("Received:");
            if (colonIdx == -1) colonIdx = input.IndexOf("Send:");
            if (colonIdx != -1) input = input.Substring(colonIdx + 9);

            int parenIdx = input.IndexOf("(");
            if (parenIdx != -1) input = input.Substring(0, parenIdx);

            string[] parts = input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<byte> bytes = new List<byte>();

            foreach (string p in parts)
            {
                if (byte.TryParse(p.Trim(), out byte b))
                {
                    bytes.Add(b);
                }
            }
            return bytes.ToArray();
        }
    }

    // ==========================================
    // 3. UI 로직 (MainForm) - 상/하 스플릿 뷰어 구성
    // ==========================================
    public class MainForm : Form
    {
        // Receive 패널 컨트롤
        private TextBox _txtRxInput;
        private Button _btnRxParse;
        private WinLabel _lblRxByteCount;
        private DataGridView _gridRx;

        // Send 패널 컨트롤
        private TextBox _txtTxInput;
        private Button _btnTxParse;
        private WinLabel _lblTxByteCount;
        private DataGridView _gridTx;

        private readonly PacketParserEngine _parser;

        public MainForm()
        {
            _parser = new PacketParserEngine();
            InitializeSplitUI();
        }

        private void BtnRxParse_Click(object sender, EventArgs e)
        {
            ProcessParsing(_txtRxInput, _lblRxByteCount, _gridRx, isReceive: true);
        }

        private void BtnTxParse_Click(object sender, EventArgs e)
        {
            ProcessParsing(_txtTxInput, _lblTxByteCount, _gridTx, isReceive: false);
        }

        // 🌟 [핵심] 중복 코드를 제거한 통합 파싱 파이프라인
        private void ProcessParsing(TextBox txtInput, WinLabel lblCount, DataGridView grid, bool isReceive)
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text)) return;

            byte[] data = _parser.ExtractBytesFromString(txtInput.Text);
            lblCount.Text = $"추출: {data.Length} bytes";
            lblCount.ForeColor = data.Length == (isReceive ? 48 : 30) || data.Length == 58 ? WinColor.Yellow : WinColor.Tomato;

            if (data.Length == 0)
            {
                MessageBox.Show("해석할 수 있는 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            List<ParsedItem> result = isReceive ? _parser.ParsePlcToOcr(data) : _parser.ParseOcrToPlc(data);

            grid.DataSource = null;
            grid.DataSource = result;

            // 1100 픽셀 와이드 해상도에 맞춘 컬럼 최적화
            grid.Columns["ByteRange"].Width = 80;
            grid.Columns["ByteRange"].HeaderText = "바이트";

            grid.Columns["ItemName"].Width = 300;
            grid.Columns["ItemName"].HeaderText = "항목 명 (Item)";

            grid.Columns["Value"].Width = 400;
            grid.Columns["Value"].HeaderText = "해석된 값 (Decoded Value)";

            grid.Columns["RawBytes"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["RawBytes"].HeaderText = "원시 바이트 (Raw)";
        }

        private void InitializeSplitUI()
        {
            this.Text = "Vision PLC Packet Parser Pro (Dual Split Viewer)";
            this.Size = new Size(1100, 900); // 🌟 대폭 확장된 UI
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 10.5F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 상/하 스플리터 컨테이너 생성
            SplitContainer splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 420, // 정확히 반반 비율 적용
                SplitterWidth = 5,
                BackColor = WinColor.FromArgb(62, 62, 66)
            };
            this.Controls.Add(splitContainer);

            // ========================================================
            // 상단 패널 구성 (Receive: PLC -> OCR)
            // ========================================================
            Panel pnlRx = new Panel { Dock = DockStyle.Fill, BackColor = WinColor.FromArgb(30, 30, 30), Padding = new Padding(10) };

            var lblRxTitle = new WinLabel { Text = "▼ [RECEIVED] PLC ➔ OCR PC (48 or 58 Bytes) 로그 입력:", Location = new Point(10, 10), Size = new Size(600, 20), ForeColor = WinColor.LimeGreen, Font = new Font("Consolas", 11F, FontStyle.Bold) };
            _txtRxInput = new TextBox { Location = new Point(10, 35), Height = 25, BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _txtRxInput.Width = pnlRx.Width - 160;

            _btnRxParse = new Button { Text = "PARSE RX", Location = new Point(pnlRx.Width - 140, 33), Size = new Size(120, 28), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 10F, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnRxParse.FlatAppearance.BorderSize = 0;
            _btnRxParse.Click += BtnRxParse_Click;

            _lblRxByteCount = new WinLabel { Text = "추출: 0 bytes", Location = new Point(10, 65), Size = new Size(200, 20), ForeColor = WinColor.Gray };

            _gridRx = CreateStyledGrid();

            pnlRx.Controls.AddRange(new Control[] { lblRxTitle, _txtRxInput, _btnRxParse, _lblRxByteCount, _gridRx });
            splitContainer.Panel1.Controls.Add(pnlRx);

            // ========================================================
            // 하단 패널 구성 (Send: OCR -> PLC)
            // ========================================================
            Panel pnlTx = new Panel { Dock = DockStyle.Fill, BackColor = WinColor.FromArgb(30, 30, 30), Padding = new Padding(10) };

            var lblTxTitle = new WinLabel { Text = "▲ [SEND] OCR PC ➔ PLC (30 Bytes) 로그 입력:", Location = new Point(10, 10), Size = new Size(600, 20), ForeColor = WinColor.Orange, Font = new Font("Consolas", 11F, FontStyle.Bold) };
            _txtTxInput = new TextBox { Location = new Point(10, 35), Height = 25, BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _txtTxInput.Width = pnlTx.Width - 160;

            _btnTxParse = new Button { Text = "PARSE TX", Location = new Point(pnlTx.Width - 140, 33), Size = new Size(120, 28), BackColor = WinColor.FromArgb(204, 74, 74), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 10F, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnTxParse.FlatAppearance.BorderSize = 0;
            _btnTxParse.Click += BtnTxParse_Click;

            _lblTxByteCount = new WinLabel { Text = "추출: 0 bytes", Location = new Point(10, 65), Size = new Size(200, 20), ForeColor = WinColor.Gray };

            _gridTx = CreateStyledGrid();

            pnlTx.Controls.AddRange(new Control[] { lblTxTitle, _txtTxInput, _btnTxParse, _lblTxByteCount, _gridTx });
            splitContainer.Panel2.Controls.Add(pnlTx);

            // 리사이즈 이벤트 시 수동 레이아웃 보정
            pnlRx.Resize += (s, e) => { _txtRxInput.Width = pnlRx.Width - 160; _btnRxParse.Left = pnlRx.Width - 140; };
            pnlTx.Resize += (s, e) => { _txtTxInput.Width = pnlTx.Width - 160; _btnTxParse.Left = pnlTx.Width - 140; };
        }

        // 🌟 중복되는 그리드 스타일링을 메서드로 통합
        private DataGridView CreateStyledGrid()
        {
            return new DataGridView
            {
                Location = new Point(10, 90),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Height = 310, // SplitContainer 내에서 유동적으로 변함
                Width = 1060,
                BackgroundColor = WinColor.FromArgb(45, 45, 48),
                ForeColor = WinColor.Black,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };
        }
    }
}