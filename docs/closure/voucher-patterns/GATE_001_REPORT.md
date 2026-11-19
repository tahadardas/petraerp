# AMN-VPAT-GATE-001 — مخطط VoucherPatternConfiguration (التقرير) · يتوقف للموافقة

**الأساس:** `2c584f4` (GATE-000) فوق `6798286`. **قرارات المالك المعتمدة:** (1) الحساب الموحد: الخيار A (للأطراف الجدد وبلا حركة)؛ (2) سند اليومية = `VoucherFormKind` فوق القبض/الدفع؛ (3) القبض/الدفع المتعدد الطرق مؤجل؛ (4) نمط بلا منح فروع = لا أحد. قرار VP-024 (قيد الأمين على المصادقة) لم يُحسم بعد وليس مطلوباً قبل GATE-003.

## ما نُفِّذ (مخطط + ترقية + تحقق فقط؛ لا أثر محاسبي جديد)
| الملف | التغيير |
|---|---|
| `src/PetraERP.Domain/Patterns/PatternVoucherPolicy.cs` (جديد) | `VoucherFormKind` (Generic/MultiMethodReceipt/MultiMethodPayment) و`VoucherCaptionMode` و`PatternVoucherPolicy` و`VoucherPatternRules` (Validate/Effective) و`PatternConfigurationUpgrader` |
| `PatternConfiguration.cs` | `CurrentSchemaVersion = 2` وكتلة `Voucher` اختيارية |
| `PatternAccountRoles.Normalize` | يستدعي التحقق ثم الترقية؛ المسودة الجديدة تُخزَّن بمخطط 2 |
| `PatternContracts.cs` + `PatternMapping.cs` | `PatternVoucherPolicyDto` وتحويل الاتجاهين |
| `openapi/petraerp-v1.json` + `Generated/PetraApiClient.g.cs` | أُعيد توليدهما؛ فحص `Update-ApiContract.ps1 -Check` ينجح |

## القواعد المثبتة (كلها بأكواد ثابتة)
- كتلة `Voucher` مسموحة فقط لعمليات القبض/الدفع/يومية الصندوق/القيد، وإلا `patterns.voucher_block_not_allowed`.
- `GenericVoucher` وحده يعمل؛ المتعدد الطرق مرفوض `patterns.voucher_form_unsupported` (القرار 3).
- خيارات لم يطبقها الخادم بعد (`DenyMutationsOnOrBefore`، `PerLineJournal`، `AutoPost`) تُقبل بقيمتها الافتراضية فقط وإلا `patterns.voucher_option_not_enforced`: لا يُخزَّن وعد لا يفيه المحرك.
- مخطط خارج 1..2 مرفوض `patterns.schema_unsupported`.
- الترقية غير هدامة: النسخة المنشورة بمخطط 1 لا تُعاد كتابتها وتعمل بقيم افتراضية (`Effective`).
- `LockDocumentDate` يبقى مستقلاً عن تاريخ القطع (VP-009/010).

## التحقق (نُفِّذ فعلاً)
| المجموعة | النتيجة |
|---|---|
| بناء Release للحل | 0 أخطاء، 0 تحذيرات |
| Domain | 241/241 (جديد: `VoucherPatternSchemaTests`، 18 حالة) |
| SQL `PatternEngineSqlTests` | 6/6 (جديد: التخزين بمخطط 2 + 4 رفضات بأكواد ثابتة) |
| SQL: ترقيم الأنماط، سندات Treasury، ترحيل المبيعات، فواتير الشراء | 101/101 (ضمن 102 مع الجديد) |
| تجانس OpenAPI/العميل المولَّد | ينجح (`-Check`) |

**فشل لا علاقة له بهذه البوابة (قائم مسبقاً):**
- `ApiContractTests.ContractsExposeNoBinaryFloatingPointOrRawDecimalMembers`: أعضاء decimal خام في DTOs التجميع/الأصول (`AssemblyDocumentLineDto…`) تخالف قاعدة «decimal strings». DTO الجديد لا يحوي decimal.
- `AmeenPatternManagerTests.EachFamilyOpensThePatternListNarrowedToIt` (Desktop) وبقية فشل Desktop المذكور سابقاً.

## لم يُنفَّذ عمداً
- **قرار 4 (لا منح = لا أحد):** لم أطبقه بعد. أثره على البيانات القائمة: `GetRunnablePatternsForBranchAsync` (`SqlPatternService.cs:484`) والترقيم (`SqlPatternService.Numbering.cs:24,137`) يعدّان «بلا منح = كل الفروع»، والاختبار `BranchGrantFiltersRunnablePatterns` يثبت ذلك عمداً؛ والمبيعات لا تتحقق من المنح. تطبيقه الآن يخفي أي نمط قديم بلا منح من الواجهة. **المقترح:** ترحيل بيانات إضافي يمنح كل فروع الشركة للأنماط الخالية من المنح (يحفظ السلوك الحالي)، ثم توحيد القاعدة وتحديث الاختبار. أحتاج موافقتك على هذا الترحيل قبل التنفيذ.
- لا شاشة ولا أثر على السندات المرحَّلة.

## Rollback
عكس Commit البوابة. لا هجرة SQL فيها ولا تعديل لبيانات.

## قرار المالك (19 نوفمبر 2026)
**GATE-001 معتمد فقط.** ترحيل منح الفروع للأنماط القديمة غير مأذون به؛ قاعدة «لا أحد» تبقى معلّقة، والسلوك الحالي («بلا منح = كل الفروع») كما هو. الانتقال إلى GATE-002 لم يُؤذن به بعد.

## قرار مطلوب (سابق)
1. الموافقة على GATE-001 كما هو؟ 2. الموافقة على ترحيل منح الفروع للأنماط القديمة قبل توحيد قاعدة «لا أحد»؟ 3. الانتقال إلى GATE-002 (تصفية الحسابات + تاريخ القطع + قواعد الخادم + بداية الحساب الموحد بالخيار A)؟
