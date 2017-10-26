using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;

namespace UNETSteamworks
{
    public class SteamNetworkClient : NetworkClient {

        public SteamNetworkConnection steamConnection { 
            get {
                return connection as SteamNetworkConnection;
            }
        }

        public string status { get { return m_AsyncConnect.ToString(); } }

        public void Connect()
        {
            Connect("localhost", 4444);

            m_AsyncConnect = ConnectState.Connected;

            steamConnection.Initialize();
            steamConnection.InvokeHandlerNoData(MsgType.Connect);
        }

        public SteamNetworkClient(NetworkConnection conn) : base(conn)
        {
        }



    }

}
