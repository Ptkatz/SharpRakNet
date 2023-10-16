using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace SharpRakNet
{
    public class AckRange
    {
        public uint Start { get; set; }
        public uint End { get; set; }

        public AckRange(uint start, uint end)
        {
            Start = start;
            End = end;
        }
    }

    public class ACKSet
    {
        private List<AckRange> ack;
        private List<AckRange> nack;
        private uint last_max;

        public ACKSet()
        {
            ack = new List<AckRange>();
            nack = new List<AckRange>();
            last_max = 0;
        }

        public void Insert(uint s)
        {
            lock (this)
            {
                if (s != 0)
                {
                    if (s > last_max && s != last_max + 1)
                    {
                        nack.Add(new AckRange(last_max + 1, s - 1));
                    }

                    if (s > last_max)
                    {
                        last_max = s;
                    }
                }

                for (int i = 0; i < ack.Count; i++)
                {
                    var a = ack[i];
                    if (a.Start != 0 && s == a.Start - 1)
                    {
                        ack[i] = new AckRange(s, a.End);
                        return;
                    }
                    if (s == a.End + 1)
                    {
                        ack[i] = new AckRange(a.Start, s);
                        return;
                    }
                }
                ack.Add(new AckRange(s, s));
            }
        }

        public List<AckRange> GetAck()
        {
            var ret = new List<AckRange>(ack);
            ack.Clear();
            return ret;
        }

        public List<AckRange> GetNack()
        {
            var ret = new List<AckRange>(nack);
            nack.Clear();
            return ret;
        }
    }

    public class RecvQ
    {
        private uint sequenced_frame_index;
        private uint last_ordered_index;
        private ACKSet sequence_number_ackset;
        private Dictionary<uint, FrameSetPacket> packets;
        private Dictionary<uint, FrameSetPacket> ordered_packets;
        private FragmentQ fragment_queue;

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
            lock (packets)
            {
                if (packets.ContainsKey(frame.sequence_number))
                {
                    return;
                }

                sequence_number_ackset.Insert(frame.sequence_number);

                switch (frame.GetReliability())
                {
                    case Reliability.Unreliable:
                        packets[frame.sequence_number] = frame;
                        break;
                    case Reliability.UnreliableSequenced:
                        uint sequenced_frame_index = frame.sequenced_frame_index;
                        if (sequenced_frame_index >= this.sequenced_frame_index)
                        {
                            if (!packets.ContainsKey(frame.sequence_number))
                            {
                                packets[frame.sequence_number] = frame;
                                sequenced_frame_index++;
                            }
                        }
                        break;
                    case Reliability.Reliable:
                        packets[frame.sequence_number] = frame;
                        break;
                    case Reliability.ReliableOrdered:
                        if (frame.ordered_frame_index < last_ordered_index)
                        {
                            return;
                        }

                        if (frame.IsFragment())
                        {
                            fragment_queue.Insert(frame);

                            foreach (FrameSetPacket i in fragment_queue.Flush())
                            {
                                ordered_packets[i.ordered_frame_index] = i;
                            }
                        }
                        else
                        {
                            ordered_packets[frame.ordered_frame_index] = frame;
                        }
                        break;
                    case Reliability.ReliableSequenced:
                        sequenced_frame_index = frame.sequenced_frame_index;
                        if (sequenced_frame_index >= this.sequenced_frame_index)
                        {
                            if (!packets.ContainsKey(frame.sequence_number))
                            {
                                packets[frame.sequence_number] = frame;
                                sequenced_frame_index++;
                            }
                        }
                        break;
                }
            }
        }

        public List<AckRange> GetAck()
        {
            return sequence_number_ackset.GetAck();
        }

        public List<AckRange> GetNack()
        {
            return sequence_number_ackset.GetNack();
        }

        public List<FrameSetPacket> Flush(IPEndPoint peerAddr)
        {
            List<FrameSetPacket> ret = new List<FrameSetPacket>();
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

            List<uint> packetsKeys = packets.Keys.ToList();
            packetsKeys.Sort();

            foreach (uint i in packetsKeys)
            {
                FrameSetPacket v = packets[i];
                ret.Add(v);
            }

            packets.Clear();
            return ret;
        }

        public int GetOrderedPacketCount()
        {
            return ordered_packets.Count;
        }

        public int GetFragmentQueueSize()
        {
            return fragment_queue.Size();
        }

        public List<uint> GetOrderedKeys()
        {
            return ordered_packets.Keys.ToList();
        }

        public int GetSize()
        {
            return packets.Count;
        }
    }

    public class SendQ
    {
        private ushort mtu;
        private uint ack_sequence_number;
        private uint sequence_number;
        private uint reliable_frame_index;
        private uint sequenced_frame_index;
        private uint ordered_frame_index;
        private ushort compound_id;
        private List<FrameSetPacket> packets;
        private List<SentPacketInfo> sent_packet;
        private long rto;
        private long srtt;

        public const long DEFAULT_TIMEOUT_MILLS = 50;
        private const long RTO_UBOUND = 12000;
        private const long RTO_LBOUND = 50;

        public SendQ(ushort mtu)
        {
            this.mtu = mtu;
            ack_sequence_number = 0;
            sequence_number = 0;
            reliable_frame_index = 0;
            sequenced_frame_index = 0;
            ordered_frame_index = 0;
            compound_id = 0;
            packets = new List<FrameSetPacket>();
            sent_packet = new List<SentPacketInfo>();
            rto = DEFAULT_TIMEOUT_MILLS;
            srtt = DEFAULT_TIMEOUT_MILLS;
        }

        public void Insert(Reliability reliability, byte[] buf)
        {
            switch (reliability)
            {
                case Reliability.Unreliable:
                    if (buf.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket frame = new FrameSetPacket(reliability, new List<byte>(buf));
                    packets.Add(frame);
                    break;
                case Reliability.UnreliableSequenced:
                    if (buf.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket sequencedFrame = new FrameSetPacket(reliability, new List<byte>(buf));
                    sequencedFrame.ordered_frame_index = ordered_frame_index;
                    sequencedFrame.sequenced_frame_index = sequenced_frame_index;
                    packets.Add(sequencedFrame);
                    sequenced_frame_index++;
                    break;
                case Reliability.Reliable:
                    if (buf.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket reliableFrame = new FrameSetPacket(reliability, new List<byte>(buf));
                    reliableFrame.reliable_frame_index = reliable_frame_index;
                    packets.Add(reliableFrame);
                    reliable_frame_index++;
                    break;
                case Reliability.ReliableOrdered:
                    if (buf.Length < (mtu - 60))
                    {
                        FrameSetPacket orderedFrame = new FrameSetPacket(reliability, new List<byte>(buf));
                        orderedFrame.reliable_frame_index = reliable_frame_index;
                        orderedFrame.ordered_frame_index = ordered_frame_index;
                        packets.Add(orderedFrame);
                        reliable_frame_index++;
                        ordered_frame_index++;
                    }
                    else
                    {
                        int max = (mtu - 60);
                        int compoundSize = buf.Length / max;
                        if (buf.Length % max != 0)
                        {
                            compoundSize++;
                        }

                        for (int i = 0; i < compoundSize; i++)
                        {
                            int begin = (max * i);
                            int end = (i == compoundSize - 1) ? buf.Length : (max * (i + 1));
                            FrameSetPacket compoundFrame = new FrameSetPacket(reliability, new List<byte>(new List<byte>(buf).GetRange(begin, end - begin)));
                            compoundFrame.flags |= 16;
                            compoundFrame.compound_size = (uint)compoundSize;
                            compoundFrame.compound_id = compound_id;
                            compoundFrame.fragment_index = (uint)i;
                            compoundFrame.reliable_frame_index = reliable_frame_index;
                            compoundFrame.ordered_frame_index = ordered_frame_index;
                            packets.Add(compoundFrame);
                            reliable_frame_index++;
                        }
                        compound_id++;
                        ordered_frame_index++;
                    }
                    break;
                case Reliability.ReliableSequenced:
                    if (buf.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket reliableSequencedFrame = new FrameSetPacket(reliability, new List<byte>(buf));
                    reliableSequencedFrame.reliable_frame_index = reliable_frame_index;
                    reliableSequencedFrame.sequenced_frame_index = sequenced_frame_index;
                    reliableSequencedFrame.ordered_frame_index = ordered_frame_index;
                    packets.Add(reliableSequencedFrame);
                    reliable_frame_index++;
                    sequenced_frame_index++;
                    break;
            }
        }

        private void UpdateRTO(long rtt)
        {
            srtt = (long)((srtt * 0.8) + (rtt * 0.2));
            long rtoRight = (long)(1.5 * srtt);
            rtoRight = (rtoRight > RTO_LBOUND) ? rtoRight : RTO_LBOUND;
            rto = (rtoRight < RTO_UBOUND) ? rtoRight : RTO_UBOUND;
        }

        public long GetRTO()
        {
            return rto;
        }

        public void Nack(uint sequence, long tick)
        {
            for (int i = 0; i < sent_packet.Count; i++)
            {
                var item = sent_packet[i];
                if (item.Sent && item.Packet.sequence_number == sequence)
                {
                    item.Packet.sequence_number = sequence_number;
                    sequence_number++;
                    item.Timestamp = tick;
                    item.NackCount++;
                    item.NackedSequenceNumbers.Add(item.Packet.sequence_number);
                }
            }
        }

        public void Ack(uint sequence, long tick)
        {
            if (sequence != 0 && sequence != ack_sequence_number + 1)
            {
                for (uint i = ack_sequence_number + 1; i < sequence; i++)
                {
                    Nack(i, tick);
                }
            }

            ack_sequence_number = sequence;
            List<long> rtts = new List<long>();

            for (int i = 0; i < sent_packet.Count; i++)
            {
                var item = sent_packet[i];
                if (item.Packet.sequence_number == sequence || item.NackedSequenceNumbers.Contains(sequence))
                {
                    rtts.Add(tick - item.Timestamp);
                    sent_packet.RemoveAt(i);
                    break;
                }
            }

            foreach (long rtt in rtts)
            {
                UpdateRTO(rtt);
            }
        }

        private void Tick(long tick)
        {
            for (int i = 0; i < sent_packet.Count; i++)
            {
                var p = sent_packet[i];
                long curRto = rto;

                for (int j = 0; j < p.NackCount; j++)
                {
                    curRto = (long)(curRto * 1.5);
                }

                if (p.Sent && tick - p.Timestamp >= curRto)
                {
                    p.Packet.sequence_number = sequence_number;
                    sequence_number++;
                    p.Sent = false;
                    p.NackedSequenceNumbers.Add(p.Packet.sequence_number);
                }
            }
        }

        public List<FrameSetPacket> Flush(long tick, IPEndPoint peerAddr)
        {
            Tick(tick);

            List<FrameSetPacket> ret = new List<FrameSetPacket>();
            lock (packets)
            {
                if (sent_packet.Count > 0)
                {
                    sent_packet.Sort((x, y) => x.Packet.sequence_number.CompareTo(y.Packet.sequence_number));

                    for (int i = 0; i < sent_packet.Count; i++)
                    {
                        var p = sent_packet[i];
                        if (!p.Sent)
                        {
                            ret.Add(p.Packet);
                            p.Sent = true;
                            p.Timestamp = tick;
                            p.NackCount++;
                        }
                    }
                    return ret;
                }

                if (packets.Count > 0)
                {
                    foreach (FrameSetPacket packet in packets)
                    {
                        packet.sequence_number = sequence_number;
                        sequence_number++;
                        ret.Add(packet);
                        if (packet.IsReliable())
                        {
                            sent_packet.Add(new SentPacketInfo(packet, true, tick, 0, new List<uint> { packet.sequence_number }));
                        }
                    }
                    packets.Clear();
                }
            }

            return ret;
        }

        public bool IsEmpty()
        {
            return packets.Count == 0 && sent_packet.Count == 0;
        }

        public int GetReliableQueueSize()
        {
            return packets.Count;
        }

        public int GetSentQueueSize()
        {
            return sent_packet.Count;
        }

        private class SentPacketInfo
        {
            public FrameSetPacket Packet { get; private set; }
            public bool Sent { get; set; }
            public long Timestamp { get; set; }
            public int NackCount { get; set; }
            public List<uint> NackedSequenceNumbers { get; private set; }

            public SentPacketInfo(FrameSetPacket packet, bool sent, long timestamp, int nackCount, List<uint> nackedSequenceNumbers)
            {
                Packet = packet;
                Sent = sent;
                Timestamp = timestamp;
                NackCount = nackCount;
                NackedSequenceNumbers = nackedSequenceNumbers;
            }
        }
    }
}
