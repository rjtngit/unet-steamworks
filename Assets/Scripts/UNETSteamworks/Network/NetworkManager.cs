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
        const short SpawnMsg = 1002;

        public string autoInviteSteamId;

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
        public List<GameObject> networkPrefabs;

        // unet vars
        public NetworkClient myClient { get; private set;}
        NetworkConnection peerConn;


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

            for (int i = 0; i < networkPrefabs.Count; i++)
            {
                ClientScene.RegisterPrefab(networkPrefabs[i]);
            }

            if (SteamManager.Initialized) {
                m_LobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                m_P2PSessionRequested = Callback<P2PSessionRequest_t>.Create (OnP2PSessionRequested);
                m_GameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create (OnGameLobbyJoinRequested);

            }
           
            LogFilter.currentLogLevel = LogFilter.Info;
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
            if (!SteamManager.Initialized)
            {
                return;
            }


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
                    NetworkConnection conn;
                    if (NetworkServer.active)
                    {
                        conn = GetConnections()[1];
                    }
                    else
                    {
                        conn = GetConnections()[0];
                    }

                    conn.TransportReceive(data, Convert.ToInt32(packetSize), 0);

                }
            }


        }

        public CSteamID GetSteamIDForConnection(NetworkConnection conn)
        {
            if (NetworkServer.active)
            {
                if (NetworkServer.connections.Count >= 1 && conn == NetworkServer.connections[0])
                {
                    // this is the server-to-client connection for local player
                    return SteamUser.GetSteamID();
                }

                if (NetworkServer.connections.Count >= 2 && conn == NetworkServer.connections[1])
                {
                    // this is the server-to-client connection for peer
                    return (peerConn as SteamNetworkConnection).steamId;
                }

                if (myClient != null && conn == myClient.connection)
                {
                    // this is the client-to-server connection for local player
                    return SteamUser.GetSteamID();
                }
            }
            else
            {
                return (myClient as SteamNetworkClient).steamConnection.steamId;
            }

            return new CSteamID();
        }

        HostTopology CreateTopology()
        {
            ConnectionConfig config = new ConnectionConfig();
            config.AddChannel(QosType.ReliableSequenced);
            config.AddChannel(QosType.Unreliable);
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

            steamLobbyId.Clear();
            p2pConnectionEstablished = false;
            peerConn = null;
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

            Debug.Log("Connected to lobby");
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

                Debug.Log("Sending packet to request P2P connection");

                //send packet to request connection to host via Steam's NAT punch or relay servers
                SteamNetworking.SendP2PPacket (host, null, 0, EP2PSend.k_EP2PSendReliable);

                StartCoroutine (DoWaitForP2PSessionAcceptedAndConnect ());

            }


        }


        #region host
        IEnumerator DoShowInviteDialogWhenReady()
        {
            Debug.Log("Waiting for UNET server to start");

            while (!NetworkServer.active) 
            {
                // wait for unet server to start up
                yield return null;
            }

            Debug.Log("UNET started");

            if (!string.IsNullOrEmpty(autoInviteSteamId.Trim()))
            {
                Debug.Log("Sending invite");
                SteamFriends.InviteUserToGame(new CSteamID(ulong.Parse(autoInviteSteamId)), "+connect_lobby " + steamLobbyId.m_SteamID.ToString());
            }
            else
            {
                Debug.Log("Showing invite friend dialog");
                SteamFriends.ActivateGameOverlayInviteDialog(steamLobbyId);
            }


            yield break;
        }


        void OnP2PSessionRequested(P2PSessionRequest_t pCallback)
        {
            Debug.Log("P2P session request received");

            if (NetworkServer.active && SteamManager.Initialized) 
            {
                // accept the connection if this user is in the lobby
                int numMembers = SteamMatchmaking.GetNumLobbyMembers(steamLobbyId);

                for (int i = 0; i < numMembers; i++) 
                {
                    var member = SteamMatchmaking.GetLobbyMemberByIndex (steamLobbyId, i);

                    if (member.m_SteamID == pCallback.m_steamIDRemote.m_SteamID)
                    {
                        Debug.Log("P2P connection established");
                        Debug.Log("Sending P2P acceptance message");
                        p2pConnectionEstablished = true;

                        SteamNetworking.AcceptP2PSessionWithUser (pCallback.m_steamIDRemote);
                        SteamNetworking.SendP2PPacket (pCallback.m_steamIDRemote, null, 0, EP2PSend.k_EP2PSendReliable);

                        // create new connnection and client and connect them to server
                        peerConn = new SteamNetworkConnection(member, CreateTopology());
                        var newClient = new SteamNetworkClient(peerConn);
                        newClient.SetNetworkConnectionClass<SteamNetworkConnection>();
                        newClient.Configure(CreateTopology());
                        newClient.Connect();

                        NetworkServer.AddExternalConnection(peerConn);

                        return;
                    }
                }
            }

        }

        NetworkConnection[] GetConnections()
        {
            if (NetworkServer.active)
            {
                return new NetworkConnection[]{ NetworkServer.connections[0], peerConn };
            }
            else
            {
                return new NetworkConnection[]{  myClient.connection };
            }
        }


        void StartUnetServerForSteam()
        {
            Debug.Log("Starting UNET server");

            var t = CreateTopology();

            NetworkServer.RegisterHandler(SpawnMsg, OnSpawnRequested);

            NetworkServer.Configure(t);
            NetworkServer.dontListen = true;
            NetworkServer.Listen(0);

            // create a connection to represent the server
            myClient = ClientScene.ConnectLocalServer();
            myClient.Configure(t);
            myClient.Connect("localhost", 0);
            int id = ++SteamNetworkConnection.nextId;
            myClient.connection.Initialize("localhost", id, id, t);

            // spawn self
            var myConn = NetworkServer.connections[0];
            ClientScene.Ready(myConn);
            NetworkServer.SetClientReady(myConn);
            var myplayer = GameObject.Instantiate(playerPrefab);
            NetworkServer.SpawnWithClientAuthority(myplayer, myConn);


        }

        void OnSpawnRequested(NetworkMessage msg)
        {
            Debug.Log("Spawn request received");

            // spawn peer
            var conn = GetConnections()[1];
           
            NetworkServer.SetClientReady(conn);
            var player = GameObject.Instantiate(playerPrefab);

            bool spawned = NetworkServer.SpawnWithClientAuthority(player, conn);
            Debug.Log(spawned ? "Spawned player" :"Failed to spawn player");
        }

        #endregion

        #region client
        IEnumerator DoWaitForP2PSessionAcceptedAndConnect()
        {
            Debug.Log("Waiting for P2P acceptance message");

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
                    Debug.Log("P2P connection established");
                    p2pConnectionEstablished = true;

                    // packet was from host, assume it's notifying client that AcceptP2PSessionWithUser was called
                    P2PSessionState_t sessionState;
                    if (SteamNetworking.GetP2PSessionState (host, out sessionState)) 
                    {
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

        void ConnectToUnetServerForSteam(CSteamID hostSteamId)
        {
            Debug.Log("Connecting to UNET server");

            var t = CreateTopology();

            var conn = new SteamNetworkConnection(hostSteamId, t);

            var steamClient = new SteamNetworkClient(conn);
            steamClient.RegisterHandler(MsgType.Connect, OnConnect);

            this.myClient = steamClient;

            steamClient.SetNetworkConnectionClass<SteamNetworkConnection>();
            steamClient.Configure(t);
            steamClient.Connect();

        }

        void OnConnect(NetworkMessage msg)
        {
            Debug.Log("Connected to UNET server.");
            myClient.UnregisterHandler(MsgType.Connect);

            var conn = myClient.connection as SteamNetworkConnection;

            if (conn != null)
            {
                ClientScene.Ready(conn);
                Debug.Log("Requesting spawn");
                myClient.Send(SpawnMsg, new StringMessage(SteamUser.GetSteamID().m_SteamID.ToString()));
            }


        }
        #endregion
    }
}
