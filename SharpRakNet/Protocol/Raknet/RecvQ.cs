using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SharpRakNet.Protocol.Raknet
{
    public class RecvQ
    {
        private uint sequenced_frame_index;
        private uint last_ordered_index;
        private ACKSet sequence_number_ackset;
        private Dictionary<uint, FrameSetPacket> packets;
        private Dictionary<uint, FrameSetPacket> ordered_packets;
        private FragmentQ fragment_queue;

        private readonly object packetsLock = new object();
        private readonly object orderedPacketsLock = new object();
        private readonly object sequenceAcksetLock = new object();
        private readonly object fragmentQueueLock = new object();

        public RecvQ()
        {
            sequence_number_ackset = new ACKSet();
            packets = new Dictionary<uint, FrameSetPacket>();
            ordered_packets = new Dictionary<uint, FrameSetPacket>();
            fragment_queue = new FragmentQ();
            sequenced_frame_index = 0;
            last_ordered_index = 0;
        }

        public void Insert(FrameSetPacket frame)
        {
            lock (packetsLock)
            {
                if (packets.ContainsKey(frame.sequence_number))
                {
                    return;
                }
            }
            lock (sequenceAcksetLock)
            {
                sequence_number_ackset.Insert(frame.sequence_number);
            }


            switch (frame.GetReliability())
            {
                case Reliability.Unreliable:
                    lock (packetsLock)
                        packets[frame.sequence_number] = frame;
                    break;
                case Reliability.UnreliableSequenced:
                    uint sequenced_frame_index = frame.sequenced_frame_index;
                    if (sequenced_frame_index >= this.sequenced_frame_index)
                    {
                        if (!packets.ContainsKey(frame.sequence_number))
                        {
                            lock (packetsLock)
                                packets[frame.sequence_number] = frame;
                            this.sequenced_frame_index++;
                        }
                    }
                    break;
                case Reliability.Reliable:
                    lock (packetsLock)
                        packets[frame.sequence_number] = frame;
                    break;
                case Reliability.ReliableOrdered:
                    if (frame.ordered_frame_index < last_ordered_index)
                    {
                        return;
                    }

                    if (frame.IsFragment())
                    {
                        lock (fragmentQueueLock)
                            fragment_queue.Insert(frame);

                        foreach (FrameSetPacket i in fragment_queue.Flush())
                        {
                            lock (orderedPacketsLock)
                                ordered_packets[i.ordered_frame_index] = i;
                        }
                    }
                    else
                    {
                        lock (orderedPacketsLock)
                            ordered_packets[frame.ordered_frame_index] = frame;
                    }
                    break;
                case Reliability.ReliableSequenced:
                    sequenced_frame_index = frame.sequenced_frame_index;
                    if (sequenced_frame_index >= this.sequenced_frame_index)
                    {
                        if (!packets.ContainsKey(frame.sequence_number))
                        {
                            lock (packetsLock)
                                packets[frame.sequence_number] = frame;
                            this.sequenced_frame_index = sequenced_frame_index + 1;
                        }
                    }
                    break;
            }
        }

        public List<AckRange> GetAck()
        {
            lock (sequenceAcksetLock)
                return sequence_number_ackset.GetAck();
        }

        public List<AckRange> GetNack()
        {
            lock (sequenceAcksetLock)
                return sequence_number_ackset.GetNack();
        }

        public List<FrameSetPacket> Flush(IPEndPoint peerAddr)
        {
            List<FrameSetPacket> ret = new List<FrameSetPacket>();
            lock (orderedPacketsLock)
            {
                List<uint> orderedKeys = ordered_packets.Keys.ToList();
                orderedKeys.Sort();
                foreach (uint i in orderedKeys)
                {
                    if (i == last_ordered_index)
                    {
                        FrameSetPacket frame = ordered_packets[i];
                        ret.Add(frame);
                        ordered_packets.Remove(i);
                        last_ordered_index = i + 1;
                    }
                }
            }

            lock (packetsLock)
            {
                List<uint> packetsKeys = packets.Keys.ToList();
                packetsKeys.Sort();

                foreach (uint i in packetsKeys)
                {
                    FrameSetPacket v = packets[i];
                    ret.Add(v);
                }

                packets.Clear();
            }

            return ret;
        }

        public int GetOrderedPacketCount()
        {
            lock (orderedPacketsLock)
                return ordered_packets.Count;
        }

        public int GetFragmentQueueSize()
        {
            lock (fragmentQueueLock)
                return fragment_queue.Size();
        }

        public List<uint> GetOrderedKeys()
        {
            lock (orderedPacketsLock)
                return ordered_packets.Keys.ToList();
        }

        public int GetSize()
        {
            lock (packetsLock)
                return packets.Count;
        }
    }

}
