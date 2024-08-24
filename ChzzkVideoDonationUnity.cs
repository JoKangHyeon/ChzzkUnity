using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WebSocketSharp;
using System.Collections;
using Cysharp.Threading.Tasks;


public class ChzzkVideoDonationUnity : MonoBehaviour
{
    #region Variables

    //WSS(WS 말고 WSS) 쓰려면 필요함.
    private enum SslProtocolsHack
    {
        Tls = 192,
        Tls11 = 768,
        Tls12 = 3072
    }

    WebSocket socket = null;

    float timer = 0f;
    bool running = false;

    private const string HEARTBEAT_REQUEST = "2";
    private const string HEARTBEAT_RESPONSE = "3";

    #region Callbacks

    /// <summary>
    /// 영상 도네이션 도착시 호출되는 이벤
    /// </summary>
    public UnityEvent<Profile, VideoDonation> onVideoDonationArrive = new();
    /// <summary>
    /// 영상 도네이션 도착시 호출되는 이벤
    /// </summary>
    public UnityEvent<DonationControl> onVideoDonationControl = new();
    /// <summary>
    /// 웹소캣이 열렸을 때 호출되는 이벤트
    /// </summary>
    public UnityEvent onClose = new();
    /// <summary>
    /// 웹소캣이 닫혔을 때 호출되는 이벤트
    /// </summary>
    public UnityEvent onOpen = new();


    #endregion Callbacks

    #endregion Variables



    int closedCount = 0;
    bool reOpenTrying = false;

    #region Unity Methods

    // Start is called before the first frame update
    void Start()
    {

    }
    
    private void Update()
    {
        if (closedCount > 0)
        {
            onClose?.Invoke();
            if (!reOpenTrying)
                StartCoroutine(TryReOpen());
            closedCount--;
        }
        if (running)
        {
            timer += Time.unscaledDeltaTime;
            if (timer > 15)
            {
                socket.Send(HEARTBEAT_REQUEST);
                timer = 0;
            }
        }
    }


    private void OnDestroy()
    {
        StopListening();
    }

    #endregion Unity Methods

    #region Debug Methods


    #endregion Debug Methods

    #region Public Methods

    /// <summary>
    /// 전체 URL에서 필요한 ID만 추출
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    /// https://chzzk.naver.com/mission-donation/mission@<MissionWSSID>
    public string GetMissionWSSId(string url)
    {
        return url.Split("@")[1];
    }

    /// <summary>
    /// SessionURL을 받아옴
    /// </summary>
    /// <param name="missionWSSId">GetMissionWSSId 함수의 값을 사용</param>
    /// <returns></returns>
    public async UniTask<string> GetSessionURL(string missionWSSId)
    {
        var url = $"https://api.chzzk.naver.com/manage/v1/alerts/video@{missionWSSId}/session-url";
        var request = UnityWebRequest.Get(url);
        await request.SendWebRequest();
        SessionUrl sessionUrl = null;
        //Debug.Log(request.downloadHandler.text);
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            sessionUrl = JsonUtility.FromJson<SessionUrl>(request.downloadHandler.text);
        }
        return sessionUrl.content.sessionUrl;
    }

    /// <summary>
    /// SessionURL에서 Auth를 추출
    /// </summary>
    /// <param name="sessionURL">GetSessionURL 참고</param>
    /// <returns></returns>
    public string MakeWssURL(string sessionUrl)
    {
        string auth = sessionUrl.Split("auth=")[1];
        string server = sessionUrl.Split(".nchat")[0].Substring(12);
        //wss://ssio10.nchat.naver.com/socket.io/?auth=IQv9p1XLswyLRTu0XPin0sJ6tsSevyAwAxEXiDPxTZ0GSFIPHbhNdol8mcbpNB7Inr2ThsHg9VRH2-l4jgotMmQefAKe9U8pkoSxcxFw1OOCSl_GOwXHzMJ7iMQs1siJ&EIO=3&transport=websocket

        return $"wss://ssio{server}.nchat.naver.com/socket.io/?auth={auth}&EIO=3&transport=websocket";
    }

    string wssUrl;

    public async UniTask<string> GetWssUrlFromMissionUrl(string missionUrl)
    {
        string wssId = GetMissionWSSId(missionUrl);
        string sessionUrl = await GetSessionURL(wssId);
        return MakeWssURL(sessionUrl);
    }

    public async void Connect(string url)
    {
        wssUrl = await GetWssUrlFromMissionUrl(url);
        Connect().Forget();
    }

    public async UniTask Connect()
    {
        if (socket != null && socket.IsAlive)
        {
            socket.Close();
            socket = null;
        }


        socket = new WebSocket(wssUrl);

        //wss라서 ssl protocol을 활성화 해줘야 함.
        var sslProtocolHack = (System.Security.Authentication.SslProtocols)(SslProtocolsHack.Tls12 | SslProtocolsHack.Tls11 | SslProtocolsHack.Tls);
        socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;

        //이벤트 등록
        socket.OnMessage += ParseMessage;
        socket.OnClose += CloseConnect;
        socket.OnOpen += onSocketOpen;

        //연결
        socket.Connect();
        await UniTask.CompletedTask;
    }

    void onSocketOpen(object sender, EventArgs e)
    {
        timer = 0;
        running = true;
        socket.Send(HEARTBEAT_REQUEST);
    }

    public void StopListening()
    {
        if (socket == null) return;
        socket.Close();
        socket = null;
    }

    #endregion Public Methods

    #region Socket Event Handlers

    private void ParseMessage(object sender, MessageEventArgs e)
    {
        Debug.Log(e.Data);
        if (e.Data == HEARTBEAT_REQUEST)
        {
            timer = 0;
            socket.Send(HEARTBEAT_RESPONSE);
            return;
        }
        else if (e.Data == HEARTBEAT_RESPONSE)
        {
            return;
        }else if(e.Data == "40")
        {
            return;
        }

        VideoDonationList donations = JsonUtility.FromJson<VideoDonationList>(e.Data);
        switch (donations.videoDonation[0])
        {
            case "donation":
                //List<KeyValuePair<Profile, VideoDonation>> donationList = new List<KeyValuePair<Profile, VideoDonation>>();

                for (int i = 1; i < donations.videoDonation.Count; i++)
                {
                    VideoDonation donationObject = JsonUtility.FromJson<VideoDonation>(donations.videoDonation[i]);
                    Debug.Log(donationObject);
                    Profile profile = JsonUtility.FromJson<Profile>(donationObject.profile);
                    //donationList.Add(new KeyValuePair<Profile, VideoDonation>(profile, donationObject));
                    onVideoDonationArrive.Invoke(profile, donationObject);
                }

                /*
                foreach (KeyValuePair<Profile, VideoDonation> donation in donationList)
                {
                    if (activeVideo.ContainsKey(donation.Value.donationId))
                    {
                        activeVideo[donation.Value.donationId] = donation;
                    }
                    else
                    {
                        activeVideo.Add(donation.Value.donationId, donation);
                    }
                }*/
                
                break;
            case "donationControl":
                List<DonationControl> controlList = new List<DonationControl>();

                for (int i = 1; i < donations.videoDonation.Count; i++)
                {
                    DonationControl controlObject = JsonUtility.FromJson<DonationControl>(donations.videoDonation[i]);
                    Debug.Log(controlObject);
                    controlList.Add(controlObject);
                    onVideoDonationControl.Invoke(controlObject);
                }
                break;
        }

    }

    Dictionary<string, KeyValuePair<Profile, VideoDonation>> activeVideo;

    private void CloseConnect(object sender, CloseEventArgs e)
    {
        Debug.LogError("연결이 해제되었습니다");
        Debug.Log(e.Reason);
        Debug.Log(e.Code);
        Debug.Log(e);
        closedCount += 1;
    }

    #endregion Socket Event Handlers

    #region Private Methods
    private IEnumerator TryReOpen()
    {
        reOpenTrying = true;
        yield return new WaitForSeconds(1);
        if (!socket.IsAlive)
        {
            socket.Connect();
        }
        reOpenTrying = false;
    }
    #endregion

    #region Sub-classes
    //{"code":200,"message":null,"content":{"sessionUrl":"https://ssio10.nchat.naver.com:443?auth=OD0Xc6Jni081g3Qc7Y2E4bjRJ9HWT4EwguUZUEI40GkSjkK_TeBB-mi-Rkcj3QQ_DJ0_kNldBVLsPVatZY7e3aFUY5bsRWmRHd5WRWlu1srPqWkLo5dijESIRhKG6mOn"}}
    [Serializable]
    public class SessionUrl
    {
        public string code;
        public object message;
        public Content content;

        [Serializable]
        public class Content
        {
            public string sessionUrl;
        }
    }


    //42
    //[
    //  "donationControl",
    //  "{
    //      \"startSecond\":0,
    //      \"endSecond\":0,
    //      \"stopVideo\":true,
    //      \"titleExpose\":false,
    //      \"donationId\":\"3tyHuBALAIChH6FUMV5WgsULnZ57T\",
    //      \"payAmount\":0,
    //      \"isAnonymous\":false,
    //      \"useSpeech\":false
    //  }"
    public class DonationControl
    {
        int startSecond;
        int endSecond;
        bool stopVideo;
        bool titleExpose;
        string donationId;
        int payAmount;
        bool isAnonymous;
        bool useSpeech;
    }

    //
    //[
    //  "donation",
    //  "{
    //      \"startSecond\":0,
    //      \"endSecond\":10,
    //      \"videoType\":\"YOUTUBE\",
    //      \"videoId\":\"o6OWF-IMFVs\",
    //      \"playMode\":\"AUTO_PLAY\",
    //      \"stopVideo\":false,
    //      \"titleExpose\":true,
    //      \"donationId\":\"TEST\",
    //      \"profile\":
    //          \"{
    //              \\\"userIdHash\\\":\\\"TEST\\\",
    //              \\\"nickname\\\":\\\"TEST\\\",
    //              \\\"profileImageUrl\\\":\\\"https://nng-phinf.pstatic.net/MjAyMjAzMjNfMjAx/MDAxNjQ4MDI3NjcyNjc2.mEg9zu2Oo3aH8WMnKiL_L3FB7k4ZZhrCwdrnzdIhRzgg.HdIpxQ-jRiBhXJnphwZX_i7p98Umm9rf2wQ4MdEkhygg.PNG/%EB%84%A4%EC%9D%B4%EB%B2%84%EA%B2%8C%EC%9E%84_512x512.png\\\",
    //              \\\"badge\\\":null,
    //              \\\"title\\\":null,
    //              \\\"userRoleCode\\\":null
    //          }\",
    //      \"payAmount\":1000,
    //      \"donationText\":\"치지직, 스트리밍이 시작됩니다.\",
    //      \"isAnonymous\":false,
    //      \"useSpeech\":false
    //  }"
    //]

    //42
    //[
    //    "donation",
    //    "{
    //        \"startSecond\":20,
    //        \"endSecond\":40,
    //        \"videoType\":\"YOUTUBE\",
    //        \"videoId\":\"rU2wZhScVHE\",
    //        \"playMode\":\"AUTO_PLAY\",
    //        \"stopVideo\":false,
    //        \"titleExpose\":true,
    //        \"donationId\":\"2tZdAF3DVXguuejbNKYmorULndho2\",
    //        \"profile\":
    //            \"{
    //            \\\"userIdHash\\\":\\\"ab317fdca78c1b1f027f20fc2acf7a11\\\",
    //            \\\"nickname\\\":\\\"플코\\\",
    //            \\\"profileImageUrl\\\":\\\"\\\",
    //            \\\"userRoleCode\\\":\\\"common_user\\\",
    //            \\\"badge\\\":null,
    //            \\\"title\\\":null,
    //            \\\"verifiedMark\\\":false,
    //            \\\"activityBadges\\\":
    //                [
    //                    {
    //                    \\\"badgeNo\\\":399329,
    //                    \\\"badgeId\\\":\\\"subscription_founder\\\",
    //                    \\\"imageUrl\\\":\\\"https://ssl.pstatic.net/static/nng/glive/icon/1st.png\\\",
    //                    \\\"activated\\\":true
    //                    }
    //                ],
    //            \\\"streamingProperty\\\":
    //                {
    //                \\\"subscription\\\":
    //                    {
    //                        \\\"accumulativeMonth\\\":65,
    //                        \\\"tier\\\":1,
    //                        \\\"badge\\\":
    //                            {
    //                            \\\"imageUrl\\\":\\\"https://nng-phinf.pstatic.net/glive/subscription/badge/cf83a6b1cf3168d8bbd653e2873f060a/1/18.png\\\"
    //                            }
    //                     },
    //                \\\"nicknameColor\\\":
    //                    {
    //                    \\\"colorCode\\\":\\\"CC000\\\"
    //                    }
    //                }
    //            }\",
    //        \"payAmount\":1000,
    //        \"donationText\":\"Monster Hunter Wilds - 무기 소개 영상: 쌍검(한글 자막)\",
    //        \"isAnonymous\":false,
    //        \"tierNo\":1,
    //        \"useSpeech\":false
    //    }"
    //]

    //42[
    //  "donation",
    //  "{
    //    \"startSecond\":0,
    //    \"endSecond\":234,
    //    \"videoType\":\"YOUTUBE\",
    //    \"videoId\":\"h_z-b2h4vVY\",
    //    \"playMode\":\"AUTO_PLAY\",
    //    \"stopVideo\":false,
    //    \"titleExpose\":true,
    //    \"donationId\":\"4yAsFRIHImAtYbUwX3ppzPULo3C56\",
    //    \"profile\":
    //    \"{
    //      \\\"userIdHash\\\":\\\"105ed9e75404d5c3847d129ac36cd261\\\",
    //      \\\"nickname\\\":\\\"Nnq Maastricht\\\",
    //      \\\"profileImageUrl\\\":\\\"\\\",
    //      \\\"userRoleCode\\\":\\\"common_user\\\",
    //      \\\"badge\\\":null,
    //      \\\"title\\\":null,
    //      \\\"verifiedMark\\\":false,
    //      \\\"activityBadges\\\":
    //        [
    //          {
    //            \\\"badgeNo\\\":1275236,
    //            \\\"badgeId\\\":\\\"donation_newbie\\\",
    //            \\\"imageUrl\\\":\\\"https://ssl.pstatic.net/static/nng/glive/icon/fan.png\\\",
    //            \\\"activated\\\":true
    //          }
    //        ],
    //      \\\"streamingProperty\\\":
    //      {
    //        \\\"realTimeDonationRanking\\\":
    //        {
    //          \\\"badge\\\":
    //          {
    //            \\\"imageUrl\\\":\\\"https://ssl.pstatic.net/static/nng/glive/icon/silver.png\\\"
    //          }
    //        },
    //        \\\"nicknameColor\\\":
    //        {
    //          \\\"colorCode\\\":\\\"CC000\\\"
    //        }
    //      }
    //    }\",
    //    \"payAmount\":11700,
    //    \"donationText\":\"\\u0027선사시대: 공룡이 지배하던 지구 2 - Prehistoric Planet 2\\u0027 프리히스토릭 플래닛 2 中 티라노사우루스를 괴롭히는 케찰코아틀루스 (한글 자막)\",
    //    \"isAnonymous\":false,
    //    \"useSpeech\":false
    //  }"
    //]
    [Serializable]
    public class VideoDonationList
    {
        public List<string> videoDonation;
    }

    [Serializable]
    public class VideoDonation
    {
        public int startSecond;
        public int endSecond;
        public string videoType;
        public string videoId;
        public string playMode;
        public bool stopVideo;
        public bool titleExpose;
        public string donationId;
        public string profile;
        public int payAmount;
        public string donationText;
        public bool isAnonymous;
        public int tierNo;
        public bool useSpeech;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    [Serializable]
    public class Profile
    {
        public string userIdHash;
        public string nickname;
        public string profileImageUrl;
        public string userRoleCode;
        public string badge;
        public string title;
        public bool verifiedMark;
        public List<ActivityBadge> activityBadges;

        [Serializable]
        public class ActivityBadge
        {
            public int badgeNo;
            public string badgeId;
            public string imageUrl;
            public bool activated;
        }

        public StreamingProperty streamingProperty;
        [Serializable]
        public class StreamingProperty
        {
            public Subscription subscription;
            [Serializable]
            public class Subscription
            {
                public int accumulativeMonth;
                public int tier;
                public Badge badge;
                public class Badge
                {
                    public string imageUrl;
                }
            }
            public NicknameColor nicknameColor;
            [Serializable]
            public class NicknameColor
            {
                public string colorCode;
            }
        }
    }
    #endregion Sub-classes
}
