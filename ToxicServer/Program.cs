///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// ToxicMesServer.cs (Dummy Server for OOM Simulation)
// - 🚀 1호기 네트워크 지연 및 MES 서버 병목 상태를 완벽히 재현하는 악성 서버
// - 🚀 TCP Window Zero 현상 및 Connect 타임아웃을 유발하여 클라이언트의 큐를 폭파시킴
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ToxicServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(" [1호기 시뮬레이터] Toxic MES Server 기동 준비...");
            Console.WriteLine("==================================================");

            // OCR 프로그램이 바라보는 IP와 Port를 입력하세요. (로컬 테스트 시 127.0.0.1)
            int port = 5000;
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            Console.WriteLine($"[Listen] 포트 {port} 대기 중... OCR 프로그램을 연결하세요.\n");

            while (true)
            {
                try
                {
                    // 1. OCR 프로그램의 연결 요청을 받습니다.
                    Socket clientSocket = listener.AcceptSocket();
                    Console.WriteLine($"\n[Connected] OCR 장비 연결됨! ({clientSocket.RemoteEndPoint})");

                    // 🚨 [치명적 덫 1: TCP Receive Window 고갈]
                    // 서버가 연결은 맺어주지만, Receive()를 절대 호출하지 않습니다.
                    // 이렇게 되면 OS의 TCP 수신 버퍼가 꽉 차게 되고, OCR 장비는 Send()를 하다가 블로킹(응답 대기)에 빠집니다.
                    Console.WriteLine("[Trap 1] 데이터를 읽지 않고 방치합니다. (TCP Window Exhaustion 시작...)");

                    // 🚨 [치명적 덫 2: 갑작스러운 강제 단선]
                    // 연결된 상태로 OCR 프로그램의 큐가 꽉 차게 만든 뒤, 
                    Thread.Sleep(10000);

                    clientSocket.Close();
                    Console.WriteLine("[Trap 2] 소켓을 강제로 끊었습니다! (OCR은 재연결 시도를 하며 큐가 잠길 것입니다)");

                    // 🚨 [치명적 덫 3: 무응답 대기]
                    // 소켓을 끊은 직후 5초 동안 아예 연결(Accept)을 받아주지 않습니다.
                    // 이때 OCR 프로그램의 `m_ClientSocket.Connect(...)` 는 lock 블록 안에서 
                    // 3초간 타임아웃 대기를 하며 비전 스레드 전체를 마비시킬 것입니다.
                    Console.WriteLine("[Trap 3] 5초간 연결을 거부합니다. (Connect Timeout 병목 유발 중...)");
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }
}