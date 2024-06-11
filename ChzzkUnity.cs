using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using WebSocketSharp;

public class ChzzkUnity : MonoBehaviour
{
    //WSS(WS 말고 WSS) 쓰려면 필요함.
    private enum SslProtocolsHack
    {
        Tls = 192,
        Tls11 = 768,
        Tls12 = 3072
    }

    string cid;
    string token;
    public string channel;

    WebSocket socket = null;
    string wsURL = "wss://kr-ss3.chat.naver.com/chat";

    float timer = 0f;
    bool running = false;

    string heartbeatRequest = "{\"ver\":\"2\",\"cmd\":0}";
    string heartbeatResponse = "{\"ver\":\"2\",\"cmd\":10000}";

    #region Callbacks
    
    public UnityEvent<Profile, string> onMessage = new();
    public UnityEvent<Profile, string, DonationExtras> onDonation = new();
    public UnityEvent<Profile, SubscriptionExtras> onSubscription = new();

    #endregion Callbacks

    // Start is called before the first frame update
    void Start()
    {
        onMessage.AddListener(DebugMessage);
        onDonation.AddListener(DebugDonation);
        onSubscription.AddListener(DebugSubscription);
    }

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
    
    public void RemoveAllOnMessageListener() => onMessage.RemoveAllListeners();
    public void RemoveAllOnDonationListener() => onDonation.RemoveAllListeners();
    public void RemoveAllOnSubscriptionListener() => onSubscription.RemoveAllListeners();

    //20초에 한번 HeartBeat 전송해야 함.
    //서버에서 먼저 요청하면 안 해도 됨.
    //TimeScale에 영향 안 받기 위해서 Fixed
    void FixedUpdate()
    {
        if (running)
        {
            timer += Time.unscaledDeltaTime;
            if (timer > 15)
            {
                socket.Send(heartbeatRequest);
                timer = 0;
            }
        }
    }
    
    public async Task<ChannelInfo> GetChannelInfo(string channelId)
    {
        string URL = $"https://api.chzzk.naver.com/service/v1/channels/{channelId}";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        await request.SendWebRequest();
        ChannelInfo channelInfo = null;
        Debug.Log(request.downloadHandler.text);
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            channelInfo = JsonUtility.FromJson<ChannelInfo>(request.downloadHandler.text);
        }
        return channelInfo;
    }

    public async Task<LiveStatus> GetLiveStatus(string channelId)
    {
        string URL = $"https://api.chzzk.naver.com/polling/v2/channels/{channelId}/live-status";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        await request.SendWebRequest();
        LiveStatus liveStatus = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            liveStatus = JsonUtility.FromJson<LiveStatus>(request.downloadHandler.text);
        }

        return liveStatus;
    }

    public async Task<AccessTokenResult> GetAccessToken(string cid)
    {
        string URL = $"https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId={cid}&chatType=STREAMING";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        await request.SendWebRequest();
        AccessTokenResult accessTokenResult = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            accessTokenResult = JsonUtility.FromJson<AccessTokenResult>(request.downloadHandler.text);
        }

        return accessTokenResult;
    }

    public async void Connect()
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
        
        socket = new WebSocket(wsURL);
        //wss라서 ssl protocol을 활성화 해줘야 함.
        var sslProtocolHack = (System.Security.Authentication.SslProtocols)(SslProtocolsHack.Tls12 | SslProtocolsHack.Tls11 | SslProtocolsHack.Tls);
        socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;

        //이벤트 등록
        socket.OnMessage += Recv;
        socket.OnClose += CloseConnect;
        socket.OnOpen += OnStartChat;

        //연결
        socket.Connect();
    }

    public void Connect(string channelId)
    {
        channel = channelId;
        Connect();
    }


    void Recv(object sender, MessageEventArgs e)
    {
        try
        {                
            IDictionary<string, object> data = JsonConvert.DeserializeObject<IDictionary<string, object>>(e.Data);            

            //Cmd에 따라서
            switch ((long)data["cmd"])
            {
                case 0://HeartBeat Request
                    //하트비트 응답해줌.
                    socket.Send(heartbeatResponse);
                    //서버가 먼저 요청해서 응답했으면 타이머 초기화해도 괜찮음.
                    timer = 0;
                    break;
                case 93101://Chat
                    JArray bdy = (JArray)data["bdy"];
                    JObject bdyObject = (JObject)bdy[0];

                    //프로필이.... json이 아니라 string으로 들어옴.
                    string profileText = bdyObject["profile"].ToString();
                    profileText = profileText.Replace("\\", "");
                    Profile profile = JsonUtility.FromJson<Profile>(profileText);

                    onMessage?.Invoke(profile, bdyObject["msg"].ToString().Trim()) ;
                    break;
                case 93102://Donation & Subscription
                    bdy = (JArray)data["bdy"];
                    bdyObject = (JObject)bdy[0];

                    //프로필 스트링 변환
                    profileText = bdyObject["profile"].ToString();
                    profileText = profileText.Replace("\\", "");
                    profile = JsonUtility.FromJson<Profile>(profileText);

                    var msgTypeCode = int.Parse(bdyObject["msgTypeCode"].ToString());
                    //도네이션 및 구독과 관련된 데이터는 extras
                    string extraText = bdyObject["extras"].ToString();
                    extraText = extraText.Replace("\\", "");

                    switch (msgTypeCode)
                    {
                        case 10: // Donation
                            var donation = JsonUtility.FromJson<DonationExtras>(extraText);
                            onDonation?.Invoke(profile, bdyObject["msg"].ToString(), donation);
                            break;
                        case 11: // Subscription
                            var subscription = JsonUtility.FromJson<SubscriptionExtras>(extraText);
                            onSubscription?.Invoke(profile, subscription);
                            break;
                        default:
                            Debug.LogError($"MessageTypeCode-{msgTypeCode} is not supported");
                            Debug.LogError(bdyObject.ToString());
                            break;
                    }
                    break;
                case 93006://Temporary Restrict 블라인드 처리된 메세지.
                case 94008://Blocked Message(CleanBot) 차단된 메세지.
                case 94201://Member Sync 멤버 목록 동기화.
                case 10000://HeartBeat Response 하트비트 응답.
                case 10100://Token ACC
                    break;//Nothing to do
                default:
                    //내가 놓친 cmd가 있나?
                    Debug.LogError(data["cmd"]);
                    Debug.LogError(e.Data);
                    break;
            }
        }
        
        catch (Exception er)
        {
            Debug.Log(er.ToString());
        }
    }

    void CloseConnect(object sender, CloseEventArgs e)
    {
        Debug.Log(e.Reason);
        Debug.Log(e.Code);
        Debug.Log(e);

        try
        {
            if (socket == null) return;

            if (socket.IsAlive) socket.Close();
        }
        catch (Exception ex)
        {
            Debug.Log(ex.StackTrace);
        }
    }

    void OnStartChat(object sender, EventArgs e)
    {
        Debug.Log($"OPENED : {cid} + {token}");
        
        string message = $"{{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"{cid}\",\"bdy\":{{\"uid\":null,\"devType\":2001,\"accTkn\":\"{token}\",\"auth\":\"READ\"}},\"tid\":1}}";
        timer = 0;
        running = true;
        socket.Send(message);
    }



    public void StopListening()
    {
        socket.Close();
        socket = null;
    }

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
}
