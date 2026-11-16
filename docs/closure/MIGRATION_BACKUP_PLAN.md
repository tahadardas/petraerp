# خطة الترحيل والنسخ الاحتياطي (مسودة — لم تُجرَّب استعادة فعلية)

## ما يوجد في الشيفرة (مقروء، غير مُجرَّب هذه الجلسة)
- `BackupRestoreEndpoints`: نسخة SQL أصلية مع `CHECKSUM`، `RESTORE VERIFYONLY`، واستعادة تجريبية مع `DBCC CHECKDB` (`/administration/backups`, `/verify`, `/test-restore`) بصلاحية `System.Database.Manage`.
- اختبارات SQL موجودة: `BackupAndRestoreSqlTests`, `BackupRestoreCoordinatorSqlTests`, `BackupArchiveTests` — **لم تُشغَّل ضمن هذه الجلسة**.

## هجرة EF الجديدة الوحيدة هذه الجلسة
`20261114145007_ModuleActionAuditEvent` (قاعدة النظام المركزية): تغيّر `CK_SecurityAuditEvents_EventType` من `BETWEEN 1 AND 25` إلى `1 AND 26`.
- **Up/Down** موجودان في الملف (Down يعيد 25؛ يفشل إن وُجد صف نوعه 26 — يجب حذف/أرشفة تلك الصفوف أولًا).
- **قبل التطبيق على أي قاعدة حقيقية:** (1) نسخة كاملة بـ`BACKUP DATABASE … WITH CHECKSUM` للقاعدة المركزية؛ (2) `RESTORE VERIFYONLY`؛ (3) استعادة إلى قاعدة اختبار وتطبيق الهجرة عليها؛ (4) تشغيل `ModuleFinancialPostingGatewaySqlTests` على النتيجة؛ (5) ثم التطبيق. **لم يُطبَّق على أي قاعدة غير قواعد الاختبار المؤقتة.**

## تغييرات سلوكية في الاتصال (لا مخطط)
- `ReadCommittedConnectionInterceptor`: كل اتصال يبدأ READ COMMITTED. الرجوع: إزالة التسجيل من المواضع الأربعة في `PetraServiceRegistration`/`AccountingDbContextFactory`/`SqlFiscalDatabaseResolver`.
- `PETRAERP_DEADLOCK_RETRIES=0` يعطل إعادة الوحدة.

## غير منفَّذ
RPO/RTO مقاسان، نسخ قواعد الموديولات والمرفقات، تشفير النسخ، محاكاة انقطاع منتصف النسخ/الاستعادة، سياسة retention، استعادة لقاعدة سنة وشركة مع Registry، استعادة موديول Sample من نسخة احتياطية.
