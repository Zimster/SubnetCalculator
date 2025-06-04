using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace SubnetCalculator
{
    public class IPNetwork
    {
        public IPAddress Network { get; }
        public int Cidr { get; }
        public IPAddress Mask => UIntToIp(PrefixToMaskUInt(Cidr));
        public IPAddress Broadcast => UIntToIp(IPToUInt(Network) | ~PrefixToMaskUInt(Cidr));
        public IPAddress FirstHost => Cidr >= 31 ? Network : UIntToIp(IPToUInt(Network) + 1);
        public IPAddress LastHost => Cidr >= 31 ? Network : UIntToIp(IPToUInt(Broadcast) - 1);
        public long Hosts => Cidr >= 31 ? 2 : (1L << (32 - Cidr)) - 2;

        private IPNetwork(IPAddress net, int cidr) { Network = net; Cidr = cidr; }

        /*──────── parsing ────────*/
        public static IPNetwork Parse(string s)
        {
            var parts = s.Trim().Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) throw new FormatException("Empty string.");

            IPAddress ip = IPAddress.Parse(parts[0]);
            int prefix;

            if (parts.Length == 2)                         // user supplied mask or /prefix
            {
                prefix = parts[1].Contains('.')
                         ? MaskStringToPrefix(parts[1])
                         : int.Parse(parts[1]);
            }
            else                                           // no mask → infer tightest
            {
                prefix = TightestAlignedPrefix(ip);
            }

            uint netUInt = IPToUInt(ip) & PrefixToMaskUInt(prefix);
            return new IPNetwork(UIntToIp(netUInt), prefix);
        }

        /*──────── subnetting ─────*/
        public IEnumerable<IPNetwork> Subnet(int newPrefix)
        {
            if (newPrefix < Cidr) throw new ArgumentException("newPrefix must be >= current prefix");
            int blocks = 1 << (newPrefix - Cidr);
            uint size = 1u << (32 - newPrefix);
            uint start = IPToUInt(Network);
            for (int i = 0; i < blocks; i++)
                yield return new IPNetwork(UIntToIp(start + size * (uint)i), newPrefix);
        }

        /*──────── helpers ────────*/
        private static int TightestAlignedPrefix(IPAddress ip)
        {
            uint v = IPToUInt(ip);

            // try /1 … /31  (we leave /32 for the special case no other match)
            for (int p = 1; p <= 31; p++)
            {
                uint mask = PrefixToMaskUInt(p);
                if ((v & mask) == v)     // address is the first IP in that block?
                    return p;            // return the *first* (widest) that works
            }
            return 32;                  // fallback for odd cases
        }

        public static uint PrefixToMaskUInt(int p) => p == 0 ? 0 : uint.MaxValue << (32 - p);
        public static string PrefixToWildcardString(int p) =>
            UIntToIp(~PrefixToMaskUInt(p)).ToString();

        public static int MaskStringToPrefix(string s)
        {
            uint m = IPToUInt(IPAddress.Parse(s));
            for (int p = 32; p >= 0; p--)
                if (m == PrefixToMaskUInt(p)) return p;
            throw new FormatException("Invalid subnet mask.");
        }

        public static uint IPToUInt(IPAddress ip) => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray());
        public static IPAddress UIntToIp(uint v) => new IPAddress(BitConverter.GetBytes(v).Reverse().ToArray());
    }
}
