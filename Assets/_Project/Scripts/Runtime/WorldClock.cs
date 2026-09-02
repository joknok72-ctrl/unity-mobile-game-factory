// Real Life Sky — real time. The sky is drawn for the TRUE current instant.
// Source of truth: the device clock (UTC) — corrected against an NTP server when the network is available,
// so the sky is right even if the phone clock is off by minutes. Sub-second smoothness comes from
// Stopwatch-based interpolation between clock reads.
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

namespace RealLife.Sky
{
    public class WorldClock : MonoBehaviour
    {
        public static WorldClock Instance { get; private set; }

        [Tooltip("NTP servers (queried once at start and every 30 min).")]
        public string[] ntpServers = { "time.google.com", "pool.ntp.org", "time.cloudflare.com" };

        /// <summary>Offset to add to the device UTC clock to obtain true UTC (seconds).</summary>
        public double ClockOffsetSeconds { get; private set; }
        public bool NtpSynced { get; private set; }
        public DateTime LastNtpSync { get; private set; }
        public double LastRoundTripMs { get; private set; }

        readonly Stopwatch _sw = new Stopwatch();
        DateTime _anchorUtc;
        double _nextSync;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _anchorUtc = DateTime.UtcNow;
            _sw.Start();
        }

        void Start() { _ = SyncAsync(); }

        void Update()
        {
            // re-anchor every 60 s to avoid Stopwatch drift vs system clock (system clock is authoritative)
            if (_sw.Elapsed.TotalSeconds > 60) { _anchorUtc = DateTime.UtcNow; _sw.Restart(); }
            if (Time.unscaledTime > _nextSync) { _nextSync = Time.unscaledTime + 1800f; _ = SyncAsync(); }
        }

        /// <summary>True UTC now (device clock + NTP correction), smooth at frame rate.</summary>
        public DateTime UtcNow => _anchorUtc.AddSeconds(_sw.Elapsed.TotalSeconds + ClockOffsetSeconds);

        /// <summary>Local civil time according to the device time zone (for the HUD).</summary>
        public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, TimeZoneInfo.Local);

        async Task SyncAsync()
        {
            if (Application.internetReachability == NetworkReachability.NotReachable) return;
            foreach (var host in ntpServers)
            {
                try
                {
                    var res = await Task.Run(() => QueryNtp(host, 1500));
                    if (res.HasValue)
                    {
                        ClockOffsetSeconds = res.Value.offset;
                        LastRoundTripMs = res.Value.rttMs;
                        NtpSynced = true;
                        LastNtpSync = DateTime.UtcNow;
                        return;
                    }
                }
                catch { /* try next server */ }
            }
        }

        // SNTP (RFC 4330) client — returns (offset seconds to add to local UTC, round trip ms)
        static (double offset, double rttMs)? QueryNtp(string host, int timeoutMs)
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0) return null;
            var ep = new IPEndPoint(addresses[0], 123);
            var data = new byte[48];
            data[0] = 0x1B; // LI=0, VN=3, Mode=3 (client)
            using (var sock = new Socket(ep.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                sock.ReceiveTimeout = timeoutMs; sock.SendTimeout = timeoutMs;
                DateTime t1 = DateTime.UtcNow;
                WriteTimestamp(data, 40, t1); // transmit timestamp
                sock.Connect(ep);
                sock.Send(data);
                sock.Receive(data);
                DateTime t4 = DateTime.UtcNow;
                DateTime t2 = ReadTimestamp(data, 32); // receive timestamp at server
                DateTime t3 = ReadTimestamp(data, 40); // transmit timestamp at server
                double offset = ((t2 - t1).TotalSeconds + (t3 - t4).TotalSeconds) / 2.0;
                double rtt = ((t4 - t1) - (t3 - t2)).TotalMilliseconds;
                if (Math.Abs(offset) > 86400 * 365) return null; // garbage
                return (offset, rtt);
            }
        }

        static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static DateTime ReadTimestamp(byte[] b, int o)
        {
            ulong intPart = ((ulong)b[o] << 24) | ((ulong)b[o + 1] << 16) | ((ulong)b[o + 2] << 8) | b[o + 3];
            ulong frac = ((ulong)b[o + 4] << 24) | ((ulong)b[o + 5] << 16) | ((ulong)b[o + 6] << 8) | b[o + 7];
            double secs = intPart + frac / 4294967296.0;
            if (intPart < 2085978496UL && intPart != 0) { }
            else if (intPart == 0) return DateTime.MinValue;
            return NtpEpoch.AddSeconds(secs);
        }
        static void WriteTimestamp(byte[] b, int o, DateTime t)
        {
            double secs = (t - NtpEpoch).TotalSeconds;
            ulong intPart = (ulong)secs;
            ulong frac = (ulong)((secs - intPart) * 4294967296.0);
            b[o] = (byte)(intPart >> 24); b[o + 1] = (byte)(intPart >> 16); b[o + 2] = (byte)(intPart >> 8); b[o + 3] = (byte)intPart;
            b[o + 4] = (byte)(frac >> 24); b[o + 5] = (byte)(frac >> 16); b[o + 6] = (byte)(frac >> 8); b[o + 7] = (byte)frac;
        }
    }
}
