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
        public List<GameObject> spawnablePrefabs;

        // unet vars
        public NetworkClient myClient { get; private set;}

        // steam state vars
        CSteamID steamLobbyId;
        public bool JoinFriendTriggered { get; private set; }
        public SessionConnectionState lobbyConnectionState {get; private set;}

        // callbacks
        private Callback<LobbyEnter_t> m_LobbyEntered;
        private Callback<P2PSessionRequest_t> m_P2PSessionRequested;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(this);

            for (int i = 0; i < spawnablePrefabs.Count; i++)
            {
                ClientScene.RegisterPrefab(spawnablePrefabs[i]);
            }


            if (SteamManager.Initialized) {
                m_LobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                m_P2PSessionRequested = Callback<P2PSessionRequest_t>.Create (OnP2PSessionRequested);
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
                // invite accepted. join friend's game
                ulong lobbyId = 0;

                if (ulong.TryParse(input, out lobbyId))
                {
                    JoinFriendTriggered = true;
                    steamLobbyId = new CSteamID(lobbyId);
                    JoinFriendLobby();
                }

            }
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

            var owner = SteamMatchmaking.GetLobbyOwner(steamLobbyId);
            var me = SteamUser.GetSteamID();
            if (owner.m_SteamID == me.m_SteamID)
            {
                // lobby created. start UNET server
                // TODO 

                // prompt to invite friend
                StartCoroutine (DoShowInviteDialogWhenReady ());
            }
            else
            {
                // joined friend's lobby.
                JoinFriendTriggered = false;

                Debug.LogError("Sending packet to request p2p connection");

                //send packet to request connection to host via Steam's NAT punch or relay servers
                SteamNetworking.SendP2PPacket (owner, null, 0, EP2PSend.k_EP2PSendReliable);

                StartCoroutine (DoWaitForP2PSessionAcceptedAndConnect ());

            }


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

            Debug.LogError("Unet server ready. Showing invite friend dialog");

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

                        SteamNetworking.AcceptP2PSessionWithUser (pCallback.m_steamIDRemote);
                        SteamNetworking.SendP2PPacket (pCallback.m_steamIDRemote, null, 0, EP2PSend.k_EP2PSendReliable);

                        // Register the user with Unet server
                        // TODO

                        return;
                    }
                }
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
                var owner = SteamMatchmaking.GetLobbyOwner (steamLobbyId);
                if (senderId.m_SteamID == owner.m_SteamID)
                {
                    Debug.LogError("P2P connection accepted");

                    // packet was from owner, assume it's notifying client that AcceptP2PSessionWithUser was called
                    P2PSessionState_t sessionState;
                    if (SteamNetworking.GetP2PSessionState (owner, out sessionState)) 
                    {
                        // connect to the unet server
                       // TODO

                        yield break;
                    }

                }
            }

            Debug.LogError("Connection failed");
        }

        #endregion
    }
}
