using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Net;
using UnityEngine.Networking.NetworkSystem;
using System;
using Steamworks;

namespace UNETSteamworks
{
    public class NetworkManager : MonoBehaviour
    {
        const short MyReadyMsg = 1002;

        public enum SessionConnectionState
        {
            UNDEFINED,
            CONNECTING,
            CANCELLED,
            CONNECTED,
            FAILED,
            DISCONNECTING,
            DISCONNECTED
        }

        public static NetworkManager Instance;

        // inspector vars
        public GameObject playerPrefab;

        // unet vars
        public NetworkClient myClient { get; private set;}

        private Dictionary<ulong, SteamNetworkConnection> steamIdUnetConnectionMap = new Dictionary<ulong, SteamNetworkConnection>();
        
        // steam state vars
        CSteamID steamLobbyId;
        public bool JoinFriendTriggered { get; private set; }
        public SessionConnectionState lobbyConnectionState {get; private set;}
        private bool p2pConnectionEstablished = false; 

        // callbacks
        private Callback<LobbyEnter_t> m_LobbyEntered;
        private Callback<P2PSessionRequest_t> m_P2PSessionRequested;
        private Callback<GameLobbyJoinRequested_t> m_GameLobbyJoinRequested;


        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(this);

            ClientScene.RegisterPrefab(playerPrefab);

            if (SteamManager.Initialized) {
                m_LobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                m_P2PSessionRequested = Callback<P2PSessionRequest_t>.Create (OnP2PSessionRequested);
                m_GameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create (OnGameLobbyJoinRequested);

            }
           
        }

        void Start()
        {
            string[] args = System.Environment.GetCommandLineArgs ();
            string input = "";
            for (int i = 0; i < args.Length; i++) {
                if (args [i] == "+connect_lobby" && args.Length > i+1) {
                    input = args [i + 1];
                }
            }

            if (!string.IsNullOrEmpty(input))
            {
                // invite accepted and launched game. join friend's game
                ulong lobbyId = 0;

                if (ulong.TryParse(input, out lobbyId))
                {
                    JoinFriendTriggered = true;
                    steamLobbyId = new CSteamID(lobbyId);
                    JoinFriendLobby();
                }

            }
        }

        void Update()
        {
            if (!p2pConnectionEstablished)
            {
                return;
            }

            uint packetSize;

            if (SteamNetworking.IsP2PPacketAvailable (out packetSize))
            {
                byte[] data = new byte[packetSize];

                CSteamID senderId;

                if (SteamNetworking.ReadP2PPacket (data, packetSize, out packetSize, out senderId)) 
                {
                    Debug.LogError("message received (" + packetSize + "): " + System.Text.Encoding.Default.GetString(data));
                    var sender = GetUnetConnectionForSteamUser(senderId);
                    if (sender != null)
                    {
                        Debug.LogError("handle message");

                        sender.TransportReceive(data, Convert.ToInt32(packetSize), 0);
                    }
                }
            }


        }

        HostTopology CreateTopology()
        {
            ConnectionConfig config = new ConnectionConfig();
            config.AddChannel(QosType.ReliableSequenced);
            return new HostTopology(config, 2);
        }
            
        public void Disconnect()
        {
            
            lobbyConnectionState = SessionConnectionState.DISCONNECTED;

            if (NetworkServer.active)
            {
                NetworkServer.Shutdown();
            }

            if (myClient != null)
            {
                myClient.Disconnect();
            }

            steamIdUnetConnectionMap.Clear();
            steamLobbyId.Clear();
            p2pConnectionEstablished = false;
        }


        public void JoinFriendLobby()
        {
            if (!SteamManager.Initialized) {
                lobbyConnectionState = SessionConnectionState.FAILED;
                return;
            }

            lobbyConnectionState = SessionConnectionState.CONNECTING;
            SteamMatchmaking.JoinLobby(steamLobbyId);
            // ...continued in OnLobbyEntered callback
        }

        public void CreateLobbyAndInviteFriend()
        {
            if (!SteamManager.Initialized) {
                lobbyConnectionState = SessionConnectionState.FAILED;
                return;
            }

            lobbyConnectionState = SessionConnectionState.CONNECTING;
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, 2);
            // ...continued in OnLobbyEntered callback
        }

        void OnLobbyEntered(LobbyEnter_t pCallback)
        {
            if (!SteamManager.Initialized) {
                lobbyConnectionState = SessionConnectionState.FAILED;
                return;
            }

            steamLobbyId = new CSteamID(pCallback.m_ulSteamIDLobby);

            Debug.LogError("Connected to lobby");
            lobbyConnectionState = SessionConnectionState.CONNECTED;

            var host = SteamMatchmaking.GetLobbyOwner(steamLobbyId);
            var me = SteamUser.GetSteamID();
            if (host.m_SteamID == me.m_SteamID)
            {
                // lobby created. start UNET server
                StartUnetServerForSteam();

                // prompt to invite friend
                StartCoroutine (DoShowInviteDialogWhenReady ());
            }
            else
            {
                // joined friend's lobby.
                JoinFriendTriggered = false;

                Debug.LogError("Sending packet to request p2p connection");

                //send packet to request connection to host via Steam's NAT punch or relay servers
                SteamNetworking.SendP2PPacket (host, null, 0, EP2PSend.k_EP2PSendReliable);

                StartCoroutine (DoWaitForP2PSessionAcceptedAndConnect ());

            }


        }

        public SteamNetworkConnection GetUnetConnectionForSteamUser(CSteamID userId)
        {
            SteamNetworkConnection result;

            if (steamIdUnetConnectionMap.TryGetValue(userId.m_SteamID, out result))
            {
                return result;
            }

            Debug.LogError("Failed to find steam user id");
            return null;
        }

        #region host
        IEnumerator DoShowInviteDialogWhenReady()
        {
            Debug.LogError("Waiting for unet server to start");

            while (!NetworkServer.active) 
            {
                // wait for unet server to start up
                yield return null;
            }
                
            ConnectToUnetServerForSteam(SteamUser.GetSteamID());

            while (myClient == null || !myClient.isConnected)
            { 
                yield return null;
            }

            Debug.LogError("Showing invite friend dialog");
            SteamFriends.ActivateGameOverlayInviteDialog(steamLobbyId);

            yield break;
        }


        void OnP2PSessionRequested(P2PSessionRequest_t pCallback)
        {
            Debug.LogError("P2P session request received");

            if (NetworkServer.active && SteamManager.Initialized) 
            {
                // accept the connection if this user is in the lobby
                int numMembers = SteamMatchmaking.GetNumLobbyMembers(steamLobbyId);

                for (int i = 0; i < numMembers; i++) 
                {
                    var member = SteamMatchmaking.GetLobbyMemberByIndex (steamLobbyId, i);

                    if (member.m_SteamID == pCallback.m_steamIDRemote.m_SteamID)
                    {
                        Debug.LogError("Sending P2P acceptance message");
                        p2pConnectionEstablished = true;

                        SteamNetworking.AcceptP2PSessionWithUser (pCallback.m_steamIDRemote);
                        SteamNetworking.SendP2PPacket (pCallback.m_steamIDRemote, null, 0, EP2PSend.k_EP2PSendReliable);

                        // Register the user with Unet server
                        steamIdUnetConnectionMap[member.m_SteamID] = new SteamNetworkConnection(member, CreateTopology());

                        return;
                    }
                }
            }

        }


        void StartUnetServerForSteam()
        {
            Debug.LogError("Starting unet server");

            NetworkServer.RegisterHandler(MyReadyMsg, OnClientReady);

            var t = CreateTopology();

            NetworkServer.Configure(t);
            NetworkServer.Listen(0);


        }

        void OnClientReady(NetworkMessage msg)
        {
            Debug.LogError("Ready message received");

            var strMsg = msg.ReadMessage<StringMessage>();

            var senderId = new CSteamID(ulong.Parse(strMsg.value));
            var conn = GetUnetConnectionForSteamUser(senderId);

            if (conn != null && conn.RemoteSteamId.m_SteamID == senderId.m_SteamID)
            {
                Debug.LogError("server setting client ready");

                NetworkServer.SetClientReady(conn);
                var player = GameObject.Instantiate(playerPrefab);
                NetworkServer.SpawnWithClientAuthority(player, conn);
            }
        }

        #endregion

        #region client
        IEnumerator DoWaitForP2PSessionAcceptedAndConnect()
        {
            Debug.LogError("Waiting for P2P acceptance message");

            uint packetSize;
            while (!SteamNetworking.IsP2PPacketAvailable (out packetSize)) {
                yield return null;
            }

            byte[] data = new byte[packetSize];

            CSteamID senderId;

            if (SteamNetworking.ReadP2PPacket (data, packetSize, out packetSize, out senderId)) 
            {
                var host = SteamMatchmaking.GetLobbyOwner (steamLobbyId);
                if (senderId.m_SteamID == host.m_SteamID)
                {
                    Debug.LogError("P2P connection accepted");
                    p2pConnectionEstablished = true;

                    // packet was from host, assume it's notifying client that AcceptP2PSessionWithUser was called
                    P2PSessionState_t sessionState;
                    if (SteamNetworking.GetP2PSessionState (host, out sessionState)) 
                    {
                        // add host connection reference
                        steamIdUnetConnectionMap[host.m_SteamID] = new SteamNetworkConnection(host, CreateTopology());

                        // connect to the unet server
                        ConnectToUnetServerForSteam(host);

                        yield break;
                    }

                }
            }

            Debug.LogError("Connection failed");
        }

        void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t pCallback)
        {
            // Invite accepted while game running
            JoinFriendTriggered = true;
            steamLobbyId = pCallback.m_steamIDLobby;
            JoinFriendLobby();
        }

        void ConnectToUnetServerForSteam(CSteamID remoteId)
        {
            Debug.LogError("Connecting to Unet server");

            var t = CreateTopology();

            var conn = new SteamNetworkConnection(remoteId, t);
            steamIdUnetConnectionMap[SteamUser.GetSteamID().m_SteamID] = conn;

            var steamClient = new SteamNetworkClient(conn);
            steamClient.RegisterHandler(MsgType.Connect, OnConnect);

            this.myClient = steamClient;

            steamClient.SetNetworkConnectionClass<SteamNetworkConnection>();
            steamClient.Configure(t);
            steamClient.Connect(remoteId);


        }

        void OnConnect(NetworkMessage msg)
        {
            Debug.LogError("Connected to unet server");
            myClient.UnregisterHandler(MsgType.Connect);

            var conn = GetUnetConnectionForSteamUser(SteamUser.GetSteamID());

            if (conn != null)
            {
                Debug.LogError("Setting my client ready");

                ClientScene.Ready(conn);

                myClient.Send(MyReadyMsg, new StringMessage(conn.RemoteSteamId.m_SteamID.ToString()));

                StartCoroutine(DoSendTestMessages());
            }

        }

        IEnumerator DoSendTestMessages()
        {
            yield return new WaitForSeconds(3);

            myClient.Send(MyReadyMsg, new StringMessage("hello"));

        }

        #endregion
    }
}
