using SharpRakNet.Protocol.Raknet;

namespace SharpRakNet.Protocol.Packets {
    public class UnconnectedPing : Packet {
        public static new int ID = 0x01;

        public long time;
        public bool magic;
        public ulong guid;

        public UnconnectedPing() : base(new byte[] {}) {}
        public UnconnectedPing(byte[] buffer) : base(buffer) {}
        public UnconnectedPing(long time, bool magic, ulong guid) : base(new byte[] {}) {
            this.time = time;
            this.magic = magic;
            this.guid = guid;

            Buffer = Serialize();
        }

        public override byte[] Serialize() {
            RaknetWriter cursor = new RaknetWriter();
            cursor.WriteU8(PacketID.UnconnectedPing1.ToU8());
            cursor.WriteI64(time, Endian.Big);
            cursor.WriteMagic();
            cursor.WriteU64(guid, Endian.Big);

            return cursor.GetRawPayload();
        }
    }
}