# تقرير التدقيق المستقل لـ Petra ERP — نواة المحاسبة / المستودعات / API / الأداء

**تاريخ التدقيق:** 2026-11-14  
**المرجع:** `PetraERP_ANALYSIS_20261114_182648.zip` (نحو 1,700 مدخلة في الأرشيف؛ 1,182 ملف C#، 78 ملف XAML، 12 مشروعًا داخل `src`، ومشاريع اختبار مستقلة).  
**المنهج:** قراءة ثابتة للشيفرة، مقارنة السلوك بتقارير الاختبار والأداء المرفقة، تحليل الربط بين الطبقات، استخراج كتالوج التقارير، والتحقق من صلاحية XML للـ XAML وملفات المشاريع.  
**قيود:** هذه البيئة لا تحتوي .NET SDK/SQL Server/Windows WPF؛ لم أُجرِ Build أو اختبارات SQL أو تجربة LAN مباشرة. أرقام الأداء والاختبارات أدناه من سجلات المشروع وليست قياسات جديدة.

## 1 — الخلاصة التنفيذية

**القرار: NO-GO للإطلاق الإنتاجي الشامل في هذه الحالة.** ليست المشكلة غياب الأساس البرمجي، بل اكتمال تدقيق سلامة البيانات عبر السيناريوهات، الاختناقات المقاسة في الترحيل، تعارضات المعاملات، تجزئة البيانات المرجعية بحسب السنة، اختبار عبء حقيقي، ونواقص وظيفية محددة. لا ينبغي وصف البرنامج بـ«نواة مكتملة 100%» قبل إغلاق عناصر P0 والتحقق من P1.

**نقاط إيجابية مثبتة:** فصل Domain/Application/Infrastructure/API/Desktop؛ وجود SQL Server database per company-year مع Registry مركزي؛ CRUD وموافقات وقيود وترحيل وعكس وIdempotency؛ تقارير GL وAR/AP وميزان مراجعة والمبيعات والمشتريات والمخزون والعملات؛ قياس الطلبات Server-Timing وSQL command count؛ HttpClient pooled/compressed؛ اختبارات كثيرة بينها اختبارات مالية فعلية مع SQL.

**قضايا عالية الأولوية:**

1. قياس سابق للترحيل المتزامن: 79 نجاحًا و3×503 من 82 طلبًا، جميعها ضحايا deadlock SQL 1205. المصدر: `docs/Performance/PERFORMANCE_REAL_MEASUREMENTS.md` قسم 11. الإصلاح الحالي في العميل هو retry آمن مشروط (يقلل الأثر ولا يزيل جذر التعارض). مسارات القفل موثقة: `audit.ActorReferences`, `sales.Invoices`, `ledger.JournalEntries`, `ledger.JournalAudit`.
2. `ReferenceDataCache` يبني المفتاح بـ `companyId:category:key`، دون `FiscalYearId/WorkspaceId`: `src/PetraERP.Desktop/Caching/ReferenceDataCache.cs:29-30`. تمرّر الاستدعاءات عادة `companyId` و `branchId` فقط: `Connection/AccountingClients.cs:21-25` و`Connection/InventoryClient.cs:38-43` و`Patterns/PatternShortcutLoader.cs:24-35`. عند تبديل سنة مالية للشركة والفرع نفسيهما، يمكن إعادة إظهار مرجعيات تخص السنة القديمة؛ خطأ تصميم ثابت في الكود ويحتاج اختبار تكرار لإثبات أثره النهائي. `DesktopViewModel.Context.cs:197-251` يبدّل الـworkspace دون إبطال الكاش العام.
3. `TransientRetryHandler.IsReplayableAsync` يعدّ أي نص جسم يحتوي الكلمة `idempotencyKey` صالحًا لإعادة الطلب دون تحليل JSON أو التأكد من UUID غير فارغ أو ثباته: `src/PetraERP.Desktop/Connection/TransientRetryHandler.cs:33-39`. لا يثبت ذلك تكرار قيد في التطبيق الحالي، لكنه يخالف قاعدة «التحقق الصريح قبل إعادة POST» ويجب تحصينه.
4. قياسات `save-and-process`: 143–159 أمر SQL/طلب، P50 = 1,286 ms وP95 = 1,805 ms (Release على جهاز تطوير **لكن الأرقام مليئة بضجيج القياس**)، المصدر: تقرير الأداء قسم 10.1. لا يجوز نقلها باعتبارها سرعة LAN المتوقعة، لكن **عدد الأوامر** متكرر ومرتفع ويبرر profiling منظمًا.
5. لم تمر آخر نسخة باختبار قبول شامل. سجل 2026-11-14: إيقاف اختبار SQL بعد 350 من 395، ثم إصلاح Party Profile وتشغيل اختبارات محددة 9/9 فقط؛ ملف `docs/UX_AUDIT/REGRESSION_TEST_RESULTS.md`. ملف `docs/UX_AUDIT/FIXES_IMPLEMENTED.md` يسجل استعادة ملفات XAML من BAML وتعديلها يوم 14 نوفمبر دون إعادة بناء Desktop بعد آخر تغيير. حتى لو كانت ملفات XAML الـ78 صحيحة نحويًا، لا يثبت ذلك بنية WPF أو صحة الـbindings.

## 2 — نطاق الوظائف المحاسبية

| الوحدة | حالة مصدرية | الإغلاق المطلوب |
|---|---|---|
| الدليل / بطاقات الحسابات / التسلسل الهرمي | خدمات وAPI واختبارات SQL موجودة | مطابقة جمع الأبناء ومنع الحذف مع الحركات، وقفل سنوات سابقة |
| القيود / اليومية / الموافقات / الترحيل / العكس | محرك فعلي يستخدم SERIALIZABLE وPostingRequests | إثبات عدم ازدواج القيد والرصيد الصفري تحت concurrency، ومعالجة deadlocks |
| الأرصدة الافتتاحية والإقفال/الترحيل السنوي | خدمات OpeningBalance/Rollover موجودة | اختبار نهاية إلى نهاية للسنة السابقة والجديدة، النقد والبنك والذمم والمخزون |
| الزبائن والموردون / الربط بالأستاذ / التسويات | خدمات Party وTreasury وOpenItem وReconciliation | توفيق الذمم مع GL مع الضمان عدم إسناد حركة حساب مراقبة يدوي إلى طرف خطأ |
| المبيعات والمردودات | خدمات جاهزة وربط بالمخزون والقيود | اعتماد اختبارات أسعار/خصم/ضريبة/عملة وحركات مخزون/تكلفة وتزامن |
| المشتريات والمردودات | خدمات جاهزة وحالات موافقة | تغطية شاملة لفواتير مرتجعة وتسوية مسبقات وN+1 وترحيل التكاليف |
| المستودعات والوحدات وFIFO/WAC | خدمات وسجلات وتقييمات موجودة | جرد وفروق جرد، مطابقة GL مع Inventory، كميات سالبة بتفويض |
| الصندوق / البنك / سندات قبض ودفع | خدمات وتقارير موجودة | تسوية كشوف البنك غير منفذة (CASH-04) |
| العملات وفروق الصرف | خدمات أسعار/إعادة تقييم وتقارير | تقرير FX-08 المستقل غائب؛ تحقق محاسبة حقيقي عبر العملات |
| مراكز الكلفة | دليل/حركات/تقارير موجودة | توزيع التكاليف CC-05 غير منفذ؛ اختبار قيود المراكز |
| الشركات/الفروع/السنوات/التسويات | Registry وعزل سنوي وخدمات InterCompany | مقارنة السنوات FIN-11 غير منفذة؛ اختبار العزل والحسابات المقابلة |
| التقارير والطباعة | تقارير وFastReport/Excel/HTML/PDF موجودة | بيانات كبيرة، RTL، ميزان مجاميع، رؤوس/تذييلات وخط عربي، طباعة فعلية |

### كتالوج التقارير الداخلي

الملف: `src/PetraERP.Application/Reports/ReportMasterCatalog.cs` — **105 تقارير: 77 Implemented، 13 Partial، 15 NotImplemented** (هذه حالات *مُعلنة في الشيفرة* لا نسب تحقق).

**غير منفذ (15):** GL-09 حسابات تجاوزت الحد؛ FIN-11 مقارنة سنوات؛ FIN-12 مقارنة فترات وموازنة؛ FX-08 تقرير إعادة التقييم غير المحقق؛ CASH-04 تسوية بنك؛ INV-08 الجرد وفروقاته؛ INV-17 أرصدة وحدات بديلة؛ SALES-08 ربح الفاتورة؛ SALES-09 ربح المواد؛ BUY-06 تكاليف شراء وشحن؛ CC-05 توزيع التكاليف؛ SYSTEM-01 الصلاحيات الفعلية؛ SYSTEM-03 أحداث الترحيل والعكس؛ SYSTEM-04 قاعدة/سنة المصدر ومقارنة الافتتاح؛ SYSTEM-07 اختبارات غياب مصادر التقارير.

**جزئي (13):** GL-03, GL-07, FIN-03, FIN-04, FIN-10, FX-03, FX-10, ARAP-09, ARAP-10, INV-04, INV-15, CC-02, SYSTEM-02. راجع الملاحظات التفصيلية في الكتالوج قبل تصنيف أي تقرير من هذه الفئة جاهزًا.

**مهم:** ربط الحسابات الأب للزبائن والموردين له كود حديث ومخطط `20261112005607_InvoiceAccountAndPartyParents.cs` واختبارات `PartyAccountPolicySqlTests`؛ وثيقة `PRODUCTION_READINESS.md` بتاريخ 12 نوفمبر لا تعكس تمامًا هذه التطورات ويجب ألا تُستخدم وحدها لتقرير النقص.

## 3 — تدقيق HTTP / API وواجهة WPF

### [P0] تخزين البيانات المرجعية عبر سنوات مختلفة

- الخادم يختار قاعدة السنة من workspace، بينما المفتاح المشترك في `ReferenceDataCache` لا يضم workspace ولا السنة. التخزين static singleton، وتغيير workspace لا يُبطله صراحة.
- القيم المتأثرة المؤكدة من الاستدعاءات: شجرة الحسابات، مجموعات المواد، وأنماط المستندات للمسار الذي يستخدم التحميل المخزن. الخطر: إظهار GUID قديم أو نمط نسخة أخرى، ورفض 404/422 عند الحفظ أو اختيار حساب خاطئ ظاهريًا.
- الإصلاح: المفتاح `{ServerIdentity}:{User/PermissionScope}:{CompanyId}:{FiscalYearId}:{BranchId}:{Category}:{Version}` حسب نوع البيانات، مع مسح cache وin-flight عند تغيير الحساب/السنة/المستخدم، وإبطال النسخ المتسابق عند كتابة القيمة المتأخرة. يفضّل تمرير WorkspaceContext صريحًا بدل GUID company فقط.
- الاختبار: افتح 2025، حمّل الحسابات؛ افتح 2026 للشركة نفسها مع حسابات تختلف؛ تحقق أنها تخص 2026؛ عد إلى 2025؛ احذف/غيّر مجموعة وتحقق من إبطال القديم. اختبر الاستجابة البطيئة الواصلة بعد تبديل السنة.

### [P1] تعدد طلبات جلب المرجعيات للشاشات

- `SalesInvoiceCardViewModel.cs:1149-1209`: يبدأ 7 قراءات مستقلة (أنماط، زبائن، مستودعات، مواد، وحدات، إعدادات العملات، مراكز كلفة) ثم يحمل دليل الحسابات؛ وعندما تحمل الأنماط نفسها، قد تنفذ طلبًا لكل نمط. عدد HTTP النهائي يعتمد على الصلاحيات وعدد الأنماط والإعدادات، ولم يُقَس في هذه المراجعة.
- `PatternShortcutLoader.cs`: التحميل عبر `BusinessOperationKind` يخزّن النتيجة، لكن تحميل بطاقة المبيعات يمرّر **predicate** إلى overload يستدعي `LoadCoreAsync` مباشرة دون cache. لذلك لا يمكن افتراض أن كل فتح للبطاقة يستفيد من cache.
- `PurchasesViewModel.cs:458-519`: 8 قراءات مرجعية مشروطة تقريبًا، ثم N طلب HTTP لجلب تفاصيل الأنماط (`GetPatternAsync` لكل نمط). `Task.WhenAll` يزيل التسلسل ولكنه لا يخفض عدد الطلبات ويمكن أن يخلق ذروة ضغط متزامنة.
- الإجراء: endpoint مخصص `GET /workspace/bootstrap?features=sales,purchases` يعيد نسخة مرجعيات محسوبة مع ETag/Version وسجلات منقحة؛ endpoint أنماط مجمعة يعيد active revisions دون استعلام تفصيلي لكل نمط؛ cache آمن بالـworkspace ثم invalidation دقيق.
- القياس: Network trace لكل فتح شاشة بعد إحماء مرتين؛ عدّ HTTP، الحجم بعد الضغط، latency P50/P95، SQL per request، وجدول ملاحظة request waterfall.

### [P1] إخفاء أخطاء التحميل يسبب حالة UI غير مفهومة

- `SalesInvoiceCardViewModel.cs:1205-1208` يبتلع كل الاستثناءات في LoadLookupsAsync (بما فيها تمييز غير واضح لإلغاء المستخدم) ويترك عناصر جزئية؛ `SalesInvoiceCardViewModel.Adjustments.cs:110-114` يخفي فشل جلب دليل الحسابات. النتيجة المحتملة نافذة نصف مكتملة بلا سبب واضح، ولو أن API رفضت الطلب.
- الإصلاح: إخطاء ذات رموز مستقرة وحقل مقصود ورسالة قابلة للتنفيذ، وعرض Loading/Error/Retry لكل Lookup، مع CancellationToken وأداة correlation؛ لا تعطيل صامت لحقل إلزامي.

### [P1] إبطال كاش غير مكتمل

- `ChartOfAccountsClient.DeleteAccountAsync` (`Connection/AccountingClients.cs:55-56`) يستدعي API مباشرة دون `InvalidateCategory` بينما create/update/activate تبطل الكاش.
- `InventoryClient.DeactivateItemGroupAsync/ReactivateItemGroupAsync/DeleteItemGroupAsync` (`Connection/InventoryClient.cs:65-72`) تعيد API مباشرة دون إبطال `ItemGroups`.
- `ReferenceDataCache.cs:54-69`: المصنع داخل Task.Run يستعمل CancellationToken لأول مستدعٍ ويعيد الكتابة إلى cache حتى لو وقع invalidation أثناء التحميل؛ سباق إبطال/إرجاع قيمة قديمة يحتاج معالجة generation/version.

### [P1] طلبات إعادة المحاولة

- `TransientRetryHandler.cs` يعيد بعض 503 مرتين بفواصل 400ms ثم 1,200ms، وفقط GET/HEAD أو طلبات ذات idempotency. هذه خطوة جيدة، لكنها غير بديل عن إصلاح lock order.
- يجب Parsing صحيح للحقل `idempotencyKey` في JSON/headers، وعدم retry لـPOST قبل التأكد من خادم يدعم هذا المفتاح لنفس العملية. حافظ على payload وtoken وworkspace كما هما. سجّل retry count وHTTP status وrequest id دون بيانات محاسبية حساسة.

### الجوانب السليمة

- `DesktopSession.cs:25-31`: `SocketsHttpHandler`، pooling، decompression، HTTP/2 connections.
- `PetraPipeline.cs`: response compression، routing، rate limiting، auth، workspace middleware.
- `RequestTimingMiddleware.cs`: `Server-Timing` مع الإجمالي ووقت SQL وعدد أوامر SQL، وتسجيل slow>=500ms.
- OpenAPI المرفق يحتوي 239 path و290 method operation و466 schema؛ لا يعني هذا أن عدد الطلبات لكل شاشة 290، ولا يثبت تطابق الـJSON مع الخادم بعد آخر التعديلات دون تشغيل اختبار العقد.

## 4 — أداء SQL والدفاتر

### بيانات القياس المتوفرة (يجب عدم تعميمها خارج جهاز التطوير)

| العملية | الدليل المرفق | النتيجة |
|---|---|---|
| Save+Post فاتورة مبيعات HTTP Release محلي | `PERFORMANCE_REAL_MEASUREMENTS.md` §10 | P50 1,286ms، P95 1,805ms، 143–159 أمر SQL؛ توقيت متأثر بضجيج الجهاز |
| تسجيل دخول دافئ | المرجع نفسه §10 | P50 355.8ms، P95 375.2ms؛ غالبًا كلفة PBKDF2 المقصودة |
| تسجيل دخول بارد | المرجع نفسه §10 | 3,839.7ms في تجربة واحدة؛ لا تعمم |
| تحميل قائمة مبيعات صفحة 100 | المرجع نفسه §3 | P50 126.8ms، P95 169.9ms، 7 أوامر SQL في القياس المحدد |
| تقرير Ledger 100,000 صف بعد تحسين | المرجع نفسه §4 | ~5,423ms و~842.6MB ذاكرة مخصصة في تشغيل واحد؛ ليست P95 |
| 10/25 مستخدمًا متزامنًا للترحيل | المرجع نفسه §11 | 79/82 نجاحًا، 3 deadlock 503؛ throughput تجريبي 1–1.7 طلب/ثانية |
| ترتيب 10,000 فاتورة | `UX_AUDIT/REGRESSION_TEST_RESULTS.md` 14-11 | اختبار تجاوز 16 دقيقة وأُوقف، دون تحديد Root Cause مؤكد |

### [P0] Deadlock وlocking order

الفحص القديم لحوادث SQL حدد تضارب ترتيب أقفال `ActorReferences`, `SalesInvoices`, `JournalEntries`, `JournalAudit`. ينبغي إنشاء traces بـ Extended Events وcorrelation لكل مرحلة Save/Approve/Post، وتوحيد ترتيب القفل عبر جميع مسارات الكتابة والقراءات ذات القفل، وتقليل استعلامات الائتمان المتكررة **فقط** حيث يظل التحقق المحاسبي دقيقًا؛ الاختبار النهائي = 0 deadlocks تحت ضغط 10/25/50 مستخدمًا، 0 duplicate numbers/transactions و0 journal imbalance.

### [P1] خفض SQL commands دون كسر سلامة القيد

- قياسات سابقة أحصت تكرار التحقق من نطاق السنة 6×، ActorReferences 6×، الزبون 5×، الائتمان/التسويات 4× في طلب الترحيل. بعض هذه الاستعلامات مقصود للاتساق داخل المعاملة؛ **لا تحذفها آليًا**. اجمع queries المشتركة ضمن Unit of Work محاسبي، وتحقق من النسخ RowVersion قبل الإلزام.
- استخدم `Query Store` + `Actual Execution Plan` + `SET STATISTICS IO,TIME` لترتيب الأعلى أثرًا؛ وفّر counts ومقارنة before/after.
- `SqlProfessionalReportService.Ledger.cs:85-96`: يعدّ كامل الفترة قبل فلترة العناصر، ثم يحمل IDs المفلترة وجميع صفوف الفترة (`ToListAsync`) والروابط؛ حتى تقرير انتقائي يمكن أن يحمّل الكثير من الصفوف. يجب تصميم server-side streaming/pagination للحركات، مع حساب opening/running balances بكفاءة وبنتائج مماثلة للنسخة الحالية.
- الحفظ الدفعي/تسلسل الإجراءات وعمليات `SERIALIZABLE` ينبغي تحسينها دون تقليل حماية الترحيل أو تغيير دقة DECIMAL.

### ميزانيات أداء مقترحة (أهداف قبول مستقبلية، وليست نتائج حالية)

تُقاس في بيئة Windows + SQL Server 2022 Express على عتاد LAN مرجعي ومع بيانات معتبرة، بعد إحماء وثلاث تشغيلات على الأقل:

| العملية | P95 مستهدف مبدئي | شرط |
|---|---:|---|
| البحث بمرجعيات مخزنة دافئة | ≤150ms | لا يعيد استعلام الشبكة بلا سبب |
| قوائم 100 صف | ≤350ms | API + JSON + WPF UI، على LAN محلية |
| فتح فاتورة سابقة | ≤500ms | مع تفاصيل وعلاقات مرجعية مخزنة |
| Save+Approve+Post فاتورة عادية | ≤800ms | صحة قيد/مخزون، لا حذف validation |
| أول صفحة من دفتر حركة كبير | ≤1,000ms | server paging؛ يتطلب تعديل التصميم |
| النجاح المالي تحت ضغط متزامن | 100% بلا تكرار | 0 deadlocks غير مسترجعة و0 فروق ميزان |

لا تُقبل هذه الحدود كأرقام تعاقدية قبل تثبيت حجم البيانات وعدد المستخدمين والعتاد.

## 5 — اختبارات سلامة مالية ملزمة للإغلاق

1. **Double-entry invariant:** مجموع BaseDebit = BaseCredit لكل قيد ولكل سنة وشركة وفرع بعد كل ترحيل وعكس، مع منع القيد بلا طرف محاسبي صحيح.
2. **Idempotency & concurrent posts:** نفس المفتاح ونفس payload = نتيجة واحدة؛ نفس المفتاح وpayload مختلف = 409؛ الضغط 10/25/50 مستخدمًا + timeout/deadlock + retry، دون رقم مكرر ودون قيد مكرر.
3. **Sales-Purchases-Inventory:** فاتورة بيع/مردود ومشتريات/مردود وخصم/ضريبة/تكاليف، نتيجة GL = تكلفة WAC/FIFO والمخزون؛ استبعاد المخزون السالب غير المعتمد.
4. **AR/AP subledger-to-GL:** إجمالي ذمم الأطراف المتتبع في Open Items والتسويات = حسابات مراقبة الذمم بعد استثناء القيود اليدوية المبررة وتفسير الفروقات.
5. **Fiscal rollover:** ترحيل أرصدة عام 2025 إلى قاعدة 2026 للشركة نفسها، ثم حفظ 2026 واستمرار تقارير 2025 دون تلوث cache أو كتابة عكسية للقديم.
6. **Multi-currency:** الربط بين TransactionDebit/Credit وBaseDebit/Credit بالأسعار الفعلية والتواريخ، فحص فرق الصرف المحقق وغير المحقق، الاستعادة وإعادة تشغيل workflow.
7. **Inventory valuation:** تقييم WAC/FIFO، الجرد وتعديل الفروق، عدم تكرار كلفة بعد reversal، مطابقة حسابات مخزون GL مع ledger/stock.
8. **Security & isolation:** صلاحيات قراءة/كتابة/اعتماد/ترحيل لكل مستخدم وفرع وشركة وسنة، اختبار منع الوصول بتغيير GUID وWorkspace headers وتحديثه أثناء الطلب.
9. **Database integrity:** FK/orphan checks، المبالغ السالبة غير المشروعة، duplicated document numbers، breaks في `SourceDocument ↔ Journal ↔ StockMovement ↔ Settlement`، RowVersions وunique indexes.
10. **Recovery:** BACKUP/RESTORE والتدوير السنوي والتكرار بعد فشل منتصف معاملة، مع Checksum ومطابقة الأرصدة قبل وبعد الاستعادة.

## 6 — واجهة WPF والطباعة

- اجتازت ملفات XAML الـ78 فحص XML well-formed، وملفات مشاريع src الـ12 صالحة نحويًا كـXML. هذا **لا يكفي** لإثبات Build أو Runtime أو صحة Bindings.
- `DirectoryViewModels.cs:22,86-94`: سقف 300 صف، مع `Rows.Clear` ثم `Rows.Add` فرادى؛ debounce 80ms وidentity equality موجودان، لكن تغيّر محتوى صف مع ثبات الـID قد لا ينعكس في الصف المعروض عند التحديث (ينبغي مقارنة Version/DTO أو تحديث كل عنصر). هناك فرق بين virtualization للواجهة وserver data paging.
- `SalesInvoiceCardViewModel.cs:1205-1208`: لا تعرض للموظف سبب فشل تحميل المرجعيات.
- الطباعة: وجود `FastReportPdfExporter`, `ExcelReportExporter`, `PrintTemplateRenderer`, `PrintTemplateDesignerWindow` لا يثبت مطابقة RTL أو الخطوط العربية أو الترويسة/التذييل؛ يلزم Golden PDFs واختبار طابعة فعلية وبحث داخل PDF والنقل للورق.
- السجل الأخير يحكي عن استعادة ملفات XAML من BAML وتغيير قوالب موارد؛ يجب فرض Build Release + tests + فتح النافذتين + اختبار لوحة المفاتيح وخانات 50 سطر قبل الإعلان.

## 7 — حالة الاختبارات (بدون تجميل)

- سجل 12 نوفمبر: Domain 217/217، Application 94/94، Modules 25/25، Reporting 40/40، API 50 نجح مع 100 متخطى، Infrastructure 117 نجح مع 278 متخطى، Desktop 597 نجح مع 9 متخطى. SQL SalesPosting + PartyAccountPolicy = 85/85 بحسب سجلات المشروع.
- لاحقًا: Desktop سجل 602/611 مع 9 skipped بعد تتبع التعديلات بالقيمة؛ اختبار rendering كان flaky في تشغيلات أخرى.
- 14 نوفمبر: اختبار SQL infrastructure الشامل لم يكتمل (350/395 قبل إيقافه)، وإصلاح Profile أعقبته 9 اختبارات محددة ناجحة. لم يُعد كامل الاختبار، والملف الأخير يقول صراحة إن Desktop لم يُبنَ بعد بعض تعديلات XAML.
- لا يمكن نقل أي نتيجة «PASS سابق» إلى التزام قبول أحدث Commit دون rerun وإثبات version/commit/environment.

## 8 — الترتيب التنفيذي الموصى به

### المرحلة P0 — سلامة البيانات والاعتماد

1. إصلاح keying/invalidation للكاش عبر الشركة + السنة + Workspace، وإضافة concurrency tests لتبديل السنة أثناء loading.
2. تحقيق وتحسين SQL lock order لحالات deadlocks، مع اختبار مالي متزامن مستقل عن الواجهة وقياس 0 اختلال.
3. تدقيق مسارات retry/idempotency/transaction boundaries ووحدات Save/Approve/Post لضمان مرة واحدة عمليًا.
4. تشغيل Build Release + جميع الاختبارات غير الصريحة ثم اختبارات SQL الصريحة في بيئة نظيفة، وإعادة تشغيل التقطّع 3 مرات؛ توثيق فشل الاختبارات الثقيلة بدل تجاهلها.
5. اعتماد reconciliation بين GL والذمم والمخزون والفواتير والسندات، مع عزل قواعد السنوات وتدوير 2025→2026.

### المرحلة P1 — الأداء وسهولة الإدخال

6. قياس HTTP waterfall للشاشات، ثم API تجميعي للـbootstrap وactive patterns، وcache صحيح لكل workspace.
7. إزالة فشل التحميل الصامت؛ رسائل وأكواد خطأ قابلة للتنفيذ، retry محصور وإظهار حالة الحقول.
8. قياس وتتبع 143–159 SQL command في ترحيل فاتورة؛ تحويل التكرار الآمن إلى batching على أساس بيانات وcorrectness tests.
9. server-side paging/keyset/streaming للتقارير الثقيلة واختبار الذاكرة والـGC على 100k–1M حركة.
10. اختبارات الأداء: LAN، 10/25/50 مستخدمًا، 1 ساعة soak، P50/P95/P99، metrics متزامنة من العميل/API/SQL.

### المرحلة P2 — استكمال ميزات النواة

11. تسوية البنك، الجرد وفروق الجرد، التقارير المالية الجزئية، مقارنة السنوات، وربحية الفاتورة/المادة حسب متطلبات المحاسبة والمستودعات.
12. ربط وتوحيد النوافذ والـLookups والحقول المترابطة، UX automation واختبارات ضغط واجهة، وإثبات الطباعة العربية بتقارير حقيقية.
13. تقرير قبول نهائي واحد يحتوي: commit SHA، Schema Version، build/test evidence، Zero Data Difference، قياسات before/after، ملاحظات الاستثناءات المعتمدة.

## 9 — قرار الاعتماد

**غير جاهز للإطلاق العام بعد.** السماح ببيئة تجريبية محدودة ممكن فقط على بيانات اختبار منفصلة عن الإنتاج، مع نسخ احتياطية وقيود تشغيلية ومراقبة حقيقية. لا توجد في هذا التدقيق أدلة تكفي لتأكيد سلامة قاعدة تشغيل فعلية أو كفاءة عدد المستخدمين المتوقع، ولا ينبغي جعل إزالة زر مقفل بديلًا عن استكمال صلاحية أو حقل مطلوب محاسبيًا.

**مراجعة المصادر الأساسية:**
- `docs/Performance/PERFORMANCE_REAL_MEASUREMENTS.md`
- `docs/UX_AUDIT/REGRESSION_TEST_RESULTS.md`
- `docs/UX_AUDIT/PRODUCTION_READINESS.md`
- `docs/UX_AUDIT/FIXES_IMPLEMENTED.md`
- `src/PetraERP.Application/Reports/ReportMasterCatalog.cs`
- `src/PetraERP.Desktop/Caching/ReferenceDataCache.cs`
- `src/PetraERP.Desktop/Connection/DesktopSession.cs` و`TransientRetryHandler.cs`
- `src/PetraERP.Desktop/Sales/SalesInvoiceCardViewModel.cs`
- `src/PetraERP.Desktop/Purchases/PurchasesViewModel.cs`
- `src/PetraERP.Infrastructure/Reports/Professional/SqlProfessionalReportService.Ledger.cs`
- `src/PetraERP.Infrastructure/GeneralLedger/SqlPostingService.cs`
- `src/PetraERP.Api/Hosting/RequestTimingMiddleware.cs`
