using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Linq;

namespace SubnetCalculator
{
    public class IPv6Network
    {
        public IPAddress Network { get; }
        public int Cidr { get; }
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
                throw new FormatException("Invalid IPv6 address.");

            int prefix = parts.Length > 1 ? int.Parse(parts[1]) : 128;
            if (prefix < 0 || prefix > 128)
                throw new FormatException("Invalid prefix.");

            BigInteger netBig = IPToBigInt(ip) & PrefixToMask(prefix);
            return new IPv6Network(BigIntToIp(netBig), prefix);
        }

        public IEnumerable<IPv6Network> Subnet(int newPrefix)
        {
            if (newPrefix < Cidr || newPrefix < 0 || newPrefix > 128)
                throw new ArgumentException("newPrefix must be between current prefix and 128");

            BigInteger blocks = BigInteger.One << (newPrefix - Cidr);
            BigInteger size = BigInteger.One << (128 - newPrefix);
            BigInteger start = IPToBigInt(Network);
            for (BigInteger i = BigInteger.Zero; i < blocks; i++)
            {
                yield return new IPv6Network(BigIntToIp(start + size * i), newPrefix);
            }
        }

        public static BigInteger IPToBigInt(IPAddress ip)
            => new BigInteger(ip.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

        public static IPAddress BigIntToIp(BigInteger v)
        {
            byte[] bytes = v.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length < 16)
                bytes = Enumerable.Repeat((byte)0, 16 - bytes.Length).Concat(bytes).ToArray();
            return new IPAddress(bytes);
        }

        public static BigInteger PrefixToMask(int p)
        {
            if (p == 0) return BigInteger.Zero;
            return ((BigInteger.One << p) - BigInteger.One) << (128 - p);
        }
    }
}
