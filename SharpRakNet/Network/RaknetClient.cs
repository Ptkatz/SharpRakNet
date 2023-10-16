using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SharpRakNet.Network
{
    public class AsyncUdpClient
    {
        public UdpClient Socket;

        public delegate void PacketReceivedDelegate(IPEndPoint address, byte[] packet);
        public PacketReceivedDelegate PacketReceived = delegate { };

        public AsyncUdpClient()
        {
            Socket = Common.CreateListener(new IPEndPoint(IPAddress.Any, 0));
        }

        public AsyncUdpClient(IPEndPoint address)
        {
            Socket = Common.CreateListener(address);
        }

        public void Send(IPEndPoint address, byte[] packet)
        {
            Socket.Send(packet, packet.Length, address);
        }

        public void RunLoop()
        {
            while (true)
            {
                IPEndPoint source = new IPEndPoint(0, 0);
                byte[] packet;
                try
                {
                    packet = Socket.Receive(ref source);
                }
                catch (SocketException e)
                {
                    if (e.ErrorCode == 10054)
                    {
                        //Console.WriteLine("Received connection reset - Rcon server probably not running.");
                        break;
                    }
                    else
                    {
                        throw e;
                    }
                }

                PacketReceived(source, packet);
            }
        }
    }

    public class RaknetClient
    {
        public AsyncUdpClient Socket;
        private ulong guid;
        private RaknetSession Session;
        private byte rak_version = 0xB;

        public delegate void SessionEstablishedDelegate(RaknetSession session);
        public SessionEstablishedDelegate SessionEstablished = delegate { };

        public RaknetClient()
        {
            Socket = new AsyncUdpClient();
            Socket.PacketReceived += this.OnPacketReceived;
            guid = (ulong)new Random().NextDouble() * ulong.MaxValue;
        }

        private void OnPacketReceived(IPEndPoint peer_addr, byte[] data)
        {
            switch (PacketIDExtensions.FromU8(data[0]))
            {
                case PacketID.OpenConnectionReply1:
                    {
                        HandleOpenConnectionReply1(peer_addr, data);
                        break;
                    }
                case PacketID.OpenConnectionReply2:
                    {
                        HandleOpenConnectionReply2(peer_addr, data);
                        break;
                    }
                case PacketID.IncompatibleProtocolVersion:
                    {
                        throw new RaknetError("NotSupportVersion");
                    }
                default:
                    {
                        Session.HandleFrameSet(peer_addr, data);
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

            Session = new RaknetSession(Socket, peer_addr, guid)
            {
                rak_version = rak_version,
                Recvq = new RecvQ(),
                Sendq = new SendQ(reply2Packet.mtu),
            };
            Session.HandleConnect();
            SessionEstablished(Session);
        }

        public void Send(IPEndPoint peer_addr, byte[] packet)
        {
            Socket.Send(peer_addr, packet);
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
            Socket.RunLoop();
        }
    }
}
