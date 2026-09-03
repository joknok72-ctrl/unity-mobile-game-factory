# Unity Mobile Game Factory 🎮📱

مستودع **جاهز ومُجرَّب** لبناء ألعاب Unity 3D للموبايل (Android) بدون أي كمبيوتر:
ترفع مشروع Unity هنا → GitHub Actions يبني الـ APK تلقائياً → تحمّله من **Releases** على موبايلك.

- المحرك: **Unity 6000.0.82f1 LTS** + URP 17 · IL2CPP ARM64+ARMv7 · Android 7.0+
- البناء: GitHub Actions (Buildalon) بترخيص Unity Personal أونلاين — لا يحتاج ملف ترخيص
- الناتج: `Releases → vX.Y.Z → <اسم اللعبة>-vX.Y.Z.apk` (أو AAB لمتجر Google Play عند اختيار ذلك يدوياً)

> ⚠️ **للـ AI في أي محادثة جديدة: اقرأ [`FACTORY_RULES.md`](FACTORY_RULES.md) أولاً.** فيه كل القواعد والأخطاء التي واجهناها وحلولها.

---

## كيف أطلب لعبة جديدة؟ (انسخ هذه الرسالة في محادثة جديدة)

```
اقرأ أولاً ملف FACTORY_RULES.md في المستودع:
https://github.com/joknok72-ctrl/unity-mobile-game-factory
ثم ابنِ Full Project لعبة Unity 3D للموبايل (Android فقط) بعنوان: <اسم اللعبة>
الفكرة: <وصف اللعبة>
- التزم بكل قواعد FACTORY_RULES.md (Unity 6000.0.82f1، URP، Legacy Input، .meta لكل ملف، لا Shader.Find لشيدرات URP، HUD تشخيصي).
- حدّث build.json (الاسم، com.<studio>.<game>، الاتجاه، المشاهد).
- تحقق من الكود محلياً (mono-mcs) قبل الرفع.
- ارفع على branch main في نفس المستودع، انتظر البناء، وتأكد أن الـ APK ظهر في Releases.
- بعد ذلك اطلب مني سكرين شوت من اللعبة وأصلح أي مشكلة تظهر.
```

## ماذا يحدث بعد الرفع؟
1. أي `push` على `main` يشغّل `.github/workflows/build-android.yml`.
2. يثبّت Unity + Android module، يفعّل الترخيص، يبني بـ `BuildScript.Build`.
3. بعد ~10–13 دقيقة تجد الإصدار في: `https://github.com/joknok72-ctrl/unity-mobile-game-factory/releases/latest`
4. ثبّت الـ APK على الموبايل (فعّل "مصادر غير معروفة").

## بنية المستودع
```
.github/workflows/build-android.yml   ← خط الإنتاج (لا يُعدَّل لكل لعبة)
Assets/Editor/BuildScript.cs          ← سكربت البناء العام (لا يُعدَّل لكل لعبة)
Assets/Settings/                      ← إعدادات URP للموبايل
Assets/Scenes/Main.unity              ← مشهد قالب (مكعب يدور) — يُستبدل باللعبة
Assets/Scripts/TemplateBootstrap.cs   ← سكربت القالب — يُستبدل باللعبة
Assets/Resources/TemplateMaterial.mat ← مادة تشير إلى URP/Lit (تمنع مشكلة الشاشة البنفسجية)
build.json                            ← اسم اللعبة / الحزمة / الاتجاه / المشاهد
FACTORY_RULES.md                      ← القواعد الإلزامية
```

## `build.json`
```json
{
  "productName": "اسم اللعبة",
  "companyName": "اسم الاستوديو",
  "packageName": "com.studio.game",
  "orientation": "Portrait | Landscape | AutoRotation",
  "scenes": ["Assets/Scenes/Main.unity"],
  "armv7": true,
  "minSdk": 24
}
```

## الـ Secrets (مضافة بالفعل على هذا المستودع)
| Secret | الوظيفة |
|---|---|
| `UNITY_EMAIL` / `UNITY_PASSWORD` | حساب Unity (cloud.unity.com) — تفعيل الترخيص Personal. لو فعّلت التحقق بخطوتين (2FA) على الحساب سيفشل التفعيل |
| `ANDROID_KEYSTORE_BASE64` / `ANDROID_KEYSTORE_PASS` / `ANDROID_KEYALIAS_NAME` / `ANDROID_KEYALIAS_PASS` | توقيع الـ APK (نفس المفتاح لكل الألعاب حتى تُحدَّث التثبيتات فوق بعضها) |

## تشغيل يدوي
Actions → **Build Android APK** → Run workflow → اختر `androidPackage` (APK) أو `androidAppBundle` (AAB للمتجر).

## الحالة
- ✅ خط الإنتاج مُجرَّب: عدة إصدارات بُنيت ونُشرت وثُبِّتت على جهاز حقيقي.
- ✅ المشروع الحالي قالب فارغ (مكعب يدور + نص) للتأكد من أن كل شيء يعمل.
- 🔜 اللعبة القادمة تُبنى فوق هذا القالب في محادثة جديدة.
