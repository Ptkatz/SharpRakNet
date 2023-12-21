using System;
using System.Net.Sockets;
using System.Net;
using SharpRakNet.Protocol;
using System.Threading;
using SharpRakNet.Protocol.Raknet;

namespace SharpRakNet.Network
{
    public class RaknetClient
    {
        public AsyncUdpClient Socket { get; private set; }
        public RaknetSession Session { get; private set; }
        private ulong guid;
        private byte rak_version = 0xB;

        public delegate void SessionEstablishedDelegate(RaknetSession session);
        public SessionEstablishedDelegate SessionEstablished = delegate { };

        public RaknetClient()
        {
            Socket = new AsyncUdpClient();
            Socket.PacketReceived += OnPacketReceived;
            guid = (ulong)new Random().NextDouble() * ulong.MaxValue;
        }

        private void OnPacketReceived(IPEndPoint peer_addr, byte[] data)
        {
            switch ((PacketID)data[0]) {
                case PacketID.OpenConnectionReply1:
                    HandleOpenConnectionReply1(peer_addr, data);
                    break;
                case PacketID.OpenConnectionReply2:
                    HandleOpenConnectionReply2(peer_addr, data);
                    break;
                case PacketID.IncompatibleProtocolVersion:
                    throw new RaknetError("NotSupportVersion");
                default: {
                    if (Session == null) break;

                    lock (Session)
                    {
                        Session.HandleFrameSet(peer_addr, data);
                    }

                    break;
                }
            }
        }

        private void HandleOpenConnectionReply1(IPEndPoint peer_addr, byte[] data)
        {
            OpenConnectionReply1 reply1Packet = Packet.ReadPacketConnectionOpenReply1(data);
            OpenConnectionRequest2 request2packet = new OpenConnectionRequest2
            {
                magic = true,
                address = peer_addr,
                mtu = reply1Packet.mtu_size,
                guid = guid
            };
            byte[] request2Buf = Packet.WritePacketConnectionOpenRequest2(request2packet);
            Socket.Send(peer_addr, request2Buf);
        }

        private void HandleOpenConnectionReply2(IPEndPoint peer_addr, byte[] data)
        {
            OpenConnectionReply2 reply2Packet = Packet.ReadPacketConnectionOpenReply2(data);

            Session = new RaknetSession(Socket, peer_addr, guid, rak_version, new RecvQ(), new SendQ(reply2Packet.mtu));
            Session.HandleConnect();
            SessionEstablished(Session);
        }

        public void Send(IPEndPoint peer_addr, byte[] packet)
        {
            Socket.Send(peer_addr, packet);
        }

        public void Subscribe<T>(Action<T> action) where T : Packet
        {
            if(Session == null) throw new Exception("Session not yet connected, cannot subscribe before a session is established...");

            Session.Subscribe(action);
        }

        public void BeginConnection(IPEndPoint address)
        {
            OpenConnectionRequest1 request1Packet = new OpenConnectionRequest1
            {
                magic = true,
                protocol_version = rak_version,
                mtu_size = Common.RAKNET_CLIENT_MTU,
            };
            byte[] request1Buf = Packet.WritePacketConnectionOpenRequest1(request1Packet);
            Send(address, request1Buf);
            Socket.Run();
        }

        public void EndConnection()
        {
            Socket.Stop();
        }
    }
}
