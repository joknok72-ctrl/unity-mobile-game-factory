// Real Life Sky — HUD (Arabic, uGUI). Shows the real data behind the picture: local time & UTC (with NTP
// status), position (lat/lon/alt, GPS accuracy), Sun & Moon altitude/azimuth, sunrise/sunset, moonrise/moonset,
// Moon phase & distance, twilight phase, ground illuminance (lux), adaptation luminance, and what the camera points
// at (heading/pitch). Bottom-left: tap to toggle gyro/touch. Built entirely in code (no scene YAML to hand-write).
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RealLife.Astronomy;

namespace RealLife.Sky
{
    public class SkyHud : MonoBehaviour
    {
        Text _top, _bottom;
        SkyModel.RiseSet _sunRs, _moonRs; DateTime _rsDay = DateTime.MinValue; double _rsLat, _rsLon;
        float _nextRefresh;
        Font _font;
        SkyCamera _cam;

        void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
            var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 2340); scaler.matchWidthOrHeight = 0.5f;

            _top = MakeText(canvasGo.transform, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -70), new Vector2(-24, -24), TextAnchor.UpperRight, 30);
            _bottom = MakeText(canvasGo.transform, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 40), new Vector2(-24, 420), TextAnchor.LowerRight, 28);

            var btn = new GameObject("ModeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btn.transform.SetParent(canvasGo.transform, false);
            var rt = btn.GetComponent<RectTransform>(); rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(120, 110); rt.sizeDelta = new Vector2(200, 80);
            btn.GetComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            var bl = MakeText(btn.transform, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter, 28);
            bl.text = "جيرو / لمس";
            btn.GetComponent<Button>().onClick.AddListener(() => { if (_cam != null) _cam.ToggleMode(); });
            _cam = Camera.main != null ? Camera.main.GetComponent<SkyCamera>() : null;
        }

        Text MakeText(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, TextAnchor anchor, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
            var t = go.GetComponent<Text>(); t.font = _font; t.fontSize = size; t.alignment = anchor; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.lineSpacing = 1.15f;
            var o = go.GetComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            var dir = CelestialDirector.Instance; var clock = WorldClock.Instance; var geo = GeoLocation.Instance; var exp = ExposureController.Instance;
            if (dir == null || clock == null || geo == null || dir.Snapshot.Planets == null) return;
            var s = dir.Snapshot;
            RefreshRiseSet(clock, geo);

            var sb = new StringBuilder();
            DateTime local = clock.LocalNow;
            sb.Append(local.ToString("yyyy-MM-dd  HH:mm:ss")).Append("  (").Append(TimeZoneInfo.Local.BaseUtcOffset >= TimeSpan.Zero ? "UTC+" : "UTC-").Append(TimeZoneInfo.Local.BaseUtcOffset.ToString(@"h\:mm")).Append(")\n");
            sb.Append("UTC ").Append(clock.UtcNow.ToString("HH:mm:ss.f")).Append(clock.NtpSynced ? $"  ⏱ NTP ±{clock.LastRoundTripMs / 2:F0}ms" : "  ⏱ ساعة الجهاز").Append('\n');
            sb.Append(FormatLatLon(geo.LatitudeDeg, geo.LongitudeDeg)).Append($"  ↕{geo.AltitudeM:F0}m");
            sb.Append(geo.State == GeoLocation.FixState.Fixed ? $"  📍GPS ±{geo.HorizontalAccuracyM:F0}m" : geo.State == GeoLocation.FixState.Stored ? "  📍آخر موقع محفوظ" : geo.State == GeoLocation.FixState.Denied ? "  📍بدون إذن الموقع" : "  📍جارٍ تحديد الموقع…").Append('\n');
            if (_cam != null) sb.Append($"الاتجاه {_cam.HeadingDeg:F1}° {Cardinal(_cam.HeadingDeg)}   الارتفاع {_cam.PitchDeg:F1}°   {(_cam.mode == SkyCamera.Mode.Gyro ? "جيروسكوب" : "لمس")}\n");
            _top.text = sb.ToString();

            sb.Clear();
            sb.Append(PhaseName(dir.Phase)).Append('\n');
            sb.Append($"☀ الشمس: ارتفاع {s.Sun.Horizontal.ApparentAltDeg:F2}°  سمت {s.Sun.Horizontal.AzimuthDeg:F1}°  بعد {s.Sun.DistanceAu:F5} AU\n");
            sb.Append($"   شروق {Fmt(_sunRs.Rise)}  زوال {Fmt(_sunRs.Transit)}  غروب {Fmt(_sunRs.Set)}\n");
            var m = s.Moon;
            sb.Append($"☾ القمر: ارتفاع {m.Horizontal.ApparentAltDeg:F2}°  سمت {m.Horizontal.AzimuthDeg:F1}°  بعد {m.DistanceKm:F0} كم\n");
            sb.Append($"   إضاءة {m.IlluminatedFraction * 100:F1}%  ({MoonPhaseName(m)})  قدر {m.VisualMagnitude:F1}  قطر {m.AngularDiameterRad * AstroTime.Rad2Deg * 60:F1}′\n");
            sb.Append($"   طلوع {Fmt(_moonRs.Rise)}  غياب {Fmt(_moonRs.Set)}\n");
            sb.Append("الكواكب: ");
            string[] names = { "عطارد", "الزهرة", "المريخ", "المشتري", "زحل" };
            for (int i = 0; i < 5; i++) { var p = s.Planets[i]; if (p.Horizontal.ApparentAltDeg > 0) sb.Append($"{names[i]} {p.Horizontal.ApparentAltDeg:F0}°/{p.VisualMagnitude:F1}  "); }
            sb.Append('\n');
            sb.Append($"الإضاءة الأرضية {FmtLux(dir.GroundIlluminanceLux)}   تكيّف العين {FmtLum(exp != null ? exp.AdaptationLuminance : 0)}  {(exp != null && exp.Scotopic > 0.5f ? "رؤية ليلية" : exp != null && exp.Scotopic > 0.1f ? "رؤية وسطية" : "رؤية نهارية")}\n");
            _bottom.text = sb.ToString();
        }

        void RefreshRiseSet(WorldClock clock, GeoLocation geo)
        {
            DateTime local = clock.LocalNow;
            DateTime day = local.Date;
            if (day != _rsDay || Math.Abs(geo.LatitudeDeg - _rsLat) > 0.01 || Math.Abs(geo.LongitudeDeg - _rsLon) > 0.01)
            {
                _rsDay = day; _rsLat = geo.LatitudeDeg; _rsLon = geo.LongitudeDeg;
                DateTime midnightUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TimeZoneInfo.Local);
                var obs = geo.Observer;
                _sunRs = SkyModel.FindRiseSet(CelestialBodyId.Sun, midnightUtc, obs);
                _moonRs = SkyModel.FindRiseSet(CelestialBodyId.Moon, midnightUtc, obs);
            }
        }

        static string Fmt(DateTime? utc) => utc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(utc.Value, TimeZoneInfo.Local).ToString("HH:mm:ss") : "—";
        static string FmtLux(double lux) => lux >= 1000 ? $"{lux / 1000:F1} كيلولوكس" : lux >= 1 ? $"{lux:F0} لوكس" : $"{lux * 1000:F2} ملّي لوكس";
        static string FmtLum(double l) => l >= 1 ? $"{l:F0} cd/m²" : $"{l * 1000:F2} mcd/m²";
        static string Cardinal(float h) { string[] c = { "ش", "ش ش ق", "ش ق", "ق ش ق", "ق", "ق ج ق", "ج ق", "ج ج ق", "ج", "ج ج غ", "ج غ", "غ ج غ", "غ", "غ ش غ", "ش غ", "ش ش غ" }; return c[(int)Math.Round(h / 22.5) % 16]; }
        static string FormatLatLon(double lat, double lon) => $"{Math.Abs(lat):F5}°{(lat >= 0 ? "N" : "S")} {Math.Abs(lon):F5}°{(lon >= 0 ? "E" : "W")}";
        static string PhaseName(SkyModel.DayPhase p)
        {
            switch (p)
            {
                case SkyModel.DayPhase.Day: return "نهار";
                case SkyModel.DayPhase.CivilTwilight: return "شفق مدني (الشمس بين 0° و −6°)";
                case SkyModel.DayPhase.NauticalTwilight: return "شفق بحري (−6° إلى −12°)";
                case SkyModel.DayPhase.AstronomicalTwilight: return "شفق فلكي (−12° إلى −18°)";
                default: return "ليل فلكي";
            }
        }
        static string MoonPhaseName(BodyState m)
        {
            // Bright limb position angle (from N through E): waxing Moon is lit on its western limb (PA≈270°, sin<0).
            double f = m.IlluminatedFraction;
            bool waxing = Math.Sin(m.BrightLimbAngleRad) < 0;
            if (f < 0.02) return "محاق"; if (f > 0.98) return "بدر";
            if (Math.Abs(f - 0.5) < 0.03) return waxing ? "تربيع أول" : "تربيع ثاني";
            if (f < 0.5) return waxing ? "هلال متزايد" : "هلال متناقص";
            return waxing ? "أحدب متزايد" : "أحدب متناقص";
        }
    }
}
