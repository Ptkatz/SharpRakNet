using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpRakNet
{
    public class FrameSetPacket
    {
        public byte id;
        public uint sequence_number;
        public byte flags;
        public ushort length_in_bytes;
        public uint reliable_frame_index;
        public uint sequenced_frame_index;
        public uint ordered_frame_index;
        public byte order_channel;
        public uint compound_size;
        public ushort compound_id;
        public uint fragment_index;
        public List<byte> data = new List<byte>();
        
        public FrameSetPacket()
        {
            this.id = 0;
            this.sequence_number = 0;
            this.flags = 0;
            this.length_in_bytes = 0;
            this.reliable_frame_index = 0;
            this.sequenced_frame_index = 0;
            this.ordered_frame_index = 0;
            this.order_channel = 0;
            this.compound_size = 0;
            this.compound_id = 0;
            this.fragment_index = 0;
            this.data = new List<byte>();
        }

        static readonly byte NEEDS_B_AND_AS_FLAG = 0x4;
        static readonly byte CONTINUOUS_SEND_FLAG = 0x8;

        public FrameSetPacket(Reliability r, List<byte> data)
        {
            byte flag = (byte)((byte)r << 5);

            this.id = 0;
            this.sequence_number = 0;
            this.flags = flag;
            this.length_in_bytes = (ushort)data.Count;
            this.reliable_frame_index = 0;
            this.sequenced_frame_index = 0;
            this.ordered_frame_index = 0;
            this.order_channel = 0;
            this.compound_size = 0;
            this.compound_id = 0;
            this.fragment_index = 0;
            this.data = data;
        }

        public static FrameSetPacket Deserialize(byte[] buf)
        {
            RaknetReader reader = new RaknetReader(buf);

            FrameSetPacket ret = new FrameSetPacket();
            ret.id = reader.ReadU8();
            ret.sequence_number = reader.ReadU24(Endian.Little);
            ret.flags = reader.ReadU8();
            ret.length_in_bytes = (ushort)(reader.ReadU16(Endian.Big) / 8);

            if (ret.IsReliable())
            {
                ret.reliable_frame_index = reader.ReadU24(Endian.Little);
            }

            if (ret.IsSequenced())
            {
                ret.sequenced_frame_index = reader.ReadU24(Endian.Little);
            }
            if (ret.IsOrdered())
            {
                ret.ordered_frame_index = reader.ReadU24(Endian.Little);
                ret.order_channel = reader.ReadU8();
            }

            if ((ret.flags & 16) != 0)
            {
                ret.compound_size = reader.ReadU32(Endian.Big);
                ret.compound_id = reader.ReadU16(Endian.Big);
                ret.fragment_index = reader.ReadU32(Endian.Big);
            }

            byte[] buffer = reader.Read(ret.length_in_bytes);
            ret.data = new List<byte>(buffer);
            
            return ret;
        }

        public byte[] Serialize()
        {
            RaknetWriter writer = new RaknetWriter();

            byte id = (byte)(0x80 | NEEDS_B_AND_AS_FLAG);

            if ((flags & 16) != 0 && fragment_index != 0)
            {
                id |= NEEDS_B_AND_AS_FLAG;
            }

            writer.WriteU8(id);
            writer.WriteU24(sequence_number, Endian.Little);
            writer.WriteU8(flags);
            writer.WriteU16((ushort)(length_in_bytes * 8), Endian.Big);

            if (IsReliable())
            {
                writer.WriteU24(reliable_frame_index, Endian.Little);
            }

            if (IsSequenced())
            {
                writer.WriteU24(sequenced_frame_index, Endian.Little);
            }
            if (IsOrdered())
            {
                writer.WriteU24(ordered_frame_index, Endian.Little);
                writer.WriteU8(order_channel);
            }

            if ((flags & 16) != 0)
            {
                writer.WriteU32(compound_size, Endian.Big);
                writer.WriteU16(compound_id, Endian.Big);
                writer.WriteU32(fragment_index, Endian.Big);
            }
            writer.Write(data.ToArray());

            return writer.GetRawPayload();
        }

        public bool IsFragment()
        {
            return (flags & 16) != 0;
        }

        public bool IsReliable()
        {
            Reliability r = (Reliability)((flags & 224) >> 5);
            return r == Reliability.Reliable || r == Reliability.ReliableOrdered || r == Reliability.ReliableSequenced;
        }

        public bool IsOrdered()
        {
            Reliability r = (Reliability)((flags & 224) >> 5);
            return r == Reliability.UnreliableSequenced || r == Reliability.ReliableOrdered || r == Reliability.ReliableSequenced;
        }

        public bool IsSequenced()
        {
            Reliability r = (Reliability)((flags & 224) >> 5);
            return r == Reliability.UnreliableSequenced || r == Reliability.ReliableSequenced;
        }

        public Reliability GetReliability()
        {
            return (Reliability)((flags & 224) >> 5);
        }

        public int Size()
        {
            int ret = 0;
            ret += 1; // id
            ret += 3; // sequence number
            ret += 1; // flags
            ret += 2; // length_in_bits

            if (IsReliable())
            {
                ret += 3; // reliable frame index
            }
            if (IsSequenced())
            {
                ret += 3; // sequenced frame index
            }
            if (IsOrdered())
            {
                ret += 4; // ordered frame index + order channel
            }
            if ((flags & 16) != 0)
            {
                ret += 10; // compound size + compound id + fragment index
            }
            ret += data.Count; // body
            return ret;
        }
    }
}
