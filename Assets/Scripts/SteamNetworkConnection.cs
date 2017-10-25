using UnityEngine.Networking;
using Steamworks;

public class SteamNetworkConnection : NetworkConnection
{
    CSteamID m_RemoteSteamId;
    public CSteamID RemoteSteamId
    {
        get { return m_RemoteSteamId; }
    }

    HostTopology m_HostTopology;
    NetworkWriter writer;

    public SteamNetworkConnection(CSteamID remoteId, HostTopology hostTopology)
    {
        m_RemoteSteamId = remoteId;
        m_HostTopology = hostTopology;

        writer = new NetworkWriter();
    }

    public override void Initialize(string address, int hostId, int connectionId, HostTopology hostTopology)
    {
        base.Initialize(address, hostId, connectionId, hostTopology);
    }

    public void Update()
    {
        FlushChannels();
    }

    public override bool TransportSend(byte[] bytes, int numBytes, int channelId, out byte error)
    {
        EP2PSend eP2PSendType = EP2PSend.k_EP2PSendReliable;

        QosType qos = m_HostTopology.DefaultConfig.Channels[channelId].QOS;
        if (qos == QosType.Unreliable || qos == QosType.UnreliableFragmented || qos == QosType.UnreliableSequenced)
        {
            eP2PSendType = EP2PSend.k_EP2PSendUnreliable;
        }

        if (SteamNetworking.SendP2PPacket(RemoteSteamId, bytes, (uint)numBytes, eP2PSendType, channelId))
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

    public override bool Send(short msgType, MessageBase msg)
    {
        EP2PSend eP2PSendType = EP2PSend.k_EP2PSendReliable;

        QosType qos = m_HostTopology.DefaultConfig.Channels[0].QOS;
        if (qos == QosType.Unreliable || qos == QosType.UnreliableFragmented || qos == QosType.UnreliableSequenced)
        {
            eP2PSendType = EP2PSend.k_EP2PSendUnreliable;
        }

        writer.StartMessage(msgType);
        msg.Serialize(writer);
        writer.FinishMessage();
        return SteamNetworking.SendP2PPacket(RemoteSteamId, writer.AsArray(),(uint) writer.AsArray().Length, eP2PSendType, 0);
    }

}