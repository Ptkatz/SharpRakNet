using SharpRakNet.Protocol.Packets;
using SharpRakNet.Protocol.Raknet;
using SharpRakNet.Network;

using System.Threading;
using System.Net;
using System;

namespace RaknetClientTest
{
    internal class Program
    {
        private static RaknetClient client;
        private static string Path;

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Path = @"C:\Users\Administrator\Desktop\Client.exe";
            }
            else
            {
                Path = args[0];
            }

            client = new RaknetClient();
            client.SessionEstablished += OnSessionEstablished;

            client.BeginConnection(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 19132));

            Console.ReadKey();
        }

        static void OnSessionEstablished(RaknetSession session)
        {


            //var b = File.ReadAllBytes(Path);
            Console.WriteLine("OnSessionEstablished");
            session.SessionDisconnected += OnDisconnected;
            session.SessionReceiveRaw += OnReceive;
            Thread.Sleep(1000);

            UnconnectedPing packet = new UnconnectedPing(
                4,
                true,
                3
            );

            session.Sendq.Insert(Reliability.Unreliable, packet.Serialize());

            //session.Sendq.Insert(Reliability.ReliableOrdered, b);
        }

        static void OnDisconnected(RaknetSession session)
        {
            Console.WriteLine(session.PeerEndPoint);
        }

        static void OnReceive(IPEndPoint source, byte[] buf)
        {
            PrintBytes(buf);
        }

        public static void PrintBytes(byte[] byteArray)
        {
            Console.Write("[");
            foreach (byte b in byteArray)
            {
                Console.Write(b + " ");
            }
            Console.Write("]\n");
        }
    }
}
