# 제3자 구성요소 고지 (THIRD-PARTY NOTICES)

몽탐색기 에는 **제3자 구성요소 없음.**

- 소스 저장소는 외부 패키지(NuGet 등)·외부 DLL·폰트·소리를 포함하지 않는다. 빌드는 윈도우에 기본 포함된 .NET Framework 4 컴파일러와 기본 참조(System, System.Drawing, System.Windows.Forms, System.Web.Extensions, Microsoft.VisualBasic)만 쓰며, 이들은 운영체제가 제공하는 것으로 함께 배포하지 않는다.
- src\MongUpdate.cs 는 몽 계열 앱이 함께 쓰는 자체 제작 업데이트 공통 모듈로, 몽탐색기 와 같은 GPL-3.0 을 따른다.
- 윈도우 셸 API(ExplorerBrowser 등)는 운영체제 기능을 호출할 뿐 코드를 포함하지 않는다.

구성요소를 추가하면 이 파일에 구성요소·버전·라이선스(SPDX)·출처를 적고 라이선스 원문을 licenses/{구성요소}/ 에 보관한다.
