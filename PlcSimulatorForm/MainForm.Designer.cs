using System.Drawing;
using System.Windows.Forms;

namespace VisionTestTool.VirtualPlc
{
    partial class VirtualPlcServerForm
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopServerInternal();

                if (uiRefreshTimer != null)
                {
                    uiRefreshTimer.Stop();
                    uiRefreshTimer.Dispose();
                }

                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dgvHeaderStyle = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dgvCellStyle = new System.Windows.Forms.DataGridViewCellStyle();
            this.pnlTopBar = new System.Windows.Forms.Panel();
            this.lblPort = new System.Windows.Forms.Label();
            this.numPort = new System.Windows.Forms.NumericUpDown();
            this.btnStartServer = new System.Windows.Forms.Button();
            this.btnStopServer = new System.Windows.Forms.Button();
            this.lblInterval = new System.Windows.Forms.Label();
            this.numSendInterval = new System.Windows.Forms.NumericUpDown();
            this.chkPeriodicSend = new System.Windows.Forms.CheckBox();
            this.btnSendOnce = new System.Windows.Forms.Button();
            this.lblServerStatus = new System.Windows.Forms.Label();
            this.gbxPlcToPc = new System.Windows.Forms.GroupBox();
            this.chkAutoIncrementEncoder = new System.Windows.Forms.CheckBox();
            this.chkAutoToggleHeartbeat = new System.Windows.Forms.CheckBox();
            this.lblRewinder = new System.Windows.Forms.Label();
            this.numRewinder = new System.Windows.Forms.NumericUpDown();
            this.gbxByte4 = new System.Windows.Forms.GroupBox();
            this.chkB4_StartStop = new System.Windows.Forms.CheckBox();
            this.chkB4_Error = new System.Windows.Forms.CheckBox();
            this.lblB4Raw = new System.Windows.Forms.Label();
            this.numB4Raw = new System.Windows.Forms.NumericUpDown();
            this.gbxByte5 = new System.Windows.Forms.GroupBox();
            this.cmbVisionCommand = new System.Windows.Forms.ComboBox();
            this.chkVisionCmdBitMode = new System.Windows.Forms.CheckBox();
            this.lblB5Raw = new System.Windows.Forms.Label();
            this.numB5Raw = new System.Windows.Forms.NumericUpDown();
            this.gbxByte6 = new System.Windows.Forms.GroupBox();
            this.cmbNgMarkPos = new System.Windows.Forms.ComboBox();
            this.chkNgMarkBitMode = new System.Windows.Forms.CheckBox();
            this.lblB6Raw = new System.Windows.Forms.Label();
            this.numB6Raw = new System.Windows.Forms.NumericUpDown();
            this.gbxByte7 = new System.Windows.Forms.GroupBox();
            this.chkB7_MachinePlc = new System.Windows.Forms.CheckBox();
            this.chkB7_Heartbeat = new System.Windows.Forms.CheckBox();
            this.chkB7_ApiConn = new System.Windows.Forms.CheckBox();
            this.lblB7Raw = new System.Windows.Forms.Label();
            this.numB7Raw = new System.Windows.Forms.NumericUpDown();
            this.lblUnwinder = new System.Windows.Forms.Label();
            this.numUnwinder = new System.Windows.Forms.NumericUpDown();
            this.lblLot1 = new System.Windows.Forms.Label();
            this.txtLotId1 = new System.Windows.Forms.TextBox();
            this.lblModel = new System.Windows.Forms.Label();
            this.txtModelInfo = new System.Windows.Forms.TextBox();
            this.lblSpeed = new System.Windows.Forms.Label();
            this.numSpeed = new System.Windows.Forms.NumericUpDown();
            this.chkSpeedAsciiMode = new System.Windows.Forms.CheckBox();
            this.lblLot2 = new System.Windows.Forms.Label();
            this.txtLotId2 = new System.Windows.Forms.TextBox();
            this.gbxByte56 = new System.Windows.Forms.GroupBox();
            this.chkB56_MirrorX = new System.Windows.Forms.CheckBox();
            this.chkB56_FlipY = new System.Windows.Forms.CheckBox();
            this.cmbRotation = new System.Windows.Forms.ComboBox();
            this.lblB56Raw = new System.Windows.Forms.Label();
            this.numB56Raw = new System.Windows.Forms.NumericUpDown();
            this.lblB57Raw = new System.Windows.Forms.Label();
            this.numB57Raw = new System.Windows.Forms.NumericUpDown();
            this.lblB56Warn = new System.Windows.Forms.Label();
            this.lblTxSummary = new System.Windows.Forms.Label();
            this.txtTxHex = new System.Windows.Forms.TextBox();
            this.gbxPcToPlc = new System.Windows.Forms.GroupBox();
            this.grdRxParsed = new System.Windows.Forms.DataGridView();
            this.lblRxSummary = new System.Windows.Forms.Label();
            this.txtRxHex = new System.Windows.Forms.TextBox();
            this.uiRefreshTimer = new System.Windows.Forms.Timer(this.components);
            this.pnlTopBar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSendInterval)).BeginInit();
            this.gbxPlcToPc.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numRewinder)).BeginInit();
            this.gbxByte4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB4Raw)).BeginInit();
            this.gbxByte5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB5Raw)).BeginInit();
            this.gbxByte6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB6Raw)).BeginInit();
            this.gbxByte7.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB7Raw)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numUnwinder)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSpeed)).BeginInit();
            this.gbxByte56.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB56Raw)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numB57Raw)).BeginInit();
            this.gbxPcToPlc.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grdRxParsed)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlTopBar
            // 
            this.pnlTopBar.BackColor = System.Drawing.Color.FromArgb(32, 33, 36);
            this.pnlTopBar.Controls.Add(this.lblPort);
            this.pnlTopBar.Controls.Add(this.numPort);
            this.pnlTopBar.Controls.Add(this.btnStartServer);
            this.pnlTopBar.Controls.Add(this.btnStopServer);
            this.pnlTopBar.Controls.Add(this.lblInterval);
            this.pnlTopBar.Controls.Add(this.numSendInterval);
            this.pnlTopBar.Controls.Add(this.chkPeriodicSend);
            this.pnlTopBar.Controls.Add(this.btnSendOnce);
            this.pnlTopBar.Controls.Add(this.lblServerStatus);
            this.pnlTopBar.Location = new System.Drawing.Point(0, 0);
            this.pnlTopBar.Name = "pnlTopBar";
            this.pnlTopBar.Size = new System.Drawing.Size(1420, 52);
            this.pnlTopBar.TabIndex = 0;
            // 
            // lblPort
            // 
            this.lblPort.BackColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.lblPort.ForeColor = System.Drawing.Color.White;
            this.lblPort.Location = new System.Drawing.Point(12, 10);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(75, 32);
            this.lblPort.TabIndex = 0;
            this.lblPort.Text = "서버 포트";
            this.lblPort.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numPort
            // 
            this.numPort.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numPort.ForeColor = System.Drawing.Color.White;
            this.numPort.Location = new System.Drawing.Point(90, 13);
            this.numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numPort.Name = "numPort";
            this.numPort.Size = new System.Drawing.Size(80, 25);
            this.numPort.TabIndex = 1;
            this.numPort.Value = new decimal(new int[] { 5000, 0, 0, 0 });
            // 
            // btnStartServer
            // 
            this.btnStartServer.BackColor = System.Drawing.Color.FromArgb(16, 122, 82);
            this.btnStartServer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStartServer.ForeColor = System.Drawing.Color.White;
            this.btnStartServer.Location = new System.Drawing.Point(178, 9);
            this.btnStartServer.Name = "btnStartServer";
            this.btnStartServer.Size = new System.Drawing.Size(110, 34);
            this.btnStartServer.TabIndex = 2;
            this.btnStartServer.Text = "서버 시작";
            this.btnStartServer.UseVisualStyleBackColor = false;
            // 
            // btnStopServer
            // 
            this.btnStopServer.BackColor = System.Drawing.Color.Maroon;
            this.btnStopServer.Enabled = false;
            this.btnStopServer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStopServer.ForeColor = System.Drawing.Color.White;
            this.btnStopServer.Location = new System.Drawing.Point(294, 9);
            this.btnStopServer.Name = "btnStopServer";
            this.btnStopServer.Size = new System.Drawing.Size(100, 34);
            this.btnStopServer.TabIndex = 3;
            this.btnStopServer.Text = "서버 정지";
            this.btnStopServer.UseVisualStyleBackColor = false;
            // 
            // lblInterval
            // 
            this.lblInterval.BackColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.lblInterval.ForeColor = System.Drawing.Color.White;
            this.lblInterval.Location = new System.Drawing.Point(410, 10);
            this.lblInterval.Name = "lblInterval";
            this.lblInterval.Size = new System.Drawing.Size(105, 32);
            this.lblInterval.TabIndex = 4;
            this.lblInterval.Text = "송신 주기(ms)";
            this.lblInterval.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numSendInterval
            // 
            this.numSendInterval.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numSendInterval.ForeColor = System.Drawing.Color.White;
            this.numSendInterval.Location = new System.Drawing.Point(518, 13);
            this.numSendInterval.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numSendInterval.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numSendInterval.Name = "numSendInterval";
            this.numSendInterval.Size = new System.Drawing.Size(75, 25);
            this.numSendInterval.TabIndex = 5;
            this.numSendInterval.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // chkPeriodicSend
            // 
            this.chkPeriodicSend.AutoSize = true;
            this.chkPeriodicSend.Checked = true;
            this.chkPeriodicSend.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkPeriodicSend.ForeColor = System.Drawing.Color.White;
            this.chkPeriodicSend.Location = new System.Drawing.Point(604, 15);
            this.chkPeriodicSend.Name = "chkPeriodicSend";
            this.chkPeriodicSend.Size = new System.Drawing.Size(119, 21);
            this.chkPeriodicSend.TabIndex = 6;
            this.chkPeriodicSend.Text = "주기적 자동 송신";
            this.chkPeriodicSend.UseVisualStyleBackColor = true;
            // 
            // btnSendOnce
            // 
            this.btnSendOnce.BackColor = System.Drawing.Color.FromArgb(0, 122, 204);
            this.btnSendOnce.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSendOnce.ForeColor = System.Drawing.Color.White;
            this.btnSendOnce.Location = new System.Drawing.Point(732, 9);
            this.btnSendOnce.Name = "btnSendOnce";
            this.btnSendOnce.Size = new System.Drawing.Size(120, 34);
            this.btnSendOnce.TabIndex = 7;
            this.btnSendOnce.Text = "1회 즉시 송신";
            this.btnSendOnce.UseVisualStyleBackColor = false;
            // 
            // lblServerStatus
            // 
            this.lblServerStatus.BackColor = System.Drawing.Color.FromArgb(24, 25, 27);
            this.lblServerStatus.ForeColor = System.Drawing.Color.Tomato;
            this.lblServerStatus.Location = new System.Drawing.Point(864, 9);
            this.lblServerStatus.Name = "lblServerStatus";
            this.lblServerStatus.Size = new System.Drawing.Size(544, 34);
            this.lblServerStatus.TabIndex = 8;
            this.lblServerStatus.Text = "● 서버 대기 중 (Stopped)";
            this.lblServerStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // gbxPlcToPc
            // 
            this.gbxPlcToPc.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.gbxPlcToPc.Controls.Add(this.chkAutoIncrementEncoder);
            this.gbxPlcToPc.Controls.Add(this.chkAutoToggleHeartbeat);
            this.gbxPlcToPc.Controls.Add(this.lblRewinder);
            this.gbxPlcToPc.Controls.Add(this.numRewinder);
            this.gbxPlcToPc.Controls.Add(this.gbxByte4);
            this.gbxPlcToPc.Controls.Add(this.gbxByte5);
            this.gbxPlcToPc.Controls.Add(this.gbxByte6);
            this.gbxPlcToPc.Controls.Add(this.gbxByte7);
            this.gbxPlcToPc.Controls.Add(this.lblUnwinder);
            this.gbxPlcToPc.Controls.Add(this.numUnwinder);
            this.gbxPlcToPc.Controls.Add(this.lblLot1);
            this.gbxPlcToPc.Controls.Add(this.txtLotId1);
            this.gbxPlcToPc.Controls.Add(this.lblModel);
            this.gbxPlcToPc.Controls.Add(this.txtModelInfo);
            this.gbxPlcToPc.Controls.Add(this.lblSpeed);
            this.gbxPlcToPc.Controls.Add(this.numSpeed);
            this.gbxPlcToPc.Controls.Add(this.chkSpeedAsciiMode);
            this.gbxPlcToPc.Controls.Add(this.lblLot2);
            this.gbxPlcToPc.Controls.Add(this.txtLotId2);
            this.gbxPlcToPc.Controls.Add(this.gbxByte56);
            this.gbxPlcToPc.Controls.Add(this.lblTxSummary);
            this.gbxPlcToPc.Controls.Add(this.txtTxHex);
            this.gbxPlcToPc.ForeColor = System.Drawing.Color.White;
            this.gbxPlcToPc.Location = new System.Drawing.Point(12, 60);
            this.gbxPlcToPc.Name = "gbxPlcToPc";
            this.gbxPlcToPc.Size = new System.Drawing.Size(740, 788);
            this.gbxPlcToPc.TabIndex = 1;
            this.gbxPlcToPc.TabStop = false;
            this.gbxPlcToPc.Text = "PLC (Server) → OCR PC (Client) 송신 패킷 제어 (Byte 00~59 / Word 00~29, 원본 00~57)";
            // 
            // chkAutoIncrementEncoder
            // 
            this.chkAutoIncrementEncoder.AutoSize = true;
            this.chkAutoIncrementEncoder.Checked = true;
            this.chkAutoIncrementEncoder.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoIncrementEncoder.ForeColor = System.Drawing.Color.Cyan;
            this.chkAutoIncrementEncoder.Location = new System.Drawing.Point(16, 26);
            this.chkAutoIncrementEncoder.Name = "chkAutoIncrementEncoder";
            this.chkAutoIncrementEncoder.Size = new System.Drawing.Size(277, 21);
            this.chkAutoIncrementEncoder.TabIndex = 0;
            this.chkAutoIncrementEncoder.Text = "송신 시 Rewinder/Unwinder 인코더 자동 증가";
            // 
            // chkAutoToggleHeartbeat
            // 
            this.chkAutoToggleHeartbeat.AutoSize = true;
            this.chkAutoToggleHeartbeat.Checked = true;
            this.chkAutoToggleHeartbeat.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoToggleHeartbeat.ForeColor = System.Drawing.Color.Cyan;
            this.chkAutoToggleHeartbeat.Location = new System.Drawing.Point(380, 26);
            this.chkAutoToggleHeartbeat.Name = "chkAutoToggleHeartbeat";
            this.chkAutoToggleHeartbeat.Size = new System.Drawing.Size(274, 21);
            this.chkAutoToggleHeartbeat.TabIndex = 1;
            this.chkAutoToggleHeartbeat.Text = "송신 시 PLC 하트비트(Byte 7 Bit 1) 자동 토글";
            // 
            // [1] Byte 00~03 (Word 00~01) : Rewinder Encoder Count
            // 
            this.lblRewinder.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblRewinder.Location = new System.Drawing.Point(14, 56);
            this.lblRewinder.Name = "lblRewinder";
            this.lblRewinder.Size = new System.Drawing.Size(260, 28);
            this.lblRewinder.TabIndex = 2;
            this.lblRewinder.Text = "[B00~03 | W00~01] Rewinder Count";
            this.lblRewinder.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numRewinder
            // 
            this.numRewinder.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numRewinder.ForeColor = System.Drawing.Color.White;
            this.numRewinder.Location = new System.Drawing.Point(280, 58);
            this.numRewinder.Maximum = new decimal(new int[] { -1, 0, 0, 0 });
            this.numRewinder.Name = "numRewinder";
            this.numRewinder.Size = new System.Drawing.Size(445, 25);
            this.numRewinder.TabIndex = 3;
            // 
            // [2] Byte 04 (Word 02 Low) : Link Status 1 (PLC 상태)
            // 
            this.gbxByte4.Controls.Add(this.chkB4_StartStop);
            this.gbxByte4.Controls.Add(this.chkB4_Error);
            this.gbxByte4.Controls.Add(this.lblB4Raw);
            this.gbxByte4.Controls.Add(this.numB4Raw);
            this.gbxByte4.ForeColor = System.Drawing.Color.White;
            this.gbxByte4.Location = new System.Drawing.Point(14, 92);
            this.gbxByte4.Name = "gbxByte4";
            this.gbxByte4.Size = new System.Drawing.Size(350, 124);
            this.gbxByte4.TabIndex = 4;
            this.gbxByte4.TabStop = false;
            this.gbxByte4.Text = "[Byte 04 | Word 02 Low] Link Status (PLC 상태)";
            // 
            // chkB4_StartStop
            // 
            this.chkB4_StartStop.AutoSize = true;
            this.chkB4_StartStop.Checked = true;
            this.chkB4_StartStop.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkB4_StartStop.Location = new System.Drawing.Point(14, 28);
            this.chkB4_StartStop.Name = "chkB4_StartStop";
            this.chkB4_StartStop.Size = new System.Drawing.Size(230, 21);
            this.chkB4_StartStop.TabIndex = 0;
            this.chkB4_StartStop.Text = "Bit 0 : 설비 운전 상태 (1=Start / 0=Stop)";
            // 
            // chkB4_Error
            // 
            this.chkB4_Error.AutoSize = true;
            this.chkB4_Error.Location = new System.Drawing.Point(14, 56);
            this.chkB4_Error.Name = "chkB4_Error";
            this.chkB4_Error.Size = new System.Drawing.Size(244, 21);
            this.chkB4_Error.TabIndex = 1;
            this.chkB4_Error.Text = "Bit 7 : 설비 에러 상태 (1=Error / 0=Normal)";
            // 
            // lblB4Raw
            // 
            this.lblB4Raw.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblB4Raw.Location = new System.Drawing.Point(14, 86);
            this.lblB4Raw.Name = "lblB4Raw";
            this.lblB4Raw.Size = new System.Drawing.Size(140, 26);
            this.lblB4Raw.TabIndex = 2;
            this.lblB4Raw.Text = "Byte 04 Raw (0~255)";
            this.lblB4Raw.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numB4Raw
            // 
            this.numB4Raw.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numB4Raw.ForeColor = System.Drawing.Color.Yellow;
            this.numB4Raw.Location = new System.Drawing.Point(160, 87);
            this.numB4Raw.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numB4Raw.Name = "numB4Raw";
            this.numB4Raw.Size = new System.Drawing.Size(100, 25);
            this.numB4Raw.TabIndex = 3;
            this.numB4Raw.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // [3] Byte 05 (Word 02 High) : Vision Command
            // 
            this.gbxByte5.Controls.Add(this.cmbVisionCommand);
            this.gbxByte5.Controls.Add(this.chkVisionCmdBitMode);
            this.gbxByte5.Controls.Add(this.lblB5Raw);
            this.gbxByte5.Controls.Add(this.numB5Raw);
            this.gbxByte5.ForeColor = System.Drawing.Color.White;
            this.gbxByte5.Location = new System.Drawing.Point(375, 92);
            this.gbxByte5.Name = "gbxByte5";
            this.gbxByte5.Size = new System.Drawing.Size(350, 124);
            this.gbxByte5.TabIndex = 5;
            this.gbxByte5.TabStop = false;
            this.gbxByte5.Text = "[Byte 05 | Word 02 High] Vision Command";
            // 
            // cmbVisionCommand
            // 
            this.cmbVisionCommand.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.cmbVisionCommand.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbVisionCommand.ForeColor = System.Drawing.Color.White;
            this.cmbVisionCommand.Items.AddRange(new object[] {
            "0 : None (명령 없음)",
            "1 : Vision Start (검사 시작)",
            "2 : Vision Stop (검사 정지)"});
            this.cmbVisionCommand.Location = new System.Drawing.Point(14, 26);
            this.cmbVisionCommand.Name = "cmbVisionCommand";
            this.cmbVisionCommand.Size = new System.Drawing.Size(320, 25);
            this.cmbVisionCommand.TabIndex = 0;
            // 
            // chkVisionCmdBitMode
            // 
            this.chkVisionCmdBitMode.AutoSize = true;
            this.chkVisionCmdBitMode.Location = new System.Drawing.Point(14, 56);
            this.chkVisionCmdBitMode.Name = "chkVisionCmdBitMode";
            this.chkVisionCmdBitMode.Size = new System.Drawing.Size(268, 21);
            this.chkVisionCmdBitMode.TabIndex = 1;
            this.chkVisionCmdBitMode.Text = "Bit 플래그 모드로 전송 (체크 해제 시 정수값 0,1,2)";
            // 
            // lblB5Raw
            // 
            this.lblB5Raw.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblB5Raw.Location = new System.Drawing.Point(14, 86);
            this.lblB5Raw.Name = "lblB5Raw";
            this.lblB5Raw.Size = new System.Drawing.Size(140, 26);
            this.lblB5Raw.TabIndex = 2;
            this.lblB5Raw.Text = "Byte 05 Raw (0~255)";
            this.lblB5Raw.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numB5Raw
            // 
            this.numB5Raw.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numB5Raw.ForeColor = System.Drawing.Color.Yellow;
            this.numB5Raw.Location = new System.Drawing.Point(160, 87);
            this.numB5Raw.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numB5Raw.Name = "numB5Raw";
            this.numB5Raw.Size = new System.Drawing.Size(100, 25);
            this.numB5Raw.TabIndex = 3;
            // 
            // [4] Byte 06 (Word 03 Low) : NG Tag Marking Position
            // 
            this.gbxByte6.Controls.Add(this.cmbNgMarkPos);
            this.gbxByte6.Controls.Add(this.chkNgMarkBitMode);
            this.gbxByte6.Controls.Add(this.lblB6Raw);
            this.gbxByte6.Controls.Add(this.numB6Raw);
            this.gbxByte6.ForeColor = System.Drawing.Color.White;
            this.gbxByte6.Location = new System.Drawing.Point(14, 224);
            this.gbxByte6.Name = "gbxByte6";
            this.gbxByte6.Size = new System.Drawing.Size(350, 136);
            this.gbxByte6.TabIndex = 6;
            this.gbxByte6.TabStop = false;
            this.gbxByte6.Text = "[Byte 06 | Word 03 Low] NG Tag Marking Position";
            // 
            // cmbNgMarkPos
            // 
            this.cmbNgMarkPos.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.cmbNgMarkPos.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbNgMarkPos.ForeColor = System.Drawing.Color.White;
            this.cmbNgMarkPos.Items.AddRange(new object[] {
            "0 : None (마킹 없음)",
            "1 : Left (Normal / 좌측)",
            "2 : Right (Reverse / 우측)"});
            this.cmbNgMarkPos.Location = new System.Drawing.Point(14, 26);
            this.cmbNgMarkPos.Name = "cmbNgMarkPos";
            this.cmbNgMarkPos.Size = new System.Drawing.Size(320, 25);
            this.cmbNgMarkPos.TabIndex = 0;
            // 
            // chkNgMarkBitMode
            // 
            this.chkNgMarkBitMode.AutoSize = true;
            this.chkNgMarkBitMode.Location = new System.Drawing.Point(14, 58);
            this.chkNgMarkBitMode.Name = "chkNgMarkBitMode";
            this.chkNgMarkBitMode.Size = new System.Drawing.Size(298, 21);
            this.chkNgMarkBitMode.TabIndex = 1;
            this.chkNgMarkBitMode.Text = "Bit 위치 모드 (Bit0:None, Bit1:Left, Bit2:Right)";
            // 
            // lblB6Raw
            // 
            this.lblB6Raw.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblB6Raw.Location = new System.Drawing.Point(14, 96);
            this.lblB6Raw.Name = "lblB6Raw";
            this.lblB6Raw.Size = new System.Drawing.Size(140, 26);
            this.lblB6Raw.TabIndex = 2;
            this.lblB6Raw.Text = "Byte 06 Raw (0~255)";
            this.lblB6Raw.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numB6Raw
            // 
            this.numB6Raw.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numB6Raw.ForeColor = System.Drawing.Color.Yellow;
            this.numB6Raw.Location = new System.Drawing.Point(160, 97);
            this.numB6Raw.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numB6Raw.Name = "numB6Raw";
            this.numB6Raw.Size = new System.Drawing.Size(100, 25);
            this.numB6Raw.TabIndex = 3;
            // 
            // [5] Byte 07 (Word 03 High) : Link Status 2 (PLC 상태)
            // 
            this.gbxByte7.Controls.Add(this.chkB7_MachinePlc);
            this.gbxByte7.Controls.Add(this.chkB7_Heartbeat);
            this.gbxByte7.Controls.Add(this.chkB7_ApiConn);
            this.gbxByte7.Controls.Add(this.lblB7Raw);
            this.gbxByte7.Controls.Add(this.numB7Raw);
            this.gbxByte7.ForeColor = System.Drawing.Color.White;
            this.gbxByte7.Location = new System.Drawing.Point(375, 224);
            this.gbxByte7.Name = "gbxByte7";
            this.gbxByte7.Size = new System.Drawing.Size(350, 136);
            this.gbxByte7.TabIndex = 7;
            this.gbxByte7.TabStop = false;
            this.gbxByte7.Text = "[Byte 07 | Word 03 High] Link Status (PLC 상태)";
            // 
            // chkB7_MachinePlc
            // 
            this.chkB7_MachinePlc.AutoSize = true;
            this.chkB7_MachinePlc.Checked = true;
            this.chkB7_MachinePlc.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkB7_MachinePlc.Location = new System.Drawing.Point(14, 22);
            this.chkB7_MachinePlc.Name = "chkB7_MachinePlc";
            this.chkB7_MachinePlc.Size = new System.Drawing.Size(218, 21);
            this.chkB7_MachinePlc.TabIndex = 0;
            this.chkB7_MachinePlc.Text = "Bit 0 : Machine PLC 연결 상태 (1=Conn)";
            // 
            // chkB7_Heartbeat
            // 
            this.chkB7_Heartbeat.AutoSize = true;
            this.chkB7_Heartbeat.Location = new System.Drawing.Point(14, 46);
            this.chkB7_Heartbeat.Name = "chkB7_Heartbeat";
            this.chkB7_Heartbeat.Size = new System.Drawing.Size(210, 21);
            this.chkB7_Heartbeat.TabIndex = 1;
            this.chkB7_Heartbeat.Text = "Bit 1 : PLC ↔ OCR PC 하트비트";
            // 
            // chkB7_ApiConn
            // 
            this.chkB7_ApiConn.AutoSize = true;
            this.chkB7_ApiConn.Checked = true;
            this.chkB7_ApiConn.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkB7_ApiConn.Location = new System.Drawing.Point(14, 70);
            this.chkB7_ApiConn.Name = "chkB7_ApiConn";
            this.chkB7_ApiConn.Size = new System.Drawing.Size(172, 21);
            this.chkB7_ApiConn.TabIndex = 2;
            this.chkB7_ApiConn.Text = "Bit 2 : API 연결 상태 (1=Conn)";
            // 
            // lblB7Raw
            // 
            this.lblB7Raw.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblB7Raw.Location = new System.Drawing.Point(14, 96);
            this.lblB7Raw.Name = "lblB7Raw";
            this.lblB7Raw.Size = new System.Drawing.Size(140, 26);
            this.lblB7Raw.TabIndex = 3;
            this.lblB7Raw.Text = "Byte 07 Raw (0~255)";
            this.lblB7Raw.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numB7Raw
            // 
            this.numB7Raw.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numB7Raw.ForeColor = System.Drawing.Color.Yellow;
            this.numB7Raw.Location = new System.Drawing.Point(160, 97);
            this.numB7Raw.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numB7Raw.Name = "numB7Raw";
            this.numB7Raw.Size = new System.Drawing.Size(100, 25);
            this.numB7Raw.TabIndex = 4;
            this.numB7Raw.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // [6] Byte 08~11 (Word 04~05) : Unwinder Encoder Count
            // 
            this.lblUnwinder.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblUnwinder.Location = new System.Drawing.Point(14, 368);
            this.lblUnwinder.Name = "lblUnwinder";
            this.lblUnwinder.Size = new System.Drawing.Size(260, 28);
            this.lblUnwinder.TabIndex = 8;
            this.lblUnwinder.Text = "[B08~11 | W04~05] Unwinder Count";
            this.lblUnwinder.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numUnwinder
            // 
            this.numUnwinder.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numUnwinder.ForeColor = System.Drawing.Color.White;
            this.numUnwinder.Location = new System.Drawing.Point(280, 370);
            this.numUnwinder.Maximum = new decimal(new int[] { -1, 0, 0, 0 });
            this.numUnwinder.Name = "numUnwinder";
            this.numUnwinder.Size = new System.Drawing.Size(445, 25);
            this.numUnwinder.TabIndex = 9;
            // 
            // [7] Byte 12~21 (Word 06~10) : LOT ID 1 (10자리 문자열)
            // 
            this.lblLot1.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblLot1.Location = new System.Drawing.Point(14, 402);
            this.lblLot1.Name = "lblLot1";
            this.lblLot1.Size = new System.Drawing.Size(260, 28);
            this.lblLot1.TabIndex = 10;
            this.lblLot1.Text = "[B12~21 | W06~10] LOT ID 1 (10자)";
            this.lblLot1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // txtLotId1
            // 
            this.txtLotId1.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.txtLotId1.ForeColor = System.Drawing.Color.White;
            this.txtLotId1.Location = new System.Drawing.Point(280, 404);
            this.txtLotId1.MaxLength = 10;
            this.txtLotId1.Name = "txtLotId1";
            this.txtLotId1.Size = new System.Drawing.Size(445, 25);
            this.txtLotId1.TabIndex = 11;
            this.txtLotId1.Text = "LOT261014A";
            // 
            // [8] Byte 22~41 (Word 11~20) : Model Info (20자리 문자열)
            // 
            this.lblModel.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblModel.Location = new System.Drawing.Point(14, 436);
            this.lblModel.Name = "lblModel";
            this.lblModel.Size = new System.Drawing.Size(260, 28);
            this.lblModel.TabIndex = 12;
            this.lblModel.Text = "[B22~41 | W11~20] Model Info (20자)";
            this.lblModel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // txtModelInfo
            // 
            this.txtModelInfo.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.txtModelInfo.ForeColor = System.Drawing.Color.White;
            this.txtModelInfo.Location = new System.Drawing.Point(280, 438);
            this.txtModelInfo.MaxLength = 20;
            this.txtModelInfo.Name = "txtModelInfo";
            this.txtModelInfo.Size = new System.Drawing.Size(445, 25);
            this.txtModelInfo.TabIndex = 13;
            this.txtModelInfo.Text = "MODEL_CATHODE_01";
            // 
            // [9] Byte 42~45 (Word 21~22) : Equipment Speed (설비 속도)
            // 
            this.lblSpeed.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblSpeed.Location = new System.Drawing.Point(14, 470);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new System.Drawing.Size(260, 28);
            this.lblSpeed.TabIndex = 14;
            this.lblSpeed.Text = "[B42~45 | W21~22] 설비 속도(M/Min)";
            this.lblSpeed.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // numSpeed
            // 
            this.numSpeed.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numSpeed.ForeColor = System.Drawing.Color.White;
            this.numSpeed.Location = new System.Drawing.Point(280, 472);
            this.numSpeed.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            this.numSpeed.Name = "numSpeed";
            this.numSpeed.Size = new System.Drawing.Size(140, 25);
            this.numSpeed.TabIndex = 15;
            this.numSpeed.Value = new decimal(new int[] { 120, 0, 0, 0 });
            // 
            // chkSpeedAsciiMode
            // 
            this.chkSpeedAsciiMode.AutoSize = true;
            this.chkSpeedAsciiMode.ForeColor = System.Drawing.Color.Gainsboro;
            this.chkSpeedAsciiMode.Location = new System.Drawing.Point(430, 474);
            this.chkSpeedAsciiMode.Name = "chkSpeedAsciiMode";
            this.chkSpeedAsciiMode.Size = new System.Drawing.Size(295, 21);
            this.chkSpeedAsciiMode.TabIndex = 16;
            this.chkSpeedAsciiMode.Text = "속도를 3자리 ASCII 문자열로 인코딩 (해제 시 Int32)";
            // 
            // [10] Byte 46~55 (Word 23~27) : LOT ID 2 (10자리 문자열 - 인터페이스 맵 노란색 강조 반영)
            // 
            this.lblLot2.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblLot2.ForeColor = System.Drawing.Color.Gold;
            this.lblLot2.Location = new System.Drawing.Point(14, 504);
            this.lblLot2.Name = "lblLot2";
            this.lblLot2.Size = new System.Drawing.Size(260, 28);
            this.lblLot2.TabIndex = 17;
            this.lblLot2.Text = "[B46~55 | W23~27] LOT ID 2 (10자)";
            this.lblLot2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // txtLotId2
            // 
            this.txtLotId2.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.txtLotId2.ForeColor = System.Drawing.Color.Gold;
            this.txtLotId2.Location = new System.Drawing.Point(280, 506);
            this.txtLotId2.MaxLength = 10;
            this.txtLotId2.Name = "txtLotId2";
            this.txtLotId2.Size = new System.Drawing.Size(445, 25);
            this.txtLotId2.TabIndex = 18;
            this.txtLotId2.Text = "LOT261014B";
            //
            // [11] Byte 56~57 (Word 28) : Image Status + Spare (수정버전 - F2 창에서 체크 시에만 송신)
            //
            this.gbxByte56.Controls.Add(this.chkB56_MirrorX);
            this.gbxByte56.Controls.Add(this.chkB56_FlipY);
            this.gbxByte56.Controls.Add(this.cmbRotation);
            this.gbxByte56.Controls.Add(this.lblB56Raw);
            this.gbxByte56.Controls.Add(this.numB56Raw);
            this.gbxByte56.Controls.Add(this.lblB57Raw);
            this.gbxByte56.Controls.Add(this.numB57Raw);
            this.gbxByte56.Controls.Add(this.lblB56Warn);
            this.gbxByte56.ForeColor = System.Drawing.Color.White;
            this.gbxByte56.Location = new System.Drawing.Point(14, 540);
            this.gbxByte56.Name = "gbxByte56";
            this.gbxByte56.Size = new System.Drawing.Size(711, 96);
            this.gbxByte56.TabIndex = 19;
            this.gbxByte56.TabStop = false;
            this.gbxByte56.Text = "[Byte 56~57 | Word 28] Image Status + Spare (수정버전 - F2 창에서 체크 시 송신)";
            //
            // chkB56_MirrorX
            //
            this.chkB56_MirrorX.AutoSize = true;
            this.chkB56_MirrorX.Location = new System.Drawing.Point(14, 28);
            this.chkB56_MirrorX.Name = "chkB56_MirrorX";
            this.chkB56_MirrorX.Size = new System.Drawing.Size(160, 21);
            this.chkB56_MirrorX.TabIndex = 0;
            this.chkB56_MirrorX.Text = "Bit 0 : X axis Mirror";
            //
            // chkB56_FlipY
            //
            this.chkB56_FlipY.AutoSize = true;
            this.chkB56_FlipY.Location = new System.Drawing.Point(196, 28);
            this.chkB56_FlipY.Name = "chkB56_FlipY";
            this.chkB56_FlipY.Size = new System.Drawing.Size(150, 21);
            this.chkB56_FlipY.TabIndex = 1;
            this.chkB56_FlipY.Text = "Bit 1 : Y axis Flip";
            //
            // cmbRotation
            //
            this.cmbRotation.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.cmbRotation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbRotation.ForeColor = System.Drawing.Color.White;
            this.cmbRotation.Items.AddRange(new object[] {
            "회전 없음 (Bit 2~5 = 0)",
            "Bit 2 : Rotate 90",
            "Bit 3 : Rotate 180",
            "Bit 4 : Rotate 270",
            "Bit 5 : Rotate 360"});
            this.cmbRotation.Location = new System.Drawing.Point(380, 26);
            this.cmbRotation.Name = "cmbRotation";
            this.cmbRotation.Size = new System.Drawing.Size(316, 25);
            this.cmbRotation.TabIndex = 2;
            //
            // lblB56Raw
            //
            this.lblB56Raw.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblB56Raw.Location = new System.Drawing.Point(14, 60);
            this.lblB56Raw.Name = "lblB56Raw";
            this.lblB56Raw.Size = new System.Drawing.Size(140, 26);
            this.lblB56Raw.TabIndex = 3;
            this.lblB56Raw.Text = "Byte 56 Raw (0~255)";
            this.lblB56Raw.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // numB56Raw
            //
            this.numB56Raw.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numB56Raw.ForeColor = System.Drawing.Color.Yellow;
            this.numB56Raw.Location = new System.Drawing.Point(160, 61);
            this.numB56Raw.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numB56Raw.Name = "numB56Raw";
            this.numB56Raw.Size = new System.Drawing.Size(100, 25);
            this.numB56Raw.TabIndex = 4;
            //
            // lblB57Raw
            //
            this.lblB57Raw.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblB57Raw.Location = new System.Drawing.Point(272, 60);
            this.lblB57Raw.Name = "lblB57Raw";
            this.lblB57Raw.Size = new System.Drawing.Size(150, 26);
            this.lblB57Raw.TabIndex = 5;
            this.lblB57Raw.Text = "Byte 57 Spare (0~255)";
            this.lblB57Raw.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // numB57Raw
            //
            this.numB57Raw.BackColor = System.Drawing.Color.FromArgb(37, 37, 40);
            this.numB57Raw.ForeColor = System.Drawing.Color.Yellow;
            this.numB57Raw.Location = new System.Drawing.Point(428, 61);
            this.numB57Raw.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            this.numB57Raw.Name = "numB57Raw";
            this.numB57Raw.Size = new System.Drawing.Size(100, 25);
            this.numB57Raw.TabIndex = 6;
            //
            // lblB56Warn
            //
            this.lblB56Warn.ForeColor = System.Drawing.Color.OrangeRed;
            this.lblB56Warn.Location = new System.Drawing.Point(536, 60);
            this.lblB56Warn.Name = "lblB56Warn";
            this.lblB56Warn.Size = new System.Drawing.Size(168, 26);
            this.lblB56Warn.TabIndex = 7;
            this.lblB56Warn.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // [12] Line End (CR/LF = 0x0D 0x0A = 2573) 및 송신 덤프 - 원본 B56~57 / 수정 B58~59
            //
            this.lblTxSummary.BackColor = System.Drawing.Color.FromArgb(20, 60, 40);
            this.lblTxSummary.ForeColor = System.Drawing.Color.White;
            this.lblTxSummary.Location = new System.Drawing.Point(14, 644);
            this.lblTxSummary.Name = "lblTxSummary";
            this.lblTxSummary.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.lblTxSummary.Size = new System.Drawing.Size(711, 30);
            this.lblTxSummary.TabIndex = 20;
            this.lblTxSummary.Text = "Line End : CR(13) LF(10) = 0x0A0D (2573) 고정 [원본 B56~57 / 수정 B58~59] | 누적 송신: 0건";
            this.lblTxSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtTxHex
            // 
            this.txtTxHex.BackColor = System.Drawing.Color.FromArgb(24, 25, 27);
            this.txtTxHex.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtTxHex.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtTxHex.ForeColor = System.Drawing.Color.LightGreen;
            this.txtTxHex.Location = new System.Drawing.Point(14, 678);
            this.txtTxHex.Multiline = true;
            this.txtTxHex.Name = "txtTxHex";
            this.txtTxHex.ReadOnly = true;
            this.txtTxHex.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtTxHex.Size = new System.Drawing.Size(711, 96);
            this.txtTxHex.TabIndex = 21;
            // 
            // gbxPcToPlc
            // 
            this.gbxPcToPlc.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.gbxPcToPlc.Controls.Add(this.grdRxParsed);
            this.gbxPcToPlc.Controls.Add(this.lblRxSummary);
            this.gbxPcToPlc.Controls.Add(this.txtRxHex);
            this.gbxPcToPlc.ForeColor = System.Drawing.Color.White;
            this.gbxPcToPlc.Location = new System.Drawing.Point(764, 60);
            this.gbxPcToPlc.Name = "gbxPcToPlc";
            this.gbxPcToPlc.Size = new System.Drawing.Size(644, 788);
            this.gbxPcToPlc.TabIndex = 2;
            this.gbxPcToPlc.TabStop = false;
            this.gbxPcToPlc.Text = "OCR PC (Client) → PLC (Server) 수신 데이터 모니터링 (Byte 00~29 / Word 00~14 순서)";
            // 
            // grdRxParsed
            // 
            this.grdRxParsed.AllowUserToAddRows = false;
            this.grdRxParsed.AllowUserToDeleteRows = false;
            this.grdRxParsed.AllowUserToResizeColumns = false;
            this.grdRxParsed.AllowUserToResizeRows = false;
            this.grdRxParsed.BackgroundColor = System.Drawing.Color.FromArgb(33, 33, 33);
            this.grdRxParsed.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.grdRxParsed.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            this.grdRxParsed.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.None;
            dgvHeaderStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dgvHeaderStyle.BackColor = System.Drawing.Color.FromArgb(64, 64, 64);
            dgvHeaderStyle.Font = new System.Drawing.Font("맑은 고딕", 9.5F, System.Drawing.FontStyle.Bold);
            dgvHeaderStyle.ForeColor = System.Drawing.Color.White;
            this.grdRxParsed.ColumnHeadersDefaultCellStyle = dgvHeaderStyle;
            this.grdRxParsed.ColumnHeadersHeight = 32;
            this.grdRxParsed.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dgvCellStyle.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            dgvCellStyle.Font = new System.Drawing.Font("맑은 고딕", 9.5F, System.Drawing.FontStyle.Bold);
            dgvCellStyle.ForeColor = System.Drawing.Color.White;
            dgvCellStyle.SelectionBackColor = System.Drawing.Color.RosyBrown;
            dgvCellStyle.SelectionForeColor = System.Drawing.Color.White;
            this.grdRxParsed.DefaultCellStyle = dgvCellStyle;
            this.grdRxParsed.EnableHeadersVisualStyles = false;
            this.grdRxParsed.GridColor = System.Drawing.Color.FromArgb(64, 64, 64);
            this.grdRxParsed.Location = new System.Drawing.Point(14, 28);
            this.grdRxParsed.MultiSelect = false;
            this.grdRxParsed.Name = "grdRxParsed";
            this.grdRxParsed.ReadOnly = true;
            this.grdRxParsed.RowHeadersVisible = false;
            this.grdRxParsed.RowTemplate.Height = 31;
            this.grdRxParsed.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.grdRxParsed.Size = new System.Drawing.Size(616, 504);
            this.grdRxParsed.TabIndex = 0;
            // 
            // lblRxSummary
            // 
            this.lblRxSummary.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.lblRxSummary.Location = new System.Drawing.Point(14, 540);
            this.lblRxSummary.Name = "lblRxSummary";
            this.lblRxSummary.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.lblRxSummary.Size = new System.Drawing.Size(616, 30);
            this.lblRxSummary.TabIndex = 1;
            this.lblRxSummary.Text = "수신 패킷 대기 중 (30 Bytes | B00~29 / W00~14)";
            this.lblRxSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtRxHex
            // 
            this.txtRxHex.BackColor = System.Drawing.Color.FromArgb(24, 25, 27);
            this.txtRxHex.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtRxHex.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtRxHex.ForeColor = System.Drawing.Color.LightSkyBlue;
            this.txtRxHex.Location = new System.Drawing.Point(14, 574);
            this.txtRxHex.Multiline = true;
            this.txtRxHex.Name = "txtRxHex";
            this.txtRxHex.ReadOnly = true;
            this.txtRxHex.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtRxHex.Size = new System.Drawing.Size(616, 200);
            this.txtRxHex.TabIndex = 2;
            // 
            // uiRefreshTimer
            // 
            this.uiRefreshTimer.Interval = 100;
            // 
            // VirtualPlcServerForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(1420, 860);
            this.Controls.Add(this.gbxPcToPlc);
            this.Controls.Add(this.gbxPlcToPc);
            this.Controls.Add(this.pnlTopBar);
            this.Font = new System.Drawing.Font("맑은 고딕", 9.5F, System.Drawing.FontStyle.Bold);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "VirtualPlcServerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Roll-Map OCR - 가상 PLC 서버 시뮬레이터 (Virtual PLC Server)";
            this.pnlTopBar.ResumeLayout(false);
            this.pnlTopBar.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSendInterval)).EndInit();
            this.gbxPlcToPc.ResumeLayout(false);
            this.gbxPlcToPc.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numRewinder)).EndInit();
            this.gbxByte4.ResumeLayout(false);
            this.gbxByte4.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB4Raw)).EndInit();
            this.gbxByte5.ResumeLayout(false);
            this.gbxByte5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB5Raw)).EndInit();
            this.gbxByte6.ResumeLayout(false);
            this.gbxByte6.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB6Raw)).EndInit();
            this.gbxByte7.ResumeLayout(false);
            this.gbxByte7.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB7Raw)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numUnwinder)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSpeed)).EndInit();
            this.gbxByte56.ResumeLayout(false);
            this.gbxByte56.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numB56Raw)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numB57Raw)).EndInit();
            this.gbxPcToPlc.ResumeLayout(false);
            this.gbxPcToPlc.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grdRxParsed)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel pnlTopBar;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.NumericUpDown numPort;
        private System.Windows.Forms.Button btnStartServer;
        private System.Windows.Forms.Button btnStopServer;
        private System.Windows.Forms.Label lblInterval;
        private System.Windows.Forms.NumericUpDown numSendInterval;
        private System.Windows.Forms.CheckBox chkPeriodicSend;
        private System.Windows.Forms.Button btnSendOnce;
        private System.Windows.Forms.Label lblServerStatus;
        private System.Windows.Forms.GroupBox gbxPlcToPc;
        private System.Windows.Forms.CheckBox chkAutoIncrementEncoder;
        private System.Windows.Forms.CheckBox chkAutoToggleHeartbeat;
        private System.Windows.Forms.Label lblRewinder;
        private System.Windows.Forms.NumericUpDown numRewinder;
        private System.Windows.Forms.GroupBox gbxByte4;
        private System.Windows.Forms.CheckBox chkB4_StartStop;
        private System.Windows.Forms.CheckBox chkB4_Error;
        private System.Windows.Forms.Label lblB4Raw;
        private System.Windows.Forms.NumericUpDown numB4Raw;
        private System.Windows.Forms.GroupBox gbxByte5;
        private System.Windows.Forms.ComboBox cmbVisionCommand;
        private System.Windows.Forms.CheckBox chkVisionCmdBitMode;
        private System.Windows.Forms.Label lblB5Raw;
        private System.Windows.Forms.NumericUpDown numB5Raw;
        private System.Windows.Forms.GroupBox gbxByte6;
        private System.Windows.Forms.ComboBox cmbNgMarkPos;
        private System.Windows.Forms.CheckBox chkNgMarkBitMode;
        private System.Windows.Forms.Label lblB6Raw;
        private System.Windows.Forms.NumericUpDown numB6Raw;
        private System.Windows.Forms.GroupBox gbxByte7;
        private System.Windows.Forms.CheckBox chkB7_MachinePlc;
        private System.Windows.Forms.CheckBox chkB7_Heartbeat;
        private System.Windows.Forms.CheckBox chkB7_ApiConn;
        private System.Windows.Forms.Label lblB7Raw;
        private System.Windows.Forms.NumericUpDown numB7Raw;
        private System.Windows.Forms.Label lblUnwinder;
        private System.Windows.Forms.NumericUpDown numUnwinder;
        private System.Windows.Forms.Label lblLot1;
        private System.Windows.Forms.TextBox txtLotId1;
        private System.Windows.Forms.Label lblModel;
        private System.Windows.Forms.TextBox txtModelInfo;
        private System.Windows.Forms.Label lblSpeed;
        private System.Windows.Forms.NumericUpDown numSpeed;
        private System.Windows.Forms.CheckBox chkSpeedAsciiMode;
        private System.Windows.Forms.Label lblLot2;
        private System.Windows.Forms.TextBox txtLotId2;
        private System.Windows.Forms.GroupBox gbxByte56;
        private System.Windows.Forms.CheckBox chkB56_MirrorX;
        private System.Windows.Forms.CheckBox chkB56_FlipY;
        private System.Windows.Forms.ComboBox cmbRotation;
        private System.Windows.Forms.Label lblB56Raw;
        private System.Windows.Forms.NumericUpDown numB56Raw;
        private System.Windows.Forms.Label lblB57Raw;
        private System.Windows.Forms.NumericUpDown numB57Raw;
        private System.Windows.Forms.Label lblB56Warn;
        private System.Windows.Forms.Label lblTxSummary;
        private System.Windows.Forms.TextBox txtTxHex;
        private System.Windows.Forms.GroupBox gbxPcToPlc;
        private System.Windows.Forms.DataGridView grdRxParsed;
        private System.Windows.Forms.Label lblRxSummary;
        private System.Windows.Forms.TextBox txtRxHex;
        private System.Windows.Forms.Timer uiRefreshTimer;
    }
}