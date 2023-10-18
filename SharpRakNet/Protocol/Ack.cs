using System.Collections.Generic;

public class AckRange
{
    public uint Start { get; set; }
    public uint End { get; set; }

    public AckRange(uint start, uint end)
    {
        Start = start;
        End = end;
    }
}

public class ACKSet
{
    private List<AckRange> ack;
    private List<AckRange> nack;
    private uint last_max;

    public ACKSet()
    {
        ack = new List<AckRange>();
        nack = new List<AckRange>();
        last_max = 0;
    }

    private object ackLock = new object();
    private object nackLock = new object();

    public void Insert(uint s)
    {
        if (s != 0)
        {
            lock (ackLock)
            {
                if (s > last_max && s != last_max + 1)
                {
                    lock (nackLock)
                    {
                        nack.Add(new AckRange(last_max + 1, s - 1));
                    }
                }

                if (s > last_max)
                {
                    last_max = s;
                }
            }

            lock (ackLock)
            {
                if (ack.Count > 0)
                {
                    for (int i = 0; i < ack.Count; i++)
                    {
                        AckRange a = ack[i];
                        if (a != null)
                        {
                            if (a.Start != 0 && s == a.Start - 1)
                            {
                                ack.Insert(i, new AckRange(s, a.End));
                                return;
                            }
                            if (s == a.End + 1)
                            {
                                ack.Insert(i, new AckRange(a.Start, s));
                                return;
                            }
                        }
                    }
                }
                ack.Add(new AckRange(s, s));
            }
        }
    }

    public List<AckRange> GetAck()
    {
        lock (ackLock)
        {
            var ret = new List<AckRange>(ack);
            ack.Clear();
            return ret;
        }
    }

    public List<AckRange> GetNack()
    {
        lock (nackLock)
        {
            var ret = new List<AckRange>(nack);
            nack.Clear();
            return ret;
        }
    }
}