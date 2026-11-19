# VoucherPattern_API_Impact — AMN-VPAT-GATE-000 (تدقيق فقط)

**SHA المُدقَّق:** `6798286cf801068d47f86af1017a3207d9b517de` (يطابق SHA الوارد في SRS v2.6؛ لم أفترضه، قرأته من `git rev-parse HEAD`).
**لم يُعدَّل أي كود.** التشغيل الوحيد: 27 اختبار SQL (Treasury/Pattern/Party/JournalVoucherHeader) نجحت كلها على هذا SHA.
أرقام الأسطر من المصدر في المصفوفة `VoucherPattern_Evidence_Matrix.csv`.

## 1. الموجود فعلاً (قاعدة قابلة للبناء)
- محرك أنماط: إنشاء/نسخ/نشر/تعطيل/ترقيم ذري/منح فروع (`PatternEndpoints.cs:23-91`، اختبارات `PatternEngineSqlTests`).
- سند القبض/الدفع يحفظ `PatternRevisionId` ويتحقق من أنه منشور وفعال ومن نوع العملية وله منحة للفرع (`Drafts.cs:147-153`).
- يطبّق النمط على الخادم: حساب الصندوق/البنك، العملة، `LockDocumentDate`، `RequiresApproval`، الترقيم.
- تقارير الأستاذ تصفّي بالمصدر وبنسخ الأنماط (`SqlProfessionalReportService.Sources.cs:10-37,77-81`).

## 2. أين يتغير الـAPI (حسب الفجوات المؤكدة)
| التغيير | الأثر | يكسر عميلاً؟ |
|---|---|---|
| `VoucherFormKind` على `DocumentPattern`/Revision (`PatternConfiguration.cs`، `PatternContracts.cs`) | حقل جديد اختياري، افتراضي `GenericVoucher` | لا |
| `SchemaVersion` 2 في `PatternConfiguration` (`:9`) | Normalize/Upgrade عند القراءة، لا إعادة كتابة للنسخ المنشورة | لا |
| سياسات جديدة: Lookup، Cutoff، PerLineJournal، AutoPost، ApprovalSteps، Attachments | كتل اختيارية في JSON النسخة؛ الغياب = السلوك الحالي | لا |
| `GET patterns/{id}/effective` (مقترح) | قراءة نسخة فعالة مقيدة بالشركة/الفرع/الصلاحية | إضافة فقط |
| تصفية البحث بـ`patternRevisionId` على `ledger/chart/accounts` و`cost-centers` | معامل اختياري | لا |
| ربط سند اليومية/القيد بنمط: `PatternRevisionId` على `JournalVoucherHeaders` | عمود قابل للإفراغ + FK | لا (الصفوف القديمة NULL) |
| سند قبض/دفع متعدد الطرق | نقطة/عقد جديد يجمع الطرق في وثيقة؛ `SettlementMethod` الحالي يبقى | إضافة فقط |
| الحساب الموحد للطرف | يغيّر `SqlPartyService` ومحلّل حساب الذمة؛ انظر خطة السلامة | **يؤثر على بيانات**، انظر الخطة |

## 3. تناقضات في الشيفرة الحالية يجب إصلاحها قبل البناء فوقها
1. **القائمة مقابل الحفظ:** `SqlPatternService.cs:484` يعدّ نمطاً بلا منح فروع متاحاً لكل الفروع، بينما `Drafts.cs:151-153` يرفضه عند الحفظ. نمط ظاهر في القائمة قد يفشل لاحقاً.
2. **عقد الأنواع:** `PatternContracts.cs:6-20` ينقصه الأنواع 13-16 الموجودة في `BusinessOperationKind.cs:7-25`.
3. **`LineFields`:** مخزَّنة ومعروضة عناوين فقط (`VouchersViewModel.cs:390`)؛ الخادم لا يفرض الإظهار ولا الإلزام، خلافاً لما توحي به المواصفة (VP-020).
4. **حد الموافقة:** `ThresholdAmount` يُطبَّق في المبيعات فقط (`SqlSalesInvoiceService.Posting.cs:74`) وليس في سندات Treasury (`Posting.cs:58`).
5. **سند اليومية خارج الأنماط:** `JournalVoucherHeaderRow.cs:12-18` يحمل `Kind=DAILY` و`CashAccountId` فقط؛ لا نمط ولا ترقيم نمط. هذا يعارض «كل سند تابع لنمط منشور» (MASTER AMN-VPAT-026).

## 4. نقاط تحتاج قراراً قبل GATE-001
- هل يبقى `CashJournal/ManualJournal` عمليتين للنمط أم يُمثَّل سند اليومية كـ `VoucherFormKind=GenericVoucher` فوق `CashReceipt/CashPayment`؟ (الأول أقل تغييراً على Treasury.)
- هل «الدفع/القبض المتعدد الطرق» مستخدم فعلياً لدى المحاسب (SRS: «إن ثبت»)؟
- قيد الأمين «لا مصادقة مع مدين ودائن معاً»: يُحاكى أم يُتجاوز؟ (VP-024)
