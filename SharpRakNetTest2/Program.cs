using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SharpRakNet;
using SharpRakNet.Network;

namespace SharpRakNetTest2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            RaknetClient socket = new RaknetClient();
            socket.SessionEstablished += OnSessionEstablished;

            socket.BeginConnection(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 19132));

            Console.ReadKey();
        }

        static void OnSessionEstablished(RaknetSession session)
        {
            var b = File.ReadAllBytes(@"C:\Users\Administrator\Desktop\Client.exe");
            Console.WriteLine("OnSessionEstablished");
            session.SessionDisconnected += OnDisconnected;
            session.SessionReceive += OnReceive;
            session.Sendq.Insert(Reliability.ReliableOrdered, b);
        }

        static void OnDisconnected(RaknetSession session)
        {
            Console.WriteLine(session.PeerEndPoint);
        }
        static void OnReceive(byte[] buf)
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
