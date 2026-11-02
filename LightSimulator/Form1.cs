using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// 🌟 [방어적 코딩] 명시적 별칭 선언
using WinColor = System.Drawing.Color;
using WinLabel = System.Windows.Forms.Label;
using FontStyle = System.Drawing.FontStyle;

namespace MachineVision.Tool.LightSimulator
{
    // ==========================================
    // 1. 데이터 모델 및 인터페이스 (KPC Protocol)
    // ==========================================
    public class LightChannelState
    {
        public int ChannelId { get; set; }  // 0~3
        public bool IsOn { get; set; }
        public int PwmValue { get; set; }   // 0~1023
    }

    public interface ILightSimulator : IDisposable
    {
        event Action<LightChannelState> OnChannelStateChanged;
        event Action<string, WinColor> OnLogReceived;

        bool IsOpen { get; }
        void OpenPort(string portName);
        void ClosePort();
    }

    // ==========================================
    // 2. 비즈니스 로직 (🚀 RS232 통신 파서 엔진)
    // ==========================================
    public class KpcLightSimulatorCore : ILightSimulator
    {
        private SerialPort _serialPort;
        private readonly ConcurrentQueue<byte> _receiveBuffer = new ConcurrentQueue<byte>();
        private readonly LightChannelState[] _channels = new LightChannelState[4];

        private Task _parseTask;
        private CancellationTokenSource _cts;

        public event Action<LightChannelState> OnChannelStateChanged;
        public event Action<string, WinColor> OnLogReceived;

        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

        public KpcLightSimulatorCore()
        {
            for (int i = 0; i < 4; i++)
            {
                _channels[i] = new LightChannelState { ChannelId = i, IsOn = false, PwmValue = 0 };
            }
        }

        public void OpenPort(string portName)
        {
            if (IsOpen) throw new InvalidOperationException("포트가 이미 열려 있습니다.");

            // 🌟 매뉴얼 스펙 적용: 9600 bps, 8 Data Bit, 1 Stop Bit, None Parity
            _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();

            _cts = new CancellationTokenSource();
            _parseTask = Task.Run(() => ParseLoopAsync(_cts.Token));

            OnLogReceived?.Invoke($"[{portName}] 포트 개방 및 시뮬레이터 가동 (9600, N, 8, 1)", WinColor.LimeGreen);
        }

        public void ClosePort()
        {
            _cts?.Cancel();

            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;

                OnLogReceived?.Invoke("포트가 닫혔습니다.", WinColor.Orange);
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead > 0)
                {
                    byte[] buffer = new byte[bytesToRead];
                    _serialPort.Read(buffer, 0, bytesToRead);

                    // 🌟 수신된 바이트를 ConcurrentQueue에 밀어 넣음 (UI 스레드 차단 방지)
                    foreach (byte b in buffer)
                    {
                        _receiveBuffer.Enqueue(b);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogReceived?.Invoke($"[수신 오류] {ex.Message}", WinColor.Tomato);
            }
        }

        // 🌟 스레드 안전한 링버퍼 기반 패킷 파싱 엔진
        private async Task ParseLoopAsync(CancellationToken token)
        {
            const byte STX = 0x02;
            const byte ETX = 0x03;
            List<byte> packet = new List<byte>();

            while (!token.IsCancellationRequested)
            {
                if (_receiveBuffer.TryDequeue(out byte b))
                {
                    if (b == STX)
                    {
                        packet.Clear();
                        packet.Add(b);
                    }
                    else if (b == ETX && packet.Count > 0 && packet[0] == STX)
                    {
                        packet.Add(b);
                        ProcessPacket(packet.ToArray());
                        packet.Clear();
                    }
                    else if (packet.Count > 0)
                    {
                        packet.Add(b);
                        // 가비지 데이터(STX 없이 들어오거나 너무 긴 데이터) 방어
                        if (packet.Count > 20) packet.Clear();
                    }
                }
                else
                {
                    await Task.Delay(10, token).ConfigureAwait(false);
                }
            }
        }

        private void ProcessPacket(byte[] packet)
        {
            // 방어적 코딩: 최소 패킷 길이 검증 (STX + CH + CMD + ETX = 4바이트)
            if (packet.Length < 4) return;

            string rawAscii = Encoding.ASCII.GetString(packet).Replace("\x02", "[STX]").Replace("\x03", "[ETX]");

            char chChar = (char)packet[1];
            char cmdChar = (char)packet[2];

            int chIndex = chChar - '0';
            if (chIndex < 0 || chIndex > 3)
            {
                OnLogReceived?.Invoke($"[오류] 잘못된 채널 번호 수신: {rawAscii}", WinColor.Tomato);
                return;
            }

            var targetChannel = _channels[chIndex];

            // 1. 4Byte 통신: ON/OFF 제어 (STX + CH + 'O'/'F' + ETX)
            if (packet.Length == 4)
            {
                if (cmdChar == 'O' || cmdChar == 'S') // 매뉴얼 예외 대비 'S' 도 허용
                {
                    targetChannel.IsOn = true;
                    OnLogReceived?.Invoke($"[CH {chIndex + 1}] 조명 ON 수신: {rawAscii}", WinColor.Cyan);
                }
                else if (cmdChar == 'F' || cmdChar == 'P') // 매뉴얼 예외 대비 'P' 도 허용
                {
                    targetChannel.IsOn = false;
                    OnLogReceived?.Invoke($"[CH {chIndex + 1}] 조명 OFF 수신: {rawAscii}", WinColor.Gray);
                }

                OnChannelStateChanged?.Invoke(targetChannel);
            }
            // 2. 8Byte 통신: PWM 밝기 제어 (STX + CH + 'W' + "0000" + ETX)
            else if (packet.Length == 8 && cmdChar == 'W')
            {
                string pwmStr = Encoding.ASCII.GetString(packet, 3, 4);
                if (int.TryParse(pwmStr, out int pwmValue))
                {
                    if (pwmValue > 1023) pwmValue = 1023; // 매뉴얼 상 MAX 1023 방어
                    targetChannel.PwmValue = pwmValue;

                    OnLogReceived?.Invoke($"[CH {chIndex + 1}] 밝기 변경 ({pwmValue}) 수신: {rawAscii}", WinColor.Yellow);
                    OnChannelStateChanged?.Invoke(targetChannel);
                }
                else
                {
                    OnLogReceived?.Invoke($"[오류] PWM 값 파싱 실패: {rawAscii}", WinColor.Tomato);
                }
            }
            else
            {
                OnLogReceived?.Invoke($"[경고] 알 수 없는 패킷 규격: {rawAscii}", WinColor.Orange);
            }
        }

        public void Dispose()
        {
            ClosePort();
        }
    }

    // ==========================================
    // 3. UI 로직 (MainForm 및 채널 뷰어)
    // ==========================================
    public partial class MainForm : Form
    {
        private ComboBox _cboPorts;
        private Button _btnConnect;
        private RichTextBox _rtbLog;

        private ChannelControl[] _channelControls;
        private readonly ILightSimulator _simulator;

        public MainForm()
        {
            _simulator = new KpcLightSimulatorCore();
            _simulator.OnLogReceived += Simulator_OnLogReceived;
            _simulator.OnChannelStateChanged += Simulator_OnChannelStateChanged;

            InitializeDarkUI();
            LoadPorts();
        }

        private void LoadPorts()
        {
            _cboPorts.Items.Clear();
            _cboPorts.Items.AddRange(SerialPort.GetPortNames());
            if (_cboPorts.Items.Count > 0) _cboPorts.SelectedIndex = 0;
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_simulator.IsOpen)
                {
                    if (string.IsNullOrWhiteSpace(_cboPorts.Text)) return;
                    _simulator.OpenPort(_cboPorts.Text);
                    _btnConnect.Text = "DISCONNECT";
                    _btnConnect.BackColor = WinColor.FromArgb(204, 74, 74);
                    _cboPorts.Enabled = false;
                }
                else
                {
                    _simulator.ClosePort();
                    _btnConnect.Text = "CONNECT";
                    _btnConnect.BackColor = WinColor.FromArgb(0, 122, 204);
                    _cboPorts.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"연결 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Simulator_OnLogReceived(string msg, WinColor color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Simulator_OnLogReceived(msg, color)));
                return;
            }

            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            _rtbLog.ScrollToCaret();
        }

        private void Simulator_OnChannelStateChanged(LightChannelState state)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Simulator_OnChannelStateChanged(state)));
                return;
            }

            // 해당 채널의 UI 컨트롤 업데이트
            if (state.ChannelId >= 0 && state.ChannelId < 4)
            {
                _channelControls[state.ChannelId].UpdateState(state.IsOn, state.PwmValue);
            }
        }

        private void InitializeDarkUI()
        {
            this.Text = "KPC45-12V-4CH Virtual Controller (RS232 Emulator)";
            this.Size = new Size(680, 520);
            this.BackColor = WinColor.FromArgb(30, 30, 30);
            this.ForeColor = WinColor.WhiteSmoke;
            this.Font = new Font("Consolas", 10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };
            this.Controls.Add(pnlTop);

            var lblPort = new WinLabel { Text = "COM Port:", Location = new Point(10, 20), Size = new Size(80, 20), ForeColor = WinColor.LightSkyBlue };
            _cboPorts = new ComboBox { Location = new Point(100, 18), Size = new Size(120, 23), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = WinColor.FromArgb(45, 45, 48), ForeColor = WinColor.White };

            Button btnRefresh = new Button { Text = "↻", Location = new Point(230, 17), Size = new Size(30, 25), BackColor = WinColor.FromArgb(62, 62, 66), FlatStyle = FlatStyle.Flat };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Click += (s, e) => LoadPorts();

            _btnConnect = new Button { Text = "CONNECT", Location = new Point(280, 15), Size = new Size(150, 30), BackColor = WinColor.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 11F, FontStyle.Bold) };
            _btnConnect.FlatAppearance.BorderSize = 0;
            _btnConnect.Click += BtnConnect_Click;

            pnlTop.Controls.AddRange(new Control[] { lblPort, _cboPorts, btnRefresh, _btnConnect });

            // 🌟 4채널 디지털 UI 컨테이너
            var pnlChannels = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 200, Padding = new Padding(10, 10, 10, 10) };
            this.Controls.Add(pnlChannels);
            pnlChannels.BringToFront();

            _channelControls = new ChannelControl[4];
            for (int i = 0; i < 4; i++)
            {
                _channelControls[i] = new ChannelControl($"CH {i + 1}");
                pnlChannels.Controls.Add(_channelControls[i]);
            }

            // 로그 뷰어
            var pnlLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            this.Controls.Add(pnlLog);
            pnlLog.BringToFront();

            var lblLog = new WinLabel { Text = "[ RS232 통신 패킷 모니터 ]", Dock = DockStyle.Top, Height = 25, ForeColor = WinColor.Gold, Font = new Font("Consolas", 10F, FontStyle.Bold) };
            _rtbLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = WinColor.FromArgb(18, 18, 18), ForeColor = WinColor.LightGray, ReadOnly = true, BorderStyle = BorderStyle.None };

            pnlLog.Controls.Add(_rtbLog);
            pnlLog.Controls.Add(lblLog);
        }
    }

    // ==========================================
    // 4. 개별 채널 상태 표시용 커스텀 컨트롤
    // ==========================================
    public class ChannelControl : Panel
    {
        private Panel _pnlLight;
        private WinLabel _lblTitle;
        private WinLabel _lblPwm;
        private ProgressBar _prgPwm;

        public ChannelControl(string title)
        {
            this.Size = new Size(150, 160);
            this.BackColor = WinColor.FromArgb(45, 45, 48);
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Margin = new Padding(5);

            _lblTitle = new WinLabel { Text = title, Location = new Point(0, 10), Size = new Size(150, 20), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas", 12F, FontStyle.Bold), ForeColor = WinColor.WhiteSmoke };

            _pnlLight = new Panel { Location = new Point(50, 40), Size = new Size(50, 50), BackColor = WinColor.FromArgb(60, 60, 60) }; // 초기 상태: 꺼짐 (어두운 회색)

            _lblPwm = new WinLabel { Text = "PWM: 0", Location = new Point(0, 100), Size = new Size(150, 20), TextAlign = ContentAlignment.MiddleCenter, ForeColor = WinColor.LightSkyBlue };

            _prgPwm = new ProgressBar { Location = new Point(20, 125), Size = new Size(110, 15), Maximum = 1023, Value = 0, Style = ProgressBarStyle.Continuous };

            this.Controls.AddRange(new Control[] { _lblTitle, _pnlLight, _lblPwm, _prgPwm });
        }

        public void UpdateState(bool isOn, int pwm)
        {
            // On 이면 눈에 띄는 노란색, Off면 어두운 회색으로 램프 표현
            _pnlLight.BackColor = isOn ? WinColor.Yellow : WinColor.FromArgb(60, 60, 60);

            // PWM 프로그레스바 및 텍스트 표현
            _lblPwm.Text = $"PWM: {pwm}";
            _prgPwm.Value = Math.Max(0, Math.Min(1023, pwm));
        }
    }
}