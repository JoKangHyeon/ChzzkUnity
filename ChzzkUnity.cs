using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WebSocketSharp;
using System.Collections;
using Cysharp.Threading.Tasks;

public class ChzzkUnity : MonoBehaviour
{
    #region Variables

    //WSS(WS 말고 WSS) 쓰려면 필요함.
    private enum SslProtocolsHack
    {
        Tls = 192,
        Tls11 = 768,
        Tls12 = 3072
    }

    private const string WS_URL = "wss://kr-ss3.chat.naver.com/chat";
    private const string HEARTBEAT_REQUEST = "{\"ver\":\"2\",\"cmd\":0}";
    private const string HEARTBEAT_RESPONSE = "{\"ver\":\"2\",\"cmd\":10000}";

    string cid;
    string token;
    public string channel;

    WebSocket socket = null;

    float timer = 0f;
    bool running = false;

    #region Callbacks

    public UnityEvent<Profile, string> onMessage = new();
    public UnityEvent<Profile, string, DonationExtras> onDonation = new();
    public UnityEvent<Profile, SubscriptionExtras> onSubscription = new();
    public UnityEvent onClose = new();
    public UnityEvent onOpen = new();

    #endregion Callbacks

    #endregion Variables



    int closedCount = 0;
    bool reOpenTrying = false;

    #region Unity Methods

    // Start is called before the first frame update
    void Start()
    {
        onMessage.AddListener(DebugMessage);
        onDonation.AddListener(DebugDonation);
        onSubscription.AddListener(DebugSubscription);
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
    }
    
    public IEnumerator TryReOpen()
    {
        reOpenTrying = true;
        yield return new WaitForSeconds(1);
        if (!socket.IsAlive)
        {
            socket.Connect();
        }
        reOpenTrying = false;
    }

    //20초에 한번 HeartBeat 전송해야 함.
    //서버에서 먼저 요청하면 안 해도 됨.
    //TimeScale에 영향 안 받기 위해서 Fixed
    void FixedUpdate()
    {
        //HOTFIX : 성능저하가 너무 심해서 Socket.isAlive 체크 제거
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

    private void DebugMessage(Profile profile, string str)
    {
        Debug.Log($"| [Message] {profile.nickname} - {str}");
    }
    private void DebugDonation(Profile profile, string str, DonationExtras donation)
    {
        //isAnonymous가 true면 profile은 null임을 유의
        Debug.Log(donation.isAnonymous
            ? $"| [Donation] 익명 - {str} - {donation.payAmount}/{donation.payType}"
            : $"| [Donation] {profile.nickname} - {str} - {donation.payAmount}/{donation.payType}");
    }
    private void DebugSubscription(Profile profile, SubscriptionExtras subscription)
    {
        Debug.Log($"| [Subscription] {profile.nickname} - {subscription.month}");
    }

    #endregion Debug Methods

    #region Public Methods

    public void RemoveAllOnMessageListener() => onMessage.RemoveAllListeners();
    public void RemoveAllOnDonationListener() => onDonation.RemoveAllListeners();
    public void RemoveAllOnSubscriptionListener() => onSubscription.RemoveAllListeners();

    public async UniTask<ChannelInfo> GetChannelInfo(string channelId)
    {
        var url = $"https://api.chzzk.naver.com/service/v1/channels/{channelId}";
        var request = UnityWebRequest.Get(url);
        await request.SendWebRequest();
        ChannelInfo channelInfo = null;
        //Debug.Log(request.downloadHandler.text);
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            channelInfo = JsonUtility.FromJson<ChannelInfo>(request.downloadHandler.text);
        }
        return channelInfo;
    }

    public async UniTask<LiveStatus> GetLiveStatus(string channelId)
    {
        var url = $"https://api.chzzk.naver.com/polling/v2/channels/{channelId}/live-status";
        var request = UnityWebRequest.Get(url);
        await request.SendWebRequest();
        LiveStatus liveStatus = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            liveStatus = JsonUtility.FromJson<LiveStatus>(request.downloadHandler.text);
        }

        return liveStatus;
    }

    public async UniTask<AccessTokenResult> GetAccessToken(string cid)
    {
        var url = $"https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId={cid}&chatType=STREAMING";
        var request = UnityWebRequest.Get(url);
        await request.SendWebRequest();
        AccessTokenResult accessTokenResult = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            accessTokenResult = JsonUtility.FromJson<AccessTokenResult>(request.downloadHandler.text);
        }

        return accessTokenResult;
    }

    public async UniTask Connect()
    {
        if (socket != null && socket.IsAlive)
        {
            socket.Close();
            socket = null;
        }

        LiveStatus liveStatus = await GetLiveStatus(channel);
        cid = liveStatus.content.chatChannelId;
        AccessTokenResult accessTokenResult = await GetAccessToken(cid);
        token = accessTokenResult.content.accessToken;
        socket = new WebSocket(WS_URL);

        //wss라서 ssl protocol을 활성화 해줘야 함.
        var sslProtocolHack = (System.Security.Authentication.SslProtocols)(SslProtocolsHack.Tls12 | SslProtocolsHack.Tls11 | SslProtocolsHack.Tls);
        socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;

        //이벤트 등록
        socket.OnMessage += ParseMessage;
        socket.OnClose += CloseConnect;
        socket.OnOpen += StartChat;

        //연결
        socket.Connect();
        await UniTask.CompletedTask;
    }

    public void Connect(string channelId)
    {
        channel = channelId;
        Connect().Forget();
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
        try
        {
            IDictionary<string, object> data = JsonConvert.DeserializeObject<IDictionary<string, object>>(e.Data);
            Debug.Log(e.Data);

            JArray body;
            JObject bodyObject;
            Profile profile;
            string profileText;
            
            //Cmd에 따라서
            switch ((long)data["cmd"])
            {
                case 0://HeartBeat Request
                    //하트비트 응답해줌.
                    socket.Send(HEARTBEAT_RESPONSE);
                    //서버가 먼저 요청해서 응답했으면 타이머 초기화해도 괜찮음.
                    timer = 0;
                    break;
                case 93101://Chat
                    body = (JArray)data["bdy"];
                    foreach (JToken jToken in body)
                    {
                        bodyObject = (JObject)jToken;
                        //프로필이.... json이 아니라 string으로 들어옴.
                        profileText = bodyObject["profile"]?.ToString();
                        if (profileText != null)
                        {
                            profileText = profileText.Replace("\\", "");
                            profile = JsonUtility.FromJson<Profile>(profileText);
                            onMessage?.Invoke(profile, bodyObject["msg"]?.ToString().Trim());
                        }
                    }

                    break;
                case 93102://Donation & Subscription
                    body = (JArray)data["bdy"];

                    foreach (JToken jToken in body)
                    {
                        bodyObject = (JObject)jToken;
                        
                        //프로필 스트링 변환
                        profileText = bodyObject["profile"].ToString();
                        profileText = profileText.Replace("\\", "");
                        profile = JsonUtility.FromJson<Profile>(profileText);

                        var msgTypeCode = int.Parse(bodyObject["msgTypeCode"].ToString());
                        //도네이션과 관련된 데이터는 extra
                        string extraText = null;
                        if (bodyObject.TryGetValue("extra", value: out JToken value))
                        {
                            extraText = value.ToString();
                        }
                        else if (bodyObject.TryGetValue("extras", out JToken value1))
                        {
                            extraText = value1.ToString();
                        }

                        extraText = extraText.Replace("\\", "");

                        switch (msgTypeCode)
                        {
                            case 10: // Donation
                                var donation = JsonUtility.FromJson<DonationExtras>(extraText);
                                onDonation?.Invoke(profile, bodyObject["msg"].ToString(), donation);
                                break;
                            case 11: // Subscription
                                var subscription = JsonUtility.FromJson<SubscriptionExtras>(extraText);
                                onSubscription?.Invoke(profile, subscription);
                                break;
                            default:
                                Debug.LogError($"MessageTypeCode-{msgTypeCode} is not supported");
                                Debug.LogError(bodyObject.ToString());
                                break;
                        }
                    }

                    break;
                case 93006://Temporary Restrict 블라인드 처리된 메세지.
                case 94008://Blocked Message(CleanBot) 차단된 메세지.
                case 94201://Member Sync 멤버 목록 동기화.
                case 10000://HeartBeat Response 하트비트 응답.
                    break;
                case 10100://Token ACC
                    //Debug.Log(data["cmd"]);
                    //Debug.Log(e.Data);
                    onOpen?.Invoke();
                    break;//Nothing to do
                default:
                    //내가 놓친 cmd가 있나?
                    //Debug.Log(data["cmd"]);
                    //Debug.Log(e.Data);
                    break;
            }
        }

        catch (Exception er)
        {
            Debug.LogError(er.ToString());
        }
    }

    private void CloseConnect(object sender, CloseEventArgs e)
    {
        Debug.LogError("연결이 해제되었습니다");
        Debug.Log(e.Reason);
        Debug.Log(e.Code);
        Debug.Log(e);
        closedCount += 1;
    }

    private void StartChat(object sender, EventArgs e)
    {
        Debug.Log($"OPENED : {cid} + {token}");

        var message = $"{{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"{cid}\",\"bdy\":{{\"uid\":null,\"devType\":2001,\"accTkn\":\"{token}\",\"auth\":\"READ\"}},\"tid\":1}}";
        timer = 0;
        running = true;
        socket.Send(message);
    }

    #endregion Socket Event Handlers

    #region Sub-classes


    [Serializable]
    public class LiveStatus
    {
        public int code;
        public string message;
        public Content content;

        [Serializable]
        public class Content
        {
            public string liveTitle;
            public string status;
            public int concurrentUserCount;
            public int accumulateCount;
            public bool paidPromotion;
            public bool adult;
            public string chatChannelId;
            public string categoryType;
            public string liveCategory;
            public string liveCategoryValue;
            public string livePollingStatusJson;
            public string faultStatus;
            public string userAdultStatus;
            public bool chatActive;
            public string chatAvailableGroup;
            public string chatAvailableCondition;
            public int minFollowerMinute;
        }
    }

    [Serializable]
    public class AccessTokenResult
    {
        public int code;
        public string message;
        public Content content;
        [Serializable]
        public class Content
        {
            public string accessToken;

            [Serializable]
            public class TemporaryRestrict
            {
                public bool temporaryRestrict;
                public int times;
                public int duration;
                public int createdTime;
            }
            public bool realNameAuth;
            public string extraToken;
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
        public string verifiedMark;
        public List<String> activityBadges;
        public StreamingProperty streamingProperty;
        [Serializable]
        public class StreamingProperty
        {

        }
    }

    [Serializable]
    public class SubscriptionExtras
    {
        public int month;
        public string tierName;
        public string nickname;
        public int tierNo;
    }

    [Serializable]
    public class DonationExtras
    {
        System.Object emojis;
        public bool isAnonymous;
        public string payType;
        public int payAmount;
        public string streamingChannelId;
        public string nickname;
        public string osType;
        public string donationType;

        public List<WeeklyRank> weeklyRankList;
        [Serializable]
        public class WeeklyRank
        {
            public string userIdHash;
            public string nickName;
            public bool verifiedMark;
            public int donationAmount;
            public int ranking;
        }
        public WeeklyRank donationUserWeeklyRank;
    }

    [Serializable]
    public class ChannelInfo
    {
        public int code;
        public string message;
        public Content content;

        [Serializable]
        public class Content
        {
            public string channelId;
            public string channelName;
            public string channelImageUrl;
            public bool verifiedMark;
            public string channelType;
            public string channelDescription;
            public int followerCount;
            public bool openLive;
        }
    }

    #endregion Sub-classes
}
