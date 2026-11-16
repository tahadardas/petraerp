# مصفوفة ترتيب الأقفال (المرحلة 2.5) — 2026-11-15

**المصدر:** جرد كل مواضع `UPDLOCK/HOLDLOCK/sp_getapplock` في `src/PetraERP.Infrastructure` (قراءة الشيفرة، لا تتبّع زمن تشغيل شامل). كل الكتابات المالية داخل معاملة `SERIALIZABLE`؛ لم يُزَل أو يُخفَّف أي مستوى عزل.

## الترتيب العام المُلاحَظ (من الأعلى إلى الأدنى؛ يجب ألا يُعكس)
| الرتبة | المورد | النمط | من يأخذه |
|---|---|---|---|
| 1 | `sp_getapplock 'PetraERP.Security.Administration'` | **Shared** (معاملة) | كل كتابة مالية/مخزنية: `InventoryWriteScope`, `SqlPostingService`, `SqlSalesInvoiceService.PostingSupport` |
| 1′ | نفس المورد | **Exclusive** | `SecurityAdministrationLock` (تغيير المستخدمين/الأدوار) — يمنع الكتابة المالية أثناء تغيير الصلاحيات |
| 2 | صف `system.Companies` | UPDLOCK,HOLDLOCK | المخزون، الترحيل، المبيعات/المشتريات/السندات، العملات، التقويم، التزويد |
| 3 | صف `system.Branches` | UPDLOCK,HOLDLOCK | المخزون، الترحيل، المبيعات/المشتريات/السندات |
| 4 | صف المستند (`sales.Invoices` / `purchases.Invoices` / `treasury.Vouchers` / `ledger.JournalEntries`) | UPDLOCK,HOLDLOCK | خدمة المستند المعنية؛ قيدان معًا (عكس) يُقفلان بترتيب ثابت (`SqlPostingService` id1/id2) |
| 4b | فاتورة مبيعات مرتبطة بسند | UPDLOCK,HOLDLOCK | `SqlTreasuryVoucherService.Posting` **بعد** صف السند (سند ← فاتورة) |
| 5 | `ledger.AccountingPeriods` ثم `ledger.FiscalYears` | UPDLOCK,HOLDLOCK | الترحيل وإغلاق الفترة بالترتيب نفسه (تعليق صريح في `SqlAccountingCalendarService`) |
| 6 | `ledger.Accounts` / `ledger.CostCenters` (للتحقق عند الترحيل) | UPDLOCK,HOLDLOCK | `SqlPostingService` وإدارة الدليل |
| 7 | `ledger.PostingSequences` | UPDATE WITH UPDLOCK,HOLDLOCK | آخر خطوة قبل كتابة القيد (تسلسل الترحيل) |
| 7b | ترقيم المستند (applock للنمط + `DocumentNumberCounters`) / `inventory.DocumentSequences` | applock / UPDLOCK | الحفظ-والمعالجة والمخزون |
| 8 | `audit.ActorReferences` | قراءة مشتركة؛ UPDLOCK فقط عند إنشاء الصف | كل `SaveChanges` (الحل الجذري لـdeadlock الأول) |

مسارات خارجة عن الشجرة: إعادة تقييم العملات `sp_getapplock` من نوع **Session** (3 ثوانٍ مهلة) قبل أخذ قفل الشركة؛ التزويد `applock` حصري؛ `SqlAdministratorConfirmation` يقفل صف المستخدم في قاعدة المركز (قاعدة منفصلة، لا تتداخل مع قفل السنة).

## ما تثبته الاختبارات (وما لا تثبته)
- **مُثبَت بالتشغيل:** مبيعات حتى 50 مستخدمًا (0 deadlock بلا إعادة محاولة)، مشتريات+مبيعات+سندات 150 طلبًا، مستندات المخزون (صرف+نقل) 25 مستخدمًا، 0 فرق دفتر/مخزون، بلا أرقام مكررة.
- **غير مُثبَت:** عكس المستندات، الجرد (Count)، إعادة التقييم، وتسوية البنك تحت الحمل؛ الترتيب أعلاه مبني على القراءة. أي مسار جديد يجب أن يلتزم بالرتب 1→8 ويُضاف له اختبار حمل قبل الاعتماد.

## قرارات للمرحلة 2.5
- **لا أقفال Warehouse/Item بدل قفل الشركة الآن:** الشرط في البرومبت (اختبارات تثبت عدم السالب وعدم ازدواج الترحيل تحت هذا التغيير) غير متوفر؛ القفل الحالي أوسع من اللازم لكنه سليم، وكلفته معروفة (انظر `INVENTORY_LOCK_CONTENTION.md`). يُعاد فتح القرار بعد: (أ) قياس تأثير تقليل الأوامر، (ب) اختبارات تزامن مخصصة للرصيد السالب على مستودعين.
- **`SERIALIZABLE` باقٍ** في كل الكتابات المالية.
