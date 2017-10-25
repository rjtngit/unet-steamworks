using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;

namespace UNETSteamworks
{
    public class SteamNetworkClient : NetworkClient {

        CSteamID remoteId;

        public SteamNetworkConnection steamConnection { 
            get {
                return connection as SteamNetworkConnection;
            }
        }

        public void Connect(CSteamID remoteId)
        {
            Connect("localhost", 0);
          
            m_AsyncConnect = ConnectState.Connected;

            this.remoteId = remoteId;

            steamConnection.Initialize();
            steamConnection.InvokeHandlerNoData(MsgType.Connect);

        }

        public SteamNetworkClient(NetworkConnection conn) : base(conn)
        {
        }



    }

}
