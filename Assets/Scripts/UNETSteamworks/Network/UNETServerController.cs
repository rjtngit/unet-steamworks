using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;
using UnityEngine.Networking.NetworkSystem;

[System.Serializable]
public class UNETServerController {

    // inspector vars
    public string autoInviteSteamId;  // Set this in the Inspector to automatically invite a Steam user - good for testing in Editor
    public GameObject playerPrefab;

    // UNET vars
    private List<NetworkConnection> connectedClients = new List<NetworkConnection>();

    // Steamworks callbacks
    private Callback<P2PSessionRequest_t> m_P2PSessionRequested;

    /// local client-to-server connection
    public NetworkClient myClient
    {
        get
        {
            return  SteamNetworkManager.Instance.myClient;
        }
        set
        {
            SteamNetworkManager.Instance.myClient = value;
        }
    }

    public CSteamID SteamLobbyID
    {
        get
        {
            return SteamNetworkManager.Instance.steamLobbyId;
        }
    }
   
    public void Init()
    {
        if (SteamManager.Initialized) {
            m_P2PSessionRequested = Callback<P2PSessionRequest_t>.Create (OnP2PSessionRequested);

        }
    }

    public void StartUNETServerAndInviteFriend()
    {
        if (SteamNetworkManager.Instance.lobbyConnectionState != SteamNetworkManager.SessionConnectionState.CONNECTED)
        {
            Debug.LogError("Not connected to lobby");
            return;
        }

        // lobby created. start UNET server
        StartUNETServer();

        // prompt to invite friend
        SteamNetworkManager.Instance.StartCoroutine (DoShowInviteDialogWhenReady ());
    }

    void StartUNETServer()
    {
        Debug.Log("Starting UNET server");

        // Listen for player spawn request messages 
        NetworkServer.RegisterHandler(NetworkMessages.SpawnRequestMsg, OnSpawnRequested);

        // Start UNET server
        NetworkServer.Configure(SteamNetworkManager.hostTopology);
        NetworkServer.dontListen = true;
        NetworkServer.Listen(0);

        // Create a local client-to-server connection to the "server"
        // Connect to localhost to trick UNET's ConnectState state to "Connected", which allows data to pass through TransportSend
        myClient = ClientScene.ConnectLocalServer();
        myClient.Configure(SteamNetworkManager.hostTopology);
        myClient.Connect("localhost", 0);
        myClient.connection.ForceInitialize();

        // Add local client to server's list of connections
        // Here we get the connection from the NetworkServer because it represents the server-to-client connection
        var serverToClientConn = NetworkServer.connections[0];
        AddConnection(serverToClientConn);

        // Spawn self
        ClientScene.Ready(serverToClientConn);
        SpawnPlayer(serverToClientConn);
    }

    IEnumerator DoShowInviteDialogWhenReady()
    {
        Debug.Log("Waiting for UNET server to start");

        while (!NetworkServer.active) 
        {
            // wait for unet server to start up
            yield return null;
        }

        Debug.Log("UNET server started");
        InviteFriendsToLobby();
    }

    public void InviteFriendsToLobby()
    {
        if (!string.IsNullOrEmpty(autoInviteSteamId.Trim()))
        {
            Debug.Log("Sending invite");
            SteamFriends.InviteUserToGame(new CSteamID(ulong.Parse(autoInviteSteamId)), "+connect_lobby " + SteamLobbyID.m_SteamID.ToString());
        }
        else
        {
            Debug.Log("Showing invite friend dialog");
            SteamFriends.ActivateGameOverlayInviteDialog(SteamLobbyID);
        }
    }

    bool SpawnPlayer(NetworkConnection conn)
    {
        NetworkServer.SetClientReady(conn);
        var player = GameObject.Instantiate(playerPrefab);

        return NetworkServer.SpawnWithClientAuthority(player, conn);
    }

    void OnSpawnRequested(NetworkMessage msg)
    {
        Debug.Log("Spawn request received");

        // Read the contents of this message. It should contain the steam ID of the sender
        var strMsg = msg.ReadMessage<StringMessage>();
        if (strMsg != null)
        {
            ulong steamId;
            if (ulong.TryParse(strMsg.value, out steamId))
            {
                var conn = GetClient(new CSteamID(steamId));

                if (conn != null)
                {
                    // spawn peer
                    if (SpawnPlayer(conn))
                    {
                        Debug.Log("Spawned player");
                        return;
                    }
                }
            }
        }

        Debug.LogError("Failed to spawn player");
    }


    void OnP2PSessionRequested(P2PSessionRequest_t pCallback)
    {
        Debug.Log("P2P session request received");

        if (NetworkServer.active && SteamManager.Initialized) 
        {
            // Accept the connection if this user is in the lobby
            int numMembers = SteamMatchmaking.GetNumLobbyMembers(SteamLobbyID);

            for (int i = 0; i < numMembers; i++) 
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex (SteamLobbyID, i);

                if (member.m_SteamID == pCallback.m_steamIDRemote.m_SteamID)
                {
                    Debug.Log("P2P connection established");
                    Debug.Log("Sending P2P acceptance message");

                    SteamNetworking.AcceptP2PSessionWithUser (pCallback.m_steamIDRemote);
                    SteamNetworking.SendP2PPacket (pCallback.m_steamIDRemote, null, 0, EP2PSend.k_EP2PSendReliable);

                    // create new connnection for this client and connect them to server
                    var newConn = new SteamNetworkConnection(member);
                    newConn.ForceInitialize();

                    NetworkServer.AddExternalConnection(newConn);
                    AddConnection(newConn);

                    return;
                }
            }
        }

    }

    public bool IsHostingServer()
    {
        return NetworkServer.active;
    }


    public void Disconnect()
    {
        if (NetworkServer.active)
        {
            NetworkServer.Shutdown();
        }

        connectedClients.Clear();
    }

    public void AddConnection(NetworkConnection conn)
    {
        connectedClients.Add(conn);
    }

    public CSteamID GetSteamIDForConnection(NetworkConnection conn)
    {
        // The server has at least 2 connections:
        // 1: The local client to server connection
        // 2: The server to local client connection
        // 3+: All of the server to remote client connections

        if (NetworkServer.connections.Count >= 1 && conn == NetworkServer.connections[0])
        {
            // this is the server-to-client connection for local player
            return SteamUser.GetSteamID();
        }

        if (myClient != null && conn == myClient.connection )
        {
            // this is the client-to-server connection for local player
            return SteamUser.GetSteamID();
        }

        for (int i = 0; i < connectedClients.Count; i++)
        {
            if (connectedClients[i] != conn)
            {
                continue;
            }
          
            var steamConn = connectedClients[i] as SteamNetworkConnection;
            if (steamConn != null)
            {
                // this is a server-to-client connection for a remote client
                return steamConn.steamId;
            }
            else
            {
                Debug.LogError("Client is not a SteamNetworkConnection");
            }
        }

        Debug.LogError("Could not find Steam ID");
        return CSteamID.Nil;
    }

    public NetworkConnection GetClient(CSteamID steamId)
    {
        if (steamId.m_SteamID == SteamUser.GetSteamID().m_SteamID)
        {
            // get the local client
            if (NetworkServer.active && NetworkServer.connections.Count > 0)
            {
                return NetworkServer.connections[0];
            }
        }

        // find remote client
        for (int i = 0; i < connectedClients.Count; i++)
        {
            var steamConn = connectedClients[i] as SteamNetworkConnection;
            if (steamConn != null && steamConn.steamId.m_SteamID == steamId.m_SteamID)
            {
                return steamConn;
            }
        }

        Debug.LogError("Client not found");
        return null;
    }

}
