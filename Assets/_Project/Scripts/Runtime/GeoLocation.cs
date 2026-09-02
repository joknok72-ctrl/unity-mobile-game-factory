// Real Life Sky — real place. Uses the phone GPS (Input.location) with runtime permission on Android,
// persists the last good fix, and falls back to a stored location until a fix arrives. Also reads the
// barometer-less standard atmosphere for the altitude (used for refraction pressure), and the ambient
// temperature default (15 °C) unless the user overrides it.
using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace RealLife.Sky
{
    public class GeoLocation : MonoBehaviour
    {
        public static GeoLocation Instance { get; private set; }

        public enum FixState { Unknown, RequestingPermission, Starting, Fixed, Denied, Failed, Stored }
        public FixState State { get; private set; } = FixState.Unknown;

        public double LatitudeDeg { get; private set; }
        public double LongitudeDeg { get; private set; }
        public double AltitudeM { get; private set; }
        public double HorizontalAccuracyM { get; private set; } = double.NaN;
        public DateTime FixTimeUtc { get; private set; }
        public bool HasFix => State == FixState.Fixed || State == FixState.Stored;

        // Standard atmosphere pressure at altitude (hPa) — ISA barometric formula
        public double PressureHPa => 1013.25 * Math.Pow(1.0 - 2.25577e-5 * Math.Max(0, AltitudeM), 5.25588);
        public double TemperatureC = 15.0;

        const string KeyLat = "rl_lat", KeyLon = "rl_lon", KeyAlt = "rl_alt", KeyHas = "rl_has";

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (PlayerPrefs.GetInt(KeyHas, 0) == 1)
            {
                LatitudeDeg = PlayerPrefs.GetFloat(KeyLat);
                LongitudeDeg = PlayerPrefs.GetFloat(KeyLon);
                AltitudeM = PlayerPrefs.GetFloat(KeyAlt);
                State = FixState.Stored;
            }
        }

        void Start() { StartCoroutine(AcquireLocation()); }

        IEnumerator AcquireLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                State = FixState.RequestingPermission;
                Permission.RequestUserPermission(Permission.FineLocation);
                float t = 0;
                while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && t < 30f) { t += Time.unscaledDeltaTime; yield return null; }
                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation)) { State = HasFix ? FixState.Stored : FixState.Denied; yield break; }
            }
#endif
            if (!Input.location.isEnabledByUser) { State = HasFix ? FixState.Stored : FixState.Denied; yield break; }
            State = FixState.Starting;
            Input.location.Start(5f, 10f); // desired accuracy 5 m, update every 10 m
            Input.compass.enabled = true;
            int wait = 30;
            while (Input.location.status == LocationServiceStatus.Initializing && wait > 0) { yield return new WaitForSeconds(1); wait--; }
            if (Input.location.status != LocationServiceStatus.Running) { State = HasFix ? FixState.Stored : FixState.Failed; yield break; }
            while (true)
            {
                var d = Input.location.lastData;
                if (d.timestamp > 0)
                {
                    LatitudeDeg = d.latitude; LongitudeDeg = d.longitude; AltitudeM = d.altitude;
                    HorizontalAccuracyM = d.horizontalAccuracy;
                    FixTimeUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(d.timestamp);
                    State = FixState.Fixed;
                    PlayerPrefs.SetFloat(KeyLat, (float)LatitudeDeg); PlayerPrefs.SetFloat(KeyLon, (float)LongitudeDeg);
                    PlayerPrefs.SetFloat(KeyAlt, (float)AltitudeM); PlayerPrefs.SetInt(KeyHas, 1); PlayerPrefs.Save();
                }
                yield return new WaitForSeconds(5);
            }
        }

        /// <summary>Astronomical observer for the current position.</summary>
        public RealLife.Astronomy.Observer Observer => new RealLife.Astronomy.Observer
        {
            LatitudeDeg = LatitudeDeg, LongitudeDeg = LongitudeDeg, AltitudeM = AltitudeM,
            TemperatureC = TemperatureC, PressureHPa = PressureHPa
        };
    }
}
