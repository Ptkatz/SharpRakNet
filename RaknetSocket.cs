using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading;

namespace SharpRakNet
{
    public class RaknetSocket
    {
        private const int bufSize = 64 * 1024;

        public IPEndPoint LocalEndPoint;
        public IPEndPoint PeerEndPoint;
        private AsyncCallback recv = null;
        private UdpClient udpClient;

        public RecvQ recvq;
        public SendQ sendq;
        public int last_heartbeat_time;
        public byte[] user_data_receiver;
        public object LockSender = new object();
        public byte raknet_version;

        public RaknetSocket() { }

        public RaknetSocket(IPEndPoint peerEndPoint, UdpClient udpClient, AsyncCallback recv, ushort mtu, byte raknetVersion) 
        {
            this.udpClient = udpClient;
            this.PeerEndPoint = peerEndPoint;
            this.LocalEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint;
            this.recv = recv;
            this.recvq = new RecvQ();
            this.sendq = new SendQ(mtu);
            this.raknet_version = raknetVersion;
        }

        public RaknetSocket Connect(IPEndPoint address, byte raknetVersion)
        {
            Random random = new Random();
            ulong guid = (ulong)random.NextDouble() * ulong.MaxValue;
            udpClient = Utils.CreateListener(new IPEndPoint(IPAddress.Any, 0));

            byte[] reply1Buf;
            byte[] reply2Buf;
            PeerEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    OpenConnectionRequest1 request1Packet = new OpenConnectionRequest1
                    {
                        magic = true,
                        protocol_version = raknetVersion,
                        mtu_size = Utils.RAKNET_CLIENT_MTU,
                    };
                    byte[] request1Buf = Packet.WritePacketConnectionOpenRequest1(request1Packet);
                    udpClient.Send(request1Buf, request1Buf.Length, address);
                    udpClient.Client.ReceiveTimeout = 2000;
                    reply1Buf = udpClient.Receive(ref PeerEndPoint);
                    udpClient.Client.ReceiveTimeout = 0;
                    if (reply1Buf[0] != PacketID.OpenConnectionReply1.ToU8())
                    {
                        if (reply1Buf[0] == PacketID.IncompatibleProtocolVersion.ToU8())
                        {
                            throw new RaknetError("NotSupportVersion");
                        }
                        else
                        {
                            continue;
                        }
                    }
                    break;
                }
                catch (TimeoutException)
                {
                    Console.WriteLine("Timeout");
                    continue;
                }
                catch (Exception)
                {
                    throw;
                }
            }
            OpenConnectionReply1 reply1Packet = Packet.ReadPacketConnectionOpenReply1(reply1Buf);
            OpenConnectionRequest2 request2packet = new OpenConnectionRequest2
            {
                magic = true,
                address = PeerEndPoint,
                mtu = reply1Packet.mtu_size,
                guid = guid
            };
            byte[] request2Buf = Packet.WritePacketConnectionOpenRequest2(request2packet);
            while (true)
            {
                try
                {
                    udpClient.Send(request2Buf, request2Buf.Length, address);
                    udpClient.Client.ReceiveTimeout = 2000;
                    reply2Buf = udpClient.Receive(ref PeerEndPoint);
                    udpClient.Client.ReceiveTimeout = 0;
                    if (reply2Buf[0] != PacketID.OpenConnectionReply2.ToU8())
                    {
                        if (reply2Buf[0] == PacketID.IncompatibleProtocolVersion.ToU8())
                        {
                            throw new RaknetError("NotSupportVersion");
                        }
                        else
                        {
                            continue;
                        }
                    }
                    OpenConnectionReply2 reply2Packet = Packet.ReadPacketConnectionOpenReply2(reply2Buf);
                    break;
                }
                catch (TimeoutException)
                {
                    Console.WriteLine("Timeout");
                    continue;
                }
                catch (Exception)
                {
                    throw;
                }
            }
            sendq = new SendQ(reply1Packet.mtu_size);
            recvq = new RecvQ();

            ConnectionRequest packet = new ConnectionRequest
            {
                guid = guid,
                time = Utils.CurTimestampMillis(),
                use_encryption = 0x00,
            };
            byte[] buf = Packet.WritePacketConnectionRequest(packet);

            udpClient.Send(buf, buf.Length, address);

            sendq.Insert(Reliability.ReliableOrdered, buf);

            var rk = new RaknetSocket
            {
                udpClient = udpClient,
                PeerEndPoint = address,
                LocalEndPoint = (IPEndPoint)udpClient.Client.LocalEndPoint,
                user_data_receiver = new byte[0],
                recvq = new RecvQ(),
                sendq = sendq,
                raknet_version = raknetVersion,
            };
            StartPing();
            StartReiver();

            return rk;
        }

        public void StartPing()
        {
            var timer = new Timer(SendPing, null,
                        new Random().Next(1000, 1500), new Random().Next(1000, 1500));
        }

        public void SendPing(object obj)
        {
            Console.WriteLine("ping");
            ConnectedPing connectedPing = new ConnectedPing 
            {
                client_timestamp = Utils.CurTimestampMillis(),
            };

            byte[] buf = Packet.WritePacketConnectedPing(connectedPing);
            sendq.Insert(Reliability.Unreliable, buf);
            foreach (FrameSetPacket item in sendq.Flush(Utils.CurTimestampMillis(), PeerEndPoint))
            {
                byte[] sdata = item.Serialize();
                udpClient.Send(sdata, sdata.Length, PeerEndPoint);
            }
        }

        public void SendData(byte[] buf)
        {
            sendq.Insert(Reliability.ReliableOrdered, buf);
            foreach (FrameSetPacket item in sendq.Flush(Utils.CurTimestampMillis(), PeerEndPoint))
            {
                byte[] sdata = item.Serialize();
                udpClient.Send(sdata, sdata.Length, PeerEndPoint);
            }
        }

        public void StartReiver()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            udpClient.BeginReceive(recv = (ar) =>
            {
                try
                {
                    udpClient = (UdpClient)ar.AsyncState;
                    byte[] receivedData = udpClient.EndReceive(ar, ref remoteEndPoint);
                    udpClient.BeginReceive(recv, udpClient);
                    HandleFrameSet(receivedData, remoteEndPoint);
                }
                catch (Exception)
                {
                    throw;
                }
            }, udpClient);
        }

        public void HandleFrameSet(byte[] data, IPEndPoint peer_addr)
        {
            var reader = new RaknetReader(data);
            byte headerFlags = reader.ReadU8();
            switch (PacketIDExtensions.FromU8(headerFlags))
            {
                case PacketID.Nack:
                    {
                        Console.WriteLine("Nack");
                        break;
                    }
                case PacketID.Ack:
                    {
                        Console.WriteLine("Ack");
                        var acks = recvq.GetAck();
                        if (acks.Count != 0)
                        {
                            Ack packet = new Ack
                            {
                                record_count = (ushort)acks.Count,
                                sequences = acks,
                            };
                            byte[] buf = Packet.WritePacketAck(packet);
                            udpClient.Send(buf, buf.Length, peer_addr);
                        }
                        else
                        {
                            Console.WriteLine("Ack Empty");
                        }
                        break;
                    }
                default:
                    {
                        if (PacketIDExtensions.FromU8(data[0]) >= PacketID.FrameSetPacketBegin && PacketIDExtensions.FromU8(data[0]) < PacketID.FrameSetPacketEnd)
                        {
                            HandleFrame(data, peer_addr);
                            var acks = recvq.GetAck();
                            if (acks.Count != 0)
                            {
                                Ack packet = new Ack
                                {
                                    record_count = (ushort)acks.Count,
                                    sequences = acks,
                                };
                                byte[] buf = Packet.WritePacketAck(packet);
                                udpClient.Send(buf, buf.Length, peer_addr);
                            }
                        }
                        break;
                    }
            }
        }

        public void HandleFrame(byte[] data, IPEndPoint peer_addr)
        {
            FrameSetPacket frame = FrameSetPacket.Deserialize(data);
            recvq.Insert(frame);
            PacketID packetID = PacketIDExtensions.FromU8(frame.data[0]);
            Console.Write($"DF: ");
            Utils.PrintBytes(frame.data.ToArray());
            switch (packetID)
            {
                case PacketID.ConnectionRequest:
                    {
                        ConnectionRequest packet = Packet.ReadPacketConnectionRequest(frame.data.ToArray());
                        var packet_reply = new ConnectionRequestAccepted
                        {
                            client_address = peer_addr,
                            system_index = 0,
                            request_timestamp = packet.time,
                            accepted_timestamp = Utils.CurTimestampMillis(),
                        };
                        byte[] buf = Packet.WritePacketConnectionRequestAccepted(packet_reply);
                        sendq.Insert(Reliability.ReliableOrdered, buf);
                        foreach (FrameSetPacket item in sendq.Flush(Utils.CurTimestampMillis(), peer_addr))
                        {
                            byte[] sdata = item.Serialize();
                            udpClient.Send(sdata, sdata.Length, peer_addr);
                        }
                        break;
                    }
                case PacketID.ConnectionRequestAccepted:
                    {
                        Console.WriteLine("ConnectionRequestAccepted");
                        ConnectionRequestAccepted packet = Packet.ReadPacketConnectionRequestAccepted(frame.data.ToArray());
                        NewIncomingConnection packet_reply = new NewIncomingConnection
                        {
                            server_address = (IPEndPoint)udpClient.Client.LocalEndPoint,
                            request_timestamp = packet.request_timestamp,
                            accepted_timestamp = Utils.CurTimestampMillis(),
                        };
                        byte[] buf = Packet.WritePacketNewIncomingConnection(packet_reply);
                        sendq.Insert(Reliability.ReliableOrdered, buf);
                        foreach (FrameSetPacket item in sendq.Flush(Utils.CurTimestampMillis(), peer_addr))
                        {
                            byte[] sdata = item.Serialize();
                            udpClient.Send(sdata, sdata.Length, peer_addr);
                        }
                        //ConnectedPing ping = new ConnectedPing
                        //{
                        //    client_timestamp = Utils.CurTimestampMillis(),
                        //};
                        //byte[] pingBuf = Packet.WritePacketConnectedPing(ping);
                        //sendq.Insert(Reliability.Unreliable, pingBuf);
                        break;
                    }
                case PacketID.NewIncomingConnection:
                    {
                        Console.WriteLine("NewIncomingConnection");
                        break;
                    }
                case PacketID.ConnectedPing:
                    {
                        Console.WriteLine("ConnectedPing");
                        ConnectedPing pingPacket = Packet.ReadPacketConnectedPing(frame.data.ToArray());
                        ConnectedPong pongPacket = new ConnectedPong
                        {
                            client_timestamp = pingPacket.client_timestamp,
                            server_timestamp = Utils.CurTimestampMillis(),
                        };
                        byte[] buf = Packet.WritePacketConnectedPong(pongPacket);
                        sendq.Insert(Reliability.Unreliable, buf);
                        foreach (FrameSetPacket item in sendq.Flush(Utils.CurTimestampMillis(), peer_addr))
                        {
                            byte[] sdata = item.Serialize();
                            udpClient.Send(sdata, sdata.Length, peer_addr);
                        }
                        break;
                    }
                case PacketID.ConnectedPong:
                    Console.WriteLine("ConnectedPong");
                    break;
                case PacketID.Disconnect:
                    Console.WriteLine("Disconnect");
                    break;
                default:
                    {
                        Console.Write($"Result: ");
                        Utils.PrintBytes(frame.data.ToArray());
                        break;
                    }
            }

        }
    }
}
