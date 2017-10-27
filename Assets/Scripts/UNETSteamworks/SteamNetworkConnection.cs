using UnityEngine.Networking;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking.NetworkSystem;
using System.Text;
using System.Collections.Generic;


public class SteamNetworkConnection : NetworkConnection
{
    public CSteamID steamId;

    public SteamNetworkConnection() : base()
    {
    }

    public SteamNetworkConnection(CSteamID steamId, HostTopology hostTopology)
    {
        this.steamId = steamId;
    }

    public override bool TransportSend(byte[] bytes, int numBytes, int channelId, out byte error)
    {
        if (steamId.m_SteamID == SteamUser.GetSteamID().m_SteamID)
        {
            // sending to self. short circuit
            TransportReceive(bytes, numBytes, channelId);
            error = 0;
            return true;
        }

        EP2PSend eP2PSendType = EP2PSend.k_EP2PSendReliable;

        QosType qos = SteamNetworkManager.hostTopology.DefaultConfig.Channels[channelId].QOS;
        if (qos == QosType.Unreliable || qos == QosType.UnreliableFragmented || qos == QosType.UnreliableSequenced)
        {
            eP2PSendType = EP2PSend.k_EP2PSendUnreliable;
        }

        // Send packet to peer through Steam
        if (SteamNetworking.SendP2PPacket(steamId, bytes, (uint)numBytes, eP2PSendType))
        {
            error = 0;
            return true;
        }
        else
        {
            error = 1;
            return false;
        }
    }

}

