# 00 — الحالة الحالية (Gate A + Gate B جزئي)

**التاريخ:** 2026-11-14 — **Base commit:** `2e8e365` + تعديلات غير مُلتزَمة (لا commit حتى الآن) — **SDK:** 10.0.402 (`.tools/dotnet/10.0.402`) — **الخادم:** SQL Server Express المحلي (`.\SQLEXPRESS`).
مصدر الأدلة: `.artifacts/gate-a/*`, `.artifacts/gate-b/*`.

التصنيف: **IV** = Implemented+Verified، **IU** = Implemented+Unverified، **P** = Partial، **M** = Missing، **B** = Broken، **BL** = Blocked.

| # | العنصر | الحالة | الدليل / المسار | الأولوية |
|---|---|---|---|---|
| 1 | `dotnet restore --locked-mode` + Build Release (0 تحذير / 0 خطأ) | IV | `.artifacts/gate-a/build-release.txt` | — |
| 2 | اختبارات Release قبل التعديل: Domain 217، Application 94، Modules 30، Reporting 40، Infrastructure 120 ناجح (285 صريح)، Api 50 ناجح (102 صريح)، Desktop 623 ناجح (9 متخطاة) | IV | `.artifacts/gate-a/tests-release-baseline.txt` | — |
| 3 | `ReferenceDataCache`: المفتاح company-only، إلغاء المنتظر الأول يلغي الجميع، نتيجة قديمة تُعيد نفسها بعد الإبطال | **B → IV** | `src/PetraERP.Desktop/Caching/ReferenceDataCache.cs`، 10 اختبارات `ReferenceDataCacheTests` (3×تكرار نجح) | P0 |
| 4 | `TransientRetryHandler`: يبحث عن النص `idempotencyKey` في الجسم | **B → IV** | `Connection/TransientRetryHandler.cs`، 19 اختبارًا `TransientRetryIdempotencyRulesTests` | P0 |
| 5 | تعارضات SQL 1205 على مسارات الترحيل: تظهر 503 للمستخدم وتترك مستندات Approved غير مُرحَّلة | **B → IV (تخفيف)** | `src/PetraERP.Api/Security/FinancialRequestScope.cs` + `DeadlockRetry.cs`؛ قبل/بعد في `04_BASELINE_METRICS.md` | P0 |
| 6 | **السبب الجذري للـdeadlock** (مسار المبيعات) | **B → IV**: التُقطت الرسوم (`.artifacts/gate-b/deadlock-*.xml`) وأُصلح تعارضان: صف `ActorReferences` الساخن، وتسريب العزل SERIALIZABLE عبر تجمع الاتصالات. **Lock Order Matrix لبقية المسارات لم تُرسم** | `SQL_DEADLOCK_ROOT_CAUSE.md` | P0 |
| 7 | سجل Idempotency دائم (WorkspaceId, OperationType, Key, PayloadHash, State, ReceiptId) على كل العمليات | P — موجود لترحيل القيود/المبيعات/الوثائق؛ لم يُراجَع لبقية العمليات | `DocumentIdempotencyHttpSqlTests` (موجود) | P0 |
| 8 | حالة Committed-but-response-lost (استعلام حالة بالمفتاح) | M | — | P0 |
| 9 | 10/25/50 مستخدمًا (مبيعات، ترحيل فعلي) | **IV**: 0 فشل و0 deadlock عند 1/5/10/25/50 بلا إعادة محاولة؛ 182/182 مرحَّلة؛ فرق القيود 0؛ أرقام مكررة 0. **المشتريات/السندات/المخزون تحت الضغط لم تُقَس** | `concurrency-isolationfix-noretry-50users.txt` | P0 |
| 10 | Soak ≥ ساعة | P — 120 ثانية فقط (133 طلبًا، 0 أخطاء، heap مستقر)؛ الساعة لم تُشغَّل | `.artifacts/gate-b/soak-120s-after.txt` | P0 |
| 11 | `GET/POST /inventory/documents` يفشل `write_scope_required` | **B → IV** | `SqlInventoryDocumentService.Scope.cs`، `InventoryDocumentHttpSqlTests` | P0 |
| 12 | منصة الموديولات: تشغيل خادم، HTTP، تدقيق، بوابات ترحيل ومخزون، **توقيع ECDSA حقيقي وإبطال ناشر** | IV (جزئي) | `ExternalModuleHttpSqlTests` (حزمة موقّعة)، `PackageSigningTests`؛ **عزل العملية ومخططات الموديول والأحداث غير منفذة** — `MODULE_SDK_COMPATIBILITY.md` | P1 |
| 13 | توحيد نوافذ المخزون/السندات/المشتريات/المبيعات (شريط أوامر + مجاميع + تنقل) | IU بصريًا — اختبارات ربط فقط؛ صورة المخزون وحدها فُحصت | `docs/UX_AUDIT/WINDOW_UNIFICATION_SPEC.md` | P2 |
| 14 | كتالوج التقارير: 77 مكتمل / 13 جزئي / 15 غير منفذ | **لم يُعاد إثباته** | `ReportMasterCatalog.cs` | Gate E |
| 15 | Gates C–H | NOT STARTED | — | — |

## ما لم يُنفَّذ بعد (صراحة)
الوثائق `01`–`04` أدناه مبنية على قراءة وقياس فعلي لما تم لمسه فقط؛ **خريطة الاعتماديات الكاملة (Endpoint→Service→SQL→DTO→ViewModel→XAML) ومقارنة OpenAPI ببقية النظام لم تُنجز**.
