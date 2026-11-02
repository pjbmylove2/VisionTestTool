# VisionTestTool

머신비전·OCR 검사 장비(FA)를 개발하고 현장에서 검증할 때 쓰는 **테스트·시뮬레이션·분석 도구 모음**입니다.
실제 설비(PLC, 카메라, 조명, 공유 폴더, MES 서버) 없이 비전 프로그램을 시험하거나, 현장 로그·이미지·데이터셋을 빠르게 분석하는 용도입니다.

## 개발 환경

| 항목 | 내용 |
|---|---|
| IDE | Visual Studio 2022 |
| 언어 | C# |
| 프레임워크 | .NET Framework 4.7.2 (WinForms) — `ReproduceError5`만 .NET 8 콘솔 |
| 패키지 | NuGet `packages.config` (저장소에는 포함하지 않음, 빌드 시 자동 복원) |

```bash
# 솔루션 전체 빌드 (VS 2022 Developer Command Prompt)
nuget restore VisionTestTool.sln
msbuild VisionTestTool.sln /p:Configuration=Debug
```

## 프로젝트 구성

> 프로젝트별 실행 화면 캡처와 코드 분석은 [docs/PROJECTS.md](docs/PROJECTS.md)에 있습니다.

### 설비·통신 시뮬레이터

| 프로젝트 | 설명 |
|---|---|
| **PlcSimulatorForm** | Roll-Map OCR 인터페이스 맵을 구현한 **가상 PLC TCP 서버**. PLC→PC 패킷(엔코더, PLC 상태, Vision Command, NG Tag, LOT ID, 모델, 속도, Image Status)을 주기적으로 송신하고 PC→PLC 응답(30B)을 실시간 해석해 표시. F2 창에서 송수신 항목을 골라 가변 길이 패킷을 맞추며, **원본 58B / 수정 60B(W28 Image Status: X Mirror·Y Flip·Rotate 90/180/270/360)** 전환 지원. 엔코더 자동 증가, 하트비트 토글, Raw 바이트 직접 입력(오류 패턴 시험) 가능 |
| **LightSimulator** | KPC45-12V-4CH 조명 컨트롤러 **RS-232 에뮬레이터**(9600bps, 8N1). STX/ETX 프레임의 ON/OFF·PWM 밝기 명령을 파싱해 4채널 상태를 표시 |
| **FileCopySimulator** | 카메라 이미지가 공유 폴더에 쌓이는 상황을 재현하는 **24/7 이미지 복사 시뮬레이터**. 원본 파일명 규격(`yyyyMMdd_HHmmssfff_…_camN`)을 유지하며 타임스탬프만 바꿔 무한 복사, 자정 폴더 전환과 버퍼(히스테리시스) 제어 포함 |
| **Simulator2** | FileCopySimulator 확장판(Hybrid Mode). 24/7 연속 모드와 **단순 원본 복사 모드**(지정 간격으로 1회전 후 자동 종료) 지원 |
| **ToxicServer** | 장애 재현용 **악성 MES TCP 서버**(콘솔). 연결 후 수신하지 않아 TCP Window를 고갈시키고, 강제 단선·Accept 지연으로 클라이언트 송신 큐 폭주와 Connect 타임아웃을 유발 |
| **ReproduceError5** | 네트워크 공유 폴더의 **Win32 Error 32(공유 위반) / Error 5(접근 거부)** 재현기(.NET 8 콘솔). 이미지 파일을 독점 점유한 뒤 읽기 전용으로 바꿔 비전 프로그램의 파일 접근 예외 처리를 시험 |

### 로그·통신 분석

| 프로젝트 | 설명 |
|---|---|
| **CommLogProfiler** | PLC↔OCR PC 통신 로그 다중 파일 분석기. 패킷을 해석해 연결 끊김·NG·예외 이벤트를 추출하고, 하트비트로 반복 전송되는 과거 결과와 **새 판독 결과(엣지 트리거)** 를 구분해 24시간 요약 대시보드로 표시 |
| **PacketParserTool** | PLC→OCR(48/58B), OCR→PLC(30B) **패킷 파서**. 송수신 로그 문자열을 붙여 넣으면 바이트를 추출해 항목별로 해석(좌우 분할 뷰) |
| **MemoryAnalyzer** | 비전 프로그램 로그의 `[Mem]`/`[Res]` 항목을 여러 날짜 파일에 걸쳐 병합 파싱하고 ScottPlot으로 **메모리·리소스 추이 그래프** 표시(수백만 포인트 대응) |
| **MemoryTracker** | 실행 중 프로세스의 RAM·Commit·Paged 메모리, 핸들, **GDI/USER 객체 수**, 스레드를 실시간 모니터링(GDI 한도 임박 시 경고) |

### 이미지 비교

| 프로젝트 | 설명 |
|---|---|
| **Comparator** | OpenCvSharp 기반 이미지 A(기준)/B(대상) **차이 검출기**. 픽셀 차이 이진화·팽창 후 차이 마스크, 불량 영역을 도려낸 이미지, 빨간색 오버레이 이미지 3종 저장 |
| **OpenCvMaskComparator** | 기준 이미지에서 문자 영역을 이진화해 마스크로 만들고 대상 이미지에 투영하는 **마스킹 비교 도구** |

### 파일·데이터셋 유틸리티

| 프로젝트 | 설명 |
|---|---|
| **DatasetExtractor** | YOLO 형식 라벨 TXT 폴더를 스캔해 파일별 **클래스 개수 CSV** 생성(대용량 폴더 스트리밍 처리) |
| **FileListExtractor** | 폴더 내 머신비전 이미지 확장자 파일명만 추출·정렬해 TXT로 저장 |
| **FileRenamer** | 조건에 맞는 파일을 Output 폴더로 **일괄 복사·이름 변경** |
| **XmlGenerator** | 폴더를 재귀 탐색해 파일 목록을 `<string>파일명</string>` XML 항목으로 출력(권한 없는 폴더는 건너뜀) |
| **VisionTestToll** | 빈 WinForms 템플릿 프로젝트 |

## 주의

- `ToxicServer`, `ReproduceError5`는 **의도적으로 장애를 일으키는 도구**입니다. 운영 설비나 실서버가 아닌 테스트 환경에서만 사용하십시오.
- 코드 안의 IP·공유 폴더 경로는 예시 값입니다(`192.0.2.x`는 문서용 예약 대역). 사용 환경에 맞게 바꿔서 쓰십시오.
