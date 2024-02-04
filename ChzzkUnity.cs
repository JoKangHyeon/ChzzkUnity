using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using WebSocketSharp;

public class ChzzkUnity : MonoBehaviour
{
    private enum SslProtocolsHack
    {
        Tls = 192,
        Tls11 = 768,
        Tls12 = 3072
    }

    string cid;
    string token;
    string channel;

    WebSocket socket = null;
    string wsURL = "wss://kr-ss3.chat.naver.com/chat";

    float timer = 0f;
    bool running = false;

    string heartbeatRequest = "{\"ver\":\"2\",\"cmd\":0}";
    string heartbeatResponse = "{\"ver\":\"2\",\"cmd\":10000}";

    public Action<Profile, string> onMessage = (profile,str) => {};
    public Action<Profile, string, DonationExtras> onDonation = (profile, str, extra) => { };

    // Start is called before the first frame update
    void Start()
    {
        
    }

    public void removeAllOnMessageListener() 
    {
        onMessage = (profile, str) => { };
    }

    public void removeAllOnDonationListener()
    {
        onMessage = (profile, str) => { };
    }

    // Update is called once per frame
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

    IEnumerator Connect()
    {
        string URL = $"https://api.chzzk.naver.com/polling/v2/channels/{channel}/live-status";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            LiveStatus status = JsonUtility.FromJson<LiveStatus>(request.downloadHandler.text);
            cid = status.content.chatChannelId;
            //cidOutput.text = cid;
            URL = $"https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId={cid}&chatType=STREAMING";
            request = UnityWebRequest.Get(URL);
            //Debug.Log(URL);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                //Debug.Log(request.downloadHandler.text);
                AccessTokenResult tokenResult = JsonUtility.FromJson<AccessTokenResult>(request.downloadHandler.text);
                token = tokenResult.content.accessToken;
                socket = new WebSocket(wsURL);

                var sslProtocolHack = (System.Security.Authentication.SslProtocols)(SslProtocolsHack.Tls12 | SslProtocolsHack.Tls11 | SslProtocolsHack.Tls);
                socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;
                socket.OnMessage += Recv;
                socket.OnClose += CloseConnect;
                socket.OnOpen += OnStartChat;
                socket.Connect();
            }
            else
            {
                Debug.Log($"ERROR On get token : {request.result} : {request.error}");
            }
        }
        else
        {
            Debug.Log($"ERROR On get cid : {request.result} : {request.error}");
        }
    }

    void Recv(object sender, MessageEventArgs e)
    {
        
        try
        {
            //kills.Add(JsonUtility.FromJson<Kill>(e.Data));            
            IDictionary<string, object> data = JsonConvert.DeserializeObject<IDictionary<string, object>>(e.Data);

            StringBuilder sb = new StringBuilder();
            foreach(string key in data.Keys)
            {
                if (data[key] != null) 
                {
                    sb.Append("\n" + key + ":" + data[key].ToString());
                }
                else
                {
                    sb.Append("\n" + key + ": NULL");
                }
                
            }
            //Debug.Log(data["cmd"].GetType());

            

            switch ((long)data["cmd"])
            {
                case 0://HeartBeat Request
                    socket.Send(heartbeatResponse);
                    timer = 0;
                    break;
                case 93101://Chat
                    //Debug.Log(data["bdy"].GetType());
                    JArray bdy = (JArray)data["bdy"];
                    //Debug.Log(bdy[0].GetType());
                    JObject bdyObject = (JObject)bdy[0];

                    string profileText = bdyObject["profile"].ToString();
                    profileText = profileText.Replace("\\", "");
                    //Debug.Log(profileText);                                      
                    Profile profile = JsonUtility.FromJson<Profile>(profileText);

                    //debugText.Append($"{profile.nickname}({profile.userIdHash}) : {bdyObject["msg"]}\n");
                    onMessage(profile, bdyObject["msg"].ToString());
                    break;
                case 93102://Donation
                    bdy = (JArray)data["bdy"];
                    bdyObject = (JObject)bdy[0];
                    profileText = bdyObject["profile"].ToString();
                    profileText = profileText.Replace("\\", "");
                    profile = JsonUtility.FromJson<Profile>(profileText);

                    string extraText = bdyObject["extra"].ToString();
                    extraText = extraText.Replace("\\", "");
                    DonationExtras extras = JsonUtility.FromJson<DonationExtras>(extraText);
                    onDonation(profile, bdyObject["msg"].ToString(), extras);
                    //Debug.Log(data["cmd"]);
                    //Debug.Log(e.Data);
                    break;
                case 94008://Blocked Message(CleanBot)
                case 94201://Member Sync
                case 10000://HeartBeat Response
                case 10100://Token ACC
                    break;//Nothing to do
                default:
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
        Debug.Log("OPENED");
        string message = $"{{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"{cid}\",\"bdy\":{{\"uid\":null,\"devType\":2001,\"accTkn\":\"{token}\",\"auth\":\"READ\"}},\"tid\":1}}";
        timer = 0;
        running = true;
        socket.Send(message);
    }


    public void StartListening(string channelId)
    {
        if(socket!=null && socket.IsAlive)
        {
            socket.Close();
            socket = null;
        }
        channel = channelId;
        StartCoroutine(Connect());
    }

    public void StopListening()
    {
        socket.Close();
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
    public class DonationExtras
    {
        public bool isAnonymous;
        public string payType;
        public int payAmount;
        public string nickname;
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
            public long ctime;
            public long utime;
            public string msgTid;
            public long msgTime;
        }
        public int cmd;
        public string tid;
        public string cid;
    }
}
