using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpRakNet.Protocol.Raknet
{
    public class RaknetError : Exception
    {
        public RaknetError(string message) : base(message)
        {
        }
    }
}
