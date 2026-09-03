# Unity Mobile Game Factory 🎮📱

مستودع **جاهز ومُجرَّب** لبناء ألعاب Unity 3D للموبايل (Android) بدون أي كمبيوتر:
ترفع مشروع Unity هنا → GitHub Actions يبني الـ APK تلقائياً → تحمّله من **Releases** على موبايلك.

- المحرك: **Unity 6000.0.82f1 LTS** + URP 17 · IL2CPP ARM64+ARMv7 · Android 7.0+
- البناء: GitHub Actions (Buildalon) بترخيص Unity Personal أونلاين — لا يحتاج ملف ترخيص
- الناتج: `Releases → vX.Y.Z → <اسم اللعبة>-vX.Y.Z.apk` (أو AAB لمتجر Google Play عند اختيار ذلك يدوياً)

> ⚠️ **للـ AI في أي محادثة جديدة: اقرأ [`FACTORY_RULES.md`](FACTORY_RULES.md) أولاً.** فيه كل القواعد والأخطاء التي واجهناها وحلولها.

---

## طريقة العمل (الأسهل) — zip ← التطبيق ← APK
1. في محادثة جديدة مع أي AI انسخ الرسالة أدناه. الـ AI يسلّمك **ملف `.zip` واحد** فيه مشروع اللعبة كامل.
2. ارفع الـ zip على **تطبيق البناء** (رابطك على Cloudflare Pages) → يبدأ البناء فوراً.
3. بعد ~10–13 دقيقة: ✅ نجح → زر تحميل APK · ❌ فشل → زر «انسخ الخطأ» → ابعته للـ AI → يرجّع لك zip جديد.

> الـ AI **لا يلمس هذا المستودع أبداً**. المستودع هو المصنع فقط (خط الإنتاج + القالب + القواعد).

### الرسالة التي تنسخها للـ AI في محادثة جديدة
```
ابنِ لي Full Project لعبة Unity 3D للموبايل (Android فقط) بعنوان: <اسم اللعبة>
الفكرة: <وصف اللعبة>

المتطلبات الإلزامية:
- المحرك: Unity 6000.0.82f1 LTS مع URP 17.0.4، uGUI، Legacy Input فقط (لا Input System)، IL2CPP ARM64+ARMv7.
- اقرأ والتزم بكل قواعد هذا الملف قبل أي شيء:
  https://raw.githubusercontent.com/joknok72-ctrl/unity-mobile-game-factory/main/FACTORY_RULES.md
- ابدأ من القالب الجاهز (نزّله كـ zip):
  https://github.com/joknok72-ctrl/unity-mobile-game-factory/archive/refs/heads/main.zip
  واستبدل Assets/Scenes و Assets/Scripts و Assets/Resources و build.json بمحتوى اللعبة.
- لا تعدّل ولا ترفع أي شيء على GitHub. الناتج المطلوب هو ملف zip واحد فقط فيه:
  Assets/ و Packages/manifest.json و ProjectSettings/ و build.json (بدون Library أو Temp أو .git).
- كل ملف له .meta، تحقق من الكود محلياً (mono-mcs) قبل التسليم، لا Shader.Find لشيدرات URP، وأضف HUD نصي تشخيصي.
- أعطني رابط تحميل الـ zip. لو رجعت لك بنص خطأ من البناء، أصلحه وسلّمني zip جديد كامل.
```

### لو الـ AI شغّال في نفس منصة Genspark
ممكن يرفع الـ zip بنفسه على تطبيق البناء عبر API:
`curl -F "file=@Game.zip" -F "name=Game" -F "profile=fast" -H "X-PIN: <PIN>" https://<app>.pages.dev/api/upload`

## أنماط البناء (profile)
| | `fast` (الافتراضي) | `release` |
|---|---|---|
| المعالجات | ARM64 فقط | ARM64 + ARMv7 |
| IL2CPP | Debug · OptimizeSize · stripping Low | Release · OptimizeSpeed · stripping Medium |
| Burst AOT | معطّل | مفعّل |
| الوقت التقريبي | ~8–10 دقيقة (أقل مع الكاش) | ~15–18 دقيقة |
| الاستخدام | تجربة على الموبايل | النشر / المتجر (AAB دائماً release) |

المصنع يحفظ مجلد `Library` في كاش GitHub Actions (مفتاح = profile + hash لـ manifest.json/ProjectVersion.txt) فتُسرّع البناءات التالية لنفس الحزم. ويبلّغ التقدم (`step`/`percent`) وإحصاءات النتيجة (`errors`/`warnings`/`fixes`/`duration`) في body الـ release.

### إصلاح تلقائي (FactoryBuild.FixRenderPipeline)
- URP asset بدون Renderer ← يُنشأ `Factory_AutoRenderer.asset` ويُسند.
- GraphicsSettings بدون pipeline مع وجود URP ← يُسند أول URP asset.

## ماذا يحدث داخل المصنع؟
- `build-zip.yml`: يستقبل الـ zip من التطبيق، يدمج ملفات المصنع (سكربت البناء، إعدادات URP، إصدار Unity)، يبني، ويرجّع APK أو `error.txt`.
- `build-android.yml`: أي `push` على `main` يبني القالب نفسه (للتأكد أن خط الإنتاج سليم).

## بنية المستودع
```
.github/workflows/build-zip.yml       ← بناء الـ zip المرفوع من التطبيق
.github/workflows/build-android.yml   ← بناء القالب عند أي push
Assets/FactoryEditor/FactoryBuild.cs  ← سكربت البناء العام (يُدمَج تلقائياً في كل zip)
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

## تشغيل يدوي للقالب
Actions → **Build Android APK** → Run workflow → اختر `androidPackage` (APK) أو `androidAppBundle` (AAB للمتجر).

## الحالة
- ✅ خط الإنتاج مُجرَّب: عدة إصدارات بُنيت ونُشرت وثُبِّتت على جهاز حقيقي.
- ✅ المشروع الحالي قالب فارغ (مكعب يدور + نص) للتأكد من أن كل شيء يعمل.
- ✅ تطبيق البناء (zip → APK) على Cloudflare Pages.
- 🔜 كل لعبة جديدة = zip من محادثة AI جديدة → التطبيق.
