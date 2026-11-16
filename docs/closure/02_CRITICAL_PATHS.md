# المسارات الحرجة (Gate A)

كل مسار: نقطة الدخول ← الخدمة ← ما يُكتب ← الضمانات ← الاختبار الذي يثبتها. "مُقاس تحت الحمل" لا يُكتب إلا حيث وُجد قياس فعلي (انظر `04_BASELINE_METRICS.md`).

| المسار | الدخول (API) | الضمانات | اختبار مُثبِت | تحت الحمل |
|---|---|---|---|---|
| ترحيل فاتورة مبيعات | `POST …/sales-invoices/{id}/post` (مفتاح تكرار) | معاملة واحدة تملكها `SqlPostingService`، قيد متوازن، تحقق مخزون سالب، إعادة المحاولة على 1205 بنفس المفتاح | `SalesPostingSqlTests*`, `PostingRequestIdempotencySqlTests` | نعم: 50 مستخدمًا 0 deadlock/0 فرق |
| ترحيل فاتورة مشتريات | `POST …/purchase-invoices/{id}/post` | كما أعلاه + طبقات FIFO | `PurchaseInvoiceSqlTests*`, `PurchaseInvoiceReversalSqlTests` | ضمن اختبار 150 طلبًا مختلطًا |
| سند قبض/دفع | `POST …/treasury/vouchers/{id}/post` | تخصيص على بنود مفتوحة تحت قفل، قيد مُرحَّل | `TreasuryVoucherSqlTests`, `OpenItemSettlementSqlTests` | ضمن اختبار 150 طلبًا |
| مستندات المخزون (تحويل/تسوية/جرد) | `…/inventory/documents` | القيد ونقاط المخزون معًا؛ ربط نطاق الكتابة بالعملية | `InventorySqlTests`, `InventoryDocumentHttpSqlTests` | **غير مقيس** |
| قيد يدوي | `…/ledger/journals` (مسودة ← اعتماد ← ترحيل) | حالات متسلسلة؛ صلاحية لكل خطوة | `JournalDraftLifecycleSqlTests`, `PostingSqlTests` | غير مقيس |
| عكس مستند | `…/reverse` لكل نوع | قيد عكسي مرتبط، الأصل يصبح معكوسًا | `PurchaseInvoiceReversalSqlTests`, `SalesReturnSqlTests` | غير مقيس |
| ترحيل جماعي | `…/posting-batches` | دُفعة بمفتاح؛ ذرية لكل مستند | `BulkDocumentPostingSqlTests` | غير مقيس |
| إعادة تقييم العملات | `…/fx-revaluation` | طلب بمفتاح تكرار، قيد فروقات | `FxRevaluation*SqlTests` (مشروع API) | غير مقيس |
| ترحيل موديول خارجي | `…/modules/*` | توقيع حزمة، بوابة ترحيل واحدة، وسم `[module:id]` ومفتاح `#key` | `ExternalModuleHttpSqlTests`, `ModuleFinancialPostingGatewaySqlTests` | ثلاثة مستندات بالتوازي فقط |
| تسوية بنك | `…/treasury/bank-reconciliations` | فرق صفر للإكمال، سطر يُسوّى مرة واحدة | `BankReconciliation*SqlTests` | غير مقيس |

## نقاط الاختناق المعروفة (مُعالَجة)
1. صف `audit.ActorReferences`: قراءة مشتركة أولًا بدل `UPDLOCK` الدائم.
2. تسريب `SERIALIZABLE` عبر تجمع الاتصالات: `ReadCommittedConnectionInterceptor`.
3. استنزاف التجمع: `FinancialWriteGate` (24 كتابة متزامنة افتراضيًا) ورد 503 قابل لإعادة المحاولة.

## غير مُنجَز
- مصفوفة ترتيب الأقفال (Lock Order Matrix) لكل مسار؛ اختُبر مسار المبيعات فقط تحت الضغط الشديد.
- قياس المخزون والتسويات والعملات تحت الحمل.
