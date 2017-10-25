using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;

public class SteamNetworkClient : NetworkClient {

    CSteamID remoteId;


    public void Connect(CSteamID remoteId)
    {
        this.remoteId = remoteId;

        connection.InvokeHandlerNoData(MsgType.Connect);
    }

    public SteamNetworkClient(NetworkConnection conn) : base(conn)
    {
    }


}
