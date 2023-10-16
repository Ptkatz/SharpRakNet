using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
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

            while (true) { }
        }

        static void OnSessionEstablished(RaknetSession session)
        {
            Console.WriteLine("OnSessionEstablished");
            session.SessionDisconnected += OnDisconnected;
            session.SessionReceive += OnReceive;
            session.SendFrame(new byte[] { 1, 2, 3 }, Reliability.ReliableOrdered);
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
