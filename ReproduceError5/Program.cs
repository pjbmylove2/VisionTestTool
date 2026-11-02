using System;
using System.IO;
using System.Threading;

namespace ReproduceError5
{
    class Program
    {
        static void Main(string[] args)
        {
            // 🚨 [설정] 실제 비전 프로그램이 감시하는 네트워크 공유 폴더 경로로 변경하십시오.
            // 예: @"\\192.0.2.10\RollMap\20260928\" (오늘 날짜 폴더)
            string targetFolder = @"\\192.0.2.10\share\image\RollMap\20260928";

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            Console.WriteLine("====================================================");
            Console.WriteLine("[FA Network Simulator] Error 5 / Error 32 발생기");
            Console.WriteLine($"타겟 폴더: {targetFolder}");
            Console.WriteLine("====================================================");

            int fileIndex = 1;
            while (true)
            {
                // 프로님의 정규식(Regex) 필터를 통과하도록 파일명 구성 (cam0 대상)
                string fileName = $"260928_{DateTime.Now.Ticks}_075689_{fileIndex:D6}_cam1.jpg";
                string fullPath = Path.Combine(targetFolder, fileName);

                try
                {
                    // 1. [Error 32 유발] 파일을 열고 독점(FileShare.None)하여 비전 프로그램의 접근을 막음
                    using (FileStream fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] dummyData = System.Text.Encoding.UTF8.GetBytes("Dummy Image Payload Data");
                        fs.Write(dummyData, 0, dummyData.Length);

                        Console.WriteLine($"[1단계] 파일 생성 및 점유 중 (Error 32 유발 상태): {fileName}");

                        // 비전 프로그램이 이 파일에 접근하려다 Error 32(Sharing Violation)를 맞고 Sleep(100)을 타도록 1초간 점유
                        Thread.Sleep(1000);
                    } // using 블록이 끝나면 파일 핸들이 닫힘.

                    // 2. [Error 5 유발] 핸들을 놓자마자 파일 속성을 '읽기 전용'으로 변경
                    // 숨김(Hidden) 속성은 요청에 따라 제외했습니다.
                    FileInfo fi = new FileInfo(fullPath);
                    fi.IsReadOnly = true;

                    Console.WriteLine($"[2단계] 읽기 전용 속성 부여 완료 (Error 5 유발 상태): {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Simulator Error] {ex.Message}");
                }

                fileIndex++;

                // 3초 대기 후 다음 악성 파일 생성
                Thread.Sleep(3000);
            }
        }
    }
}