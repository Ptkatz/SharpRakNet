using SharpRakNet.Protocol.Raknet;

using System.Net.Sockets;
using System.Net;
using System;

namespace SharpRakNet.Network
{
    public class AsyncUdpClient
    {
        public UdpClient Socket;

        public delegate void PacketReceivedDelegate(IPEndPoint address, byte[] packet);
        public PacketReceivedDelegate PacketReceived = delegate { };
        private AsyncCallback recv = null;
        private bool _closed = false;


        public AsyncUdpClient()
        {
            Socket = Common.CreateListener(new IPEndPoint(IPAddress.Any, 0));
        }

        public AsyncUdpClient(IPEndPoint address)
        {
            Socket = Common.CreateListener(address);
        }

        public void Send(IPEndPoint address, byte[] packet)
        {
            try
            {
                Socket?.BeginSend(packet, packet.Length, address, (ar) =>
                {
                    if (_closed) return;
                    
                    UdpClient client = (UdpClient)ar.AsyncState;
                    client.EndSend(ar);
                }, Socket);
            } catch {};
        }

        public void Run()
        {
            IPEndPoint source = new IPEndPoint(0, 0);
            Socket?.BeginReceive(recv = (ar) =>
            {
                if (_closed) return;

                Socket = (UdpClient)ar.AsyncState;
                byte[] receivedData = Socket.EndReceive(ar, ref source);
                Socket.BeginReceive(recv, Socket);

                PacketReceived(source, receivedData);
            }, Socket);
        }

        public void Stop()
        {
            _closed = true;
            Socket?.Close();
        }
    }

}
