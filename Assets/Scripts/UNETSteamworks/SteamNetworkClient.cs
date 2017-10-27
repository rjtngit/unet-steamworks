using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;


public class SteamNetworkClient : NetworkClient {

    public SteamNetworkConnection steamConnection { 
        get {
            return connection as SteamNetworkConnection;
        }

    }

    public string status { get { return m_AsyncConnect.ToString(); } }

    public void Connect()
    {
        // Connect to localhost and trick UNET by setting ConnectState state to "Connected", which triggers some initialization and allows data to pass through TransportSend
        Connect("localhost", 0);
        m_AsyncConnect = ConnectState.Connected;

        // manually init connection
        connection.ForceInitialize();

        // send Connected message
        connection.InvokeHandlerNoData(MsgType.Connect);
    }

    public SteamNetworkClient(NetworkConnection conn) : base(conn)
    {
    }



}


