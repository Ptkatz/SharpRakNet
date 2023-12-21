using System.Collections.Generic;
using System.Net;
using System;

namespace SharpRakNet.Protocol.Raknet {
    public abstract class Packet {
        public byte[] Buffer { get; set; }

        public Packet(byte[] buffer) {
            Buffer = buffer;
        }

        public T Cast<T>() where T : Packet {
            return (T)Activator.CreateInstance(typeof(T), new object[] { Buffer });
        }

        public abstract byte[] Serialize();

        public abstract void Deserialize();

        public static byte[] WritePacketPing(PacketUnconnectedPing packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.UnconnectedPing1);
            cursor.WriteI64(packet.time, Endian.Big);
            cursor.WriteMagic();
            cursor.WriteU64(packet.guid, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static PacketUnconnectedPong ReadPacketPong(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new PacketUnconnectedPong
            {
                time = cursor.ReadI64(Endian.Big),
                guid = cursor.ReadU64(Endian.Big),
                magic = cursor.ReadMagic(),
                motd = cursor.ReadString(),
            };
        }

        public static byte[] WritePacketPong(PacketUnconnectedPong packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.UnconnectedPong);
            cursor.WriteI64(packet.time, Endian.Big);
            cursor.WriteU64(packet.guid, Endian.Big);
            cursor.WriteMagic();
            cursor.WriteString(packet.motd);
            return cursor.GetRawPayload();
        }

        public static OpenConnectionRequest1 ReadPacketConnectionOpenRequest1(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new OpenConnectionRequest1
            {
                magic = cursor.ReadMagic(),
                protocol_version = cursor.ReadU8(),
                mtu_size = (ushort)(buf.Length + 28)
            };
        }

        public static byte[] WritePacketConnectionOpenRequest1(OpenConnectionRequest1 packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.OpenConnectionRequest1);
            cursor.WriteMagic();
            cursor.WriteU8(packet.protocol_version);
            cursor.Write(new byte[packet.mtu_size - 46]);
            return cursor.GetRawPayload();
        }

        public static OpenConnectionRequest2 ReadPacketConnectionOpenRequest2(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new OpenConnectionRequest2
            {
                magic = cursor.ReadMagic(),
                address = cursor.ReadAddress(),
                mtu = cursor.ReadU16(Endian.Big),
                guid = cursor.ReadU64(Endian.Big),
            };
        }

        public static byte[] WritePacketConnectionOpenRequest2(OpenConnectionRequest2 packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.OpenConnectionRequest2);
            cursor.WriteMagic();
            cursor.WriteAddress(packet.address);
            cursor.WriteU16(packet.mtu, Endian.Big);
            cursor.WriteU64(packet.guid, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static OpenConnectionReply1 ReadPacketConnectionOpenReply1(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new OpenConnectionReply1
            {
                magic = cursor.ReadMagic(),
                guid = cursor.ReadU64(Endian.Big),
                use_encryption = cursor.ReadU8(),
                mtu_size = cursor.ReadU16(Endian.Big),
            };
        }

        public static byte[] WritePacketConnectionOpenReply1(OpenConnectionReply1 packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.OpenConnectionReply1);
            cursor.WriteMagic();
            cursor.WriteU64(packet.guid, Endian.Big);
            cursor.WriteU8(packet.use_encryption);
            cursor.WriteU16(packet.mtu_size, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static OpenConnectionReply2 ReadPacketConnectionOpenReply2(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new OpenConnectionReply2
            {
                magic = cursor.ReadMagic(),
                guid = cursor.ReadU64(Endian.Big),
                address = cursor.ReadAddress(),
                mtu = cursor.ReadU16(Endian.Big),
                encryption_enabled = cursor.ReadU8(),
            };
        }

        public static byte[] WritePacketConnectionOpenReply2(OpenConnectionReply2 packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.OpenConnectionReply2);
            cursor.WriteMagic();
            cursor.WriteU64(packet.guid, Endian.Big);
            cursor.WriteAddress(packet.address);
            cursor.WriteU16(packet.mtu, Endian.Big);
            cursor.WriteU8(packet.encryption_enabled);
            return cursor.GetRawPayload();
        }

        public static AlreadyConnected ReadPacketAlreadyConnected(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new AlreadyConnected
            {
                magic = cursor.ReadMagic(),
                guid = cursor.ReadU64(Endian.Big),
            };
        }

        public static byte[] WritePacketAlreadyConnected(AlreadyConnected packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.AlreadyConnected);
            cursor.WriteMagic();
            cursor.WriteU64(packet.guid, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static IncompatibleProtocolVersion ReadPacketIncompatibleProtocolVersion(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new IncompatibleProtocolVersion
            {
                server_protocol = cursor.ReadU8(),
                magic = cursor.ReadMagic(),
                server_guid = cursor.ReadU64(Endian.Big),
            };
        }

        public static byte[] WriteIncompatibleProtocolVersion(IncompatibleProtocolVersion packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.IncompatibleProtocolVersion);
            cursor.WriteU8(packet.server_protocol);
            cursor.WriteMagic();
            cursor.WriteU64(packet.server_guid, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static Nack ReadPacketNack(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            var record_count = cursor.ReadU16(Endian.Big);
            var sequences = cursor.ReadSequences(record_count);
            return new Nack
            {
                record_count = record_count,
                sequences = sequences,
            };
        }

        public static byte[] WritePacketNack(Nack packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.Nack);
            cursor.WriteSequences(packet.sequences);
            return cursor.GetRawPayload();
        }

        public static Ack ReadPacketAck(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            var record_count = cursor.ReadU16(Endian.Big);
            var sequences = cursor.ReadSequences(record_count);

            return new Ack
            {
                record_count = record_count,
                sequences = sequences,
            };
        }

        public static byte[] WritePacketAck(Ack packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.Ack);
            cursor.WriteSequences(packet.sequences);
            return cursor.GetRawPayload();
        }

        public static ConnectionRequest ReadPacketConnectionRequest(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new ConnectionRequest
            {
                guid = cursor.ReadU64(Endian.Big),
                time = cursor.ReadI64(Endian.Big),
                use_encryption = cursor.ReadU8(),
            };
        }

        public static byte[] WritePacketConnectionRequest(ConnectionRequest packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.ConnectionRequest);
            cursor.WriteU64(packet.guid, Endian.Big);
            cursor.WriteI64(packet.time, Endian.Big);
            cursor.WriteU8(packet.use_encryption);
            return cursor.GetRawPayload();
        }

        public static ConnectionRequestAccepted ReadPacketConnectionRequestAccepted(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new ConnectionRequestAccepted
            {
                client_address = cursor.ReadAddress(),
                system_index = cursor.ReadU16(Endian.Big),
                request_timestamp = 0,
                accepted_timestamp = 0,
            };
        }

        public static byte[] WritePacketConnectionRequestAccepted(ConnectionRequestAccepted packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.ConnectionRequestAccepted);
            cursor.WriteAddress(packet.client_address);
            cursor.WriteU16(packet.system_index, Endian.Big);
            for (int i = 0; i < 10; i++)
            {
                IPEndPoint tmpEndpoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 19132);
                cursor.WriteAddress(tmpEndpoint);
            }

            cursor.WriteI64(packet.request_timestamp, Endian.Big);
            cursor.WriteI64(packet.request_timestamp, Endian.Big);

            return cursor.GetRawPayload();
        }

        public static NewIncomingConnection ReadPacketNewIncomingConnection(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            NewIncomingConnection packet = new NewIncomingConnection
            {
                server_address = cursor.ReadAddress(),
            };
            for (int i = 0; i < 10; i++)
            {
                cursor.ReadAddress();
            }
            packet.request_timestamp = cursor.ReadI64(Endian.Big);
            packet.accepted_timestamp = cursor.ReadI64(Endian.Big);
            return packet;
        }

        public static byte[] WritePacketNewIncomingConnection(NewIncomingConnection packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.NewIncomingConnection);
            cursor.WriteAddress(packet.server_address);
            IPEndPoint tmpAddress = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0);
            for (int i = 0; i < 10; i++)
            {
                cursor.WriteAddress(tmpAddress);
            }
            cursor.WriteI64(packet.request_timestamp, Endian.Big);
            cursor.WriteI64(packet.accepted_timestamp, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static ConnectedPing ReadPacketConnectedPing(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new ConnectedPing
            {
                client_timestamp = cursor.ReadI64(Endian.Big),
            };
        }

        public static byte[] WritePacketConnectedPing(ConnectedPing packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.ConnectedPing);
            cursor.WriteI64(packet.client_timestamp, Endian.Big);
            return cursor.GetRawPayload();
        }

        public static ConnectedPong ReadPacketConnectedPong(byte[] buf)
        {
            RaknetReader cursor = new RaknetReader(buf);
            cursor.ReadU8();
            return new ConnectedPong
            {
                client_timestamp = cursor.ReadI64(Endian.Big),
                server_timestamp = cursor.ReadI64(Endian.Big),
            };
        }

        public static byte[] WritePacketConnectedPong(ConnectedPong packet)
        {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.ConnectedPong);
            cursor.WriteI64(packet.client_timestamp, Endian.Big);
            cursor.WriteI64(packet.server_timestamp, Endian.Big);
            return cursor.GetRawPayload();
        }
    }

    public enum PacketID
    {
        ConnectedPing = 0x00,
        UnconnectedPing1 = 0x01,
        UnconnectedPing2 = 0x02,
        ConnectedPong = 0x03,
        UnconnectedPong = 0x1c,
        OpenConnectionRequest1 = 0x05,
        OpenConnectionReply1 = 0x06,
        OpenConnectionRequest2 = 0x07,
        OpenConnectionReply2 = 0x08,
        ConnectionRequest = 0x09,
        ConnectionRequestAccepted = 0x10,
        AlreadyConnected = 0x12,
        NewIncomingConnection = 0x13,
        Disconnect = 0x15,
        IncompatibleProtocolVersion = 0x19,
        FrameSetPacketBegin = 0x80,
        FrameSetPacketEnd = 0x8d,
        Nack = 0xa0,
        Ack = 0xc0,
        Game = 0xfe,
    }

    public class ConnectedPing
    {
        public long client_timestamp { get; set; }
    }

    public class PacketUnconnectedPing
    {
        public long time { get; set; }
        public bool magic { get; set; }
        public ulong guid { get; set; }
    }

    public class PacketUnconnectedPong
    {
        public long time { get; set; }
        public ulong guid { get; set; }
        public bool magic { get; set; }
        public string motd { get; set; }
    }

    public class ConnectedPong
    {
        public long client_timestamp { get; set; }
        public long server_timestamp { get; set; }
    }

    public class OpenConnectionRequest1
    {
        public bool magic { get; set; }
        public byte protocol_version { get; set; }
        public ushort mtu_size { get; set; }
    }

    public class OpenConnectionRequest2
    {
        public bool magic { get; set; }
        public IPEndPoint address { get; set; }
        public ushort mtu { get; set; }
        public ulong guid { get; set; }
    }

    public class OpenConnectionReply1
    {
        public bool magic { get; set; }
        public ulong guid { get; set; }
        public byte use_encryption { get; set; }
        public ushort mtu_size { get; set; }
    }

    public class OpenConnectionReply2
    {
        public bool magic { get; set; }
        public ulong guid { get; set; }
        public IPEndPoint address { get; set; }
        public ushort mtu { get; set; }
        public byte encryption_enabled { get; set; }
    }

    public class ConnectionRequest
    {
        public ulong guid { get; set; }
        public long time { get; set; }
        public byte use_encryption { get; set; }
    }

    public class ConnectionRequestAccepted
    {
        public IPEndPoint client_address { get; set; }
        public ushort system_index { get; set; }
        public long request_timestamp { get; set; }
        public long accepted_timestamp { get; set; }
    }

    public class NewIncomingConnection
    {
        public IPEndPoint server_address { get; set; }
        public long request_timestamp { get; set; }
        public long accepted_timestamp { get; set; }
    }

    public class IncompatibleProtocolVersion
    {
        public byte server_protocol { get; set; }
        public bool magic { get; set; }
        public ulong server_guid { get; set; }
    }

    public class AlreadyConnected
    {
        public bool magic { get; set; }
        public ulong guid { get; set; }
    }

    public class Nack
    {
        public ushort record_count { get; set; }
        public List<AckRange> sequences { get; set; }
    }

    public class Ack
    {
        public ushort record_count { get; set; }
        public List<AckRange> sequences { get; set; }
    }
}
