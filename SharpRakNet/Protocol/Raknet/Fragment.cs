using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpRakNet.Protocol.Raknet
{
    public class Fragment
    {
        public byte flags { get; set; }
        public uint compound_size { get; set; }
        public uint ordered_frame_index { get; set; }
        public Dictionary<uint, FrameSetPacket> frames { get; set; }

        public Fragment(byte flags, uint compound_size, uint ordered_frame_index)
        {
            this.flags = flags;
            this.compound_size = compound_size;
            this.ordered_frame_index = ordered_frame_index;
            frames = new Dictionary<uint, FrameSetPacket>();
        }

        public bool Full()
        {
            return frames.Count == compound_size;
        }

        public void Insert(FrameSetPacket frame)
        {
            if (Full() || frames.ContainsKey(frame.fragment_index))
            {
                return;
            }

            frames[frame.fragment_index] = frame;
        }

        public FrameSetPacket Merge()
        {
            if (!Full())
            {
                throw new InvalidOperationException("Cannot merge fragments until the fragment is full.");
            }

            List<uint> keys = frames.Keys.ToList();
            keys.Sort();

            uint sequence_number = frames[keys.Last()].sequence_number;
            List<byte> buffer = new List<byte>();

            foreach (uint key in keys)
            {
                buffer.AddRange(frames[key].data);
            }

            FrameSetPacket ret = new FrameSetPacket
            {
                id = 0,
                sequence_number = sequence_number,
                flags = 0,
                length_in_bytes = (ushort)(buffer.Count * 8),
                reliable_frame_index = 0,
                sequenced_frame_index = 0,
                ordered_frame_index = ordered_frame_index,
                order_channel = 0,
                compound_size = 0,
                compound_id = 0,
                fragment_index = 0,
                data = buffer.ToArray()
            };

            ret.flags = (byte)(flags & 224);
            return ret;
        }
    }

    public class FragmentQ
    {
        private Dictionary<ushort, Fragment> fragments;

        public FragmentQ()
        {
            fragments = new Dictionary<ushort, Fragment>();
        }

        public void Insert(FrameSetPacket frame)
        {
            lock (fragments)
            {
                if (fragments.ContainsKey(frame.compound_id))
                {
                    fragments[frame.compound_id].Insert(frame);
                }
                else
                {
                    Fragment v = new Fragment(frame.flags, frame.compound_size, frame.ordered_frame_index);
                    ushort k = frame.compound_id;
                    v.Insert(frame);
                    fragments[k] = v;
                }
            }
        }

        public List<FrameSetPacket> Flush()
        {
            List<FrameSetPacket> ret = new List<FrameSetPacket>();

            List<ushort> keys = fragments.Keys.ToList();

            lock (fragments)
            {
                foreach (ushort i in keys)
                {
                    Fragment a = fragments[i];
                    if (a.Full())
                    {
                        ret.Add(a.Merge());
                        fragments.Remove(i);
                    }
                }
            }
            return ret;
        }

        public int Size()
        {
            return fragments.Count;
        }
    }
}
