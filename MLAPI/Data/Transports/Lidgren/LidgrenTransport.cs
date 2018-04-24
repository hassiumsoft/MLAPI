using Lidgren.Network;
using MLAPI.MonoBehaviours.Core;
using System.Collections.Generic;
using UnityEngine;

namespace MLAPI.Data.Transports.Lidgren
{
    public class LidgrenTransport : IUDPTransport
    {
        public ChannelType InternalChannel => ChannelType.ReliableSequenced;

        public uint ServerNetId => uint.MaxValue;

        public uint HostDummyId => uint.MaxValue - 1;

        public uint InvalidDummyId => uint.MaxValue - 2;

        private Dictionary<int, NetDeliveryMethod> channels = new Dictionary<int, NetDeliveryMethod>();
        private int channelKey = 0;
        private NetServer server;
        private NetClient client;


        private Dictionary<uint, NetConnection> connectedClients = new Dictionary<uint, NetConnection>();
        private uint clientIdCounter = 0;
        private Queue<uint> releasedClientIds = new Queue<uint>();

        public int AddChannel(ChannelType type, object settings)
        {
            switch (type)
            {
                case ChannelType.Unreliable:
                    channels.Add(channelKey, NetDeliveryMethod.Unreliable);
                    break;
                case ChannelType.UnreliableFragmented:
                    channels.Add(channelKey, NetDeliveryMethod.Unreliable);
                    break;
                case ChannelType.UnreliableSequenced:
                    channels.Add(channelKey, NetDeliveryMethod.UnreliableSequenced);
                    break;
                case ChannelType.Reliable:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableUnordered);
                    break;
                case ChannelType.ReliableFragmented:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableUnordered);
                    break;
                case ChannelType.ReliableSequenced:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableSequenced);
                    break;
                case ChannelType.StateUpdate:
                    channels.Add(channelKey, NetDeliveryMethod.UnreliableSequenced);
                    break;
                case ChannelType.ReliableStateUpdate:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableOrdered);
                    break;
                case ChannelType.AllCostDelivery:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableUnordered);
                    break;
                case ChannelType.UnreliableFragmentedSequenced:
                    channels.Add(channelKey, NetDeliveryMethod.UnreliableSequenced);
                    break;
                case ChannelType.ReliableFragmentedSequenced:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableSequenced);
                    break;
                default:
                    channels.Add(channelKey, NetDeliveryMethod.ReliableSequenced);
                    break;
            }
            channelKey++;
            return channelKey - 1;
        }

        public void Connect(string address, int port, object settings, out byte error)
        {
            NetPeerConfiguration config = (NetPeerConfiguration)settings;
            client = new NetClient(config);
            client.Start();
            client.Connect(address, port);
            error = 0;
        }

        public void DisconnectClient(uint clientId)
        {
            for (int i = 0; i < server.ConnectionsCount; i++)
            {
                if ((uint)server.Connections[i].Tag == clientId)
                    server.Connections[i].Disconnect(string.Empty);
            }
        }

        public void DisconnectFromServer()
        {
            client.Disconnect(string.Empty);
        }

        public int GetCurrentRTT(uint clientId, out byte error)
        {
            error = 0;
            if (server != null)
            {
                for (int i = 0; i < server.ConnectionsCount; i++)
                {
                    return ((int)(server.Connections[i].AverageRoundtripTime / 1000f));
                }
            } else if (client != null)
                return ((int)(client.ServerConnection.AverageRoundtripTime / 1000f));
            return 0;
        }

        public int GetNetworkTimestamp()
        {
            //TODO. I don't know how the Lidgren Timestamp system work, if anyone knows. File a PR :)
            return 0;
        }

        public int GetRemoteDelayTimeMS(uint clientId, int remoteTimestamp, out byte error)
        {
            //TODO. I don't know how the Lidgren Timestamp system work, if anyone knows. File a PR :)
            error = 0;
            return 0;
        }

        public object GetSettings()
        {
            return new NetPeerConfiguration("MLAPI");
        }

        public NetEventType PollReceive(out uint clientId, out int channelId, ref byte[] data, int bufferSize, out int receivedSize, out byte error)
        {
            NetIncomingMessage message = null;
            if (server != null)
                message = server.ReadMessage();
            if (client != null)
                message = client.ReadMessage();

            if (message == null)
            {
                receivedSize = 0;
                channelId = 0;
                clientId = 0;
                error = 0;
                return NetEventType.Nothing;
            }

            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:

                    receivedSize = 0;
                    channelId = 0;
                    clientId = 0;
                    error = 0;

                    if (client != null && client.ConnectionStatus == NetConnectionStatus.Connected)
                    {
                        client.ServerConnection.Approve();
                        return NetEventType.Connect;
                    }
                    break;
                case NetIncomingMessageType.ConnectionApproval:
                    message.SenderConnection.Approve();
                    if (releasedClientIds.Count > 0)
                    {
                        uint id = releasedClientIds.Dequeue();
                        message.SenderConnection.Tag = id;
                        message.SenderConnection.Peer.Tag = id;
                        connectedClients.Add(id, message.SenderConnection);
                        clientId = id;
                    }
                    else
                    {
                        clientId = clientIdCounter;
                        connectedClients.Add(clientIdCounter, message.SenderConnection);
                        clientIdCounter++;
                    }
                    receivedSize = 0;
                    error = 0;
                    channelId = 0;
                    return NetEventType.Connect;
                case NetIncomingMessageType.Data:
                    receivedSize = message.LengthBytes;
                    data = message.Data;
                    error = 0;
                    clientId = (uint)message.SenderConnection.Tag;
                    channelId = 0;
                    return NetEventType.Data;
                default:
                    {
                        receivedSize = 0;
                        channelId = 0;
                        clientId = 0;
                        error = 0;
                        return NetEventType.Nothing;
                    }
            }
            return NetEventType.Nothing;
        }

        public void QueueMessageForSending(uint clientId, ref byte[] dataBuffer, int dataSize, int channelId, bool skipQueue, out byte error)
        {
            if (server != null)
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write(dataBuffer, 0, dataSize);
                server.SendMessage(msg, connectedClients[clientId], channels[channelId]);
                error = 0;
            }

            if (client != null)
            {
                NetOutgoingMessage msg = client.CreateMessage();
                msg.Write(dataBuffer, 0, dataSize);
                server.SendMessage(msg, client.ServerConnection, channels[channelId]);
                error = 0;
            }
            error = 0;
        }

        public void RegisterServerListenSocket(object settings)
        {
            server = new NetServer((NetPeerConfiguration)settings);
            server.Configuration.Port = NetworkingManager.singleton.NetworkConfig.ConnectPort;
            server.Configuration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            server.Start();
        }

        public void SendQueue(uint clientId, out byte error)
        {
            //Lidgren handles this for us
            error = 0;
            return;
        }

        public void Shutdown()
        {
            server.Shutdown(string.Empty);
            server = null;
            client.Shutdown(string.Empty);
            client = null;
        }
    }
}
