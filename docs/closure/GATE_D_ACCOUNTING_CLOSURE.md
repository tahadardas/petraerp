# Gate D — الإغلاق المحاسبي (2026-11-15)

**الحالة: PARTIAL** — ما نُفِّذ مُثبَت بتشغيل فعلي؛ فجوات صريحة أدناه. لا PASS للبوابة.

## ما شُغِّل فعلًا
58 اختبار SQL صريح على SQL Express محلي، **58 نجح، 0 فشل** (4د 54ث). الناتج الخام: `.artifacts/gateD-run1.txt`.
الفئات: LedgerSql, LedgerFinancialPrecisionSql, AccountingReconciliationSql, AccountingReconciliationRolloverSql, FiscalYearRolloverSql, FxMonetaryEligibilitySql, OpenItemSettlementSql, OpeningBalanceSql, PatternNumberingSql, JournalDraftLifecycleSql, PostingSql, PostingRequestIdempotencySql, PurchaseInvoiceReversalSql, SalesReturnSql, InventorySql, FifoWarehouseSql, TreasuryVoucherSql, TrialBalanceOptionsSql, ProfessionalStatementReconciliationSql, InventoryOpeningSql, AccountingCalendarSql.

## التغطية مقابل مجالات البوابة
| المجال | الدليل | الحكم |
|---|---|---|
| دفتر الأستاذ/القيود/الدقة | LedgerSql, LedgerFinancialPrecision, JournalDraftLifecycle, LedgerReportSql, LedgerKeysetPaging | مُغطّى |
| ذمم مدينة/دائنة (تسوية البنود المفتوحة) | OpenItemSettlementSql, ProfessionalStatementReconciliationSql | مُغطّى جزئيًا |
| تقييم المخزون مقابل الدفتر | `SqlAccountingReconciliationService.CheckInventoryVsGlAsync` + AccountingReconciliationSql (نجح) | مُغطّى عبر خدمة المطابقة؛ لا اختبار منفصل بأرقام تقييم FIFO كبيرة |
| العملات/فروق الصرف | FxMonetaryEligibilitySql؛ نقطة `FxRevaluationEndpoints` موجودة | تقييم إعادة التقييم نفسه بلا اختبار SQL مسمّى هنا |
| السنوات المالية وترحيل الأرصدة | FiscalYearRolloverSql, AccountingReconciliationRolloverSql, OpeningBalanceSql | مُغطّى |
| ترقيم المستندات | PatternNumberingSql (يشمل 50 تخصيصًا متزامنًا) | مُغطّى |
| **تسوية بنك (CASH-04)** | `SqlBankReconciliationService` + 6 نقاط API + ترحيلان (annual وpetra) + صلاحية `Treasury.BankReconciliations.Manage`؛ `BankReconciliationSqlTests` و`BankReconciliationAnnualSqlTests` نجحا | **مبنية بالكامل: خلفية + شاشة Desktop** (مسار `bank-reconciliation`، قائمة السندات) |

## مفتوح (لا يُنسب له نجاح)
1. تسوية البنك: الخدمة والـAPI والاختبارات منجزة؛ المتبقي: اختبار HTTP، ودعم عملة أجنبية (التسوية بالعملة الأساسية فقط)، وكشف بنكي مستورد/مطابقة تلقائية. بند CASH-04 في كتالوج التقارير تُرك كما هو عمدًا: هو سير عمل وليس تقريرًا، وتغييره يغيّر أرقام تغطية الكتالوج.
2. ~~إعادة تقييم العملات~~ — **مُغطّاة**: `FxRevaluationHttpSqlTests` + `FxRevaluationRegressionSqlTests` (مشروع API) **41 من 41 نجحت** (14د 24ث، `.artifacts/gateD-fx.txt`)؛ تشمل الاستئناف بنفس المفتاح بعد إعادة فتح فترة، ورفض الإغلاق، وإعادة التقييم بعد تسوية جزئية. لم تُقَس تحت حمل متزامن.
3. تقييم المخزون عند أحجام كبيرة: مستندات المخزون (صرف داخلي + نقل) قيست تحت 1–25 مستخدمًا بصحة تامة (انظر أدناه)؛ تقييم FIFO بأحجام كبيرة غير مقيس.
4. ~~تزامن ترقيم المستندات~~ — **مُغطّى**: `PatternNumberingSqlTests.FiftyConcurrentAllocationsProduceUniqueMonotonicNumbersWithoutGaps` (50 تخصيصًا متزامنًا، أرقام فريدة متزايدة بلا فجوات) ضمن الـ58 الناجحة. المتبقي: ترقيم تحت حمل HTTP مختلط.
5. اختبارات API الصريحة (`ReconciliationNegativeHttpSqlTests` وغيرها) لم تُشغَّل ضمن هذا التشغيل.

## تسوية البنك — القواعد المُنفَّذة والمُثبَتة (2026-11-15)
- حساب ترحيل فعّال، مسودة واحدة مفتوحة لكل حساب/فرع (فهرس فريد مُرشَّح)، تاريخ الكشف يجب أن يتجاوز آخر تسوية مكتملة.
- السطور المؤهلة: مرحَّلة/معكوسة، حساب وفرع التسوية، تاريخها ≤ تاريخ الكشف؛ كل سطر يُسوّى مرة واحدة (فهرس فريد على `JournalLineId`)؛ الطلب كلّه أو لا شيء.
- الفرق = رصيد الكشف − (ما سوّته التسويات المكتملة قبلها + ما سوّته هذه). الإكمال فقط عند صفر تمامًا.
- المكتملة مقفلة (سياسة الكتابة + الخدمة)؛ إصدار قديم → تعارض تزامن؛ الإلغاء يحرّر السطور.
- الاختباران المنفذان: مسار قديم (`PetraDbContext`) ومسار الإنتاج السنوي (`AccountingDbContext`: نطاق كتابة، ActorReferences، سياسة الطفرات).
- نتائج بعد التغيير: Infrastructure الافتراضي 409/0 فشل، API 154/0، Desktop 665/0، اختبارات SQL الصريحة للتسوية والنموذج وترقية المدير 6/0.

### شاشة Desktop (2026-11-15)
- `BankReconciliationViewModel` + `BankReconciliationView` + `IBankReconciliationClient`؛ مسجّلة في DI والقائمة (السندات ← تسوية بنك) والتنقل وقوالب المستندات؛ نصوص عربية/إنجليزية (`BKR_*`) وتسمية الصلاحية.
- القاعدة: الخادم يملك كل الأرقام؛ الشاشة ترسل اختيارات المستخدم وتعرض الرد، ولا يُعرض «إكمال» إلا عند فرق صفر من الخادم؛ المستخدم بصلاحية العرض فقط لا يستطيع البدء أو التعديل.
- الاختبارات: `BankReconciliationViewModelTests` 3/3 (الإكمال عند الصفر فقط، عرض فقط، الإلغاء)؛ مجموعة Desktop الكاملة 668/668. لم تُجرَّب الشاشة فعليًا بالنقر في نافذة WPF حيّة ولا لُقطات شاشة.

## أدلة 2026-11-15 (الدورة الأخيرة)
- **إعادة التقييم:** 41/41 (انظر البند 2 أعلاه).
- **النسخ والاستعادة (Gate H جزئيًا):** 13/13 اختبار نسخ/استعادة + `RestoredLedgerInvariantsSqlTests`: نسخة بـCHECKSUM واستعادة وDBCC، والنسخة المستعادة تطابق المصدر في عدد القيود والسطور ومجموعَي المدين والدائن وبلا قيد غير متوازن أو تكرار تسلسل. SQL Express محلي فقط.
- **مستندات المخزون تحت الحمل** (`MeasureConcurrentInventoryIssuesAndTransfersKeepStockAndLedgerExact`، صرف داخلي + نقل بين مستودعين، `.artifacts/gateB-inventory-bench.txt`): 1/5/10/25 مستخدمًا = 2/10/20/50 مستندًا، **كلها رُحِّلت، 0 خطأ 5xx، فرق الدفتر 0، لا أرقام مكررة، المخزون مطابق تمامًا** (5000 − 41 صرفًا = 4959؛ المستودع الهدف 41 = عدد النقلات).
- **تحذير أداء صريح:** صحة تامة لكن بطء شديد: عند 25 مستخدمًا وسيط الطلب ≈ 27ث وأقصاه ≈ 29ث (2.8ث لمستخدم واحد). الأرقام على جهاز مثقل وتتذبذب، لكن الاتجاه يدل على تسلسل مرتفع على أقفال المخزون؛ لا يُعدّ ذلك مقبولًا لهدف زمني حتى يُحقَّق ويُحسَّن.
- **غير مقيس بعد:** التسويات (الجرد/Count) تحت الحمل، وإعادة التقييم تحت التزامن، وSoak ساعة.
