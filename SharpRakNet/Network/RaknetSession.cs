using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SharpRakNet.Network
{
    public class RaknetSession
    {
        public IPEndPoint PeerEndPoint { get; private set; }
        private AsyncUdpClient Socket;
        private ulong guid;
        public bool Connected;
        private int repingCount;
        public int MaxRepingCount = 6;

        public byte rak_version;
        public RecvQ Recvq;
        public SendQ Sendq;

        public delegate void PacketReceivedDelegate(RaknetSession session, Packet packet);
        public PacketReceivedDelegate PacketReceived = delegate { };

        public delegate void SessionDisconnectedDelegate(RaknetSession session);
        public SessionDisconnectedDelegate SessionDisconnected = delegate { };

        public delegate void PacketReceiveBytesDelegate(byte[] bytes);
        public PacketReceiveBytesDelegate SessionReceive = delegate { };

        public RaknetSession(AsyncUdpClient Socket, IPEndPoint Address, ulong guid, byte rak_version, RecvQ recvQ, SendQ sendQ)
        {
            this.Socket = Socket;
            this.PeerEndPoint = Address;
            this.guid = guid;
            this.Connected = true;
            this.rak_version = rak_version;
            this.Sendq = sendQ;
            this.Recvq = recvQ;

            StartPing();
            StartSender();
        }

        public void HandleFrameSet(IPEndPoint peer_addr, byte[] data)
        {
            var reader = new RaknetReader(data);
            byte headerFlags = reader.ReadU8();
            switch (PacketIDExtensions.FromU8(headerFlags))
            {
                case PacketID.Nack:
                    {
                        Nack nack = Packet.ReadPacketNack(data);
                        for (int i = 0; i < nack.record_count; i++)
                        {
                            if (nack.sequences[i].Start == nack.sequences[i].End)
                            {
                                Sendq.Nack(nack.sequences[i].Start, Common.CurTimestampMillis());
                            }
                            else
                            {
                                for (uint j = nack.sequences[i].Start; j <= nack.sequences[i].End; j++)
                                {
                                    Sendq.Nack(j, Common.CurTimestampMillis());
                                }
                            }
                        }
                        break;
                    }
                case PacketID.Ack:
                    {
                        Ack ack = Packet.ReadPacketAck(data);
                        for (int i = 0; i < ack.record_count; i++)
                        {
                            if (ack.sequences[i].Start == ack.sequences[i].End)
                            {
                                Sendq.Ack(ack.sequences[i].Start, Common.CurTimestampMillis());
                            }
                            else
                            {
                                for (uint j = ack.sequences[i].Start; j <= ack.sequences[i].End; j++)
                                {
                                    Sendq.Ack(j, Common.CurTimestampMillis());
                                }
                            }
                        }
                        break;
                    }
                default:
                    {
                        if (PacketIDExtensions.FromU8(data[0]) >= PacketID.FrameSetPacketBegin 
                            && PacketIDExtensions.FromU8(data[0]) <= PacketID.FrameSetPacketEnd)
                        {
                            HandleFrame(peer_addr, data);
                            var acks = Recvq.GetAck();
                            if (acks.Count != 0)
                            {
                                Ack packet = new Ack
                                {
                                    record_count = (ushort)acks.Count,
                                    sequences = acks,
                                };
                                byte[] buf = Packet.WritePacketAck(packet);
                                Socket.Send(peer_addr, buf);
                            }
                        }
                        break;
                    }
            }
        }

        public void HandleFrame(IPEndPoint peer_addr, byte[] data)
        {
            FrameSetPacket frame = FrameSetPacket.Deserialize(data);
            Recvq.Insert(frame);
            PacketID packetID = PacketIDExtensions.FromU8(frame.data[0]);

            switch (packetID)
            {
                case PacketID.ConnectedPing:
                    HandleConnectPing(frame.data.ToArray());
                    break;
                case PacketID.ConnectedPong:
                    repingCount = 0;
                    break;
                case PacketID.ConnectionRequest:
                    HandleConnectionRequest(peer_addr, data);
                    break;
                case PacketID.ConnectionRequestAccepted:
                    HandleConnectionRequestAccepted(frame.data.ToArray());
                    break;
                case PacketID.NewIncomingConnection:
                    break;
                case PacketID.Disconnect:
                    HandleDisconnectionNotification();
                    break;
                default:
                    SessionReceive(frame.data.ToArray());
                    break;
            }
        }

        private void HandleConnectPing(byte[] data)
        {
            ConnectedPing pingPacket = Packet.ReadPacketConnectedPing(data);
            ConnectedPong pongPacket = new ConnectedPong
            {
                client_timestamp = pingPacket.client_timestamp,
                server_timestamp = Common.CurTimestampMillis(),
            };
            byte[] buf = Packet.WritePacketConnectedPong(pongPacket);
            Sendq.Insert(Reliability.Unreliable, buf);
        }

        private void HandleConnectionRequestAccepted(byte[] data)
        {
            ConnectionRequestAccepted packet = Packet.ReadPacketConnectionRequestAccepted(data);
            NewIncomingConnection packet_reply = new NewIncomingConnection
            {
                server_address = (IPEndPoint)Socket.Socket.Client.LocalEndPoint,
                request_timestamp = packet.request_timestamp,
                accepted_timestamp = Common.CurTimestampMillis(),
            };
            byte[] buf = Packet.WritePacketNewIncomingConnection(packet_reply);
            Sendq.Insert(Reliability.ReliableOrdered, buf);
        }

        private void HandleConnectionRequest(IPEndPoint peer_addr, byte[] data)
        {
            ConnectionRequest packet = Packet.ReadPacketConnectionRequest(data);
            ConnectionRequestAccepted packet_reply = new ConnectionRequestAccepted
            {
                client_address = peer_addr,
                system_index = 0,
                request_timestamp = packet.time,
                accepted_timestamp = Common.CurTimestampMillis(),
            };
            byte[] buf = Packet.WritePacketConnectionRequestAccepted(packet_reply);
            Sendq.Insert(Reliability.ReliableOrdered, buf);
        }

        private void HandleDisconnectionNotification()
        {
            SessionDisconnected(this);
            Connected = false;
        }

        public void HandleConnect()
        {
            ConnectionRequest requestPacket = new ConnectionRequest
            {
                guid = guid,
                time = Common.CurTimestampMillis(),
                use_encryption = 0x00,
            };
            byte[] buf = Packet.WritePacketConnectionRequest(requestPacket);
            Sendq.Insert(Reliability.ReliableOrdered, buf);
        }

        public void StartSender()
        {
            Thread thread =  new Thread(() => 
            {
                while (true)
                {
                    Thread.Sleep(100);
                    foreach (FrameSetPacket item in Sendq.Flush(Common.CurTimestampMillis(), PeerEndPoint))
                    {
                        byte[] sdata = item.Serialize();
                        Socket.Send(PeerEndPoint, sdata);
                    }
                }
            });
            thread.Start();
        }

        public void StartPing()
        {
            var timer = new Timer(SendPing, null,
                        new Random().Next(10 * 1000, 10 * 1500), new Random().Next(10 * 1000, 10 * 1500));
        }

        public void SendPing(object obj)
        {
            if (Connected)
            {
                ConnectedPing pingPacket = new ConnectedPing
                {
                    client_timestamp = Common.CurTimestampMillis(),
                };
                byte[] buf = Packet.WritePacketConnectedPing(pingPacket);
                Sendq.Insert(Reliability.Unreliable, buf);
                repingCount++;
                if (repingCount > MaxRepingCount)
                {
                    HandleDisconnectionNotification();
                }
            }
        }
    }
}
