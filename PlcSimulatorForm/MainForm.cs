using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionTestTool.VirtualPlc
{
    /// <summary>
    /// [비전SW 호환 가상 PLC 서버]
    /// Roll-Map OCR 인터페이스 맵(PLC→PC 최대 60B(원본 58B) / PC→PLC 최대 30B)을 구현한 가상 PLC 서버입니다.
    ///
    /// 구 비전SW(현장 개발버전)와 신 비전SW는 "체크된 항목만" 순서대로 이어 붙이고 CR/LF를 붙이는 가변 길이 패킷을 씁니다.
    ///  - PLC→PC: Rewinder(4) · B4~5 PLC상태+Vision Command(2) · B6~7 NG Tag+Link Status(2) · Unwinder(4)
    ///            · LOT ID1(10) · Model(20) · Speed(4) · LOT ID2(10) · [수정] Image Status+Spare(2) · CR LF
    ///            ([수정] 항목은 F2 창에서 체크 시에만 송신 → 해제 시 원본 58B, 체크 시 60B)
    ///            → 신 비전SW 시스템 > 소켓 > "수신 프로토콜" 체크와 같게 맞춥니다.
    ///              (구·신 비전SW의 "PLC 상태", "NG 태그" 체크는 패킷 길이에 영향이 없습니다.
    ///               B4~5는 "Vision Command", B6~7은 "Link Status" 체크에 묶여 있습니다.)
    ///  - PC→PLC: Vision Status(2) · Line Pos(2) · Trigger Count(2) · Rewinder(4) · Ref Pos(2) · Lane No(2)
    ///            · OCR Result(2) · Realtime BCD(2) · Camera No(2) · Unwinder(4) · Heartbeat 워드(2) · BCD Pos(2) · CR LF
    ///            → 신 비전SW 시스템 > 소켓 > "송신 프로토콜" 체크와 같게 맞춥니다.
    ///  - 길이가 설정과 다른 패킷은 구 비전SW와 같이 해석하지 않고 버리며, 실제 길이를 화면에 표시합니다.
    ///
    /// 항목 체크창은 디자이너 파일을 바꾸지 않도록 코드로 만든 별도 창입니다(F2 또는 폼 우클릭 메뉴로 다시 열기).
    ///
    /// [설비 타입별 엔코더 증가] (구 비전SW 영상 처리 스레드 확인 결과, F2 창에서 선택)
    ///  - 카메라 설비(COATOR·ROLLPRESS·SLITTER·WINDER): 영상 취득 시점의 PLC Rewinder·Unwinder를 그대로 씀 → 둘 다 자동 증가.
    ///  - 공유폴더 설비(NOTCHING·LAMI1·LAMI2): 트리거·Rewinder는 이미지 파일명에서 읽음(PLC Rewinder 미사용) → Rewinder 수동 고정.
    ///    Unwinder는 파일명 5칸(날짜_시간_트리거_리와인더_cam)일 때만 PLC 값을 씀 → "Unwinder 자동 증가" 선택 시에만 증가.
    ///    6칸(…_리와인더_언와인더_cam)이면 Unwinder도 파일명 값이라 PLC 값은 쓰이지 않음.
    /// UI 컨트롤 접근과 백그라운드 소켓 송신 스레드는 잠금 스냅샷으로 분리했고, 송신 버퍼는 재사용합니다.
    /// </summary>
    public partial class VirtualPlcServerForm : Form
    {
        // ====================================================================
        // 1. 멤버 변수 및 상수 (Fields & Constants)
        // ====================================================================
        private const int TxMaxPacketSize = 60; // PLC -> PC (모든 항목 사용 시, 원본 58)
        private const int RxMaxPacketSize = 30; // PC -> PLC (모든 항목 사용 시)
        private const int RxGridRowCount = 15;
        private const byte CrByte = 13;      // 0x0D
        private const byte LfByte = 10;      // 0x0A

        private readonly object _stateLock = new object();
        private readonly object _clientLock = new object();

        // 송신용 사전 할당 버퍼 (Zero-Allocation)
        private readonly byte[] _txBuffer = new byte[TxMaxPacketSize];
        private readonly byte[] _latestTxSnapshot = new byte[TxMaxPacketSize];
        private int _latestTxLength;
        private byte[] _latestRxSnapshot = new byte[0];
        private PcToPlcLayout _latestRxLayout = new PcToPlcLayout();
        private readonly string[] _rxGridValueBuffer = new string[RxGridRowCount];
        private readonly string[] _rxGridOffsetBuffer = new string[RxGridRowCount];

        private TcpListener _listener;
        private TcpClient _connectedClient;
        private CancellationTokenSource _serverCts;

        private bool _syncingByteControls;
        private bool _isServerRunning;
        private bool _hasNewTxSnapshot;
        private bool _hasNewRxSnapshot;

        private long _txCount;
        private long _rxCount;
        private long _rxRejectedCount;
        private int _lastRejectedLength;
        private DateTime _lastTxTime;
        private DateTime _lastRxTime;
        private double _rxIntervalMs;

        // 백그라운드 송신 스레드가 읽는 스레드 안전 상태 구조체
        private PlcTxParameters _txParams;

        // 송수신 항목 구성(체크창). _stateLock으로 보호합니다.
        private PlcToPcLayout _txLayout = new PlcToPcLayout();
        private PcToPlcLayout _rxLayout = new PcToPlcLayout();
        private ProtocolLayoutForm _layoutForm;

        // ====================================================================
        // 2. 이벤트 선언 (Events)
        // ====================================================================
        // (단독 실행 폼이므로 외부 노출 이벤트 없음)

        // ====================================================================
        // 3. 생성자 및 Dispose 보조 (Constructors & Cleanup)
        // ====================================================================
        public VirtualPlcServerForm()
        {
            InitializeComponent();

            InitializeRxGrid();
            InitializeDefaultControls();
            InitializeLayoutWindow();
            BindEvents();

            CaptureUiToTxParameters();
            uiRefreshTimer.Start();
        }

        // ====================================================================
        // 4. 공개 API (Public Methods)
        // ====================================================================
        public void StartServer(int port)
        {
            StopServerInternal();

            _serverCts = new CancellationTokenSource();
            CancellationToken token = _serverCts.Token;

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isServerRunning = true;

            Interlocked.Exchange(ref _txCount, 0);
            Interlocked.Exchange(ref _rxCount, 0);
            Interlocked.Exchange(ref _rxRejectedCount, 0);

            UpdateServerUiState();

            // 1. 클라이언트 접속 수락 및 수신 루프 시작
            Task.Run(() => AcceptAndReceiveLoopAsync(token), token);

            // 2. 주기적 패킷 송신 루프 시작
            Task.Run(() => PeriodicSendLoopAsync(token), token);
        }

        public void StopServer()
        {
            StopServerInternal();
            UpdateServerUiState();
        }

        // ====================================================================
        // 5. 수명 주기 및 재정의 메서드 (Protected / Override)
        // ====================================================================
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ShowLayoutWindow();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F2)
            {
                ShowLayoutWindow();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopServerInternal();
            if (_layoutForm != null)
            {
                _layoutForm.AllowClose = true;
                _layoutForm.Close();
            }
            base.OnFormClosing(e);
        }

        // ====================================================================
        // 6. UI 이벤트 핸들러 (UI Event Handlers)
        // ====================================================================
        private void BtnStartServer_Click(object sender, EventArgs e)
        {
            try
            {
                StartServer((int)numPort.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"가상 PLC 서버 시작 실패:\n{ex.Message}",
                    "서버 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                StopServer();
            }
        }

        private void BtnStopServer_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        private void BtnSendOnce_Click(object sender, EventArgs e)
        {
            CaptureUiToTxParameters();
            SendSinglePacket(advanceSimulation: true);
        }

        private void Byte4Bits_CheckedChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB4Raw.Value;
                val = SetBit(val, 0, chkB4_StartStop.Checked);
                val = SetBit(val, 7, chkB4_Error.Checked);
                numB4Raw.Value = val;
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void NumB4Raw_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB4Raw.Value;
                chkB4_StartStop.Checked = IsBitSet(val, 0);
                chkB4_Error.Checked = IsBitSet(val, 7);
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void Byte5Controls_Changed(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                int selected = Math.Max(0, cmbVisionCommand.SelectedIndex);
                if (chkVisionCmdBitMode.Checked)
                {
                    // 비트 모드일 때: 0=0, 1=Bit0(1), 2=Bit1(2)
                    numB5Raw.Value = selected == 0 ? 0 : (1 << (selected - 1));
                }
                else
                {
                    // 정수 값 모드일 때: 0, 1, 2 그대로 전송
                    // (구·신 비전SW는 B5를 비트로 읽으므로 1=Start, 2=Stop은 두 모드 모두 같은 값입니다.)
                    numB5Raw.Value = selected;
                }
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void NumB5Raw_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB5Raw.Value;
                if (val <= 2)
                {
                    cmbVisionCommand.SelectedIndex = val;
                }
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void Byte6Controls_Changed(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                int selected = Math.Max(0, cmbNgMarkPos.SelectedIndex);
                if (chkNgMarkBitMode.Checked)
                {
                    // 맵의 Bit 0(None=1), Bit 1(Left=2), Bit 2(Right=4) 방식 대응
                    // 구·신 비전SW 해석: bit0 → 1, bit1 → 2, bit2 → 4 (낮은 비트 우선)
                    numB6Raw.Value = (byte)(1 << selected);
                }
                else
                {
                    // 일반 값 방식 (0=None, 1=Left, 2=Right) 대응
                    numB6Raw.Value = selected;
                }
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void NumB6Raw_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB6Raw.Value;
                if (!chkNgMarkBitMode.Checked && val <= 2)
                {
                    cmbNgMarkPos.SelectedIndex = val;
                }
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void Byte7Bits_CheckedChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB7Raw.Value;
                val = SetBit(val, 0, chkB7_MachinePlc.Checked);
                val = SetBit(val, 1, chkB7_Heartbeat.Checked);
                val = SetBit(val, 2, chkB7_ApiConn.Checked);
                numB7Raw.Value = val;
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void NumB7Raw_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB7Raw.Value;
                chkB7_MachinePlc.Checked = IsBitSet(val, 0);
                chkB7_Heartbeat.Checked = IsBitSet(val, 1);
                chkB7_ApiConn.Checked = IsBitSet(val, 2);
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void Byte56Bits_CheckedChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB56Raw.Value;
                val = SetBit(val, 0, chkB56_MirrorX.Checked);
                val = SetBit(val, 1, chkB56_FlipY.Checked);
                numB56Raw.Value = val;
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void CmbRotation_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                // Bit 2~5 회전은 정상 운용처럼 하나만 설정합니다(Bit 0·1·6·7은 Raw 값 유지).
                int selected = Math.Max(0, cmbRotation.SelectedIndex);
                byte val = (byte)numB56Raw.Value;
                for (int bit = 2; bit <= 5; bit++)
                {
                    val = SetBit(val, bit, selected == bit - 1);
                }
                numB56Raw.Value = val;
                UpdateRotationWarning(val);
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void NumB56Raw_ValueChanged(object sender, EventArgs e)
        {
            if (_syncingByteControls)
            {
                return;
            }

            _syncingByteControls = true;
            try
            {
                byte val = (byte)numB56Raw.Value;
                chkB56_MirrorX.Checked = IsBitSet(val, 0);
                chkB56_FlipY.Checked = IsBitSet(val, 1);

                // 회전 비트가 여러 개면 클라이언트 정책과 같이 최상위 비트를 표시합니다(PLC 오류 시험용).
                int selected = 0;
                for (int bit = 5; bit >= 2; bit--)
                {
                    if (IsBitSet(val, bit))
                    {
                        selected = bit - 1;
                        break;
                    }
                }
                cmbRotation.SelectedIndex = selected;
                UpdateRotationWarning(val);
            }
            finally
            {
                _syncingByteControls = false;
            }
            CaptureUiToTxParameters();
        }

        private void UpdateRotationWarning(byte imageStatus)
        {
            int rotationBits = 0;
            for (int bit = 2; bit <= 5; bit++)
            {
                if (IsBitSet(imageStatus, bit))
                {
                    rotationBits++;
                }
            }
            lblB56Warn.Text = rotationBits > 1 ? "회전 중복 → 최상위 적용" : string.Empty;
        }

        private void LayoutForm_LayoutChanged(object sender, EventArgs e)
        {
            lock (_stateLock)
            {
                _txLayout = _layoutForm.CreatePlcToPcLayout();
                _rxLayout = _layoutForm.CreatePcToPlcLayout();
            }
            UpdateLayoutSummary();
        }

        private void LayoutForm_EquipmentChanged(object sender, EventArgs e)
        {
            ApplyEquipmentSelection();
        }

        /// <summary>F2 창의 설비 타입 선택을 송신 스레드가 읽는 상태에 반영합니다(CaptureUiToTxParameters가 덮어쓰지 않는 필드).</summary>
        private void ApplyEquipmentSelection()
        {
            lock (_stateLock)
            {
                _txParams.SharedFolderEquipment = _layoutForm.IsSharedFolderEquipment;
                _txParams.AutoIncrementUnwinderOnly = _layoutForm.AutoIncrementUnwinder;
            }
        }

        private void UiRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            // 1. 서버 및 클라이언트 연결 상태 표시 갱신
            bool hasClient;
            string clientEndPoint = string.Empty;
            lock (_clientLock)
            {
                hasClient = _connectedClient != null && _connectedClient.Connected;
                if (hasClient)
                {
                    try
                    {
                        clientEndPoint = _connectedClient.Client.RemoteEndPoint?.ToString() ?? string.Empty;
                    }
                    catch
                    {
                        hasClient = false;
                    }
                }
            }

            if (!_isServerRunning)
            {
                UpdateLabelIfChanged(lblServerStatus, "● 서버 대기 중 (Stopped)", Color.Tomato);
            }
            else if (hasClient)
            {
                UpdateLabelIfChanged(lblServerStatus, $"● 클라이언트 연결됨 ({clientEndPoint})", Color.LimeGreen);
            }
            else
            {
                UpdateLabelIfChanged(lblServerStatus, $"● 클라이언트 접속 대기 중... (Port: {numPort.Value})", Color.Gold);
            }

            // 2. 송신(Tx) 스냅샷 UI 반영
            byte[] txCopy = null;
            uint currentRew = 0;
            uint currentUnw = 0;
            byte currentB7 = 0;
            DateTime txTime = default;

            lock (_stateLock)
            {
                if (_hasNewTxSnapshot)
                {
                    txCopy = new byte[_latestTxLength];
                    Buffer.BlockCopy(_latestTxSnapshot, 0, txCopy, 0, _latestTxLength);
                    currentRew = _txParams.RewinderCount;
                    currentUnw = _txParams.UnwinderCount;
                    currentB7 = _txParams.Byte7LinkStatus2;
                    txTime = _lastTxTime;
                    _hasNewTxSnapshot = false;
                }
            }

            if (txCopy != null)
            {
                _syncingByteControls = true;
                try
                {
                    if (!numRewinder.Focused)
                    {
                        numRewinder.Value = currentRew;
                    }
                    if (!numUnwinder.Focused)
                    {
                        numUnwinder.Value = currentUnw;
                    }
                    if (!numB7Raw.Focused)
                    {
                        numB7Raw.Value = currentB7;
                        chkB7_Heartbeat.Checked = IsBitSet(currentB7, 1);
                    }
                }
                finally
                {
                    _syncingByteControls = false;
                }

                long txTotal = Interlocked.Read(ref _txCount);
                lblTxSummary.Text = $"송신 시각: {txTime:HH:mm:ss.fff} | 크기: {txCopy.Length} Bytes | 누적 송신: {txTotal:N0} 패킷";
                UpdateTextBoxIfChanged(txtTxHex, FormatHexDump(txCopy));
            }

            // 3. 수신(Rx) 스냅샷 UI 반영
            byte[] rxCopy = null;
            PcToPlcLayout rxLayout = null;
            DateTime rxTime = default;
            double rxInterval = 0;

            lock (_stateLock)
            {
                if (_hasNewRxSnapshot)
                {
                    rxCopy = (byte[])_latestRxSnapshot.Clone();
                    rxLayout = _latestRxLayout.Copy();
                    rxTime = _lastRxTime;
                    rxInterval = _rxIntervalMs;
                    _hasNewRxSnapshot = false;
                }
            }

            if (rxCopy != null)
            {
                ParseAndRenderRxPacket(rxCopy, rxLayout, rxTime, rxInterval);
            }
            else
            {
                long rejected = Interlocked.Read(ref _rxRejectedCount);
                if (rejected > 0 && Interlocked.Read(ref _rxCount) == 0)
                {
                    int expected;
                    lock (_stateLock)
                    {
                        expected = _rxLayout.PacketLength;
                    }
                    lblRxSummary.Text = $"길이 불일치로 무시: {rejected:N0}건 (비전SW {Volatile.Read(ref _lastRejectedLength)}바이트, 시뮬레이터 기대 {expected}바이트) - F2 항목 설정 확인";
                }
            }
        }

        // ====================================================================
        // 7. 내부 비즈니스 로직 / 헬퍼 메서드 (Private Methods & Nested Types)
        // ====================================================================
        private void InitializeRxGrid()
        {
            typeof(DataGridView).InvokeMember(
                "DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null,
                grdRxParsed,
                new object[] { true });

            grdRxParsed.Columns.Clear();
            grdRxParsed.Columns.Add("colOffset", "Byte(Word)");
            grdRxParsed.Columns.Add("colName", "항목 (Item)");
            grdRxParsed.Columns.Add("colRaw", "Raw 값");
            grdRxParsed.Columns.Add("colMeaning", "해석 (Meaning)");

            grdRxParsed.Columns[0].Width = 95;
            grdRxParsed.Columns[1].Width = 175;
            grdRxParsed.Columns[2].Width = 110;
            grdRxParsed.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            foreach (DataGridViewColumn col in grdRxParsed.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            string[,] rows =
            {
                { "B0~1 (W0)", "Inspection Status", "-", "대기" },
                { "B2~3 (W1)", "Line Position", "-", "유지부 위치값 (0~65535)" },
                { "B4~5 (W2)", "Trigger Count", "-", "비전 카운트 데이터" },
                { "B6~9 (W3~4)", "Rewinder Encoder", "-", "검사 시점 Rewinder Count" },
                { "B10~11 (W5)", "Ref. Pos", "-", "NG:0, A:1, Y:2" },
                { "B12~13 (W6)", "Lane No", "-", "레인 번호 (A:1자리 / Y:3자리)" },
                { "B14~15 (W7)", "OCR Result / Defect", "-", "A:2자리 / Y:1자리" },
                { "B16~17 (W8)", "Realtime BCD Pos", "-", "실시간 DIO Count" },
                { "B18~19 (W9)", "Camera No", "-", "카메라 번호 (0~4)" },
                { "B20~23 (W10~11)", "Unwinder Encoder", "-", "검사 시점 Unwinder Count" },
                { "B24 (W12 Low)", "Vision Status (Bit 0)", "-", "연속 불량 (0:정상 / 1:연속NG)" },
                { "B24 (W12 Low)", "Vision Status (Bit 4)", "-", "PC 하트비트 (주기 토글)" },
                { "B25 (W12 High)", "Spare", "-", "예비 영역" },
                { "B26~27 (W13)", "BCD Pos", "-", "검사 시점 DIO Count" },
                { "B28~29 (W14)", "Line End (CR/LF)", "-", "0x0D 0x0A (2573)" }
            };

            for (int i = 0; i < rows.GetLength(0); i++)
            {
                grdRxParsed.Rows.Add(rows[i, 0], rows[i, 1], rows[i, 2], rows[i, 3]);
            }
            grdRxParsed.ClearSelection();
        }

        private void InitializeDefaultControls()
        {
            cmbVisionCommand.SelectedIndex = 0;
            cmbNgMarkPos.SelectedIndex = 0;
            cmbRotation.SelectedIndex = 0;
        }

        private void InitializeLayoutWindow()
        {
            _layoutForm = new ProtocolLayoutForm();
            _layoutForm.LayoutChanged += LayoutForm_LayoutChanged;
            _txLayout = _layoutForm.CreatePlcToPcLayout();
            _rxLayout = _layoutForm.CreatePcToPlcLayout();
            _layoutForm.EquipmentChanged += LayoutForm_EquipmentChanged;
            ApplyEquipmentSelection();

            var menu = new ContextMenuStrip();
            menu.Items.Add("송수신 항목 설정 (F2)", null, (s, e) => ShowLayoutWindow());
            ContextMenuStrip = menu;
        }

        private void ShowLayoutWindow()
        {
            if (_layoutForm == null || _layoutForm.IsDisposed)
            {
                return;
            }

            if (!_layoutForm.Visible)
            {
                _layoutForm.StartPosition = FormStartPosition.Manual;
                _layoutForm.Location = new Point(Right, Top);
                _layoutForm.Show(this);
            }
            _layoutForm.Activate();
            UpdateLayoutSummary();
        }

        private void UpdateLayoutSummary()
        {
            int txLength;
            int rxLength;
            lock (_stateLock)
            {
                txLength = _txLayout.PacketLength;
                rxLength = _rxLayout.PacketLength;
            }
            _layoutForm?.SetSummary(txLength, rxLength);
        }

        private void BindEvents()
        {
            btnStartServer.Click += BtnStartServer_Click;
            btnStopServer.Click += BtnStopServer_Click;
            btnSendOnce.Click += BtnSendOnce_Click;
            uiRefreshTimer.Tick += UiRefreshTimer_Tick;

            // Byte 4 양방향 연동
            chkB4_StartStop.CheckedChanged += Byte4Bits_CheckedChanged;
            chkB4_Error.CheckedChanged += Byte4Bits_CheckedChanged;
            numB4Raw.ValueChanged += NumB4Raw_ValueChanged;

            // Byte 5 양방향 연동
            cmbVisionCommand.SelectedIndexChanged += Byte5Controls_Changed;
            chkVisionCmdBitMode.CheckedChanged += Byte5Controls_Changed;
            numB5Raw.ValueChanged += NumB5Raw_ValueChanged;

            // Byte 6 양방향 연동
            cmbNgMarkPos.SelectedIndexChanged += Byte6Controls_Changed;
            chkNgMarkBitMode.CheckedChanged += Byte6Controls_Changed;
            numB6Raw.ValueChanged += NumB6Raw_ValueChanged;

            // Byte 7 양방향 연동
            chkB7_MachinePlc.CheckedChanged += Byte7Bits_CheckedChanged;
            chkB7_Heartbeat.CheckedChanged += Byte7Bits_CheckedChanged;
            chkB7_ApiConn.CheckedChanged += Byte7Bits_CheckedChanged;
            numB7Raw.ValueChanged += NumB7Raw_ValueChanged;

            // Byte 56 (수정버전 Image Status) 양방향 연동
            chkB56_MirrorX.CheckedChanged += Byte56Bits_CheckedChanged;
            chkB56_FlipY.CheckedChanged += Byte56Bits_CheckedChanged;
            cmbRotation.SelectedIndexChanged += CmbRotation_SelectedIndexChanged;
            numB56Raw.ValueChanged += NumB56Raw_ValueChanged;

            // 일반 값 변경 시 즉시 파라미터 스냅샷 갱신
            EventHandler syncHandler = (s, e) =>
            {
                if (!_syncingByteControls)
                {
                    CaptureUiToTxParameters();
                }
            };

            numSendInterval.ValueChanged += syncHandler;
            chkPeriodicSend.CheckedChanged += syncHandler;
            chkAutoIncrementEncoder.CheckedChanged += syncHandler;
            chkAutoToggleHeartbeat.CheckedChanged += syncHandler;
            numRewinder.ValueChanged += syncHandler;
            numUnwinder.ValueChanged += syncHandler;
            numSpeed.ValueChanged += syncHandler;
            chkSpeedAsciiMode.CheckedChanged += syncHandler;
            txtLotId1.TextChanged += syncHandler;
            txtLotId2.TextChanged += syncHandler;
            txtModelInfo.TextChanged += syncHandler;
            numB57Raw.ValueChanged += syncHandler;
        }

        private void CaptureUiToTxParameters()
        {
            lock (_stateLock)
            {
                _txParams.SendIntervalMs = (int)numSendInterval.Value;
                _txParams.PeriodicSendEnabled = chkPeriodicSend.Checked;
                _txParams.AutoIncrementEncoder = chkAutoIncrementEncoder.Checked;
                _txParams.AutoToggleHeartbeat = chkAutoToggleHeartbeat.Checked;
                _txParams.RewinderCount = (uint)numRewinder.Value;
                _txParams.UnwinderCount = (uint)numUnwinder.Value;
                _txParams.EquipmentSpeed = (int)numSpeed.Value;
                _txParams.SpeedAsAscii = chkSpeedAsciiMode.Checked;
                _txParams.LotId1 = txtLotId1.Text ?? string.Empty;
                _txParams.LotId2 = txtLotId2.Text ?? string.Empty;
                _txParams.ModelInfo = txtModelInfo.Text ?? string.Empty;
                _txParams.Byte4LinkStatus1 = (byte)numB4Raw.Value;
                _txParams.Byte5VisionCommand = (byte)numB5Raw.Value;
                _txParams.Byte6NgMarkPosition = (byte)numB6Raw.Value;
                _txParams.Byte7LinkStatus2 = (byte)numB7Raw.Value;
                _txParams.Byte56ImageStatus = (byte)numB56Raw.Value;
                _txParams.Byte57Spare = (byte)numB57Raw.Value;
            }
        }

        private async Task AcceptAndReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true; // Nagle 알고리즘 비활성화 (짧은 주기 패킷 지연 방지)

                    lock (_clientLock)
                    {
                        if (_connectedClient != null)
                        {
                            try
                            {
                                _connectedClient.Close();
                            }
                            catch
                            {
                            }
                        }
                        _connectedClient = client;
                    }

                    await ReceiveClientStreamAsync(client, token).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    if (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(300, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    if (client != null)
                    {
                        lock (_clientLock)
                        {
                            if (ReferenceEquals(_connectedClient, client))
                            {
                                _connectedClient = null;
                            }
                        }
                        try
                        {
                            client.Close();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        /// <summary>
        /// [TCP 스트림 프레이밍 - 구·신 비전SW와 같은 규칙]
        /// TCP는 패킷 경계가 없으므로 수신 바이트를 누적한 뒤, 패킷 시작에서 "항목 설정 길이" 끝에 CR/LF가 있을 때만 받아들입니다.
        /// 길이가 다르면 다음 CR/LF까지 버리고 다음 패킷 시작에 다시 맞춥니다(1바이트씩 밀어 엉뚱한 구간을 해석하지 않음).
        /// </summary>
        private async Task ReceiveClientStreamAsync(TcpClient client, CancellationToken token)
        {
            NetworkStream stream = client.GetStream();
            byte[] readBuf = new byte[1024];
            var accum = new List<byte>(256);

            while (!token.IsCancellationRequested && client.Connected)
            {
                int bytesRead = await stream.ReadAsync(readBuf, 0, readBuf.Length, token).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break; // 클라이언트 정상 종료
                }

                for (int i = 0; i < bytesRead; i++)
                {
                    accum.Add(readBuf[i]);
                }

                while (true)
                {
                    PcToPlcLayout layout;
                    lock (_stateLock)
                    {
                        layout = _rxLayout.Copy();
                    }

                    int expected = layout.PacketLength;
                    if (accum.Count < expected)
                    {
                        break;
                    }

                    if (accum[expected - 2] == CrByte && accum[expected - 1] == LfByte)
                    {
                        byte[] packet = accum.GetRange(0, expected).ToArray();
                        accum.RemoveRange(0, expected);
                        DateTime now = DateTime.Now;
                        lock (_stateLock)
                        {
                            _latestRxSnapshot = packet;
                            _latestRxLayout = layout;
                            if (_lastRxTime != default && now >= _lastRxTime)
                            {
                                _rxIntervalMs = (now - _lastRxTime).TotalMilliseconds;
                            }
                            _lastRxTime = now;
                            _hasNewRxSnapshot = true;
                        }

                        Interlocked.Increment(ref _rxCount);
                        continue;
                    }

                    int terminator = FindCrLf(accum);
                    if (terminator < 0)
                    {
                        if (accum.Count > readBuf.Length * 4)
                        {
                            accum.Clear();
                        }
                        break;
                    }

                    // 길이 불일치 패킷: 구 비전SW처럼 해석하지 않고 버림
                    Volatile.Write(ref _lastRejectedLength, terminator + 2);
                    Interlocked.Increment(ref _rxRejectedCount);
                    accum.RemoveRange(0, terminator + 2);
                }
            }
        }

        private static int FindCrLf(List<byte> buffer)
        {
            for (int i = 0; i + 1 < buffer.Count; i++)
            {
                if (buffer[i] == CrByte && buffer[i + 1] == LfByte)
                {
                    return i;
                }
            }
            return -1;
        }

        private async Task PeriodicSendLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int intervalMs = 100;
                bool shouldSend = false;

                lock (_stateLock)
                {
                    intervalMs = Math.Max(10, _txParams.SendIntervalMs);
                    shouldSend = _txParams.PeriodicSendEnabled;
                }

                if (shouldSend)
                {
                    SendSinglePacket(advanceSimulation: true);
                }

                try
                {
                    await Task.Delay(intervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void SendSinglePacket(bool advanceSimulation)
        {
            TcpClient client;
            lock (_clientLock)
            {
                client = _connectedClient;
            }

            lock (_stateLock)
            {
                if (advanceSimulation)
                {
                    if (_txParams.AutoIncrementEncoder && IsBitSet(_txParams.Byte4LinkStatus1, 0))
                    {
                        uint step = (uint)Math.Max(1, _txParams.EquipmentSpeed / 10);
                        if (!_txParams.SharedFolderEquipment)
                        {
                            // 카메라 설비: 검사 시점 PLC 엔코더를 쓰므로 둘 다 증가
                            _txParams.RewinderCount = unchecked(_txParams.RewinderCount + step);
                            _txParams.UnwinderCount = unchecked(_txParams.UnwinderCount + step);
                        }
                        else if (_txParams.AutoIncrementUnwinderOnly)
                        {
                            // 공유폴더 설비: Rewinder는 파일명 값이라 고정. Unwinder는 파일명 5칸일 때만 PLC 값을 씀
                            _txParams.UnwinderCount = unchecked(_txParams.UnwinderCount + step);
                        }
                    }

                    if (_txParams.AutoToggleHeartbeat)
                    {
                        bool hb = IsBitSet(_txParams.Byte7LinkStatus2, 1);
                        _txParams.Byte7LinkStatus2 = SetBit(_txParams.Byte7LinkStatus2, 1, !hb);
                    }
                }

                int length = BuildTxPacket(_txParams, _txLayout, _txBuffer);
                Buffer.BlockCopy(_txBuffer, 0, _latestTxSnapshot, 0, length);
                _latestTxLength = length;
                _lastTxTime = DateTime.Now;
                _hasNewTxSnapshot = true;

                if (client != null && client.Connected)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        stream.Write(_txBuffer, 0, length);
                        Interlocked.Increment(ref _txCount);
                    }
                    catch
                    {
                        // 송신 실패 시 수신 루프에서 연결 해제를 감지하여 정리함
                    }
                }
            }
        }

        /// <summary>
        /// [인터페이스 맵 + 구 비전SW 가변 길이] 체크된 항목만 순서대로 송신 버퍼에 조립하고 길이를 반환합니다.
        /// 모든 항목 사용 시 맵과 같은 58바이트(B0~57)입니다.
        /// </summary>
        private static int BuildTxPacket(PlcTxParameters p, PlcToPcLayout layout, byte[] buffer)
        {
            Array.Clear(buffer, 0, TxMaxPacketSize);
            int offset = 0;

            // Byte 0~3: Rewinder Encoder Count (UInt32, Little-Endian)
            if (layout.RewinderCount)
            {
                WriteUInt32Le(buffer, offset, p.RewinderCount);
                offset += 4;
            }

            // Byte 4~5: Link Status 1 (Bit 0: Start/Stop, Bit 7: Error) + Vision Command
            // 구·신 비전SW는 이 2바이트를 "Vision Command" 체크 하나로 읽습니다.
            if (layout.VisionCommand)
            {
                buffer[offset++] = p.Byte4LinkStatus1;
                buffer[offset++] = p.Byte5VisionCommand;
            }

            // Byte 6~7: NG Tag Marking Position + Link Status 2 (Bit 0: Machine PLC, Bit 1: Heartbeat, Bit 2: API)
            // 구·신 비전SW는 이 2바이트를 "Link Status" 체크 하나로 읽습니다.
            if (layout.LinkStatus)
            {
                buffer[offset++] = p.Byte6NgMarkPosition;
                buffer[offset++] = p.Byte7LinkStatus2;
            }

            // Byte 8~11: Unwinder Encoder Count (UInt32, Little-Endian)
            if (layout.UnwinderCount)
            {
                WriteUInt32Le(buffer, offset, p.UnwinderCount);
                offset += 4;
            }

            // Byte 12~21: LOT ID 1 (10자리 문자열)
            if (layout.LotId1)
            {
                WriteFixedAscii(buffer, offset, 10, p.LotId1);
                offset += 10;
            }

            // Byte 22~41: Model Info (20자리 문자열)
            if (layout.ModelInfo)
            {
                WriteFixedAscii(buffer, offset, 20, p.ModelInfo);
                offset += 20;
            }

            // Byte 42~45: Equipment Speed (구·신 비전SW는 Int32 Little-Endian으로 읽습니다. ASCII는 비교 시험용)
            if (layout.LineSpeed)
            {
                if (p.SpeedAsAscii)
                {
                    string speedStr = Math.Max(0, Math.Min(999, p.EquipmentSpeed)).ToString("D3");
                    WriteFixedAscii(buffer, offset, 4, speedStr);
                }
                else
                {
                    WriteInt32Le(buffer, offset, p.EquipmentSpeed);
                }
                offset += 4;
            }

            // Byte 46~55: LOT ID 2 (10자리 문자열)
            if (layout.LotId2)
            {
                WriteFixedAscii(buffer, offset, 10, p.LotId2);
                offset += 10;
            }

            // Byte 56~57 (수정버전): Image Status (Bit 0: X Mirror, Bit 1: Y Flip, Bit 2~5: Rotate 90/180/270/360) + Spare
            if (layout.ImageStatus)
            {
                buffer[offset++] = p.Byte56ImageStatus;
                buffer[offset++] = p.Byte57Spare;
            }

            // Line End (CR=13, LF=10 -> Word 0x0A0D = 2573)
            buffer[offset++] = CrByte;
            buffer[offset++] = LfByte;
            return offset;
        }

        private void ParseAndRenderRxPacket(byte[] rx, PcToPlcLayout layout, DateTime rxTime, double intervalMs)
        {
            // 구 비전SW 송신 함수와 같은 순서로 체크된 항목만 읽습니다. 미전송 항목은 "미전송"으로 표시합니다.
            int offset = 0;
            ushort inspStatus = 0;
            bool hasHeartbeatWord = false;
            byte visionStatus = 0;
            byte spare = 0;

            ReadUInt16Field(rx, layout.VisionStatus, ref offset, 0, out inspStatus);
            ReadUInt16Field(rx, layout.LinePos, ref offset, 1, out _);
            ReadUInt16Field(rx, layout.TriggerCount, ref offset, 2, out _);
            ReadUInt32Field(rx, layout.RewinderCount, ref offset, 3);
            ReadUInt16Field(rx, layout.RefPos, ref offset, 4, out _);
            ReadUInt16Field(rx, layout.LaneNo, ref offset, 5, out _);
            ReadUInt16Field(rx, layout.OcrResult, ref offset, 6, out _);
            ReadUInt16Field(rx, layout.RealtimeBcdPos, ref offset, 7, out _);
            ReadUInt16Field(rx, layout.CameraNo, ref offset, 8, out _);
            ReadUInt32Field(rx, layout.UnwinderCount, ref offset, 9);

            if (layout.Heartbeat)
            {
                hasHeartbeatWord = true;
                visionStatus = rx[offset];
                spare = rx[offset + 1];
                _rxGridOffsetBuffer[10] = $"B{offset} (bit0)";
                _rxGridOffsetBuffer[11] = $"B{offset} (bit4)";
                _rxGridOffsetBuffer[12] = $"B{offset + 1}";
                offset += 2;
            }
            else
            {
                _rxGridOffsetBuffer[10] = "-";
                _rxGridOffsetBuffer[11] = "-";
                _rxGridOffsetBuffer[12] = "-";
            }

            ReadUInt16Field(rx, layout.BcdPos, ref offset, 13, out _);

            ushort lineEnd = BitConverter.ToUInt16(rx, rx.Length - 2);
            _rxGridOffsetBuffer[14] = $"B{rx.Length - 2}~{rx.Length - 1}";
            _rxGridValueBuffer[10] = hasHeartbeatWord ? (IsBitSet(visionStatus, 0) ? "1 (연속 불량)" : "0 (정상)") : "미전송";
            _rxGridValueBuffer[11] = hasHeartbeatWord ? (IsBitSet(visionStatus, 4) ? "1 (ON)" : "0 (OFF)") : "미전송";
            _rxGridValueBuffer[12] = hasHeartbeatWord ? spare.ToString() : "미전송";
            _rxGridValueBuffer[14] = $"0x{lineEnd:X4} ({rx[rx.Length - 2]}, {rx[rx.Length - 1]})";

            for (int i = 0; i < RxGridRowCount; i++)
            {
                UpdateGridCellIfChanged(grdRxParsed, i, 0, _rxGridOffsetBuffer[i]);
                UpdateGridCellIfChanged(grdRxParsed, i, 2, _rxGridValueBuffer[i]);
            }

            UpdateGridCellIfChanged(grdRxParsed, 0, 3, layout.VisionStatus ? DescribeInspectionStatus(inspStatus) : "미전송");

            long rxTotal = Interlocked.Read(ref _rxCount);
            long rejected = Interlocked.Read(ref _rxRejectedCount);
            string rejectText = rejected > 0 ? $" | 길이 불일치 무시: {rejected:N0}건({Volatile.Read(ref _lastRejectedLength)}B)" : string.Empty;
            lblRxSummary.Text = $"수신 시각: {rxTime:HH:mm:ss.fff} | {rx.Length}B | 누적 수신: {rxTotal:N0}건 | 주기: {intervalMs:F0} ms{rejectText}";
            UpdateTextBoxIfChanged(txtRxHex, FormatHexDump(rx));
        }

        private void ReadUInt16Field(byte[] rx, bool enabled, ref int offset, int row, out ushort value)
        {
            if (!enabled)
            {
                value = 0;
                _rxGridOffsetBuffer[row] = "-";
                _rxGridValueBuffer[row] = "미전송";
                return;
            }

            value = BitConverter.ToUInt16(rx, offset);
            _rxGridOffsetBuffer[row] = $"B{offset}~{offset + 1}";
            _rxGridValueBuffer[row] = value.ToString();
            offset += 2;
        }

        private void ReadUInt32Field(byte[] rx, bool enabled, ref int offset, int row)
        {
            if (!enabled)
            {
                _rxGridOffsetBuffer[row] = "-";
                _rxGridValueBuffer[row] = "미전송";
                return;
            }

            uint value = BitConverter.ToUInt32(rx, offset);
            _rxGridOffsetBuffer[row] = $"B{offset}~{offset + 3}";
            _rxGridValueBuffer[row] = value.ToString("N0");
            offset += 4;
        }

        private static string DescribeInspectionStatus(ushort status)
        {
            // 구 비전SW는 1/2/4/9/10만 비트로 보내고 나머지 값은 0으로 보냅니다.
            switch (status)
            {
                case 0:
                    return "0 : 상태 없음 (구 비전SW는 1/2/4/9/10 외 값을 0으로 보냄)";
                case 1:
                    return "1 : 검사 중 (Running)";
                case 2:
                    return "2 : 검사 정지 상태 (Stopped)";
                case 3:
                    return "3 : 테스트 모드 (Test Mode)";
                case 4:
                    return "4 : bit2 (구 비전SW 예약 값)";
                case 9:
                    return "9 : 에러 (카메라 끊김)";
                case 10:
                    return "10 : 에러 (공유 폴더 끊김)";
                default:
                    return $"정의되지 않은 상태 코드 ({status})";
            }
        }

        private void StopServerInternal()
        {
            _isServerRunning = false;

            if (_serverCts != null)
            {
                try
                {
                    _serverCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                _serverCts.Dispose();
                _serverCts = null;
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }
                _listener = null;
            }

            lock (_clientLock)
            {
                if (_connectedClient != null)
                {
                    try
                    {
                        _connectedClient.Close();
                    }
                    catch
                    {
                    }
                    _connectedClient = null;
                }
            }
        }

        private void UpdateServerUiState()
        {
            btnStartServer.Enabled = !_isServerRunning;
            btnStopServer.Enabled = _isServerRunning;
            numPort.Enabled = !_isServerRunning;
        }

        private static void WriteUInt32Le(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteInt32Le(byte[] buffer, int offset, int value)
        {
            WriteUInt32Le(buffer, offset, unchecked((uint)value));
        }

        private static void WriteFixedAscii(byte[] buffer, int offset, int length, string text)
        {
            for (int i = 0; i < length; i++)
            {
                buffer[offset + i] = 0x20; // 공백(' ') 패딩
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            byte[] asciiBytes = Encoding.ASCII.GetBytes(text);
            int copyLen = Math.Min(length, asciiBytes.Length);
            Buffer.BlockCopy(asciiBytes, 0, buffer, offset, copyLen);
        }

        private static bool IsBitSet(byte value, int bitIndex) => (value & (1 << bitIndex)) != 0;

        private static byte SetBit(byte value, int bitIndex, bool enabled)
        {
            if (enabled)
            {
                return (byte)(value | (1 << bitIndex));
            }
            return (byte)(value & ~(1 << bitIndex));
        }

        private static string FormatHexDump(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 4);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0 && i % 16 == 0)
                {
                    sb.Append("\r\n");
                }
                sb.AppendFormat("[{0:D2}]{1:X2} ", i, bytes[i]);
            }
            return sb.ToString();
        }

        private static void UpdateGridCellIfChanged(DataGridView grid, int row, int col, string newVal)
        {
            if (row < 0 || row >= grid.Rows.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[row].Cells[col];
            string cur = cell.Value as string;
            if (!string.Equals(cur, newVal, StringComparison.Ordinal))
            {
                cell.Value = newVal;
            }
        }

        private static void UpdateTextBoxIfChanged(TextBox box, string newText)
        {
            if (box.Focused && box.SelectionLength > 0)
            {
                return;
            }

            if (!string.Equals(box.Text, newText, StringComparison.Ordinal))
            {
                box.Text = newText;
            }
        }

        private static void UpdateLabelIfChanged(Label lbl, string text, Color foreColor)
        {
            if (!string.Equals(lbl.Text, text, StringComparison.Ordinal))
            {
                lbl.Text = text;
            }
            if (lbl.ForeColor != foreColor)
            {
                lbl.ForeColor = foreColor;
            }
        }

        private struct PlcTxParameters
        {
            public int SendIntervalMs;
            public bool PeriodicSendEnabled;
            public bool AutoIncrementEncoder;
            public bool SharedFolderEquipment;      // NOTCHING·LAMI1·LAMI2 (F2 창 선택)
            public bool AutoIncrementUnwinderOnly;  // 공유폴더 설비에서 Unwinder만 자동 증가
            public bool AutoToggleHeartbeat;
            public uint RewinderCount;
            public uint UnwinderCount;
            public int EquipmentSpeed;
            public bool SpeedAsAscii;
            public string LotId1;
            public string LotId2;
            public string ModelInfo;
            public byte Byte4LinkStatus1;
            public byte Byte5VisionCommand;
            public byte Byte6NgMarkPosition;
            public byte Byte7LinkStatus2;
            public byte Byte56ImageStatus;
            public byte Byte57Spare;
        }

        /// <summary>PLC→PC 송신 항목. 신 비전SW "수신 프로토콜" 체크와 같은 순서·길이입니다.</summary>
        private sealed class PlcToPcLayout
        {
            public bool RewinderCount = true;
            public bool VisionCommand = true;
            public bool LinkStatus = true;
            public bool UnwinderCount = true;
            public bool LotId1 = true;
            public bool ModelInfo = true;
            public bool LineSpeed = true;
            public bool LotId2 = true;
            public bool ImageStatus = false; // 수정버전 W28 (B56 Image Status + B57 Spare)

            public int PacketLength =>
                2 + (RewinderCount ? 4 : 0) + (VisionCommand ? 2 : 0) + (LinkStatus ? 2 : 0) + (UnwinderCount ? 4 : 0)
                + (LotId1 ? 10 : 0) + (ModelInfo ? 20 : 0) + (LineSpeed ? 4 : 0) + (LotId2 ? 10 : 0) + (ImageStatus ? 2 : 0);
        }

        /// <summary>PC→PLC 수신 항목. 신 비전SW "송신 프로토콜" 체크와 같은 순서·길이입니다.</summary>
        private sealed class PcToPlcLayout
        {
            public bool VisionStatus = true;
            public bool LinePos = true;
            public bool TriggerCount = true;
            public bool RewinderCount = true;
            public bool RefPos = true;
            public bool LaneNo = true;
            public bool OcrResult = true;
            public bool RealtimeBcdPos = true;
            public bool CameraNo = true;
            public bool UnwinderCount = true;
            public bool Heartbeat = true;
            public bool BcdPos = true;

            public int PacketLength =>
                2 + (VisionStatus ? 2 : 0) + (LinePos ? 2 : 0) + (TriggerCount ? 2 : 0) + (RewinderCount ? 4 : 0)
                + (RefPos ? 2 : 0) + (LaneNo ? 2 : 0) + (OcrResult ? 2 : 0) + (RealtimeBcdPos ? 2 : 0)
                + (CameraNo ? 2 : 0) + (UnwinderCount ? 4 : 0) + (Heartbeat ? 2 : 0) + (BcdPos ? 2 : 0);

            public PcToPlcLayout Copy()
            {
                return (PcToPlcLayout)MemberwiseClone();
            }
        }

        /// <summary>
        /// 송수신 항목 체크창. 디자이너 없이 코드로 만들며, 닫기 버튼은 창을 숨기기만 합니다.
        /// </summary>
        private sealed class ProtocolLayoutForm : Form
        {
            private readonly CheckBox[] _plcToPc;
            private readonly CheckBox[] _pcToPlc;
            private readonly Label _summary = new Label { AutoSize = false, Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), ForeColor = Color.DarkBlue };

            private static readonly string[] EquipmentTypes = { "COATOR", "ROLLPRESS", "SLITTER", "WINDER", "NOTCHING", "LAMI1", "LAMI2" };
            private readonly ComboBox _equipment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(10, 22), Size = new Size(140, 24) };
            private readonly CheckBox _unwinderAuto = new CheckBox { Text = "Unwinder 자동 증가 (파일명 5칸)", AutoSize = false, Location = new Point(160, 22), Size = new Size(330, 24) };
            private readonly Label _equipmentHint = new Label { AutoSize = false, Location = new Point(10, 50), Size = new Size(480, 34), ForeColor = Color.DimGray };

            public event EventHandler LayoutChanged;

            /// <summary>설비 타입 또는 공유폴더 Unwinder 자동 증가 선택이 바뀌면 발생합니다.</summary>
            public event EventHandler EquipmentChanged;

            public bool IsSharedFolderEquipment => _equipment.SelectedIndex >= 4;

            public bool AutoIncrementUnwinder => _unwinderAuto.Checked;

            public bool AllowClose { get; set; }

            public ProtocolLayoutForm()
            {
                Text = "송수신 항목 설정 (비전SW 소켓 체크와 같게)";
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar = false;
                ClientSize = new Size(520, 496);

                var plcGroup = new GroupBox { Text = "PLC → PC 송신 (신 비전SW '수신 프로토콜')", Location = new Point(8, 8), Size = new Size(248, 340) };
                var pcGroup = new GroupBox { Text = "PC → PLC 수신 (신 비전SW '송신 프로토콜')", Location = new Point(264, 8), Size = new Size(248, 340) };

                _plcToPc = CreateChecks(plcGroup, new[]
                {
                    "Rewinder Count (4B)",
                    "Vision Command: B4 PLC상태+B5 명령 (2B)",
                    "Link Status: B6 NG Tag+B7 링크 (2B)",
                    "Unwinder Count (4B)",
                    "LOT ID1 (10B)",
                    "Model Info (20B)",
                    "Line Speed (4B)",
                    "LOT ID2 (10B)",
                    "[수정] Image Status+Spare (2B)"
                });
                _plcToPc[8].Checked = false; // 기본은 원본 58B
                _pcToPlc = CreateChecks(pcGroup, new[]
                {
                    "Vision Status (2B)",
                    "Line Position (2B)",
                    "Trigger Count (2B)",
                    "Rewinder Count (4B)",
                    "Ref. Pos (2B)",
                    "Lane No (2B)",
                    "OCR Result (2B)",
                    "Realtime BCD Pos (2B)",
                    "Camera No (2B)",
                    "Unwinder Count (4B)",
                    "Heartbeat 워드 (2B)",
                    "BCD Pos (2B)"
                });

                Controls.Add(plcGroup);
                Controls.Add(pcGroup);
                var equipmentGroup = new GroupBox { Text = "설비 타입 (엔코더 자동 증가 방식)", Location = new Point(8, 354), Size = new Size(504, 92) };
                _equipment.Items.AddRange(EquipmentTypes);
                _equipment.SelectedIndex = 1; // ROLLPRESS
                _equipment.SelectedIndexChanged += (s, e) => OnEquipmentChanged();
                _unwinderAuto.CheckedChanged += (s, e) => OnEquipmentChanged();
                equipmentGroup.Controls.Add(_equipment);
                equipmentGroup.Controls.Add(_unwinderAuto);
                equipmentGroup.Controls.Add(_equipmentHint);
                Controls.Add(equipmentGroup);
                UpdateEquipmentHint();

                Controls.Add(_summary);
            }

            private void OnEquipmentChanged()
            {
                UpdateEquipmentHint();
                EquipmentChanged?.Invoke(this, EventArgs.Empty);
            }

            private void UpdateEquipmentHint()
            {
                bool shared = IsSharedFolderEquipment;
                _unwinderAuto.Enabled = shared;
                _equipmentHint.Text = shared
                    ? "공유폴더 설비: 트리거·Rewinder는 이미지 파일명 값 → Rewinder는 증가하지 않음(수동 값 유지).\r\nUnwinder는 파일명 5칸일 때만 PLC 값 사용 → 체크 시에만 증가."
                    : "카메라 설비: 영상 취득 시점의 PLC Rewinder·Unwinder 사용 → 메인 창 '엔코더 자동 증가' 시 둘 다 증가.";
            }

            public void SetSummary(int plcToPcLength, int pcToPlcLength)
            {
                _summary.Text = $"PLC→PC 송신 {plcToPcLength}바이트 / PC→PLC 기대 {pcToPlcLength}바이트 (모두 체크: 원본 58 / 수정 60 / 30)\r\n" +
                                "신 비전SW의 'PLC 상태', 'NG 태그' 체크는 길이에 영향 없음(구 비전SW와 동일).";
            }

            public PlcToPcLayout CreatePlcToPcLayout()
            {
                return new PlcToPcLayout
                {
                    RewinderCount = _plcToPc[0].Checked,
                    VisionCommand = _plcToPc[1].Checked,
                    LinkStatus = _plcToPc[2].Checked,
                    UnwinderCount = _plcToPc[3].Checked,
                    LotId1 = _plcToPc[4].Checked,
                    ModelInfo = _plcToPc[5].Checked,
                    LineSpeed = _plcToPc[6].Checked,
                    LotId2 = _plcToPc[7].Checked,
                    ImageStatus = _plcToPc[8].Checked
                };
            }

            public PcToPlcLayout CreatePcToPlcLayout()
            {
                return new PcToPlcLayout
                {
                    VisionStatus = _pcToPlc[0].Checked,
                    LinePos = _pcToPlc[1].Checked,
                    TriggerCount = _pcToPlc[2].Checked,
                    RewinderCount = _pcToPlc[3].Checked,
                    RefPos = _pcToPlc[4].Checked,
                    LaneNo = _pcToPlc[5].Checked,
                    OcrResult = _pcToPlc[6].Checked,
                    RealtimeBcdPos = _pcToPlc[7].Checked,
                    CameraNo = _pcToPlc[8].Checked,
                    UnwinderCount = _pcToPlc[9].Checked,
                    Heartbeat = _pcToPlc[10].Checked,
                    BcdPos = _pcToPlc[11].Checked
                };
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
                base.OnFormClosing(e);
            }

            private CheckBox[] CreateChecks(GroupBox group, string[] captions)
            {
                var boxes = new CheckBox[captions.Length];
                for (int i = 0; i < captions.Length; i++)
                {
                    var box = new CheckBox
                    {
                        Text = captions[i],
                        Checked = true,
                        AutoSize = false,
                        Location = new Point(10, 22 + i * 26),
                        Size = new Size(230, 24)
                    };
                    box.CheckedChanged += (s, e) => LayoutChanged?.Invoke(this, EventArgs.Empty);
                    group.Controls.Add(box);
                    boxes[i] = box;
                }
                return boxes;
            }
        }
    }
}
