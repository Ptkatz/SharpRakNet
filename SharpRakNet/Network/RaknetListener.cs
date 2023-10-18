using SharpRakNet.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SharpRakNet.Network
{
    public class RaknetListener
    {
        public AsyncUdpClient Socket;
        private ulong guid;
        private Dictionary<IPEndPoint, RaknetSession> Sessions = new Dictionary<IPEndPoint, RaknetSession>();
        private byte rak_version = 0xB;

        public delegate void SessionConnectedDelegate(RaknetSession session);
        public SessionConnectedDelegate SessionConnected = delegate { };

        public RaknetListener(IPEndPoint address)
        {
            Socket = new AsyncUdpClient(address);
            Socket.PacketReceived += this.OnPacketReceived;
            SessionConnected += OnSessionEstablished;
            guid = (ulong)new Random().NextDouble() * ulong.MaxValue;
        }

        private void OnSessionEstablished(RaknetSession session)
        {
            session.SessionDisconnected += RemoveSession;
        }

        void RemoveSession(RaknetSession session)
        {
            IPEndPoint peerAddr = session.PeerEndPoint;
            Sessions.Remove(peerAddr);
        }

        private void OnPacketReceived(IPEndPoint peer_addr, byte[] data)
        {
            switch (PacketIDExtensions.FromU8(data[0]))
            {
                case PacketID.OpenConnectionRequest1:
                    {
                        HandleOpenConnectionRequest1(peer_addr, data);
                        break;
                    }
                case PacketID.OpenConnectionRequest2:
                    {
                        HandleOpenConnectionRequest2(peer_addr, data);
                        break;
                    }
                default:
                    {
                        if (Sessions.TryGetValue(peer_addr, out var session))
                        {
                            session.HandleFrameSet(peer_addr, data);
                        }
                        break;
                    }
            }
        }

        private void HandleOpenConnectionRequest1(IPEndPoint peer_addr, byte[] data)
        {
            OpenConnectionReply1 reply1Packet = new OpenConnectionReply1
            {
                magic = true,
                guid = guid,
                use_encryption = 0x00,
                mtu_size = Common.RAKNET_CLIENT_MTU,
            };
            byte[] reply1Buf = Packet.WritePacketConnectionOpenReply1(reply1Packet);
            Socket.Send(peer_addr, reply1Buf);
        }

        private void HandleOpenConnectionRequest2(IPEndPoint peer_addr, byte[] data)
        {
            OpenConnectionRequest2 req = Packet.ReadPacketConnectionOpenRequest2(data);
            OpenConnectionReply2 reply2Packet = new OpenConnectionReply2
            {
                magic = true,
                guid = guid,
                address = peer_addr,
                mtu = req.mtu,
                encryption_enabled = 0x00,
            };
            byte[] reply2Buf = Packet.WritePacketConnectionOpenReply2(reply2Packet);
            var session = new RaknetSession(Socket, peer_addr, guid, rak_version, new RecvQ(), new SendQ(req.mtu));
            Sessions.Add(peer_addr, session);
            Socket.Send(peer_addr, reply2Buf);
            SessionConnected(session);
        }

        public void BeginListener()
        {
            Socket.Run();
        }

        public void StopListener()
        {
            for (int i = Sessions.Count - 1; i >= 0; i--)
            {
                var session = Sessions.Values.ElementAt(i);
                session.SessionDisconnected(session);
            }
            Socket.Stop();
        }

    }
}
