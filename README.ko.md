<div align="center">

<img src="docs/assets/banner.png" alt="SysScrub" width="100%">

**Windows 관리, 드라이버 업데이트, 디스크 상태 — 앱 하나로.**

[![릴리스](https://img.shields.io/github/v/release/SametEge/SysScrub?include_prereleases&color=FF6B2C)](https://github.com/SametEge/SysScrub/releases/latest)
[![라이선스](https://img.shields.io/badge/license-MIT-FF6B2C)](LICENSE)
[![빌드](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml/badge.svg)](https://github.com/SametEge/SysScrub/actions/workflows/ci.yml)

### [⬇ 최신 릴리스 내려받기](https://github.com/SametEge/SysScrub/releases/latest)

[English](README.md) · [Türkçe](README.tr.md) · [Deutsch](README.de.md) · [日本語](README.ja.md) · [简体中文](README.zh-Hans.md)

</div>

---

<img src="docs/assets/screens/dashboard.png" alt="SysScrub 대시보드" width="100%">

## 무엇을 하나

프로그램 세 개가 할 일을, 제대로 설계한 화면 하나에 담았습니다.

| | |
|---|---|
| 🧹 **정리** | Windows, 브라우저, 앱이 남긴 것을 규칙에 따라 검사합니다. 삭제한 것은 모두 격리 폴더로 가고 한 번의 클릭으로 되돌아옵니다. |
| 🗂 **레지스트리** | 대상이 사라진 항목을 찾는 검사기 12개. 무언가를 지우기 전에 `.reg` 백업과 복원 지점을 만듭니다. |
| ⚙️ **드라이버** | 하드웨어를 알아내고 오래된 드라이버를 찾습니다. 업데이트는 Windows Update에서 옵니다 — WHQL 서명을 받았고 Microsoft가 그 하드웨어용으로 승인한 것입니다. |
| 📥 **업데이트** | winget으로 설치된 프로그램의 새 버전을 찾습니다. 각 패키지는 제작사 자신의 원본에서 내려받습니다. |
| 🚀 **시작 프로그램** | 부팅할 때 실행되는 모든 것을 한 목록에. 사용 안 함은 Windows 자체 방식을 쓰므로 작업 관리자와 어긋나지 않습니다. |
| 📦 **프로그램** | 한 번에 여러 개 제거. 결과는 항목이 실제로 사라졌는지로 확인합니다 — 종료 코드는 믿을 수 없습니다. |
| 💽 **디스크 상태** | S.M.A.R.T.와 NVMe 상태 데이터를 읽습니다: 온도, 전원 켠 시간, 총 기록량, 남은 수명. 원시 값 옆에 그 뜻도 함께 알려 줍니다. |
| 📊 **저장소 분석** | 무엇이 공간을 잡아먹고 있을까요? 트리맵 보기, 가장 큰 파일, 3단계 중복 파일 찾기. |
| 🕓 **타임라인** | 앱이 시스템에 가한 모든 변경을 시간 순서의 기록 하나로. 어느 지점에서든 되돌릴 수 있습니다. |

## 왜 정리 프로그램을 하나 더

기존 것들은 하나같이 거슬리는 짓을 합니다. 부풀린 "확보한 공간" 숫자, 되돌릴 수 없는 삭제,
비대해진 백그라운드 서비스, 유료 장벽, 원격 수집.

SysScrub이 서 있는 자리는 이렇습니다.

- **검사는 절대 지우지 않습니다.** 모든 모듈이 먼저 읽고, 무엇을 왜 찾았는지 보여 주고, 기다립니다.
  무엇을 지울지는 사용자가 정합니다.
- **되돌릴 수 없는 것은 없습니다.** 정리, 레지스트리, 드라이버, 시작 프로그램 — 모든 변경이 하나의
  타임라인에 남고 그곳에서 되돌릴 수 있습니다.
- **숫자는 진짜입니다.** 확보한 공간은 작업 전후로 디스크에서 실제로 측정하며 추정하지 않습니다.
  "시스템이 40% 빨라졌습니다" 같은 지어낸 말은 없습니다.
- **모를 때는 모른다고 말합니다.** S.M.A.R.T.를 읽을 수 없는 드라이브는 목록에서 사라지지도, 녹색
  배지를 받지도 않습니다 — 왜 읽지 못했는지 말합니다.
- **계정 없음, 광고 없음, 원격 수집 없음, 유료 등급 없음.**

---

## 모듈

### 정리

<img src="docs/assets/screens/cleaner.png" alt="정리" width="100%">

Windows, 브라우저, 앱, 게임 플랫폼, 개발 도구, 개인 정보 흔적에 걸친 48개 규칙. 각 규칙은 무엇을
지우고 그 결과가 무엇인지 밝힙니다 — 불편한 부분까지 포함해서 ("Windows.old를 지우면 이전 Windows
버전으로 되돌릴 수 없습니다").

규칙은 **코드가 아니라 데이터**입니다. [`data/rules/*.json`](data/rules)에 있습니다. 새 정리 대상을
더하는 것은 JSON 항목을 추가하는 일이지 프로그램을 고치는 일이 아닙니다.

파일 하나가 지워지기 전에 안전 검사를 거칩니다. 보호된 Windows 디렉터리, 사용자의 문서, 재분석
지점(정션과 심볼릭 링크는 절대 따라가지 않습니다), 클라우드 자리 표시자 파일은 거부됩니다. 단순한
임시 폴더 밖에 있는 것은 먼저 격리로 갑니다.

### 레지스트리

<img src="docs/assets/screens/registry.png" alt="레지스트리 정리" width="100%">

검사기 12개: 공유 DLL 카운터, 파일 연결, ProgID와 CLSID 항목, COM 서버, 형식 라이브러리, 셸 확장,
제거 항목, 응용 프로그램 경로, 시작 항목, MUICache, 설치 관리자 폴더, 소리 이벤트.

모든 발견 항목은 전체 키 경로**와 왜 죽은 항목으로 보는지**를 함께 보여 줍니다. 삭제 전에 영향을 받는
모든 키의 `.reg` 내보내기를 기록하고, 시스템 복원 지점도 만듭니다. 백업이 실패하면 아무것도 지우지
않습니다.

Windows가 동작하는 데 필요한 키 — 서비스, DriverStore, WinSxS, 구성 요소 서비스, .NET, Defender —
는 코드에 박아 둔 '절대 건드리지 않음' 목록에 있습니다.

### 드라이버

<img src="docs/assets/screens/drivers.png" alt="드라이버 업데이트" width="100%">

SetupAPI로 하드웨어 목록을 만들고, 업데이트 원본은 Windows Update입니다. 목록은 정직한 두 부류로
나뉩니다. Windows Update가 실제로 새 버전을 제공하는 드라이버와, 2년보다 오래됐지만 어떤 원본도
새것을 내놓지 않는 드라이버입니다. 두 번째 부류는 "오래됐을 수 있음"으로 표시합니다 — "오래됨"이라고
하지 않습니다. 우리가 모르기 때문입니다.

무언가를 설치하기 전에, 모든 서드파티 드라이버를 한 번의 클릭으로 백업 폴더에 내보낼 수 있습니다.

### 디스크 상태

<img src="docs/assets/screens/disk-health.png" alt="디스크 상태" width="100%">

NVMe 상태 로그(페이지 0x02)와 ATA S.M.A.R.T.를 드라이브에서 직접 읽습니다. 온도, 전원 켠 시간, 전원
켜짐 횟수, 총 기록량, 남은 수명, 예비 블록, 비정상 종료, 정정 불가 오류 — 각각 옆에 쉬운 말로 된
설명과 함께.

제조사별 특성의 의미는 [`data/smart-attributes.json`](data/smart-attributes.json)에 있어서, 새
제조사를 지원하는 일은 표에 한 줄을 더하는 것이지 코드를 고치는 것이 아닙니다.

### 저장소 분석

<img src="docs/assets/screens/disk-analysis.png" alt="저장소 분석" width="100%">

드라이브 전체의 squarified 트리맵, 가장 큰 파일, 파일 형식별 분포. 읽기 전용입니다. 파일을 지우지도,
열지도 않습니다. 클라우드 파일은 내려받지 않습니다 — 디스크에서 공간을 쓰지 않으므로 집계에도 넣지
않습니다. 읽을 수 없던 폴더는 조용히 건너뛰지 않고 세어서 알려 줍니다.

중복 파일 찾기는 세 단계로 비교합니다 — 크기, 그다음 앞뒤 4 KB, 마지막으로 전체 SHA-256 — 그래서
꼭 필요한 만큼만 해시합니다.

### 시작 프로그램과 프로그램

<img src="docs/assets/screens/startup.png" alt="시작 프로그램 관리" width="100%">

Run과 RunOnce 키(레지스트리의 두 보기), 시작 폴더, 로그온으로 실행되는 예약 작업, Microsoft가 아닌
자동 시작 서비스. 사용 안 함은 작업 관리자가 쓰는 것과 같은 `StartupApproved` 저장소에 기록하므로 둘이
어긋나지 않습니다. 부팅 지연은 추측이 아니라 Windows의 Diagnostics-Performance 이벤트 로그에서 읽은
실제 측정값입니다.

제거 기능은 각 프로그램의 제거 프로그램을 실행한 뒤, 레지스트리 항목이 실제로 사라졌는지로 결과를
확인합니다.

### 타임라인

<img src="docs/assets/screens/timeline.png" alt="타임라인" width="100%">

실행할 때마다 기록됩니다: 무엇을 지웠는지, 몇 바이트인지, 어떤 규칙인지, 되돌릴 수 있는지. 격리된
정리는 한 번의 클릭으로 복원됩니다.

---

## 설치

**설치 관리자:** [Releases](https://github.com/SametEge/SysScrub/releases/latest)에서
`SysScrub-Setup-*.exe`를 받아 실행하세요.

**포터블:** `SysScrub-*-portable-x64.zip`을 풀고 실행하면 됩니다 — 설치가 필요 없습니다. 실행 파일
옆에 빈 `portable.flag` 파일을 두면 앱이 모든 설정과 로그를 자기 폴더에 보관하고 시스템에는 아무것도
쓰지 않습니다(USB에서 쓸 때 유용합니다).

> **SmartScreen 경고:** 코드 서명 인증서로 서명하지 않았기 때문에 Windows가 "알 수 없는 게시자"
> 경고를 보여 줍니다. *추가 정보 → 실행*을 누르세요. 직접 빌드하고 싶다면 소스가 모두 여기 있습니다.

**요구 사항:** Windows 10 1809 이상(64비트). .NET 8 데스크톱 런타임이 없으면 설치 관리자가 받기를
제안합니다. self-contained 포터블 빌드는 사전 요구 사항이 없습니다.

앱은 관리자 권한으로 실행됩니다 — Windows Update 캐시, 서비스 중지, S.M.A.R.T. 읽기는 그렇지 않으면
불가능합니다.

**업데이트**는 이 저장소의 릴리스를 하루에 한 번 확인하며 설정 화면에서 설치할 수 있습니다. 내려받은
패키지는 릴리스와 함께 게시된 `SHA256SUMS.txt`로 검증합니다. 해시가 맞지 않으면 파일을 지우고 아무것도
실행하지 않습니다. 이 확인은 버전 번호만 읽고 아무것도 보내지 않으며, 끌 수도 있습니다.

## 언어

인터페이스는 **터키어, 영어, 독일어, 일본어, 한국어, 중국어 간체**로 제공되며 48개 정리 규칙 설명도
포함합니다. 처음 실행할 때 Windows 설정에서 언어를 고르고, 언제든 바꿀 수 있으며 다시 시작하지 않아도
곧바로 적용됩니다.

카탈로그는 [`data/i18n/`](data/i18n)에 있는 순수 JSON입니다 — 한 언어에 기여하는 일은 파일 하나를
보내는 것입니다. 독일어, 일본어, 한국어, 중국어 번역은 원어민 검토를 기다리고 있습니다.

## 상태

활발히 개발 중이며 현재 `0.14.0-alpha`입니다. 아홉 개 모듈이 동작하며 실제 시스템 데이터를 읽습니다.
아직 없는 것은 [docs/ROADMAP.md](docs/ROADMAP.md)에 정직하게 적혀 있습니다.

| 완료 | 아직 |
|---|---|
| 정리 · 레지스트리 · 드라이버 · 소프트웨어 업데이트 | 백그라운드 모드와 알림 영역 |
| 시작 프로그램 · 프로그램 · 디스크 상태 · 저장소 분석 | 명령 팔레트(Ctrl+K) |
| 타임라인 · 6개 언어 · 자동 업데이트 | Windows Update 외의 드라이버 원본 |

## 소스에서 빌드하기

```powershell
git clone https://github.com/SametEge/SysScrub.git
cd SysScrub
dotnet build
dotnet run --project src/SysScrub.App
```

`dist/`와 설치 관리자를 만들려면:

```powershell
./build/publish.ps1 -SelfContained
```

설치 관리자 단계에는 [Inno Setup 6](https://jrsoftware.org/isdl.php)이 필요합니다
(`winget install JRSoftware.InnoSetup`). 없으면 그 단계는 건너뛰고 포터블 결과물은 그대로 만들어집니다.

## 구성

```
src/SysScrub.Core    엔진 — 검사, 안전 확인, 드라이버와 디스크 계층. UI 의존성 없음
src/SysScrub.App     WPF 인터페이스, 디자인 시스템, 지역화
src/SysScrub.Cli     예약/자동 정리와 기술자용 보고서
tests/               테스트 496개: 안전 확인, 규칙 엔진, S.M.A.R.T. 해석, 카탈로그
data/rules           정리 규칙(JSON)
data/i18n            인터페이스 번역(JSON)
build/               릴리스 스크립트와 버전 번호
installer/           Inno Setup 스크립트와 마법사 이미지
```

## 참여

버그 신고와 제안은 [issue](https://github.com/SametEge/SysScrub/issues)로 환영합니다.

두 가지는 C#이 전혀 필요 없습니다.

- **정리 규칙** — [`data/rules/`](data/rules)에 JSON 항목을 추가하기
- **번역** — [`data/i18n/`](data/i18n)의 파일 하나를 편집하기

## 라이선스

[MIT](LICENSE) · 서드파티 고지는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에
