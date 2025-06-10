// MainWindow.xaml.cs  –  COMPLETE FILE

using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls;
using System.Numerics;

namespace SubnetCalculator
{
    public partial class MainWindow : Window
    {
        /*──────────────── data stores ───────────────*/
        private List<IPNetwork> _fixed = new();
        private readonly ObservableCollection<SubnetRequest> _req = new();
        private List<SubnetAllocation> _alloc = new();
        private readonly ObservableCollection<Ipv6SubnetRequest> _req6 = new();
        private List<Ipv6SubnetAllocation> _alloc6 = new();

        public MainWindow()
        {
            InitializeComponent();
            RequestGrid.ItemsSource = _req;
            RequestGrid6.ItemsSource = _req6;
            ApplyTheme(light: false);                         // DARK by default
        }

        /*════════════════  THEME  ════════════════*/
        private void ThemeChanged(object sender, RoutedEventArgs e) =>
            ApplyTheme(light: ThemeCheck.IsChecked == true);

        /*══════════ THEME (add AltBg) ══════════*/
        private static void ApplyTheme(bool light)
        {
            Application.Current.Resources["BgBrush"] =
                new SolidColorBrush(light ? Colors.White : Color.FromRgb(0x1E, 0x1E, 0x1E));
            Application.Current.Resources["CtrlBg"] =
                new SolidColorBrush(light ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x2D, 0x2D, 0x2D));
            Application.Current.Resources["FgBrush"] =
                new SolidColorBrush(light ? Colors.Black : Color.FromRgb(0xE9, 0xE9, 0xE9));
            Application.Current.Resources["SelBg"] =
                new SolidColorBrush(light ? Color.FromRgb(0xC6, 0xD4, 0xE6) : Color.FromRgb(0x44, 0x53, 0x6A));
            Application.Current.Resources["AltBg"] =
                new SolidColorBrush(light ? Color.FromRgb(0xE8, 0xE8, 0xE8) : Color.FromRgb(0x25, 0x25, 0x25));
        }
        /*══════════ CELL COPY handler ══════════*/
        private void OnCopyCell(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dg) return;

            var cell = dg.CurrentCell;                 // value-type, never null
            if (cell.Column == null || cell.Item == null) return;

            if (cell.Column.GetCellContent(cell.Item) is TextBlock tb &&
                !string.IsNullOrEmpty(tb.Text))
            {
                Clipboard.SetText(tb.Text);
            }
        }
            /*════════════════  FIXED-SIZE TAB  ════════════════*/
            private void OnCalculateFixed(object? sender, RoutedEventArgs e) => Guarded(() =>
        {
            int? hostsPerSubnet = TryInt(MinHostsBox.Text);   // PER subnet
            int? minSubnets = TryInt(MinSubnetsBox.Text);

            IPNetwork baseNet = ParseNet(StartNetworkBox.Text);
            int? explicitPref = ParseOptPrefix(MaskBox.Text);
            bool allow31 = Allow31CheckBox.IsChecked == true;

            int pref;

            if (explicitPref is not null)
            {
                pref = Math.Clamp(explicitPref.Value, baseNet.Cidr, allow31 ? 31 : 30);
            }
            else if (hostsPerSubnet is not null)
            {
                pref = 32 - (int)Math.Ceiling(Math.Log2(hostsPerSubnet.Value + 2));
                pref = Math.Clamp(pref, baseNet.Cidr, allow31 ? 31 : 30);
            }
            else
            {
                pref = baseNet.Cidr;
            }

            if (minSubnets is not null && minSubnets > 0)
            {
                int need = baseNet.Cidr + (int)Math.Ceiling(Math.Log2(minSubnets.Value));
                pref = Math.Max(pref, need);
                pref = Math.Clamp(pref, baseNet.Cidr, allow31 ? 31 : 30);
            }

            _fixed = baseNet.Subnet(pref).ToList();

            ResultGridFixed.ItemsSource = _fixed.Select((n, i) => RowFixed(i + 1, n)).ToList();
            SummaryFixed.Text =
                $"Borrowed {pref - baseNet.Cidr} bits – /{pref} – " +
                $"Subnets {_fixed.Count:N0} – Usable Hosts/subnet {_fixed.First().Hosts:N0} - Total Hosts/subnet {_fixed.First().Hosts + 2:N0}";
        });

        private static object RowFixed(int idx, IPNetwork n) => new
        {
            Index = idx,
            Network = n.Network,
            Prefix = "/" + n.Cidr,
            Wildcard = IPNetwork.PrefixToWildcardString(n.Cidr),
            FirstHost = n.FirstHost,
            LastHost = n.LastHost,
            Broadcast = n.Broadcast,
            Mask = n.Mask
        };

        private void OnCopyAclFixed(object s, RoutedEventArgs e) =>
            Guarded(() => CopyAcl(_fixed.Select(n => (n.Network, n.Cidr))));
        private void OnExportCsvFixed(object s, RoutedEventArgs e) =>
            Guarded(() => ExportCsv(_fixed.Select((n, i) => RowFixed(i + 1, n)), "fixed"));
        private void OnExportJsonFixed(object s, RoutedEventArgs e) =>
            Guarded(() => ExportJson(_fixed.Select((n, i) => RowFixed(i + 1, n)), "fixed"));

        /*════════════════  VLSM TAB  ════════════════*/
        private sealed class SubnetRequest
        {
            public string Label { get; set; } = "Site";
            public int? Hosts { get; set; }
            public int? ForcePrefix { get; set; }
            public string Interface { get; set; } = "";
            public string Notes { get; set; } = "";
        }
        private sealed record SubnetAllocation(int Index, string Label, string Interface, IPNetwork Net);

        private sealed class Ipv6SubnetRequest
        {
            public string Label { get; set; } = "Site";
            public int Prefix { get; set; } = 64;
            public string Interface { get; set; } = "";
            public string Notes { get; set; } = "";
        }

        private sealed record Ipv6SubnetAllocation(int Index, string Label, string Interface, IPv6Network Net);

        private void OnAddRequest(object? s, RoutedEventArgs e) => _req.Add(new SubnetRequest());
        private void OnRemoveRequest(object? s, RoutedEventArgs e)
        {
            if (RequestGrid.SelectedItem is SubnetRequest r) _req.Remove(r);
        }

        private void OnAddRequest6(object? s, RoutedEventArgs e) => _req6.Add(new Ipv6SubnetRequest());
        private void OnRemoveRequest6(object? s, RoutedEventArgs e)
        {
            if (RequestGrid6.SelectedItem is Ipv6SubnetRequest r) _req6.Remove(r);
        }

        private void OnCalculateVlsm(object? s, RoutedEventArgs e) => Guarded(() =>
        {
            /* NEW: combine start-IP + optional mask textbox */
            string startTxt = VlsmStartBox.Text.Trim();
            string maskTxt = VlsmMaskBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(startTxt))
                throw new Exception("Starting network is required.");

            if (!string.IsNullOrWhiteSpace(maskTxt))
            {
                if (maskTxt.StartsWith("/"))
                    startTxt += maskTxt;                 // "/24"
                else if (maskTxt.Contains('.'))
                    startTxt += " " + maskTxt;           // dotted mask
                else
                    startTxt += "/" + maskTxt;           // "24"
            }

            _alloc.Clear();
            IPNetwork baseNet = IPNetwork.Parse(startTxt);
            var pool = new List<IPNetwork> { baseNet };

            var ordered = _req.Where(r => r.Hosts is not null || r.ForcePrefix is not null)
                              .OrderByDescending(r => r.Hosts ?? 0)
                              .ThenBy(r => r.ForcePrefix ?? 0)
                              .ToList();

            foreach (var r in ordered)
            {
                int pref = r.ForcePrefix ?? SmallestPrefix(r.Hosts ?? 0);
                var blk = FirstFit(pool, pref) ?? throw new Exception($"Not enough space for '{r.Label}'.");
                _alloc.Add(new(_alloc.Count + 1, r.Label, r.Interface, blk));
            }

            ResultGridVlsm.ItemsSource = _alloc.Select(RowVlsm).ToList();
            long used = _alloc.Sum(a => a.Net.Hosts);
            long freeHosts = pool.Sum(a => a.Hosts);
            SummaryVlsm.Text =
                $"Allocated {_alloc.Count} blocks / {used:N0} hosts   –   Free pool {pool.Count} blocks / {freeHosts:N0} hosts";
        });

        private static object RowVlsm(SubnetAllocation a) => new
        {
            a.Index,
            a.Label,
            a.Interface,
            Network = a.Net.Network,
            Prefix = "/" + a.Net.Cidr,
            Wildcard = IPNetwork.PrefixToWildcardString(a.Net.Cidr),
            HostCount = a.Net.Hosts,
            FirstHost = a.Net.FirstHost,
            LastHost = a.Net.LastHost,
            Broadcast = a.Net.Broadcast,
            Mask = a.Net.Mask
        };

        /*─── VLSM IMPORT buttons ───*/
        private void OnImportCsvVlsm(object? s, RoutedEventArgs e) => Guarded(() => ImportCsvVlsm());
        private void OnImportJsonVlsm(object? s, RoutedEventArgs e) => Guarded(() => ImportJsonVlsm());
        private void OnImportCliVlsm(object? s, RoutedEventArgs e) => Guarded(() => ImportCliVlsm());

        /*─── VLSM EXPORT buttons ───*/
        private void OnExportCsvVlsm(object? s, RoutedEventArgs e) => Guarded(() =>
            ExportCsv(_alloc.Select(RowVlsm), "vlsm"));
        private void OnExportJsonVlsm(object? s, RoutedEventArgs e) => Guarded(() =>
            ExportJson(_alloc.Select(RowVlsm), "vlsm"));
        private void OnCopyAclVlsm(object? s, RoutedEventArgs e) => Guarded(() =>
            CopyAcl(_alloc.Select(a => (a.Net.Network, a.Net.Cidr))));
        private void OnExportCliVlsm(object? s, RoutedEventArgs e) => Guarded(() => ExportCli());

        /*════════════════  IPv6 TAB  ════════════════*/
        private void OnCalculateIpv6(object? s, RoutedEventArgs e) => Guarded(() =>
        {
            string startTxt = Ipv6StartBox.Text.Trim();
            string maskTxt = Ipv6MaskBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(startTxt))
                throw new Exception("Starting network is required.");

            if (!string.IsNullOrWhiteSpace(maskTxt))
            {
                if (maskTxt.StartsWith("/"))
                    startTxt += maskTxt;
                else
                    startTxt += "/" + maskTxt;
            }

            _alloc6.Clear();
            IPv6Network baseNet = IPv6Network.Parse(startTxt);
            var pool = new List<IPv6Network> { baseNet };

            var ordered = _req6.OrderBy(r => r.Prefix).ToList();
            foreach (var r in ordered)
            {
                var blk = FirstFit6(pool, r.Prefix) ?? throw new Exception($"Not enough space for '{r.Label}'.");
                _alloc6.Add(new(_alloc6.Count + 1, r.Label, r.Interface, blk));
            }

            ResultGridIpv6.ItemsSource = _alloc6.Select(RowIpv6).ToList();
            SummaryIpv6.Text = $"Allocated {_alloc6.Count} blocks – Pool {pool.Count} blocks";
        });

        private static object RowIpv6(Ipv6SubnetAllocation a) => new
        {
            a.Index,
            a.Label,
            a.Interface,
            Network = a.Net.Network,
            Prefix = "/" + a.Net.Cidr
        };

        private void OnExportCsvIpv6(object? s, RoutedEventArgs e) => Guarded(() =>
            ExportCsv(_alloc6.Select(RowIpv6), "ipv6"));
        private void OnExportJsonIpv6(object? s, RoutedEventArgs e) => Guarded(() =>
            ExportJson(_alloc6.Select(RowIpv6), "ipv6"));

        /*════════════════  CSV / JSON EXPORT HELPERS  ════════════════*/
        private static void ExportCsv(IEnumerable rows, string tag)
        {
            if (!SaveDlg(out var path, ".csv", "CSV files (*.csv)|*.csv")) return;
            var list = rows.Cast<object>().ToList();
            if (!list.Any()) return;

            var props = list[0].GetType().GetProperties();
            var lines = new List<string> { string.Join(",", props.Select(p => p.Name)) };
            foreach (var r in list) lines.Add(string.Join(",", props.Select(p => p.GetValue(r))));
            File.WriteAllLines(path, lines, Encoding.UTF8);
            MessageBox.Show($"{tag} CSV saved.");
        }
        private static void ExportJson(IEnumerable rows, string tag)
        {
            if (!SaveDlg(out var path, ".json", "JSON files (*.json)|*.json")) return;
            File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show($"{tag} JSON saved.");
        }

        /*════════════════  IMPORT IMPLEMENTATIONS  ════════════════*/
        private void ImportCsvVlsm()
        {
            if (!OpenDlg(out var path, "CSV files (*.csv)|*.csv")) return;

            using var p = new TextFieldParser(path)
            {
                Delimiters = new[] { "," },
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = true
            };

            if (p.EndOfData) throw new Exception("CSV empty.");

            string[] hdr = p.ReadFields()!;
            var map = hdr.Select((h, i) => new { h, i })
                         .ToDictionary(x => x.h.ToLowerInvariant(), x => x.i);

            int IDX(params string[] names)
            {
                foreach (var n in names)
                    if (map.TryGetValue(n.ToLowerInvariant(), out var i)) return i;
                return -1;
            }

            int cLabel = IDX("label");
            int cHosts = IDX("hosts", "hostcount");
            int cPref = IDX("cidr", "prefix");
            int cIntf = IDX("interface");
            int cNet = IDX("network");

            if (cHosts < 0 || cPref < 0)
                throw new Exception("CSV missing Hosts/Prefix columns.");

            _req.Clear();
            while (!p.EndOfData)
            {
                string[] row = p.ReadFields()!;
                if (row.All(string.IsNullOrWhiteSpace)) continue;

                string lbl = Get(row, cLabel) ?? Get(row, cNet) ?? "Site";
                int? host = TryInt(Get(row, cHosts));
                int? pref = TryInt(Get(row, cPref)?.TrimStart('/'));
                string intf = Get(row, cIntf) ?? "";

                _req.Add(new SubnetRequest { Label = lbl, Hosts = host, ForcePrefix = pref, Interface = intf });
            }

            static string? Get(string[] a, int i) => i >= 0 && i < a.Length ? a[i] : null;
        }

        private void ImportJsonVlsm()
        {
            if (!OpenDlg(out var path, "JSON files (*.json)|*.json")) return;
            var arr = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(path))
                      ?? throw new Exception("JSON invalid.");

            _req.Clear();
            foreach (var el in arr)
            {
                _req.Add(new SubnetRequest
                {
                    Label = el.TryGetProperty("Label", out var l) ? l.GetString() ?? "Site" : "Site",
                    Interface = el.TryGetProperty("Interface", out var i) ? i.GetString() ?? "" : "",
                    Hosts = el.TryGetProperty("Hosts", out var h) ? h.GetInt32()
                          : el.TryGetProperty("HostCount", out var hc) ? hc.GetInt32()
                          : (int?)null,
                    ForcePrefix = el.TryGetProperty("Cidr", out var c) ? c.GetInt32()
                          : TryInt(el.TryGetProperty("Prefix", out var p) ? p.GetString() : null)
                });
            }
        }

        private void ImportCliVlsm()
        {
            if (!OpenDlg(out var path, "Text files (*.txt)|*.txt")) return;
            var lines = File.ReadAllLines(path);

            var reInt = new Regex(@"^interface\s+(\S+)", RegexOptions.IgnoreCase);
            var reDesc = new Regex(@"^\s*description\s+(.+)", RegexOptions.IgnoreCase);
            var reIp = new Regex(@"^\s*ip address\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);

            _req.Clear();
            string curInt = ""; string curLbl = "";
            foreach (var ln in lines)
            {
                if (reInt.Match(ln) is { Success: true } m1) { curInt = m1.Groups[1].Value; curLbl = ""; continue; }
                if (reDesc.Match(ln) is { Success: true } m2) { curLbl = m2.Groups[1].Value; continue; }
                if (reIp.Match(ln) is { Success: true } m3)
                {
                    var net = IPNetwork.Parse($"{m3.Groups[1].Value} {m3.Groups[2].Value}");
                    _req.Add(new SubnetRequest
                    {
                        Label = string.IsNullOrWhiteSpace(curLbl) ? curInt : curLbl,
                        Hosts = (int?)net.Hosts,
                        ForcePrefix = net.Cidr,
                        Interface = curInt
                    });
                }
            }
        }

        /*════════════════  CLI EXPORT  ════════════════*/
        private void ExportCli()
        {
            if (!_alloc.Any()) { MessageBox.Show("No allocation to export."); return; }

            var missing = _alloc.Where(a => string.IsNullOrWhiteSpace(a.Interface))
                                .Select(a => a.Label).ToList();
            if (missing.Any())
            {
                MessageBox.Show("Interface missing for: " + string.Join(", ", missing));
                return;
            }

            var sb = new StringBuilder();
            foreach (var a in _alloc)
            {
                sb.AppendLine($"interface {a.Interface}");
                if (a.Interface.Contains('.'))
                    sb.AppendLine($" encapsulation dot1Q {a.Interface.Split('.').Last()}");
                sb.AppendLine($" description {a.Label}");
                sb.AppendLine($" ip address {a.Net.FirstHost} {a.Net.Mask}");
                sb.AppendLine(" no shutdown\n!");
            }

            if (!SaveDlg(out var path, ".txt", "Text files (*.txt)|*.txt")) return;
            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
            MessageBox.Show("Cisco CLI file saved.");
        }

        /*════════════════  SHARED UTILITIES  ════════════════*/
        private static void Guarded(Action act)
        {
            try { act(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Subnet Calculator", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private static int? TryInt(string? txt) => int.TryParse(txt, out var v) ? v : (int?)null;

        private static bool SaveDlg(out string path, string ext, string filter)
        {
            path = null!;
            var d = new SaveFileDialog { DefaultExt = ext, Filter = filter };
            return d.ShowDialog() == true && (path = d.FileName) != null;
        }
        private static bool OpenDlg(out string path, string filter)
        {
            path = null!;
            var d = new OpenFileDialog { Filter = filter };
            return d.ShowDialog() == true && (path = d.FileName) != null;
        }

        private static void CopyAcl(IEnumerable<(System.Net.IPAddress net, int cidr)> nets) =>
            Clipboard.SetText(string.Join(Environment.NewLine,
                nets.Select(n => $"access-list 100 permit {n.net} {IPNetwork.PrefixToWildcardString(n.cidr)}")));

        private static IPNetwork ParseNet(string s) => IPNetwork.Parse(s.Trim());
        private static int? ParseOptPrefix(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : IPNetwork.MaskStringToPrefix(s);
        private static int SmallestPrefix(int hosts) =>
            32 - (int)Math.Ceiling(Math.Log2(hosts + 2));

        private static uint IPToUInt(System.Net.IPAddress ip) =>
            BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray());

        private static IPNetwork? FirstFit(List<IPNetwork> pool, int pref)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var blk = pool[i];
                if (blk.Cidr > pref) continue;
                var subs = blk.Subnet(pref).ToList();
                if (!subs.Any()) continue;

                pool.RemoveAt(i);
                for (int j = 1; j < subs.Count; j++) pool.Add(subs[j]);

                pool.Sort((a, b) => IPToUInt(a.Network).CompareTo(IPToUInt(b.Network)));
                return subs[0];
            }
            return null;
        }

        private static IPv6Network? FirstFit6(List<IPv6Network> pool, int pref)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var blk = pool[i];
                if (blk.Cidr > pref) continue;
                var subs = blk.Subnet(pref).ToList();
                if (!subs.Any()) continue;

                pool.RemoveAt(i);
                for (int j = 1; j < subs.Count; j++) pool.Add(subs[j]);

                pool.Sort((a, b) => IPv6Network.IPToBigInteger(a.Network).CompareTo(IPv6Network.IPToBigInteger(b.Network)));
                return subs[0];
            }
            return null;
        }
    }
}
