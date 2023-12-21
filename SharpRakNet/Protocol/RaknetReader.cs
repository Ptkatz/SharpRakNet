using SharpRakNet.Protocol.Raknet;

using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net;
using System.IO;
using System;

namespace SharpRakNet
{
    public class RaknetReader
    {
        private MemoryStream bufStream;
        private BinaryReader reader;

        public RaknetReader(byte[] buf)
        {
            bufStream = new MemoryStream(buf);
            reader = new BinaryReader(bufStream);
        }

        public byte[] Read(int count)
        {
            byte[] data = reader.ReadBytes(count);
            if (data.Length != count)
            {
                throw new RaknetError("ReadPacketBufferError");
            }
            return data;
        }

        public byte ReadU8()
        {
            return reader.ReadByte();
        }

        public ushort ReadU16(Endian n)
        {
            if (bufStream.Length - bufStream.Position < 2)
            {
                throw new RaknetError("ReadPacketBufferError");
            }

            if (n == Endian.Big)
            {
                byte[] bytes = reader.ReadBytes(2);
                Array.Reverse(bytes);
                return BitConverter.ToUInt16(bytes, 0);
            }
            else
            {
                return reader.ReadUInt16();
            }
        }

        public uint ReadU24(Endian n)
        {
            if (bufStream.Length - bufStream.Position < 3)
            {
                throw new RaknetError("ReadPacketBufferError");
            }

            byte a = reader.ReadByte();
            byte b = reader.ReadByte();
            byte c = reader.ReadByte();

            if (n == Endian.Big)
            {
                return (uint)(a << 16 | b << 8 | c);
            }
            else
            {
                return (uint)(c << 16 | b << 8 | a);
            }
        }

        public uint ReadU32(Endian n)
        {
            if (bufStream.Length - bufStream.Position < 4)
            {
                throw new RaknetError("ReadPacketBufferError");
            }

            if (n == Endian.Big)
            {
                byte[] bytes = reader.ReadBytes(4);
                Array.Reverse(bytes);
                return BitConverter.ToUInt32(bytes, 0);
            }
            else
            {
                return reader.ReadUInt32();
            }
        }

        public ulong ReadU64(Endian n)
        {
            if (bufStream.Length - bufStream.Position < 8)
            {
                throw new RaknetError("ReadPacketBufferError");
            }

            if (n == Endian.Big)
            {
                byte[] bytes = reader.ReadBytes(8);
                Array.Reverse(bytes);
                return BitConverter.ToUInt64(bytes, 0);
            }
            else
            {
                return reader.ReadUInt64();
            }
        }

        public long ReadI64(Endian n)
        {
            if (bufStream.Length - bufStream.Position < 8)
            {
                throw new RaknetError("ReadPacketBufferError");
            }

            if (n == Endian.Big)
            {
                byte[] bytes = reader.ReadBytes(8);
                Array.Reverse(bytes);
                return BitConverter.ToInt64(bytes, 0);
            }
            else
            {
                return reader.ReadInt64();
            }
        }

        public string ReadString()
        {
            ushort size = ReadU16(Endian.Big);

            if (bufStream.Length - bufStream.Position < size)
            {
                throw new RaknetError("ReadPacketBufferError");
            }

            byte[] strData = reader.ReadBytes(size);
            return Encoding.UTF8.GetString(strData);
        }

        public bool ReadMagic()
        {
            byte[] magic = new byte[16];
            Read(16);

            byte[] offlineMagic = new byte[]
            {
            0x00, 0xff, 0xff, 0x00, 0xfe, 0xfe, 0xfe, 0xfe,
            0xfd, 0xfd, 0xfd, 0xfd, 0x12, 0x34, 0x56, 0x78
            };

            return magic.SequenceEqual(offlineMagic);
        }

        public IPEndPoint ReadAddress()
        {
            byte ipVersion = ReadU8();

            if (ipVersion == 4)
            {
                if (bufStream.Length - bufStream.Position < 6)
                {
                    throw new RaknetError("ReadPacketBufferError");
                }

                byte a = (byte)(0xFF - ReadU8());
                byte b = (byte)(0xFF - ReadU8());
                byte c = (byte)(0xFF - ReadU8());
                byte d = (byte)(0xFF - ReadU8());
                ushort port = ReadU16(Endian.Big);

                IPAddress ip = new IPAddress(new byte[] { a, b, c, d });
                return new IPEndPoint(ip, port);
            }
            else
            {
                if (bufStream.Length - bufStream.Position < 44)
                {
                    throw new RaknetError("ReadPacketBufferError");
                }

                ReadU16(Endian.Big); // Skip 2 bytes
                ushort port = ReadU16(Endian.Big);
                Read(4); // Skip 4 bytes

                byte[] addrData = Read(16);
                IPAddress ip = new IPAddress(addrData);
                return new IPEndPoint(ip, port);
            }
        }

        public List<AckRange> ReadSequences(ushort recordCount)
        {
            List<AckRange> sequences = new List<AckRange>();
            for (ushort i = 0; i < recordCount; i++)
            {
                byte singleSequenceNumber = ReadU8();
                uint sequence = ReadU24(Endian.Little);
                if (singleSequenceNumber == 0x01)
                {
                    sequences.Add(new AckRange(sequence, sequence));
                }
                else
                {
                    uint sequenceMax = ReadU24(Endian.Little);
                    sequences.Add(new AckRange(sequence, sequenceMax));
                }
            }
            return sequences;
        }

        public ulong Position
        {
            get { return (ulong)bufStream.Position; }
        }
    }

}
