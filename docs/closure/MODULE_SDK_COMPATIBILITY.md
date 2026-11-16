# منصة الموديولات — العقود والثقة والتوافق (حالة Gate G الجزئية)

## ما هو منفَّذ ومثبت
| البند | الحالة | الدليل |
|---|---|---|
| عقد `ModuleIdentity/Descriptor/RequestContext` + `ContractVersion` (حاليًا 1) ورفض عقد غير مدعوم | IV | `ModulePackageVerifier`، `PackageVerificationTests` |
| دورة حياة Verified→Staged→Installed→Enabled/Disabled/Failed ومنع القفزات غير القانونية؛ التعطيل لا يحذف البيانات | IV | `ModuleManagerTests` |
| توافق الإصدارات `Min/MaxCoreVersion` والاعتماديات (`^ ~ >=`) وكشف الدورات | IV | `ModuleManagerTests` |
| **توقيع رقمي حقيقي**: ECDSA P-256 / SHA-256 على بيان canonical (يغطي بصمات كل الملفات)، مفتاح عام لكل ناشر، إبطال ناشر؛ اسم الناشر وحده لا يكفي (افتراضي الخادم `RequireSignedPackages=true`) | IV | `PackageSigningTests` (7): موقّعة تُقبل؛ غير موقّعة/مفتاح آخر/بيان معدّل بعد التوقيع/ناشر مُبطَل/ناشر بلا مفتاح تُرفض ولا يُكتب شيء |
| إعادة فحص بصمة كل ملف عند كل تحميل (ملف عُدِّل بعد التثبيت لا يُحمَّل) | IV | `ModuleRuntimeTests.FileChangedAfterInstallation_IsNeverLoaded` |
| تشغيل خادم: Runtime، تفعيل/إيقاف عبر HTTP، أوامر الموديول بصلاحيات مقيّدة بما اعتمده المشغّل، بوابات ترحيل/مخزون/مراجع/تدقيق عبر نطاقات منفصلة | IV على SQL | `ExternalModuleHttpSqlTests`، `Module*GatewaySqlTests` |
| تدقيق الإجراءات غير المالية في سجل الأمن الدائم (`ModuleAction=26`)؛ الأثر المالي موسوم باسم الموديول وعكسه مقصور على صاحبه | IV على SQL | `ModuleFinancialPostingGatewaySqlTests` |
| منع zip-slip وملفات مدسوسة خارج البيان | IV | `PackageVerificationTests`، `DirectoryModuleStager` |

## غير منفَّذ (يمنع تركيب موديولات خارجية غير موثوقة)
1. **العزل**: الموديول يعمل داخل العملية في `AssemblyLoadContext` — ليس Sandbox أمنيًا. المسموح حاليًا: موديولات الطرف الأول أو ناشر موثوق فعليًا. عملية منفصلة بصلاحيات محدودة + IPC مصادَق: **لم تُصمَّم بعد**.
2. حدود CPU/ذاكرة/مهلة/طابور وcircuit isolation للموديول.
3. حالات `PendingMigration/NeedsRestart`، وتراجع الترقية عند الفشل مجرَّب (تحقق المبدأ في المدير؛ لا اختبار تراجع كامل).
4. مخططات الموديول الخاصة (schema لكل موديول) وMigration versioning؛ لا يوجد بعد.
5. Transactional Outbox/Inbox والأحداث (`JournalPosted`…): غير موجودة.
6. واجهة WPF «مدير الموديولات» وسجلات التنقل/الصفحات/التقارير declarative: غير منفذة.
7. القالبان `Module.FixedAssets` و`Module.HRPayroll`: لم يُنشآ.
8. مصفوفة توافق Core↔Module مكتوبة كجدول سياسات: الآن في كود `IsCompatible` فقط.
9. فصل بيانات الموديولات الطويلة العمر (الموظف/الأصل) عن المعاملات السنوية: سياسة غير مصممة.
