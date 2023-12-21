using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SharpRakNet.Protocol.Raknet
{
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

        private readonly object sentPacketLock = new object();
        private readonly object packetsLock = new object();
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

        public void Insert(Reliability reliability, byte[] buffer)
        {
            switch (reliability)
            {
                case Reliability.Unreliable:
                    if (buffer.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket frame = new FrameSetPacket(reliability, buffer);
                    lock (packetsLock)
                        packets.Add(frame);
                    break;
                case Reliability.UnreliableSequenced:
                    if (buffer.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket sequencedFrame = new FrameSetPacket(reliability, buffer);
                    sequencedFrame.ordered_frame_index = ordered_frame_index;
                    sequencedFrame.sequenced_frame_index = sequenced_frame_index;
                    lock (packetsLock)
                        packets.Add(sequencedFrame);
                    sequenced_frame_index++;
                    break;
                case Reliability.Reliable:
                    if (buffer.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket reliableFrame = new FrameSetPacket(reliability, buffer);
                    reliableFrame.reliable_frame_index = reliable_frame_index;
                    lock (packetsLock)
                        packets.Add(reliableFrame);
                    reliable_frame_index++;
                    break;
                case Reliability.ReliableOrdered:
                    if (buffer.Length < (mtu - 60))
                    {
                        FrameSetPacket orderedFrame = new FrameSetPacket(reliability, buffer);
                        orderedFrame.reliable_frame_index = reliable_frame_index;
                        orderedFrame.ordered_frame_index = ordered_frame_index;
                        lock (packetsLock)
                            packets.Add(orderedFrame);
                        reliable_frame_index++;
                        ordered_frame_index++;
                    }
                    else
                    {
                        int max = (mtu - 60);
                        int compoundSize = buffer.Length / max;
                        if (buffer.Length % max != 0)
                        {
                            compoundSize++;
                        }

                        for (int i = 0; i < compoundSize; i++)
                        {
                            int begin = (max * i);
                            int end = (i == compoundSize - 1) ? buffer.Length : (max * (i + 1));
                            FrameSetPacket compoundFrame = new FrameSetPacket(reliability, new List<byte>(buffer).GetRange(begin, end - begin).ToArray());
                            compoundFrame.flags |= 16;
                            compoundFrame.compound_size = (uint)compoundSize;
                            compoundFrame.compound_id = compound_id;
                            compoundFrame.fragment_index = (uint)i;
                            compoundFrame.reliable_frame_index = reliable_frame_index;
                            compoundFrame.ordered_frame_index = ordered_frame_index;
                            lock (packetsLock)
                                packets.Add(compoundFrame);
                            reliable_frame_index++;
                        }
                        compound_id++;
                        ordered_frame_index++;
                    }
                    break;
                case Reliability.ReliableSequenced:
                    if (buffer.Length > (mtu - 60))
                    {
                        throw new RaknetError("PacketSizeExceedMTU");
                    }
                    FrameSetPacket reliableSequencedFrame = new FrameSetPacket(reliability, buffer);
                    reliableSequencedFrame.reliable_frame_index = reliable_frame_index;
                    reliableSequencedFrame.sequenced_frame_index = sequenced_frame_index;
                    reliableSequencedFrame.ordered_frame_index = ordered_frame_index;
                    lock (packetsLock)
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

            lock (sent_packet)
            {
                for (int i = 0; i < sent_packet.Count; i++)
                {
                    var item = sent_packet[i];
                    if (item.Packet.sequence_number == sequence || item.NackedSequenceNumbers.Contains(sequence))
                    {
                        rtts.Add(tick - item.Timestamp);
                        lock (sentPacketLock)
                            sent_packet.RemoveAt(i);
                        break;
                    }
                }
            }

            foreach (long rtt in rtts)
            {
                UpdateRTO(rtt);
            }
        }

        private void Tick(long tick)
        {
            lock (sent_packet)
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
            lock (sentPacketLock)
            {
                if (sent_packet.Count > 0)
                {
                    var sortedSentPackets = sent_packet.Where(item => item.Packet != null).ToList();
                    sortedSentPackets.Sort((x, y) => x.Packet.sequence_number.CompareTo(y.Packet.sequence_number));

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
            }
            lock (packetsLock)
            {
                if (packets.Count > 0)
                {
                    foreach (FrameSetPacket packet in packets)
                    {
                        lock (sentPacketLock)
                        {
                            packet.sequence_number = sequence_number;
                            sequence_number++;
                            ret.Add(packet);
                            if (packet.IsReliable())
                            {
                                sent_packet.Add(new SentPacketInfo(packet, true, tick, 0, new List<uint> { packet.sequence_number }));
                            }
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