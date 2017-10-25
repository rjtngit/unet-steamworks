using UnityEngine.Networking;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking.NetworkSystem;
using System.Text;
using System.Collections.Generic;

namespace UNETSteamworks
{
    public class SteamNetworkConnection : NetworkConnection
    {
        const int k_MaxMessageLogSize = 150;

        static int nextId = -1;

        public CSteamID mySteamId;
        public CSteamID peerSteamId;

        Dictionary<short, NetworkMessageDelegate> m_MessageHandlersDict = new Dictionary<short, NetworkMessageDelegate>();

        HostTopology m_HostTopology;
        NetworkWriter writer;
        NetworkReader reader;

        NetworkMessage netMsg = new NetworkMessage();

        public SteamNetworkConnection() : base()
        {
        }

        public SteamNetworkConnection(CSteamID mySteamId, CSteamID peerSteamId, HostTopology hostTopology)
        {
            this.mySteamId = mySteamId;
            this.peerSteamId = peerSteamId;
            m_HostTopology = hostTopology;

            writer = new NetworkWriter();
            reader = new NetworkReader();
        }

        public void SetHandlers(  Dictionary<short, NetworkMessageDelegate> handlers)
        {
            m_MessageHandlersDict = handlers;
        }

        public void Initialize()
        {
            Initialize(string.Empty, 0, ++nextId, m_HostTopology);
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

            if (peerSteamId.m_SteamID == SteamUser.GetSteamID().m_SteamID)
            {
                // cannot send from peer's connection
                Debug.LogError("Cannot send to self");
                error = 1;
                return false;
            }

            if (mySteamId.m_SteamID != SteamUser.GetSteamID().m_SteamID)
            {
                // cannot send from peer's connection
                Debug.LogError("Cannot send from peer");
                error = 1;
                return false;
            }

            EP2PSend eP2PSendType = EP2PSend.k_EP2PSendReliable;

            QosType qos = m_HostTopology.DefaultConfig.Channels[channelId].QOS;
            if (qos == QosType.Unreliable || qos == QosType.UnreliableFragmented || qos == QosType.UnreliableSequenced)
            {
                eP2PSendType = EP2PSend.k_EP2PSendUnreliable;
            }

            if (SteamNetworking.SendP2PPacket(peerSteamId, bytes, (uint)numBytes, eP2PSendType, channelId))
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
            
      
        protected void MyHandleBytes(
            byte[] buffer,
            int receivedSize,
            int channelId)
        {
            // build the stream form the buffer passed in
            NetworkReader reader = new NetworkReader(buffer);

            MyHandleReader(reader, receivedSize, channelId);
        }

        protected void MyHandleReader(
            NetworkReader reader,
            int receivedSize,
            int channelId)
        {
            Debug.LogError("MyHandleReader");
            // read until size is reached.
            // NOTE: stream.Capacity is 1300, NOT the size of the available data
            while (reader.Position < receivedSize)
            {
                // the reader passed to user code has a copy of bytes from the real stream. user code never touches the real stream.
                // this ensures it can never get out of sync if user code reads less or more than the real amount.
                ushort sz = reader.ReadUInt16();
                short msgType = reader.ReadInt16();

                // create a reader just for this message
                byte[] msgBuffer = reader.ReadBytes(sz);
                NetworkReader msgReader = new NetworkReader(msgBuffer);

                if (logNetworkMessages)
                {
                    StringBuilder msg = new StringBuilder();
                    for (int i = 0; i < sz; i++)
                    {
                        msg.AppendFormat("{0:X2}", msgBuffer[i]);
                        if (i > k_MaxMessageLogSize) break;
                    }
                    Debug.Log("ConnectionRecv con:" + connectionId + " bytes:" + sz + " msgId:" + msgType + " " + msg);
                }

                netMsg.msgType = msgType;
                netMsg.reader = msgReader;
                netMsg.conn = this;
                netMsg.channelId = channelId;
                lastMessageTime = Time.time;

                Debug.LogError("InvokeHandler " + netMsg.msgType);
                MyInvokeHandler(netMsg);
            }
        }

        public override void TransportReceive(byte[] bytes, int numBytes, int channelId)
        {
            Debug.LogError("TransportReceive");
            MyHandleBytes(bytes, numBytes, channelId);
        }

        public bool MyInvokeHandler(NetworkMessage netMsg)
        {
            base.InvokeHandler(netMsg);

            if (m_MessageHandlersDict.ContainsKey(netMsg.msgType))
            {
                NetworkMessageDelegate msgDelegate = m_MessageHandlersDict[netMsg.msgType];
                msgDelegate(netMsg);
                return true;
            }
            return false;
        }
    }

}
