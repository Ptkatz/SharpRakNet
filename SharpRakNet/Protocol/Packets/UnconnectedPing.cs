using SharpRakNet.Protocol.Raknet;

namespace SharpRakNet.Protocol.Packets {

    [RegisterPacketID(1)]
    public class UnconnectedPing : Packet {
        public long time;
        public bool magic;
        public ulong guid;

        public UnconnectedPing(byte[] buffer) : base(buffer) {}
        public UnconnectedPing(long time, bool magic, ulong guid) : base(new byte[] {}) {
            this.time = time;
            this.magic = magic;
            this.guid = guid;

            Buffer = Serialize();
        }

        public override byte[] Serialize() {
            RaknetWriter stream = new RaknetWriter();
            stream.WriteU8(PacketID.UnconnectedPing1);
            stream.WriteI64(time, Endian.Big);
            stream.WriteMagic();
            stream.WriteU64(guid, Endian.Big);

            return stream.GetRawPayload();
        }

        public override void Deserialize()
        {
            if (Buffer == null) throw new System.Exception("Buffer is not present idk");

            RaknetReader stream = new RaknetReader(Buffer);
            stream.ReadU8();

            time = stream.ReadI64(Endian.Big);
            magic = stream.ReadMagic();
            guid = stream.ReadU64(Endian.Big);
        }
    }
}