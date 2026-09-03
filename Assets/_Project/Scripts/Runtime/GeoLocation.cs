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

        Coroutine _acquire;
        void Start() { Retry(); }

        /// <summary>Re-run the permission request + GPS start (bound to the HUD "الموقع" button).</summary>
        public void Retry()
        {
            if (_acquire != null) StopCoroutine(_acquire);
            _acquire = StartCoroutine(AcquireLocation());
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool HasAnyLocationPermission =>
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) || Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);
        bool _permanentlyDenied;

        static void OpenAppSettings()
        {
            // The user ticked "don't ask again": the only way to grant the permission is the system settings page of this app.
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", Application.identifier, null))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS", uri))
                    activity.Call("startActivity", intent);
            }
            catch (Exception e) { Debug.LogWarning("[RealLifeSky] cannot open app settings: " + e.Message); }
        }
#endif

        IEnumerator AcquireLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!HasAnyLocationPermission)
            {
                if (_permanentlyDenied) { OpenAppSettings(); _permanentlyDenied = false; yield return new WaitForSeconds(1f); }
                State = FixState.RequestingPermission;
                bool answered = false;
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => answered = true;
                cb.PermissionDenied += _ => answered = true;
                cb.PermissionDeniedAndDontAskAgain += _ => { answered = true; _permanentlyDenied = true; };
                Permission.RequestUserPermissions(new[] { Permission.FineLocation, Permission.CoarseLocation }, cb);
                float t = 0;
                while (!answered && !HasAnyLocationPermission && t < 60f) { t += Time.unscaledDeltaTime; yield return null; }
                if (!HasAnyLocationPermission) { State = HasFix ? FixState.Stored : FixState.Denied; yield break; }
            }
#endif
            if (!Input.location.isEnabledByUser) { State = HasFix ? FixState.Stored : FixState.Denied; yield break; }
            if (Input.location.status == LocationServiceStatus.Running || Input.location.status == LocationServiceStatus.Initializing) Input.location.Stop();
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
