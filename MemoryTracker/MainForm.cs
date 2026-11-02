using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Tool.MemoryTracker
{
    // ==========================================
    // 1. 데이터 모델 (OS 메모리 및 GDI/핸들 지표 통합)
    // ==========================================
    public struct ProcessMemoryInfo
    {
        public bool IsRunning { get; }
        public int PID { get; }
        public string ProcessName { get; }

        public long WorkingSetBytes { get; }     // 물리 RAM
        public long PrivateMemoryBytes { get; }  // 순수 전용 커밋 (4GB)
        public long PagedMemoryBytes { get; }    // 페이징 커밋 (OS 예약분, 작업관리자 8GB 오차의 원인)
        public long VirtualMemoryBytes { get; }  // 총 가상 주소 공간

        public int ThreadCount { get; }
        public int HandleCount { get; }
        public int GdiObjectCount { get; }
        public int UserObjectCount { get; }

        public ProcessMemoryInfo(bool isRunning, int pid, string name,
                                 long workingSet, long privateMem, long pagedMem, long virtualMem,
                                 int threads, int handles, int gdi, int user)
        {
            IsRunning = isRunning;
            PID = pid;
            ProcessName = name;
            WorkingSetBytes = workingSet;
            PrivateMemoryBytes = privateMem;
            PagedMemoryBytes = pagedMem;
            VirtualMemoryBytes = virtualMem;

            ThreadCount = threads;
            HandleCount = handles;
            GdiObjectCount = gdi;
            UserObjectCount = user;
        }

        public static ProcessMemoryInfo NotRunning => new ProcessMemoryInfo(false, 0, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    // ==========================================
    // 2. 비즈니스 로직 (GDI P/Invoke 및 프로세스 엔진)
    // ==========================================
    public class ProcessMonitorCore
    {
        // 🌟 Win32 API: 윈도우 그래픽(GDI) 및 UI(USER) 객체 누수 추적용 네이티브 함수
        [DllImport("User32.dll")]
        private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

        private const int GR_GDIOBJECTS = 0;
        private const int GR_USEROBJECTS = 1;

        public ProcessMemoryInfo GetProcessMemory(string targetProcessName)
        {
            if (string.IsNullOrWhiteSpace(targetProcessName))
                return ProcessMemoryInfo.NotRunning;

            string cleanName = targetProcessName.Replace(".exe", "").Trim();
            Process[] processes = Process.GetProcessesByName(cleanName);

            try
            {
                if (processes.Length == 0) return ProcessMemoryInfo.NotRunning;

                // 메모리를 가장 많이 점유하는 메인 프로세스 1개 타겟팅
                Process targetProc = processes.OrderByDescending(p => p.WorkingSet64).First();

                targetProc.Refresh();

                int gdiCount = 0;
                int userCount = 0;

                // [방어적 코딩] 관리자 권한이 없거나 권한 밖의 프로세스 접근 시 예외 방어
                try
                {
                    gdiCount = GetGuiResources(targetProc.Handle, GR_GDIOBJECTS);
                    userCount = GetGuiResources(targetProc.Handle, GR_USEROBJECTS);
                }
                catch { /* 핸들 권한 부족 시 0으로 조용히 처리 */ }

                return new ProcessMemoryInfo(
                    isRunning: true,
                    pid: targetProc.Id,
                    name: targetProc.ProcessName,
                    workingSet: targetProc.WorkingSet64,
                    privateMem: targetProc.PrivateMemorySize64,
                    pagedMem: targetProc.PagedMemorySize64, // 🌟 작업관리자 오차 추적용 지표
                    virtualMem: targetProc.VirtualMemorySize64,
                    threads: targetProc.Threads.Count,
                    handles: targetProc.HandleCount,
                    gdi: gdiCount,
                    user: userCount
                );
            }
            catch (Exception)
            {
                return ProcessMemoryInfo.NotRunning;
            }
            finally
            {
                // [Zero-Leak] 획득한 프로세스 핸들 자원 즉각 해제
                foreach (var p in processes)
                {
                    p?.Dispose();
                }
            }
        }
    }

    // ==========================================
    // 3. UI 로직 (GDI/핸들 통합 뷰어)
    // ==========================================
    public partial class MainForm : Form
    {
        private TextBox _txtProcessName;
        private Button _btnToggleMonitor;
        private CheckBox _chkAlwaysOnTop;

        private WinLabel _lblTargetInfo;
        private WinLabel _lblRamValue;
        private WinLabel _lblCommitValue;
        private WinLabel _lblPagedValue; // 🌟 추가됨

        private WinLabel _lblHandles;    // 🌟 추가됨
        private WinLabel _lblGdi;        // 🌟 추가됨
        private WinLabel _lblThreads;    // 🌟 추가됨

        private System.Windows.Forms.Timer _pollTimer;
        private readonly ProcessMonitorCore _monitorCore;
        private bool _isMonitoring = false;

        public MainForm()
        {
            _monitorCore = new ProcessMonitorCore();
            InitializeCompactUI();

            _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _pollTimer.Tick += PollTimer_Tick;
        }

        private void BtnToggleMonitor_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtProcessName.Text))
            {
                MessageBox.Show("추적할 프로세스 이름을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _isMonitoring = !_isMonitoring;

            if (_isMonitoring)
            {
                _txtProcessName.Enabled = false;
                _btnToggleMonitor.Text = "STOP";
                _btnToggleMonitor.BackColor = WinColor.FromArgb(204, 74, 74);
                _lblTargetInfo.Text = "Target PID: 스캔 중...";
                _lblTargetInfo.ForeColor = WinColor.LimeGreen;
                _pollTimer.Start();
            }
            else
            {
                _pollTimer.Stop();
                _txtProcessName.Enabled = true;
                _btnToggleMonitor.Text = "START";
                _btnToggleMonitor.BackColor = WinColor.FromArgb(0, 122, 204);
                _lblTargetInfo.Text = "Status: Stopped";
                _lblTargetInfo.ForeColor = WinColor.Gray;

                _lblRamValue.Text = "0 K";
                _lblCommitValue.Text = "0 K";
                _lblPagedValue.Text = "0 K";
                _lblHandles.Text = "Handles: 0";
                _lblGdi.Text = "GDI: 0 (Max 10000)";
                _lblThreads.Text = "Threads: 0";
            }
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            ProcessMemoryInfo info = _monitorCore.GetProcessMemory(_txtProcessName.Text);

            if (info.IsRunning)
            {
                long ramK = info.WorkingSetBytes / 1024;
                long commitK = info.PrivateMemoryBytes / 1024;
                long pagedK = info.PagedMemoryBytes / 1024;

                _lblTargetInfo.Text = $"Target PID: {info.PID} ({info.ProcessName}.exe)";
                _lblTargetInfo.ForeColor = WinColor.LimeGreen;

                _lblRamValue.Text = $"{ramK:N0} K  ({(ramK / 1024.0):N1} MB)";
                _lblCommitValue.Text = $"{commitK:N0} K  ({(commitK / 1024.0):N1} MB)";
                _lblPagedValue.Text = $"{pagedK:N0} K  ({(pagedK / 1024.0):N1} MB)";

                _lblThreads.Text = $"Threads: {info.ThreadCount:N0}";
                _lblHandles.Text = $"Handles: {info.HandleCount:N0}";

                // 🌟 GDI 한도(10,000) 임박 시 위험 경고 색상 처리
                _lblGdi.Text = $"GDI: {info.GdiObjectCount:N0} | USER: {info.UserObjectCount:N0}";
                if (info.GdiObjectCount > 8000 || info.HandleCount > 50000)
                {
                    _lblGdi.ForeColor = WinColor.Tomato;
                    _lblHandles.ForeColor = WinColor.Tomato;
                }
                else
                {
                    _lblGdi.ForeColor = WinColor.Cyan;
                    _lblHandles.ForeColor = WinColor.Cyan;
                }
            }
            else
            {
                _lblTargetInfo.Text = "Target PID: Process Dead or Terminated";
                _lblTargetInfo.ForeColor = WinColor.Tomato;

                _lblRamValue.Text = "--- K";
                _lblCommitValue.Text = "--- K";
                _lblPagedValue.Text = "--- K";
            }
        }

        private void ChkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            this.TopMost = _chkAlwaysOnTop.Checked;
        }

        private void InitializeCompactUI()
        {
            this.Text = "Vision FA Profiler Pro (Mem/GDI/Handle)";
            // 🌟 폼 사이즈를 세로로 대폭 확장하여 잘림 원천 방지
            this.ClientSize = new Size(450, 390);
            this.BackColor = WinColor.FromArgb(25, 25, 25);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 9.5F, FontStyle.Regular);
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;

            int yPos = 10;

            var lblName = new WinLabel { Text = "Process:", Location = new Point(10, yPos + 4), Size = new Size(80, 20), ForeColor = WinColor.LightGray };
            _txtProcessName = new TextBox { Location = new Point(90, yPos), Size = new Size(200, 23), BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White, Text = "devenv" };
            _btnToggleMonitor = new Button { Text = "START", Location = new Point(300, yPos - 1), Size = new Size(130, 25), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            _btnToggleMonitor.FlatAppearance.BorderSize = 0;
            _btnToggleMonitor.Click += BtnToggleMonitor_Click;

            this.Controls.Add(lblName); this.Controls.Add(_txtProcessName); this.Controls.Add(_btnToggleMonitor);

            yPos += 35;

            _lblTargetInfo = new WinLabel { Text = "Target PID: None", Location = new Point(10, yPos), Size = new Size(420, 20), Font = new Font("Consolas", 10F, FontStyle.Bold), ForeColor = WinColor.Gray };
            this.Controls.Add(_lblTargetInfo);

            yPos += 35;

            // 1. 작업 집합 (Physical RAM)
            var lblRamTitle = new WinLabel { Text = "① 작업 집합 (Working Set - RAM):", Location = new Point(10, yPos), Size = new Size(420, 15), ForeColor = WinColor.LightSkyBlue };
            _lblRamValue = new WinLabel { Text = "0 K", Location = new Point(10, yPos + 15), Size = new Size(420, 30), Font = new Font("Consolas", 15F, FontStyle.Bold), ForeColor = WinColor.White, TextAlign = ContentAlignment.MiddleRight };
            this.Controls.Add(lblRamTitle); this.Controls.Add(_lblRamValue);

            yPos += 55;

            // 2. 전용 커밋 (Private Memory)
            var lblCommitTitle = new WinLabel { Text = "② 전용 메모리 (Private Commit):", Location = new Point(10, yPos), Size = new Size(420, 15), ForeColor = WinColor.Tomato };
            _lblCommitValue = new WinLabel { Text = "0 K", Location = new Point(10, yPos + 15), Size = new Size(420, 30), Font = new Font("Consolas", 15F, FontStyle.Bold), ForeColor = WinColor.White, TextAlign = ContentAlignment.MiddleRight };
            this.Controls.Add(lblCommitTitle); this.Controls.Add(_lblCommitValue);

            yPos += 55;

            // 3. 페이징 메모리 (Paged Memory) - 작업관리자 오차 증명용
            var lblPagedTitle = new WinLabel { Text = "③ 페이징/가상 할당 (Paged Memory - OS Reserve):", Location = new Point(10, yPos), Size = new Size(420, 15), ForeColor = WinColor.Gold };
            _lblPagedValue = new WinLabel { Text = "0 K", Location = new Point(10, yPos + 15), Size = new Size(420, 30), Font = new Font("Consolas", 15F, FontStyle.Bold), ForeColor = WinColor.White, TextAlign = ContentAlignment.MiddleRight };
            this.Controls.Add(lblPagedTitle); this.Controls.Add(_lblPagedValue);

            yPos += 55;

            // 🌟 4. GDI, Handle, Thread 대시보드 (현장 누수 추적의 1티어 지표)
            var pnlResources = new Panel { Location = new Point(10, yPos), Size = new Size(420, 45), BackColor = WinColor.FromArgb(40, 40, 40), BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(pnlResources);

            _lblThreads = new WinLabel { Text = "Threads: 0", Location = new Point(5, 12), Size = new Size(110, 20), ForeColor = WinColor.LightGray };
            _lblHandles = new WinLabel { Text = "Handles: 0", Location = new Point(120, 12), Size = new Size(130, 20), ForeColor = WinColor.Cyan };
            _lblGdi = new WinLabel { Text = "GDI: 0 | USER: 0", Location = new Point(255, 12), Size = new Size(160, 20), ForeColor = WinColor.Cyan };

            pnlResources.Controls.AddRange(new Control[] { _lblThreads, _lblHandles, _lblGdi });

            yPos += 55;

            _chkAlwaysOnTop = new CheckBox { Text = "Keep Window On Top", Location = new Point(280, yPos), Size = new Size(150, 20), Checked = true };
            _chkAlwaysOnTop.CheckedChanged += ChkAlwaysOnTop_CheckedChanged;
            this.Controls.Add(_chkAlwaysOnTop);
        }
    }
}