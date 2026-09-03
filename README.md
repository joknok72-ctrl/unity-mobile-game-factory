# Real Life Sky — سماء الحياة الحقيقية 🌌

لعبة/تجربة **Unity 3D للموبايل فقط (Android)** تعرض السماء الحقيقية فوق رأسك **بالدقة الفلكية الفعلية وبالوقت الحقيقي**:
الشمس، القمر (بوجهه الصحيح ومَيَلانه الحقيقي)، الكواكب الخمسة، 8,920 نجمة حقيقية، وغلاف جوي محسوب فيزيائياً بوحدات إضاءة حقيقية (لوكس / شمعة لكل متر مربع) — كل ذلك لموقعك من GPS وتوقيت UTC مصحَّح من خوادم NTP.

> **المرحلة 1 فقط**: شيء واحد مبني بأقصى عمق وإتقان. لا يوجد أي محتوى خيالي.

---

## ✅ المُنفَّذ حالياً

| المكوّن | التفاصيل |
|---|---|
| **الزمن** | SNTP (RFC 4330) من time.google.com / pool.ntp.org / time.cloudflare.com، تعويض زمن الرحلة، ΔT، TT، ERA/GMST (IAU 2006) |
| **الموضع** | GPS الحقيقي للجهاز (Android FineLocation) مع حفظ آخر موقع |
| **الشمس/الكواكب** | VSOP87B + light-time + aberration + nutation IAU2000B + precession Fukushima-Williams |
| **القمر** | Meeus ch.47 (موضع) + ch.53 (libration بصري + فيزيائي، زاوية الموضع P، نقطة تحت الشمس) + parallax طوبوسنتري + الحجم الزاوي الحقيقي + Lommel-Seeliger/Hapke + ضوء الأرض (earthshine) + خريطة NASA LROC |
| **النجوم** | كتالوج HYG v4.1 (8,920 نجمة حتى قدر 6.5) بحركة ذاتية، لون من B-V (Ballesteros 2012)، PSF محفوظ الطاقة، تلألؤ ∝ airmass^1.75 |
| **الغلاف الجوي** | Bruneton & Neyret 2008 / Hillaire 2020: Rayleigh + Mie + أوزون، LUTs (Transmittance 256×64، Multi-scatter 32×32، Sky-View 192×108)، إشعاع الشمس 127,700 لوكس عند حد الغلاف، Hestroffer limb darkening، هالة Baumbach |
| **الانكسار** | Bennett / Sæmundsson حسب الضغط والحرارة؛ الشروق/الغروب طوبوسنتري بـ h0 = −(34′ + نصف القطر) |
| **الإدراك البشري** | تعريض Reinhard بمفتاح 0.18، تكيّف زمني (0.4 ث للضوء / 8–400 ث للظلام)، رؤية سكوتوبية (Purkinje)، ACES |
| **الكاميرا** | جيروسكوب + بوصلة (trueHeading)، أو سحب/تكبير بالأصابع؛ زاوية رؤية 1:1 حسب DPI الشاشة |
| **HUD** | عربي: الوقت الفعلي، الموقع، ارتفاع/سمت الشمس والقمر، الشروق/الغروب، الطور |

**دقة التحقق مقابل JPL / NAIF DE421:** مواضع ≤ 5″، شروق/غروب القمر مطابق للثانية، libration Δ ≤ 0.025°.

---

## 📦 تنزيل الـ APK

1. افتح تبويب **Releases** في هذا المستودع.
2. حمّل `RealLifeSky-v0.1.N.apk` من آخر إصدار.
3. على الموبايل: فعّل «تثبيت من مصادر غير معروفة» ثم ثبّت.
4. عند التشغيل امنح صلاحية **الموقع** (مطلوبة لحساب السماء فوقك بالضبط).

**المتطلبات:** Android 7.0+ (API 24)، معالج ARM64 أو ARMv7، Vulkan أو OpenGL ES 3.

---

## ⚙️ البناء التلقائي (GitHub Actions + Buildalon)

الملف: [`.github/workflows/build-android.yml`](.github/workflows/build-android.yml)

- **`buildalon/unity-setup@v2`** يثبّت Unity Hub + Unity 6000.0.82f1 + Android module على runner لينكس.
- **`buildalon/activate-unity-license@v2`** يفعّل ترخيص **Personal أونلاين** بحساب Unity (إيميل + باسورد) — **لا يحتاج ملف `.ulf` ولا كمبيوتر** (صفحة التفعيل اليدوي `license.unity3d.com/manual` أصبحت لعملاء Enterprise فقط).
- **`buildalon/unity-action@v3`** يشغّل `-executeMethod BuildScript.Build` → [`Assets/_Project/Scripts/Editor/BuildScript.cs`](Assets/_Project/Scripts/Editor/BuildScript.cs) يضبط IL2CPP، ARM64+ARMv7، minSdk 24 / targetSdk 34، Vulkan+GLES3، Portrait، التوقيع، ثم `BuildPipeline.BuildPlayer`.
- الناتج يُرفع كـ **Artifact** ويُنشر تلقائياً في **Releases** بعلامة `v0.1.<run_number>`.

### الـ Secrets المطلوبة (Settings → Secrets and variables → Actions)

| Secret | القيمة | من يضيفها |
|---|---|---|
| `UNITY_EMAIL` | إيميل حساب Unity (نفس حساب cloud.unity.com) | **أنت** |
| `UNITY_PASSWORD` | كلمة مرور حساب Unity | **أنت** |
| `ANDROID_KEYSTORE_BASE64` | keystore مُرمَّز base64 | ✅ مضافة |
| `ANDROID_KEYSTORE_PASS` | كلمة مرور الـ keystore | ✅ مضافة |
| `ANDROID_KEYALIAS_NAME` | `reallifesky` | ✅ مضافة |
| `ANDROID_KEYALIAS_PASS` | كلمة مرور المفتاح | ✅ مضافة |

> **ملاحظة مهمة:** لو حسابك على Unity مفعَّل فيه التحقق بخطوتين (2FA)، عطّله مؤقتاً من <https://id.unity.com> → Security، أو أنشئ حساب Unity جديداً مخصصاً للبناء (مجاني) واستخدمه في الـ Secrets. التفعيل الآلي لا يستطيع إدخال كود 2FA.

بعد إضافة الـ Secret-ين: **Actions → Build Android APK → Run workflow** (أول بناء ~40–60 دقيقة لأنه يحمّل Unity؛ البناءات التالية أسرع بسبب الـ cache).

---

## 🗂️ بنية المشروع

```
Assets/_Project/
├─ Scenes/RealSky.unity            مشهد بكائن Bootstrap واحد يبني كل شيء بالكود
├─ Scripts/
│  ├─ Astronomy/                   AstroTime · Nutation · Ephemerides(VSOP87B) · SkyModel · MoonOrientation
│  ├─ Runtime/                     WorldClock · GeoLocation · AtmosphereModel · CelestialDirector
│  │                               StarField · ExposureController · SkyCamera · SkyHud · Bootstrap
│  └─ Editor/BuildScript.cs        نقطة دخول GameCI
├─ Resources/
│  ├─ Shaders/                     RL_Atmosphere.hlsl · RL_SkyViewLUT · RL_Skybox · RL_Moon · RL_Stars
│  ├─ Data/stars_hyg41.bytes       8,920 نجمة (RLS1: ra, dec, pmra, pmdec, mag, B-V)
│  ├─ Data/star_names.json         145 اسم نجم
│  └─ Textures/Moon_LROC_Albedo.jpg
└─ Settings/                       URP Mobile RP Asset · Renderer · Volume Profile
Packages/manifest.json             URP 17.0.4 · Input System 1.11.2 · uGUI 2.0
ProjectSettings/                   Unity 6000.0.82f1 · Android · IL2CPP · Linear
.github/workflows/build-android.yml
```

## 🧮 نموذج البيانات

- `SkySnapshot` (لحظة واحدة): JD/TT، ERA، nutation، obliquity، `BodyState` لكل جرم (RA/Dec، Alt/Az حقيقي وظاهري، مسافة، قدر، حجم زاوي، `MoonAspect`).
- `stars_hyg41.bytes`: رأس `'RLS1'` + `int32 n` + لكل نجم 6×float32.
- لا يوجد تخزين سحابي؛ آخر موقع GPS فقط في `PlayerPrefs`.

## 🚧 غير مُنفَّذ بعد (خارج المرحلة 1)

- أرض/منظر طبيعي حقيقي للموقع (DEM + خرائط)، طقس وسُحب حقيقية من API، الشفق القطبي، الأقمار الاصطناعية/محطة الفضاء، الشُّهُب، نجوم أخفت من قدر 6.5، iOS.

## 🔜 الخطوات التالية المقترحة

1. إضافة الـ Secrets الثلاثة لـ Unity وتشغيل أول بناء.
2. المرحلة 2: أرض حقيقية (Copernicus DEM 30 م + OSM) حول الموقع.
3. المرحلة 3: سُحب/ضباب حقيقي من بيانات الأرصاد (Open-Meteo) داخل نفس نموذج الغلاف الجوي.

---
**التقنيات:** Unity 6000.0.82f1 LTS · URP 17.0.4 · C# · HLSL · GameCI v4 · IL2CPP  
**آخر تحديث:** 2026-09-03
