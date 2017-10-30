using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Net;
using UnityEngine.Networking.NetworkSystem;
using System;
using Steamworks;
 

public class SteamNetworkManager : MonoBehaviour
{
    public const int MAX_USERS = 4;

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

    public static SteamNetworkManager Instance;

    [SerializeField] 
    private UNETServerController UNETServerController;
    public List<GameObject> networkPrefabs;

    // Client-to-server connection
    public NetworkClient myClient;

    // steam state vars
    public SessionConnectionState lobbyConnectionState {get; private set;}
    public CSteamID steamLobbyId;

    // callbacks
    private Callback<LobbyEnter_t> m_LobbyEntered;
    private Callback<GameLobbyJoinRequested_t> m_GameLobbyJoinRequested;

    private static HostTopology m_hostTopology = null;
    public static HostTopology hostTopology
    {
        get
        {
            if (m_hostTopology == null)
            {
                ConnectionConfig config = new ConnectionConfig();
                config.AddChannel(QosType.ReliableSequenced);
                config.AddChannel(QosType.Unreliable);
                m_hostTopology = new HostTopology(config, MAX_USERS);

            }

            return m_hostTopology;
        }
    }

    void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(this);

        LogFilter.currentLogLevel = LogFilter.Info;

        for (int i = 0; i < networkPrefabs.Count; i++)
        {
            ClientScene.RegisterPrefab(networkPrefabs[i]);
        }

        if (SteamManager.Initialized) {
            m_LobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            m_GameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create (OnGameLobbyJoinRequested);

        }

        UNETServerController.Init();
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
            // Invite accepted, launched game. Join friend's game
            ulong lobbyId = 0;

            if (ulong.TryParse(input, out lobbyId))
            {
                JoinFriendLobby(new CSteamID(lobbyId));
            }

        }
    }

    void Update()
    {
        if (!SteamManager.Initialized)
        {
            return;
        }

        if (!IsConnectedToUNETServer())
        {
            return;
        }

        uint packetSize;

        // Read Steam packets
        if (SteamNetworking.IsP2PPacketAvailable (out packetSize))
        {
            byte[] data = new byte[packetSize];

            CSteamID senderId;

            if (SteamNetworking.ReadP2PPacket (data, packetSize, out packetSize, out senderId)) 
            {
                NetworkConnection conn;

                if (UNETServerController.IsHostingServer())
                {
                    // We are the server, one of our clients will handle this packet
                    conn = UNETServerController.GetClient(senderId);
                }
                else
                {
                    // We are a client, we only have one connection (the server).
                    conn = myClient.connection;
                }

                if (conn != null)
                {
                    // Handle Steam packet through UNET
                    conn.TransportReceive(data, Convert.ToInt32(packetSize), 0);
                }

            }
        }

    }

    public CSteamID GetSteamIDForConnection(NetworkConnection conn)
    {
        if (UNETServerController.IsHostingServer())
        {
            return UNETServerController.GetSteamIDForConnection(conn);
        }
        else
        {
            // clients only have the client-to-server connection
            var steamConn = myClient as SteamNetworkClient;
            if (steamConn != null)
            {
                return steamConn.steamConnection.steamId;
            }
        }

        Debug.LogError("Could not find Steam ID");
        return CSteamID.Nil;
    }

    public bool IsConnectedToUNETServer()
    {
        return myClient != null && myClient.connection != null && myClient.connection.isConnected;
    }

    public void Disconnect()
    {
        lobbyConnectionState = SessionConnectionState.DISCONNECTED;

        UNETServerController.Disconnect();

        if (myClient != null)
        {
            myClient.Disconnect();
        }

        steamLobbyId.Clear();
    }

    void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t pCallback)
    {
        // Invite accepted, game is already running
        JoinFriendLobby(pCallback.m_steamIDLobby);
    }

    public void JoinFriendLobby(CSteamID lobbyId)
    {
        if (!SteamManager.Initialized) {
            lobbyConnectionState = SessionConnectionState.FAILED;
            return;
        }

        lobbyConnectionState = SessionConnectionState.CONNECTING;
        SteamMatchmaking.JoinLobby(lobbyId);
        // ...continued in OnLobbyEntered callback
    }

    public void InviteFriendsToLobby()
    {
        if (lobbyConnectionState == SessionConnectionState.CONNECTING)
        {
            // Already trying to connect...
            return;
        }

        if (lobbyConnectionState != SessionConnectionState.CONNECTED)
        {
            // No lobby yet
            CreateLobbyAndInviteFriend();
        }
        else
        {
            // Already in lobby. Invite friends to current lobby
            UNETServerController.InviteFriendsToLobby();
        }
    }

    public void CreateLobbyAndInviteFriend()
    {
        if (!SteamManager.Initialized) {
            lobbyConnectionState = SessionConnectionState.FAILED;
            return;
        }

        lobbyConnectionState = SessionConnectionState.CONNECTING;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, MAX_USERS);
        // ...continued in OnLobbyEntered callback
    }

    void OnLobbyEntered(LobbyEnter_t pCallback)
    {
        if (!SteamManager.Initialized) {
            lobbyConnectionState = SessionConnectionState.FAILED;
            return;
        }

        steamLobbyId = new CSteamID(pCallback.m_ulSteamIDLobby);

        Debug.Log("Connected to Steam lobby");
        lobbyConnectionState = SessionConnectionState.CONNECTED;

        var hostUserId = SteamMatchmaking.GetLobbyOwner(steamLobbyId);
        var me = SteamUser.GetSteamID();
        if (hostUserId.m_SteamID == me.m_SteamID)
        {
            UNETServerController.StartUNETServerAndInviteFriend();
        }
        else
        {
            // joined friend's lobby.
            StartCoroutine (RequestP2PConnectionWithHost ());
        }


    }
        
    IEnumerator RequestP2PConnectionWithHost()
    {
        var hostUserId = SteamMatchmaking.GetLobbyOwner (steamLobbyId);

        //send packet to request connection to host via Steam's NAT punch or relay servers
        Debug.Log("Sending packet to request P2P connection");
        SteamNetworking.SendP2PPacket (hostUserId, null, 0, EP2PSend.k_EP2PSendReliable);

        Debug.Log("Waiting for P2P acceptance message");
        uint packetSize;
        while (!SteamNetworking.IsP2PPacketAvailable (out packetSize)) {
            yield return null;
        }

        byte[] data = new byte[packetSize];

        CSteamID senderId;

        if (SteamNetworking.ReadP2PPacket (data, packetSize, out packetSize, out senderId)) 
        {
            if (senderId.m_SteamID == hostUserId.m_SteamID)
            {
                Debug.Log("P2P connection established");

                // packet was from host, assume it's notifying client that AcceptP2PSessionWithUser was called
                P2PSessionState_t sessionState;
                if (SteamNetworking.GetP2PSessionState (hostUserId, out sessionState)) 
                {
                    // connect to the unet server
                    ConnectToUnetServerForSteam(hostUserId);

                    yield break;
                }

            }
        }

        Debug.LogError("Connection failed");
    }


    void ConnectToUnetServerForSteam(CSteamID hostSteamId)
    {
        Debug.Log("Connecting to UNET server");

        // Create connection to host player's steam ID
        var conn = new SteamNetworkConnection(hostSteamId);
        var mySteamClient = new SteamNetworkClient(conn);
        this.myClient = mySteamClient;

        // Setup and connect
        mySteamClient.RegisterHandler(MsgType.Connect, OnConnect);
        mySteamClient.SetNetworkConnectionClass<SteamNetworkConnection>();
        mySteamClient.Configure(SteamNetworkManager.hostTopology);
        mySteamClient.Connect();

    }

    void OnConnect(NetworkMessage msg)
    {
        // Set to ready and spawn player
        Debug.Log("Connected to UNET server.");
        myClient.UnregisterHandler(MsgType.Connect);

        var conn = myClient.connection;
        if (conn != null)
        {
            ClientScene.Ready(conn);
            Debug.Log("Requesting spawn");
            myClient.Send(NetworkMessages.SpawnRequestMsg, new StringMessage(SteamUser.GetSteamID().m_SteamID.ToString()));
        }

    }
}
