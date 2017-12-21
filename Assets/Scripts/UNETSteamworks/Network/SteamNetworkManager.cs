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
    public const string GAME_ID = "spacewave-unet-p2p-example"; // Unique identifier for matchmaking so we don't match up with other Spacewar games

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
    private Callback<LobbyChatUpdate_t> m_LobbyChatUpdate;
    private CallResult<LobbyMatchList_t> m_LobbyMatchList;

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

    public static int GetChannelCount()
    {
        return hostTopology.DefaultConfig.Channels.Count;
    }

    void Start()
    {
		// init
        Instance = this;
        DontDestroyOnLoad(this);

        LogFilter.currentLogLevel = LogFilter.Info;

        if (SteamManager.Initialized) {
            m_LobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            m_GameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create (OnGameLobbyJoinRequested);
            m_LobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create (OnLobbyChatUpdate);
            m_LobbyMatchList = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
        }

        UNETServerController.Init();
 
		// check if game started via friend invitation
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
                JoinLobby(new CSteamID(lobbyId));
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
        int channels = GetChannelCount();

        // Read Steam packets
        for (int chan = 0; chan < channels; chan++)
        {
            while (SteamNetworking.IsP2PPacketAvailable (out packetSize, chan))
            {
                byte[] data = new byte[packetSize];

                CSteamID senderId;

                if (SteamNetworking.ReadP2PPacket (data, packetSize, out packetSize, out senderId, chan)) 
                {
                    NetworkConnection conn;

                    if (UNETServerController.IsHostingServer())
                    {
                        // We are the server, one of our clients will handle this packet
                        conn = UNETServerController.GetClient(senderId);

                        if (conn == null)
                        {
                            // In some cases the p2p connection can persist, resulting in UNETServerController.OnP2PSessionRequested not being called. This happens usually when testing in editor.
                            // If the peers have already established a connection, reset it.
                            P2PSessionState_t sessionState;
                            if (SteamNetworking.GetP2PSessionState(senderId, out sessionState) && Convert.ToBoolean(sessionState.m_bConnectionActive))
                            {
                                Debug.Log("P2P connection is still established. Resetting.");
                                SteamNetworking.CloseP2PSessionWithUser(senderId);
                                UNETServerController.CreateP2PConnectionWithPeer(senderId);
                                conn = UNETServerController.GetClient(senderId);
                            }
                        }
                    }
                    else
                    {
                        // We are a client, we only have one connection (the server).
                        conn = myClient.connection;
                    }

                    if (conn != null)
                    {
                        // Handle Steam packet through UNET
                        conn.TransportReceive(data, Convert.ToInt32(packetSize), chan);
                    }

                }
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Disconnect();
        }

    }

    public void RegisterNetworkPrefabs()
    {
        for (int i = 0; i < networkPrefabs.Count; i++)
        {
            ClientScene.RegisterPrefab(networkPrefabs[i]);
        }
    }

    public bool IsMemberInSteamLobby(CSteamID steamUser)
    {
        if (SteamManager.Initialized) 
        {
            int numMembers = SteamMatchmaking.GetNumLobbyMembers(steamLobbyId);

            for (int i = 0; i < numMembers; i++) 
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex (steamLobbyId, i);

                if (member.m_SteamID == steamUser.m_SteamID)
                {
                    return true;
                }
            }
        }

        return false;
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

        ClientScene.DestroyAllClientObjects();

        if (SteamManager.Initialized)
        {
            SteamMatchmaking.LeaveLobby(steamLobbyId);
        }

        if (myClient != null)
        {
            myClient.Disconnect();
            myClient = null;
        }

        UNETServerController.Disconnect();
        NetworkClient.ShutdownAll();

        steamLobbyId.Clear();
    }


    void OnLobbyChatUpdate(LobbyChatUpdate_t pCallback)
    {
        if (pCallback.m_rgfChatMemberStateChange == (uint) EChatMemberStateChange.k_EChatMemberStateChangeLeft && pCallback.m_ulSteamIDLobby == steamLobbyId.m_SteamID)
        {
            Debug.Log("A client has disconnected from the UNET server");

            // user left lobby
            var userId = new CSteamID(pCallback.m_ulSteamIDUserChanged);
            if (UNETServerController.IsHostingServer())
            {
                UNETServerController.RemoveConnection(userId);
            }

            SteamNetworking.CloseP2PSessionWithUser(userId);
        }
    }


    void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t pCallback)
    {
        // Invite accepted, game is already running
        JoinLobby(pCallback.m_steamIDLobby);
    }

    public void JoinLobby(CSteamID lobbyId)
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

        UNETServerController.inviteFriendOnStart = true;
        lobbyConnectionState = SessionConnectionState.CONNECTING;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, MAX_USERS);
        // ...continued in OnLobbyEntered callback
    }

    public void FindMatch()
    {
        if (!SteamManager.Initialized) {
            lobbyConnectionState = SessionConnectionState.FAILED;
            return;
        }

        lobbyConnectionState = SessionConnectionState.CONNECTING;

        //Note: call SteamMatchmaking.AddRequestLobbyList* before RequestLobbyList to filter results by some criteria
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", GAME_ID, ELobbyComparison.k_ELobbyComparisonEqual);
        var call = SteamMatchmaking.RequestLobbyList();
        m_LobbyMatchList.Set(call, OnLobbyMatchList);
    }

    void OnLobbyMatchList(LobbyMatchList_t pCallback, bool bIOFailure)
    {
        uint numLobbies = pCallback.m_nLobbiesMatching;

        if (numLobbies <= 0)
        {
            // no lobbies found. create one
            Debug.Log("Creating lobby"); 

            UNETServerController.inviteFriendOnStart = false;
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, MAX_USERS);
            // ...continued in OnLobbyEntered callback
        }
        else
        {
            // If multiple lobbies are returned we can iterate over them with SteamMatchmaking.GetLobbyByIndex and choose the "best" one
            // In this case we are just joining the first one
            Debug.Log("Joining lobby");
            var lobby = SteamMatchmaking.GetLobbyByIndex(0);
            JoinLobby(lobby);
        }


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
            SteamMatchmaking.SetLobbyData(steamLobbyId, "game", GAME_ID);
            UNETServerController.StartUNETServer();
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

        RegisterNetworkPrefabs();

        var conn = myClient.connection;
        if (conn != null)
        {
            ClientScene.Ready(conn);
            Debug.Log("Requesting spawn");
            myClient.Send(NetworkMessages.SpawnRequestMsg, new StringMessage(SteamUser.GetSteamID().m_SteamID.ToString()));
        }

    }
}
