using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpRakNet.Protocol.Raknet
{
    public enum Reliability : byte
    {
        Unreliable = 0x00,
        UnreliableSequenced = 0x01,
        Reliable = 0x02,
        ReliableOrdered = 0x03,
        ReliableSequenced = 0x04,
    }

    public static class ReliabilityExtensions
    {
        public static byte ToByte(this Reliability reliability)
        {
            return (byte)reliability;
        }

        public static Reliability FromByte(byte flags)
        {
            switch (flags)
            {
                case 0x00:
                    return Reliability.Unreliable;
                case 0x01:
                    return Reliability.UnreliableSequenced;
                case 0x02:
                    return Reliability.Reliable;
                case 0x03:
                    return Reliability.ReliableOrdered;
                case 0x04:
                    return Reliability.ReliableSequenced;
                default:
                    throw new RankException("IncorrectReliability");
            }
        }
    }
}
