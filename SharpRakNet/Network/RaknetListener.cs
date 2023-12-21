using SharpRakNet.Protocol.Raknet;

using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Net;
using System;

namespace SharpRakNet.Network
{
    public class RaknetListener
    {
        public delegate void SessionConnectedDelegate(RaknetSession session);
        public SessionConnectedDelegate SessionConnected = delegate { };
        public AsyncUdpClient Socket;

        private static readonly Dictionary<int, List<(Type, Delegate)>> Listeners = new Dictionary<int, List<(Type, Delegate)>>();
        private Dictionary<IPEndPoint, RaknetSession> Sessions = new Dictionary<IPEndPoint, RaknetSession>();

        private byte rak_version = 0xB;
        private ulong guid;

        public RaknetListener(IPEndPoint address)
        {
            Socket = new AsyncUdpClient(address);
            Socket.PacketReceived += OnPacketReceived;
            SessionConnected += OnSessionEstablished;
            guid = (ulong)new Random().NextDouble() * ulong.MaxValue;
        }

        public void Subscribe<T>(Action<IPEndPoint, T> action) where T : Packet
        {
            Type packetType = typeof(T);

            //Ensure the packet has a registered packet id.
            RegisterPacketID attribute =
                packetType.GetCustomAttribute<RegisterPacketID>() ?? throw new Exception(packetType.FullName + " must have the RegisterPacketID attribute.");

            bool hasBufferConstructor = false;
            foreach (ConstructorInfo constructor in packetType.GetConstructors())
            {
                ParameterInfo[] parameters = constructor.GetParameters();

                if (parameters.Length != 1) continue;
                if (parameters[0].ParameterType != typeof(byte[])) continue;

                hasBufferConstructor = true;
            }

            if (!hasBufferConstructor) throw new Exception(packetType.FullName + " must have a constructor that takes only a byte[].");

            int packetId = attribute.ID;

            bool exists = Listeners.TryGetValue(packetId, out var listeners);
            if (!exists)
            {
                listeners = new List<(Type, Delegate)>();
                Listeners.Add(packetId, listeners);
            }

            listeners.Add((packetType, action));
        }

        private void OnSessionEstablished(RaknetSession session)
        {
            session.SessionReceiveRaw += OnPacketReceived;
            session.SessionDisconnected += RemoveSession;
        }

        void RemoveSession(RaknetSession session)
        {
            session.SessionReceiveRaw -= OnPacketReceived;
            IPEndPoint peerAddr = session.PeerEndPoint;

            lock(Sessions)
                Sessions.Remove(peerAddr);
        }

        private void OnPacketReceived(IPEndPoint address, byte[] data)
        {
            switch ((PacketID)data[0]) {
                case PacketID.OpenConnectionRequest1:
                    HandleOpenConnectionRequest1(address, data);
                    break;
                case PacketID.OpenConnectionRequest2:
                    HandleOpenConnectionRequest2(address, data);
                    break;
                default:
                    {
                        if (Sessions.TryGetValue(address, out var session))
                        {
                            HandleIncomingPacket(address, data);
                            session.HandleFrameSet(address, data);
                        }
                        break;
                    }
            }
        }

        private void HandleIncomingPacket(IPEndPoint address, byte[] buffer)
        {
            byte packetID = buffer[0];

            bool exists = Listeners.TryGetValue(packetID, out List<(Type, Delegate)> value);
            if (!exists) return;

            foreach ((Type, Delegate) registration in value)
            {
                Delegate callback = registration.Item2;
                Type packetType = registration.Item1;

                MethodInfo method = packetType.GetMethod("Deserialize");
                if (method == null) return;

                object packet = Activator.CreateInstance(packetType, new object[] { buffer });
                method.Invoke(packet, new object[] {});

                callback.DynamicInvoke(address, packet);
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
            lock (Sessions)
            {
                Sessions.Add(peer_addr, session);
                Socket.Send(peer_addr, reply2Buf);
                SessionConnected(session);
            }
        }

        public void BeginListener()
        {
            Socket.Run();
        }

        public void StopListener()
        {
            lock (Sessions)
            {
                for (int i = Sessions.Count - 1; i >= 0; i--)
                {
                    var session = Sessions.Values.ElementAt(i);
                    session.SessionDisconnected(session);
                }
            }

            Socket.Stop();
        }

    }
}
