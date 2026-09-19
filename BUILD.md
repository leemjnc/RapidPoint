# 소스에서 빌드하기

Windows x64와 **.NET 10 SDK**가 필요합니다. 실행 파일을 다운로드해 사용하는 경우 SDK는 필요 없습니다.

저장소 루트에서 PowerShell로 실행합니다.

```powershell
dotnet build .\RapidPoint.csproj -c Release
.\build.ps1
```

`build.ps1`은 런타임을 포함한 실행 파일을 만들고 모의 입력·좌표 선택 흐름 테스트를 실행한 뒤 `dist/RapidPoint-Windows-x64.zip`을 생성합니다. 테스트는 실제 게임에 입력을 보내지 않습니다. 로컬 디버그 경로를 배포 파일에 남기지 않도록 경로 매핑과 디버그 심볼 제외를 사용합니다.

개인 설정, 실제 게임 창 스크린샷, 계정 정보, 인증 파일, 빌드 폴더는 커밋하지 마세요. 실행 파일은 Git 저장소에 넣지 말고 GitHub Releases 첨부 파일로 배포합니다.

## 수동 테스트 실행

```powershell
$exe = (Resolve-Path '.\dist\app\RapidPoint.exe').Path
$test = Start-Process -FilePath $exe -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
$test.ExitCode  # 0이면 성공
```

다른 진단 옵션은 `--capture-flow-test`, `--input-validation <출력파일경로>`입니다. `--input-validation`은 모의 입력 검사와 로컬 대기 타이머 측정을 파일에 저장합니다. 게임에서의 실제 입력 수신이나 성공률을 측정하는 도구는 아닙니다.

## 라이선스

RapidPoint 소스는 [MIT](LICENSE)입니다. 배포본에 포함되는 .NET 런타임의 라이선스·제3자 고지는 `DOTNET-LICENSE.txt`, `DOTNET-THIRD-PARTY-NOTICES.txt`, `WINDOWSDESKTOP-LICENSE.txt`를 참고하세요. 런타임 버전을 변경한다면 해당 버전의 고지도 함께 갱신하세요.
