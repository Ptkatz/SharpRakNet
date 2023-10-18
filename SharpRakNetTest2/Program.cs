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
        static string Path;
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
            RaknetClient socket = new RaknetClient();
            socket.SessionEstablished += OnSessionEstablished;

            socket.BeginConnection(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 19132));

            Console.ReadKey();
        }

        static void OnSessionEstablished(RaknetSession session)
        {
            var b = File.ReadAllBytes(Path);
            Console.WriteLine("OnSessionEstablished");
            session.SessionDisconnected += OnDisconnected;
            session.SessionReceive += OnReceive;
            Thread.Sleep(1000);
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
