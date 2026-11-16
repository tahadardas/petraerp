# الحالة التنفيذية — Gate A و Gate B (جزئي)

**القرار الحالي: NO-GO للإنتاج.** تفاصيل: `RELEASE_GO_NO_GO.md`.

## المنجز المثبت
- Gate A: `restore --locked-mode` وBuild Release بلا تحذيرات؛ خط أساس اختبارات Release مسجل.
- Gate B / كاش: إصلاح مفتاح الكاش والإلغاء والسباق (10 اختبارات، 3 تكرارات ناجحة).
- Gate B / Idempotency-Retry على العميل: تحليل JSON فعلي للمفتاح (19 اختبارًا جديدًا).
- Gate B / SQL 1205: إعادة الوحدة الذرية كاملة على الخادم، مرتبطة بمفتاح صالح فقط. على SQL حقيقي: 13 فشل من 82 طلب ترحيل صار **0 من 82**، ورصيد القيود 0، وأرقام الفواتير بلا تكرار.
- عيب P0 اكتُشف وأُصلح: `/inventory/documents` كان يفشل للجميع (`write_scope_required`)، مما يعطّل شاشة المخزون الحية.
- منصة الموديولات: تشغيل خادم، واجهات HTTP، تدقيق في السجل الدائم، بوابتا الترحيل والمخزون، كلها على SQL حقيقي.

## الأخطاء التي عولجت وأماكنها
`Desktop/Caching/ReferenceDataCache.cs`، `Desktop/Connection/{DesktopSession,TransientRetryHandler}.cs`، `Api/Security/{FinancialRequestScope,DeadlockRetry}.cs`، `Infrastructure/Inventory/SqlInventoryDocumentService.Scope.cs`، `Infrastructure/Modules/ModulePersistence.cs` (ترتيب لا يُترجم إلى SQL).

## الأداء قبل/بعد
انظر `04_BASELINE_METRICS.md`. لا تحسين سرعة مدّعى: زمن الطلب المُعاد أعلى؛ الهدف كان سلامة الأعمال.

## المخاطر المتبقية (لم تُغلق)
السبب الجذري للـdeadlock (لا Lock Order Matrix)؛ 50 مستخدمًا وSoak ساعة؛ حالة «التزم ولم يصل الرد»؛ التوقيع الرقمي الحقيقي وعزل عملية الموديولات؛ كل Gates C–H؛ كتالوج التقارير؛ فحص بصري لنوافذ السندات/المشتريات/المبيعات؛ لا commit أُنشئ.
