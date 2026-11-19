# خطة Migrations غير هدامة — AMN-VPAT (خطة فقط، لا تنفيذ)

مبدأ: كل تغيير **إضافي** (أعمدة/جداول جديدة، قيم افتراضية تعيد السلوك الحالي). لا حذف ولا تعديل لقيد مرحَّل أو مرجع مستند. الهجرات المعتمدة السابقة غير قابلة للتعديل (حارس المخطط `AccountingDatabaseReadiness`)، فكل خطوة هجرة جديدة بتاريخ لاحق.

## M1 — مخطط النمط v2 (بلا هجرة SQL)
- `PatternConfiguration` يُخزَّن JSON في `PatternRevisions.ConfigurationJson`؛ إضافة كتل اختيارية (`VoucherFormKind`, `Lookup`, `Cutoff`, `PostingGeneration`, `AutoPost`, `ApprovalSteps`, `Attachments`) و`SchemaVersion=2`.
- القراءة: نسخة بـ`SchemaVersion=1` تُقرأ بقيم افتراضية تعطي السلوك الحالي حرفياً؛ **لا إعادة كتابة** للنسخ المنشورة.
- التراجع: لا شيء لتراجعه (JSON متوافق للخلف).

## M2 — ربط سند اليومية بنمط
- `ledger.JournalVoucherHeaders`: عمود `PatternRevisionId uniqueidentifier NULL` + FK إلى `PatternRevisions` + فهرس. الصفوف الحالية تبقى NULL (تُعرض بالنمط الافتراضي «يومية» عند القراءة، لا كتابة).
- قيد CHECK على `Kind` (`JournalVoucherHeaderRow.cs:33`) يُوسَّع بقيم جديدة إن لزم عبر هجرة مستقلة.
- التراجع: حذف العمود (لا بيانات تعتمد عليه قبل GATE-006).

## M3 — حقول على السند (Treasury)
- `treasury.Vouchers`: `VoucherFormKind tinyint NOT NULL DEFAULT 1` (GenericVoucher)؛ لاحقاً جدول `treasury.VoucherSettlementMethods` (VoucherId, Method, Amount, Reference) فقط عند اعتماد GATE-004، والسطر الأحادي الحالي يُعدّ طريقة واحدة افتراضياً.
- لا لمس لـ`PartyControlAccountId` القائم.

## M4 — سياسة الموافقات
- جدول `patterns.ApprovalStepDefinitions` (يخص النسخة المنشورة، غير قابل للتعديل بعد النشر) وجدول `treasury.VoucherApprovals` (قرار لكل خطوة: مستخدم، وقت، سبب). تُسجَّل الموافقات في المستند وسجل التدقيق؛ نشر نسخة جديدة لا يمس موافقات قديمة (VP-025).

## M5 — الحساب الموحد للطرف (الأخطر) — انظر `UnifiedPartyAccount_Safety_Plan.md`
- لا هجرة بيانات تلقائية في GATE-001. المرحلة الأولى: **سياسة** وقرار؛ الهجرة نفسها لاحقاً مع نسخة احتياطية واستعادة تجريبية.

## قواعد التنفيذ لكل هجرة
1. معاينة SQL (`dotnet ef migrations script`) تُرفق بالبوابة. 2. نسخة احتياطية + استعادة على خادم تجريبي قبل التطبيق على بيانات حقيقية. 3. تُطبَّق على قاعدة اختبار معزولة أولاً مع `AccountingDatabaseReadiness`. 4. لا تغيير `PatternRevisionId` لسند مرحَّل أبداً.
