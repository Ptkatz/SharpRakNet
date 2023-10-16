using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpRakNet
{
    public class RaknetError : Exception
    {
        public RaknetError(string message) : base(message)
        {
        }
    }
}
