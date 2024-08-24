# ChzzkUnity
![Unity](https://img.shields.io/badge/Unity-100000?style=for-the-badge&logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
<br>
Unity Chzzk chatting/donation/subscription Connection<br><br>
유니티 치지직 채팅/도네이션/구독 연결<br>

CHZZK의 비공식 API 라이브러리입니다.<br><br>

## 설치방법
> 유니티 2023.1이상에서만 동작합니다.

NuGet클라이언트를 통해 <b>WebSocketSharp</b>를 설치합니다.<br>
NuGet클라이언트가 없으시다면, 여기를 확인해보세요.<br>
     https://github.com/GlitchEnzo/NuGetForUnity/tree/master<br>
Nuget을 사용하지 않고, 직접 빌드하여 Package에 포함시켜도 됩니다.<br>
자세한 방법은 [https://github.com/sta/websocket-sharp](https://github.com/sta/websocket-sharp)을 참고하세요.<br><br>
     
유니티 패키지 관리자에서 <b>Git URL에서 패키지 설치</b>를 선택하여<br>
<b>UniTask</b>를 설치합니다.<br>
```https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask```<br><br>

유니티 패키지 관리자에서 <b>이름으로 패키지 설치</b>를 선택하여<br>
<b>Newtonsoft.Json</b>를 설치합니다.<br>
```com.unity.nuget.newtonsoft-json```

ChzzkUnity.cs를 원하는 위치에 설치합니다.<br><br>


ChzzkVideoDonationUnity.cs는 기본 기능과 별도로 동작합니다.<br>
영상 도네이션을 인식할 수 있으며, 어느 한 쪽이 없더라도 동작합니다.<br>
영상 도네이션 인식을 위해서는 치지직 스튜디오->방송관리->알림->후원 알림에서 찾을 수 있는 영상 후원 안내 URL이 필요합니다.

<br><br>

## External dependencies
#### Newtonsoft.Json
https://www.nuget.org/packages/Newtonsoft.Json/<br>
#### websocket-sharp
https://github.com/sta/websocket-sharp?tab=readme-ov-file<br>
#### UniTask
https://github.com/Cysharp/UniTask
 
 
