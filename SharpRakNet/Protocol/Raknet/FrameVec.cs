using System.Collections.Generic;
using System.Linq;

namespace SharpRakNet.Protocol.Raknet
{
    public class FrameVec
    {
        public byte id { get; set; }
        public uint sequence_number { get; set; }
        public List<FrameSetPacket> frames { get; set; }

        public FrameVec(byte[] buf)
        {
            id = 0;
            sequence_number = 0;
            frames = new List<FrameSetPacket>();

            int size = buf.Length;
            RaknetReader reader = new RaknetReader(buf);

            id = reader.ReadU8();
            sequence_number = reader.ReadU24(Endian.Little);

            while ((int)reader.Position < size)
            {
                FrameSetPacket frame = new FrameSetPacket
                {
                    id = id,
                    sequence_number = sequence_number,
                };
                frame.flags = reader.ReadU8();
                frame.length_in_bytes = (ushort)(reader.ReadU16(Endian.Big) / 8);

                if (frame.IsReliable())
                {
                    frame.reliable_frame_index = reader.ReadU24(Endian.Little);
                }

                if (frame.IsSequenced())
                {
                    frame.sequenced_frame_index = reader.ReadU24(Endian.Little);
                }
                if (frame.IsOrdered())
                {
                    frame.ordered_frame_index = reader.ReadU24(Endian.Little);
                    frame.order_channel = reader.ReadU8();
                }

                if ((frame.flags & 0x10) != 0)
                {
                    frame.compound_size = reader.ReadU32(Endian.Big);
                    frame.compound_id = reader.ReadU16(Endian.Big);
                    frame.fragment_index = reader.ReadU32(Endian.Big);
                }

                frame.data = reader.Read(frame.length_in_bytes);
                frames.Add(frame);
            }
        }

        public byte[] Serialize()
        {
            RaknetWriter writer = new RaknetWriter();
            byte id = (byte)(0x80 | 0x04 | 0x08);

            writer.WriteU8(id);
            writer.WriteU24(sequence_number, Endian.Little);

            foreach (FrameSetPacket frame in frames)
            {
                writer.WriteU8(frame.flags);
                writer.WriteU16((ushort)(frame.length_in_bytes * 8), Endian.Big);

                if (frame.IsReliable())
                {
                    writer.WriteU24(frame.reliable_frame_index, Endian.Little);
                }

                if (frame.IsSequenced())
                {
                    writer.WriteU24(frame.sequenced_frame_index, Endian.Little);
                }
                if (frame.IsOrdered())
                {
                    writer.WriteU24(frame.ordered_frame_index, Endian.Little);
                    writer.WriteU8(frame.order_channel);
                }

                if ((frame.flags & 0x08) != 0)
                {
                    writer.WriteU32(frame.compound_size, Endian.Big);
                    writer.WriteU16(frame.compound_id, Endian.Big);
                    writer.WriteU32(frame.fragment_index, Endian.Big);
                }
                writer.Write(frame.data.ToArray());
            }

            return writer.GetRawPayload();
        }
    }
}
