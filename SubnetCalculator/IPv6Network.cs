using System;
using System.Collections.Generic;

using System.Linq;
using System.Net;
using System.Numerics;


namespace SubnetCalculator
{
    public class IPv6Network
    {
        public IPAddress Network { get; }
        public int Cidr { get; }


        /*──────── parsing ────────*/

        public IPAddress FirstAddress => Network;
        public IPAddress LastAddress => BigIntToIp(IPToBigInt(Network) + HostCount - BigInteger.One);
        public BigInteger HostCount => BigInteger.One << (128 - Cidr);

        private IPv6Network(IPAddress network, int cidr)
        {
            Network = network;
            Cidr = cidr;
        }


        public static IPv6Network Parse(string s)
        {
            var parts = s.Trim().Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) throw new FormatException("Empty string.");

            IPAddress ip = IPAddress.Parse(parts[0]);
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)

                throw new FormatException("Not an IPv6 address.");
            int prefix;

            if (parts.Length == 2)
            {
                prefix = int.Parse(parts[1]);
            }
            else
            {
                prefix = TightestAlignedPrefix(ip);
            }

            if (prefix < 0 || prefix > 128)
                throw new FormatException("Invalid prefix.");

            BigInteger netBig = IPToBigInteger(ip) & PrefixToMaskBI(prefix);
            return new IPv6Network(BigIntegerToIP(netBig), prefix);
        }

        /*──────── subnetting ─────*/

        public IEnumerable<IPv6Network> Subnet(int newPrefix)
        {
            if (newPrefix < Cidr || newPrefix < 0 || newPrefix > 128)
                throw new ArgumentException("newPrefix must be between current prefix and 128");

            BigInteger blocks = BigInteger.One << (newPrefix - Cidr);
            BigInteger size = BigInteger.One << (128 - newPrefix);
            BigInteger start = IPToBigInteger(Network);
            for (BigInteger i = BigInteger.Zero; i < blocks; i++)
                yield return new IPv6Network(BigIntegerToIP(start + size * i), newPrefix);
        }

        /*──────── helpers ────────*/
        private static int TightestAlignedPrefix(IPAddress ip)
        {
            BigInteger v = IPToBigInteger(ip);
            for (int p = 1; p <= 127; p++)
            {
                BigInteger mask = PrefixToMaskBI(p);
                if ((v & mask) == v)
                    return p;
            }
            return 128;
        }

        public static BigInteger PrefixToMaskBI(int p)
        {
            if (p == 0) return BigInteger.Zero;
            return ((BigInteger.One << p) - BigInteger.One) << (128 - p);
        }

        public static IPAddress BigIntegerToIP(BigInteger v)
        {
            var bytes = v.ToByteArray(isUnsigned: true, isBigEndian: true);
=======


            if (bytes.Length < 16)
                bytes = Enumerable.Repeat((byte)0, 16 - bytes.Length).Concat(bytes).ToArray();
            return new IPAddress(bytes);
        }


        public static BigInteger IPToBigInteger(IPAddress ip)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 16)
                throw new FormatException("Not IPv6");
            return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);

        }
    }
}
