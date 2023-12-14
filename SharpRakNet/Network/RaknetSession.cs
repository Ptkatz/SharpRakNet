using SharpRakNet.Protocol.Raknet;
using System.Threading;
using System.Net;
using System;
using System.Collections.Generic;

namespace SharpRakNet.Network
{
    public class RaknetSession
    {
        public IPEndPoint PeerEndPoint { get; private set; }
        private Dictionary<int, Packet> Packets;
        public Thread SenderThread;
        public byte rak_version;
        public Timer PingTimer;
        public bool Connected;
        public RecvQ Recvq;
        public SendQ Sendq;

        private readonly AsyncUdpClient Socket;
        public int MaxRepingCount = 6;
        private int repingCount;
        private ulong guid;

        public delegate void PacketReceivedDelegate(RaknetSession session, Packet packet);
        public PacketReceivedDelegate PacketReceived = delegate { };

        public delegate void SessionDisconnectedDelegate(RaknetSession session);
        public SessionDisconnectedDelegate SessionDisconnected = delegate { };

        public delegate void PacketReceiveBytesDelegate(byte[] bytes);
        public PacketReceiveBytesDelegate SessionReceiveRaw = delegate { };

        public RaknetSession(AsyncUdpClient Socket, IPEndPoint Address, ulong guid, byte rakVersion, RecvQ recvQ, SendQ sendQ)
        {
            this.Socket = Socket;
            PeerEndPoint = Address;
            this.guid = guid;
            Connected = true;

            rak_version = rakVersion;

            Sendq = sendQ;
            Recvq = recvQ;

            StartPing();
            SenderThread = StartSender();
        }

        public void HandleFrameSet(IPEndPoint peer_addr, byte[] data)
        {
            if (!Connected)
            {
                foreach (FrameSetPacket f in Recvq.Flush(peer_addr))
                {
                    HandleFrame(peer_addr, f);
                }
            }
            var reader = new RaknetReader(data);
            byte headerFlags = reader.ReadU8();
            switch (PacketIDExtensions.FromU8(headerFlags))
            {
                case PacketID.Nack:
                    {
                        //Console.WriteLine("Nack");
                        lock (Sendq)
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
                        }

                        break;
                    }
                case PacketID.Ack:
                    {
                        //Console.WriteLine("Ack");
                        lock (Sendq)
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
                        }

                        break;
                    }
                default:
                    {
                        if (PacketIDExtensions.FromU8(data[0]) >= PacketID.FrameSetPacketBegin 
                            && PacketIDExtensions.FromU8(data[0]) <= PacketID.FrameSetPacketEnd)
                        {
                            var frames = new FrameVec(data);
                            lock (Recvq)
                            {
                                foreach (var frame in frames.frames)
                                {
                                    Recvq.Insert(frame);
                                    foreach (FrameSetPacket f in Recvq.Flush(peer_addr))
                                    {
                                        HandleFrame(peer_addr, f);
                                    }
                                }

                            }
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


        public void HandleFrame(IPEndPoint address, FrameSetPacket frame)
        {
            PacketID packetID = PacketIDExtensions.FromU8(frame.data[0]);

            switch (packetID)
            {
                case PacketID.ConnectedPing:
                    HandleConnectPing(frame.data);
                    break;
                case PacketID.ConnectedPong:
                    repingCount = 0;
                    break;
                case PacketID.ConnectionRequest:
                    HandleConnectionRequest(address, frame.data);
                    break;
                case PacketID.ConnectionRequestAccepted:
                    HandleConnectionRequestAccepted(frame.data);
                    break;
                case PacketID.NewIncomingConnection:
                    break;
                case PacketID.Disconnect:
                    HandleDisconnectionNotification();
                    break;
                default:
                    SessionReceiveRaw(frame.data);
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
            lock (Sendq)
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
            lock (Sendq)
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
            lock (Sendq)
                Sendq.Insert(Reliability.ReliableOrdered, buf);
        }

        private void HandleDisconnectionNotification()
        {
            Connected = false;
            SenderThread.Abort();
            PingTimer.Dispose();
            SessionDisconnected(this);
            GC.Collect();
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
            lock (Sendq)
                Sendq.Insert(Reliability.ReliableOrdered, buf);
        }

        public Thread StartSender()
        {
            Thread thread = new Thread(() => 
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
            return thread;
        }

        public void StartPing()
        {
            PingTimer = new Timer(SendPing, null,
                new Random().Next(1000, 1500), new Random().Next(1000, 1500));
        }

        public void SendPing(object obj)
        {
            if (!Connected) return;
            ConnectedPing pingPacket = new ConnectedPing
            {
                client_timestamp = Common.CurTimestampMillis(),
            };

            byte[] buffer = Packet.WritePacketConnectedPing(pingPacket);
            lock (Sendq)
                Sendq.Insert(Reliability.Unreliable, buffer);

            repingCount++;
            if (repingCount < MaxRepingCount) return;

            HandleDisconnectionNotification();
        }
    }
}
