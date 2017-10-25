using UnityEngine.Networking;
using Steamworks;
using UnityEngine;

namespace UNETSteamworks
{
    public class SteamNetworkConnection : NetworkConnection
    {
        CSteamID m_RemoteSteamId;
        public CSteamID RemoteSteamId
        {
            get { return m_RemoteSteamId; }
        }

        HostTopology m_HostTopology;
        NetworkWriter writer;
        NetworkReader reader;

        public SteamNetworkConnection() : base()
        {
            
        }

        public SteamNetworkConnection(CSteamID remoteId, HostTopology hostTopology)
        {
            m_RemoteSteamId = remoteId;
            m_HostTopology = hostTopology;

            writer = new NetworkWriter();
            reader = new NetworkReader();
        }

        public void Initialize()
        {
            Initialize(string.Empty, 0, -1, m_HostTopology);
        }

        public override void Initialize(string address, int hostId, int connectionId, HostTopology hostTopology)
        {
            m_HostTopology = hostTopology;
            base.Initialize(address, hostId, connectionId, hostTopology);
        }

        public void Update()
        {
            FlushChannels();
        }

        public override bool TransportSend(byte[] bytes, int numBytes, int channelId, out byte error)
        {
            Debug.LogError("TransportSend");

            if (RemoteSteamId.m_SteamID == SteamUser.GetSteamID().m_SteamID)
            {
                TransportReceive(bytes, numBytes, channelId);
                error = 0;
                return true;
            }

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
            


        
    }

}
