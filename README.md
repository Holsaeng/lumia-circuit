# 루미아 서킷 (Lumia Circuit)

루미아 섬 지하 연구소 미로에서 보급 로봇 **루미**가 크레딧을 모으고 야생동물을 피하는 미로 추격 게임입니다.
Unity 6.3 LTS(URP)와 Blender 5.2로 만들었습니다.

> **비공식 팬 게임입니다.** 이터널 리턴(Eternal Return)의 세계관 용어를 빌려 왔지만 님블뉴런과 관계가 없고,
> 공식 콘텐츠가 아닙니다. 원작의 캐릭터·로고·아트·음원은 쓰지 않았으며, 모델과 효과음은 모두 직접 만들었습니다.
> 권리자의 요청이 있으면 바로 내리겠습니다.

## 플레이

- 웹: GitHub Pages 링크(저장소 About 참고)
- 조작: 방향키 / WASD 이동 · Enter 또는 Space 시작 · P 일시정지 · Esc 타이틀로

## 규칙

- **크레딧 칩**을 모두 모으면 지역을 확보하고 다음 단계로 넘어갑니다. 단계는 `1일차 낮 · 연구소 → 1일차 밤 · 병원 → …` 순서이고, 밤에는 어둡고 동물이 빨라집니다.
- **야생동물** 4종은 성격이 다릅니다.
  - 늑대: 최단 경로로 쫓아옵니다.
  - 박쥐: 진행 방향 앞쪽에서 매복합니다.
  - 멧돼지: 모서리를 순찰하다 가까워지면 쫓습니다.
  - 닭: 제멋대로 돌아다니다 가까워지면 쫓습니다.
- **VF 혈액 샘플**(모서리의 빨간 병)을 먹으면 잠시 "VF 각성" 상태가 되어 동물을 사냥할 수 있습니다. 연속 사냥은 200 → 400 → 800 → 1600 크레딧.
- **항공 보급 상자**가 가끔 미로 가운데에 떨어집니다. 먹으면 보너스 크레딧.
- **금지구역**: 미로 사분면 하나가 예고(노랑) 뒤 금지(빨강)로 바뀝니다. 그 안에 있는 동안 **폭발 타이머**(20초)가 줄고 0이 되면 폭사합니다. 가운데 십자 통로는 언제나 안전합니다.

## 프로젝트 구조

| 경로 | 내용 |
|---|---|
| `Assets/Scripts/Game.cs` | 게임 전체(상태, 이동, 동물 AI, 금지구역, HUD, 연출). 장면은 비어 있고 모든 것을 코드로 만듭니다. |
| `Assets/Scripts/Maze.cs` | 미로 데이터(왼쪽 절반만 적고 좌우 대칭)와 벽 메시 생성 |
| `Assets/Scripts/Sfx.cs` | 효과음 합성(외부 음원 없음) |
| `Assets/Editor/ProjectSetup.cs` | URP 설정과 빌드를 코드로 수행 |
| `Tools/make_assets.py` | Blender로 모델 8종을 만들어 FBX로 내보내는 스크립트 |
| `Assets/Resources/Models/*.fbx` | 위 스크립트의 결과물 |
| `docs/` | 웹(WebGL) 빌드 — GitHub Pages가 서비스 |

## 빌드

Unity 6000.3.28f1(WebGL 모듈 포함)과 Blender 5.2가 필요합니다. 경로에 한글이 없어야 합니다.

모델 다시 만들기:

```
blender -b -P Tools/make_assets.py
```

Windows 빌드(`Build/LumiaCircuit.exe`):

```
Unity -batchmode -projectPath . -buildTarget Win64 -executeMethod ProjectSetup.Build
```

웹 빌드(`docs/`):

```
Unity -batchmode -projectPath . -buildTarget WebGL -executeMethod ProjectSetup.BuildWeb
```

## 라이선스·출처

- 글꼴: [Noto Sans KR](https://fonts.google.com/noto/specimen/Noto+Sans+KR) — SIL Open Font License 1.1
- 이터널 리턴과 관련 명칭은 님블뉴런의 자산입니다.
