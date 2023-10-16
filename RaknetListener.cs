using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;

namespace SharpRakNet
{
    public class RaknetListener
    {
        private string motd;
        private ulong guid;
        private UdpClient udpClient;
        private bool listened;
        private AsyncCallback recv = null;
        private byte raknet_version = 0xA;
        private const int bufSize = 64 * 1024;

        public RecvQ recvq;
        public SendQ sendq;

        public RaknetListener(IPEndPoint endpoint) 
        {
            udpClient = Utils.CreateListener(endpoint);
            guid = (ulong)new Random().NextDouble() * ulong.MaxValue;

        }

        public void StartListen()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            udpClient.BeginReceive(recv = (ar) =>
            {
                try
                {
                    udpClient = (UdpClient)ar.AsyncState;
                    byte[] receivedData = udpClient.EndReceive(ar, ref remoteEndPoint);
                    udpClient.BeginReceive(recv, udpClient);
                    HandleReceive(remoteEndPoint, receivedData);
                }
                catch (Exception)
                {
                    throw;
                }
            }, udpClient);
        }

        public void HandleReceive(IPEndPoint remoteEndPoint, byte[] data)
        {
            switch (PacketIDExtensions.FromU8(data[0]))
            {
                case PacketID.OpenConnectionRequest1:
                    {
                        Console.WriteLine("OpenConnectionRequest1");
                        OpenConnectionReply1 reply1Packet = new OpenConnectionReply1
                        {
                            magic = true,
                            guid = guid,
                            use_encryption = 0x00,
                            mtu_size = Utils.RAKNET_CLIENT_MTU,
                        };
                        byte[] reply1Buf = Packet.WritePacketConnectionOpenReply1(reply1Packet);
                        udpClient.Send(reply1Buf, reply1Buf.Length, remoteEndPoint);
                        break;
                    }
                case PacketID.OpenConnectionRequest2:
                    {
                        Console.WriteLine("OpenConnectionRequest2");
                        OpenConnectionRequest2 req = Packet.ReadPacketConnectionOpenRequest2(data);
                        OpenConnectionReply2 reply2Packet = new OpenConnectionReply2
                        {
                            magic = true,
                            guid = guid,
                            address = remoteEndPoint,
                            mtu = req.mtu,
                            encryption_enabled = 0x00,
                        };
                        byte[] reply2Buf = Packet.WritePacketConnectionOpenReply2(reply2Packet);
                        sendq = new SendQ(req.mtu);
                        recvq = new RecvQ();
                        udpClient.Send(reply2Buf, reply2Buf.Length, remoteEndPoint);
                        break;
                    }
                case PacketID.UnconnectedPing1:
                    {
                        Console.WriteLine("UnconnectedPing1");
                        var ping = Packet.ReadPacketPing(data);
                        var packet = new PacketUnconnectedPong
                        {
                            time = Utils.CurTimestampMillis(),
                            guid = guid,
                            magic = true,
                            motd = motd,
                        };
                        var pong = Packet.WritePacketPong(packet);
                        udpClient.Send(pong, pong.Length, remoteEndPoint);
                        break;
                    }
                case PacketID.UnconnectedPing2:
                    {
                        Console.WriteLine("UnconnectedPing2");
                        break;
                    }
                default:
                    {
                        Console.Write($"DD: ");
                        Utils.PrintBytes(data); 
                        HandleFrameSet(data, remoteEndPoint);
                        break;
                    }
            }
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
                        ConnectedPing ping = new ConnectedPing
                        {
                            client_timestamp = Utils.CurTimestampMillis(),
                        };
                        byte[] pingBuf = Packet.WritePacketConnectedPing(ping);
                        sendq.Insert(Reliability.Unreliable, pingBuf);

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

        private void SendPing(object sender, ElapsedEventArgs e)
        {
            Console.WriteLine("send ping");
        }

    }
}
