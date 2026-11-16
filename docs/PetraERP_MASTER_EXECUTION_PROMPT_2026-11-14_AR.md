# البرومبت التنفيذي الرئيسي لإغلاق نواة Petra ERP وتجهيز منصة الموديولات
**الإصدار:** 1.0 — 14 نوفمبر 2026  
**النطاق:** المحاسبة العامة، الذمم، المبيعات، المشتريات، المستودعات، التقارير، WPF UX، API، SQL Server، الأداء، التزامن، الأمن، النسخ الاحتياطي، منصة الموديولات.  
**مصدر الحقيقة الأول:** الشيفرة الحالية في أرشيف `PetraERP_ANALYSIS_20261114_182648.zip` وملفات `docs/` والاختبارات فيه، وليس هذا النص إذا اختلف مع تنفيذ مثبت.  

## [0] تعريف الدور وطريقة العمل — اقرأ قبل أي تعديل
أنت فريق هندسة برمجيات متخصص في أنظمة ERP التجارية، يعمل بأدوار Architect وSenior .NET/WPF Engineer وSQL Server Performance Engineer وAccounting Domain Expert وQA/DevSecOps. مهمتك **إصلاح واستكمال المشروع الموجود فعليًا** حتى تصبح نواة المحاسبة والمستودعات قابلة للاعتماد، وتصبح المنصة مستعدة لتركيب موديولات مثل الأصول الثابتة والموارد البشرية والرواتب والتصنيع ونقاط البيع دون تعديل بنية النواة كل مرة.

**نفّذ داخل المستودع، لا تكتفِ بكتابة توصيات أو أمثلة معزولة.** ابدأ بتدقيق وتحليل الشيفرة ثم أصلح مشكلة واحدة أو مجموعة متماسكة، واكتب اختبارات، وشغّلها، وراقب النتائج، ثم انتقل إلى المرحلة التالية تلقائيًا. لا تطلب مني الاختيار بين بدائل تفصيلية يمكن حسمها هندسيًا؛ وثّق القرار وسببه. لا تعلن PASS إذا لم تنفذ الأمر فعليًا، ولا تفترض وجود أدوات/SQL Server إن كانت مفقودة. عند تعذر تشغيل اختبار SQL أو واجهة Windows، علّمه BLOCKED وقدم الأمر أو البيئة المطلوبين صراحة؛ لا تصنفه ناجحًا أو تتجاوزه بصمت.

### قواعد غير قابلة للتفاوض
1. لا تعِد بناء النظام من الصفر، ولا تغيّر WPF إلى Electron/Blazor أو SQL Server إلى PostgreSQL أو API إلى Node.js. احتفظ بالواجهات والعقود الحالية ما لم تثبت ضرورة تغيير محددة، مع مسار ترحيل متوافق.
2. حافظ على بنية `Domain / Application / Infrastructure / Api / Desktop / Modules.*` وحقّق Dependency Rule. لا تسمح لواجهة المستخدم بالاتصال بقاعدة البيانات مباشرة.
3. **صحة القيود والتقييم المخزني والذمم فوق أي تحسين سرعة**. جميع عمليات الترحيل، الاعتماد، العكس، فتح/إغلاق السنوات، وإعادة الترقيم لها اختبارات ثبات حسابي وتدقيق وحماية من التكرار.
4. لا تزيل صلاحية أو تحققًا مطلوبًا، ولا تحول `SERIALIZABLE` إلى `READ UNCOMMITTED/NOLOCK` أو تجعل القيود غير متوازنة لتسريع العملية.
5. قاعدة SQL Server مستقلة **لكل شركة ولكل سنة مالية**، مع Registry تحكم مركزي للمستخدمين والصلاحيات والملفات والتراخيص وسجل الموديولات. لا تدمج السنوات في قاعدة واحدة لمجرد توفير اتصالات.
6. التشغيل الحالي LAN فقط عبر خادم مركزي HTTPS؛ لا تفرض سحابة أو إنترنت أو Microservices أو Redis أو Kafka أو RabbitMQ كشرط لتشغيل النواة. كل تقنية إضافية اختيارية ومبررة بقياس.
7. احتفظ بالمظهر العملي القريب من الأمين 10، لكن حسّن التنظيم والاستجابة: شريط قوائم أفقي، مساحة MDI، نوافذ وقوائم منبثقة، دعم عربي RTL، إدخال سريع بلوحة المفاتيح.
8. تجنب تغيير بيانات فعلية أو تشغيل migration تخريبي بدون نسخة احتياطية قابلة للاستعادة ومراجعة سيناريو الرجوع. لا تضع أسرارًا أو مبالغ مالية أو بيانات أشخاص في السجلات.
9. اجعل كل تغيير صغيرًا وقابلًا للمراجعة، مع اختبارات قبل/بعد وملف `CHANGELOG` وقياسات وبراهين؛ إذا فشل الاختبار، أصلح السبب بدل إلغاء الاختبار.
10. لا تعد بأن «أحدث خوارزمية» ستجعل البرنامج سريعًا آليًا؛ اختر تحسينًا بناءً على Trace/Execution Plan/Benchmark ومقارنة صحيحة.

## [1] جرد إلزامي للمشروع الحالي وبناء خط أساس
ابدأ بقراءة الملفات التالية (تحقق من موقع كل ملف في المستودع ولا تخمّن أسماء الملفات):
- `PetraERP.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `src/**`, `tests/**`, `tools/**`.
- `docs/Performance/PERFORMANCE_REAL_MEASUREMENTS.md`, `PERFORMANCE_BASELINE.md`, `PERFORMANCE_BOTTLENECKS.md`, `WPF_PERFORMANCE_AUDIT.md`, `API_SQL_OPTIMIZATION.md`.
- `docs/UX_AUDIT/WINDOW_UNIFICATION_SPEC.md`, `FIELD_AND_COMMAND_MATRIX.md`, `PRODUCTION_READINESS.md`, `REGRESSION_TEST_RESULTS.md`, `FIXES_IMPLEMENTED.md`.
- `docs/AlAmeen10-UI/SCREEN_MAPPING_AND_GAPS_AR.md`, `REFERENCE_MATRIX_AR.csv`, `PROMPT_REFERENCE_FIRST_AR.md`، ومراجع الصور الموجودة. لا تدعِ دقة بصرية لنافذة لا تتوافر صورة مرجعية لها.
- `src/PetraERP.Application/Reports/ReportMasterCatalog.cs` وهو **مرجع فجوات التقارير**.
- كود الترحيل، الـWorkspace، الـModule Runtime، الـPackaging، وواجهة WPF.

أنشئ ملفات `docs/closure/00_CURRENT_STATE.md` و`01_DEPENDENCY_MAP.md` و`02_CRITICAL_PATHS.md` و`03_SCHEMA_INTEGRITY.md` و`04_BASELINE_METRICS.md`. صنف كل عنصر بدقة إلى: **Implemented+Verified / Implemented+Unverified / Partial / Missing / Broken / Blocked**، مع `file:line`, الاختبار، تأثيره، الأولوية، المالك، الحالة. تحقق من الشجرة البرمجية، الـDI registrations، روابط الأحداث، كل Endpoint إلى Application Service إلى SQL إلى DTO إلى ViewModel إلى XAML Binding. قارن OpenAPI الناتج فعليًا مع Client Generated/Manual ونماذج الطلب والاستجابة والمصادقة والـWorkspace.

**معلومات تدقيق سابقة يجب إعادة إثباتها على أحدث Commit:** كتالوج التقارير أعلن 77 مكتملًا و13 جزئيًا و15 غير منفذ؛ ترحيل فاتورة مبيعات سجل 143–159 SQL commands وP95 نحو 1805ms على جهاز تطوير؛ اختبار ضغط سابق سجل 3 استجابات 503 بسبب deadlock من أصل 82 طلب ترحيل؛ تقرير Ledger بحجم 100 ألف حركة سجل نحو 5.4 ثانية وتخصيص ذاكرة 843 MB. هذه ليست نتائج تشغيلك الحالي وليست مقاييس LAN. سجّل baseline جديدًا.

## [2] إصلاحات P0 التي تُنفذ قبل أي تطوير تجميلي
### [2.1] عزل السنة والـWorkspace والـCache
ادرس `src/PetraERP.Desktop/Caching/ReferenceDataCache.cs` و`DesktopSession.cs` و`DesktopViewModel.Context.cs` و`Connection/AccountingClients.cs` و`InventoryClient.cs` و`Patterns/PatternShortcutLoader.cs`. المفتاح الحالي في الكاش هو `companyId:category:key`، وهو غير كافٍ للتفريق بين سنوات مالية أو Workspaces للشركة نفسها.
- أنشئ `CacheScope` immutable يحتوي `CompanyId, FiscalYearId, WorkspaceId, BranchId (when relevant), EffectivePermissionFingerprint (when data permission-dependent), Schema/ReferenceVersion (when needed)`؛ لا تضع معلومات لا تؤثر على النتائج بلا داعٍ.
- ضع `Generation/Epoch` متزايدًا للتبديل بين الـWorkspaces ولتسجيل الخروج وإبطال الصلاحيات؛ لا تسمح لطلب قديم اكتمل بعد التبديل أن يحدّث واجهة السنة الجديدة، حتى لو أعاد نتيجة صالحة لسنة سابقة.
- صحّح تصميم `single-flight`: لا تجعل `CancellationToken` لأول طالب يلغي العمل لجميع المنتظرين؛ افصل Cancellation للمنتظر عن عمر عملية الجلب المشتركة، امنع race عند eviction/invalidation، ولا تستخدم `Task.Run` لتغليف I/O async بلا حاجة.
- نفذ `InvalidateWorkspace`, `InvalidateReferenceType`, `InvalidateAll` عند الإضافة والتعديل والحذف والتعطيل والمزامنة وتغيير الصلاحيات والسنة والسياق؛ آلية push عبر SignalR اختيارية ومضبوطة إن وجدت تغييرات من مستخدمين آخرين.
- اسمح بتخزين المرجعيات شبه الثابتة فقط، لا أرصدة المخزون/الذمم/الصندوق الحالية أو صلاحيات حساسة دون إعادة تحقق.
- اختبارات تبادل 2025/2026، تغيير فرع/مستخدم، طلبات متداخلة، انقطاع الشبكة، عودة استجابة قديمة، و`cache stampede`.

### [2.2] معالجة تعارضات SQL من جذورها
ادرس `src/PetraERP.Infrastructure/GeneralLedger/SqlPostingService.cs` ومسارات Save/Approve/Post للمبيعات والمشتريات والسندات والمستودعات، والجداول `audit.ActorReferences`, `sales.Invoices`, `ledger.JournalEntries`, `ledger.JournalAudit` والـNumber Series.
- التقط Deadlock Graph XML من `system_health`/Extended Events في SQL Server مع `CorrelationId`, `WorkspaceId`, `OperationName`, `TransactionId`, SQL command metadata (من دون قيم حساسة).
- ارسم **Lock Order Matrix** لكل مسار، ووحّد ترتيب الوصول وتحديث الكيانات والـindexes، واجعل المعاملة قصيرة، ولا تنفذ I/O أو HTTP أو UI أو انتظار مستخدم داخل transaction.
- حدد ما إذا كان ارتفاع مستوى العزل `SERIALIZABLE` ضروريًا لكل خطوة؛ أبقه حيث يلزم لمنع race/phantom في ترقيم المستندات، المخزون، الذمم والتسوية. ادرس RCSI/SNAPSHOT للقراءات المناسبة **بتجربة محاسبية موثقة**، لا تبدله عالميًا دون اختبار؛ لا تستخدم NOLOCK للبيانات المالية.
- امنع حجز الأقفال وتوسيع نطاقها بسبب استعلامات واسعة أو فهارس مفقودة؛ فحص key lookups وخطط التنفيذ ومدة المعاملة والـblocking قبل تعديل SQL.
- عند وقوع `1205` أو فشل مؤقت مؤكد، أعد **الوحدة المالية الذرية كلها** من البداية مع backoff أُسي محدود + jitter، فقط عندما يضمن Idempotency محفوظًا منع تكرار الأثر؛ لا تُعد SQL statement منفردًا بعد rollback جزئي مجهول.
- تحقق من optimistic concurrency بـ`rowversion` حيث يلزم لتحرير تعديل المستندات، مع رسالة تعارض ودعم Reload/Compare بدل الكتابة فوق تعديل مستخدم آخر.
- نجاح الاختبار = عدم وجود deadlocks غير معالجة، وعدم وجود قيود/أرقام/حركات مكررة أو مفقودة، تحت ضغط 10/25/50 مستخدمًا. لا تدّعِ منع deadlock بنسبة 100%؛ الهدف منع أثره على سلامة البيانات ومعدل فشل الأعمال.

### [2.3] Idempotency وRetry مضبوطتان
ادرس `src/PetraERP.Desktop/Connection/TransientRetryHandler.cs`. التنفيذ الحالي يفحص وجود نص `idempotencyKey` في جسم الطلب، وهذا غير كافٍ.
- حلل JSON بشكل صريح، تحقّق من قيمة المفتاح UUID/format والـoperation والـscope والـpayload hash؛ لا يُعاد POST عند غياب مفتاح صالح أو في عمليات ليست مصممة لتكون idempotent.
- في الخادم: سجل durable transactional `(WorkspaceId, OperationType, IdempotencyKey, PayloadHash, State, ReceiptId)` مع قيود UNIQUE؛ نفس المفتاح ونفس payload يعيد النتيجة السابقة، واختلاف الـpayload يعيد 409؛ منع سباق Parallel Requests حتى لو بدأ المفتاح مرتين.
- حالة `Committed-but-response-lost`: عند timeout لا تنشئ مفتاحًا جديدًا وتعيد الترحيل؛ استعلم عن `Operation Status/Receipt` بالمفتاح واستعد النتيجة.
- لا تضع retry عشوائيًا لعمليات اعتماد/عكس/سداد/استيراد/ترحيل بلا إثبات؛ ضع سياسات مختلفة لـ GET وPOST آمن وعمليات غير قابلة للإعادة، مع limit إجمالي وcancellation وإظهار حالة واضحة للمستخدم.
- أثبت اختبار 100/100 من الطلبات ذات المفتاح نفسه = أثر مالي واحد + نتائج HTTP متسقة، واختبار keys متضاربة وnetwork timeout وSQL deadlock.

### [2.4] إغلاق فجوات البناء والاختبارات
- نفذ `dotnet --info`, `dotnet restore --locked-mode`, `dotnet build PetraERP.slnx -c Release`, ثم اختبارات المشاريع `Domain/Application/Infrastructure/API/Desktop/Reporting/Modules` إضافة لاختبارات SQL الفعلية. وفّق مع طريقة تشغيل xUnit v3 في المستودع؛ لا تخترع أوامر غير مدعومة.
- افحص `XAML compile` وruntime وBindings/Commands، ولا تعتبر سلامة XML نجاحًا لـWPF.
- عالج الاختبارات المتذبذبة والهجينة التي تعتمد ترتيب التنفيذ، مع Repeat 3x والاحتفاظ بالـlogs؛ ولا تغير Expected Values دون سبب محاسبي.
- احفظ كل إصدار مع SHA/commit + schema version + package lock + migration provenance.

## [3] مصفوفة اكتمال نواة المحاسبة: من البطاقة إلى القيد والتقرير
ابنِ `docs/closure/ACCOUNTING_FUNCTION_MATRIX.md` مع أدلة تشغيل واختبارات، وغطِّ ما يلي بالكامل:

### [3.1] دليل الحسابات والمرجعيات
شجرة حسابات مرنة ومتعددة المستويات، ترميز، تصنيف صحيح (أصل/خصم/حقوق/إيراد/مصروف/مقابل)، حسابات تجميعية وحسابات قابلة للترحيل، أرصدة مدينة/دائنة، طبيعة الرصيد، إيقاف، ارتباط بفروع ومراكز كلفة، بطاقات كاملة، حذف آمن فقط إن لم توجد مراجع، حسابات رقابية للذمم والمخزون، إعادة هيكلة دون تغيير تقارير تاريخية. التحقق من قاعدة سنة مستقلة، ومنع جعل parent حساب حركة خطأ، وحماية الـFKs والأكواد المكررة.

### [3.2] اليومية والقيود
قيد يدوي وآلي، أنماط وترقيم وحالات Draft/Approved/Posted/Reversed، فصل المهام، المعتمد غير المُرحِّل حسب السياسة، توازن القيد بالعملة الأساسية (وأي قواعد عملات إضافية موثقة)، عدم تعديل قيد مرحّل، عكس linked reversal، قيود تصحيح، مرفقات/وصف/أثر تدقيق، إغلاق الفترات، إثبات SourceDocument ↔ Journal ↔ Audit ↔ Balances، `RowVersion`، وإعادة فتح فترة بإذن خاص وسجل مبرر.

### [3.3] الزبائن والموردون والذمم
بطاقات أطراف مستقلة أو متعددة الأدوار، حساب ذمة مضبوط بسياسة الشركة لا بتخمين أول رمز (`121/221`) أو البحث في اسم الحساب، حد ائتمان، شروط دفع واستحقاق، Open Items، تسوية دفعات جزئية، سلف ودفع مقدّم، أعمار ديون، مردودات، خصومات وضرائب مع إعداداتها، أرصدة أول المدة وربطها بالـGL، فروقات صرف، كشف أرصدة أطراف يطابق حسابات الرقابة. **كشف حساب الزبون/المورد يعرض الأطراف فقط**؛ الحسابات الأخرى تذهب إلى دفتر الأستاذ. اختبر تغيير علاقة الطرف/الحساب وتصحيحها دون فصل قيود تاريخية.

### [3.4] المبيعات والمشتريات
الدورة New → Edit → Save → Approve → Post → Print → Return/Reverse، أنماط المستندات، تعدد الفروع والمستودعات والعملات، أسطر بفواتير افتراضية 50 سطرًا دون 50 استعلامًا، كميات ووحدات وتحويلاتها، أسعار وحسميات وضرائب ومصاريف الشحن ومراكز الكلفة، حسابات الصندوق/الحساب المقابل، حساب التكلفة WAC/FIFO، الاستحقاقات، أرصدة المخزون السالب حسب **سياسة صريحة**. حافظ على دقة الإجماليات والتقريب Decimal وثبات إعادة فتح الفواتير وطباعة الإجماليات. لا تختَر أول نمط أو مستودع عشوائيًا؛ النمط المحدد ثم الوحيد المتاح ثم اختيار واضح.

### [3.5] المستودعات
مواد ومجموعات ووحدات وتحويلات ووحدات بديلة ومجموعات حسابية، مستودعات وفروع، مستند إدخال/إخراج/تحويل، مشتريات ومبيعات ومردودات، جرد وفرق جرد مع اعتماد وقيود محاسبية، الحجز إن كان مدعومًا وسياساته، مخزون سالب مقيد بالسياسة، FIFO/WAC وحساب كلفة مرتجع وعكس صحيح، حركة قيمة/كمية وGL stock reconciliation. افحص حالات تغيير طريقة التقييم والمخزون الافتتاحي والأرصدة عبر السنة.

### [3.6] الصندوق والبنوك والأوراق التجارية
سند قبض ودفع، السداد والتخصيص، الحركات النقدية والتحويل بين حسابات، الشيكات وأوراق القبض والدفع والتمرير/الارتجاع، **تسوية بنك فعلية** Bank Statement / Match / Unmatch / Outstanding / Charges / Interest / Reconciliation Posting / Closing؛ إدخال يدوي أو CSV محافظ ومتحقق، منع التكرار، فصل صلاحيات المطابقة والإقرار.

### [3.7] العملات والمراكز والسنوات والفروع
أسعار الصرف التاريخية والنشرة، عملة أصلية وعملة أساس، إعادة تقييم وقيود محققة/غير محققة، سعر وتاريخ لكل قيد، `decimal` مع سياسة Round/Precision وعمليات الضرب والقسمة، تقارير عملات. مراكز الكلفة والتوزيع وفق قاعدة معتمدة، لا تضع قيودًا افتراضية بلا سياسة. قاعدة لكل شركة وسنة، فتح قاعدتين **قراءة فقط للمقارنة**، ترحيل افتتاح مع تحقق hash/checksum/mapping ومطابقة قبل/بعد، تجميد سنة مقفلة، إدارة فرع وربط الحسابات. تسويات بين منشآت مستقلة عبر مديونيات متبادلة مستندة إلى عمليات موثقة، دون كتابة مباشرة في قواعد مختلفة في معاملة موزعة هشة؛ استخدم workflow durable بحالات وتعويض/مطابقة وختم source IDs.

### [3.8] ترقيم المستندات وخيارات الشركة
نطاق الرقم حسب (شركة/فرع/سنة/نوع/نمط)، توليد atomic تحت الضغط، سياسة إظهار رقم إنجليزي، تجميع الأرقام **كل رقمين أو ثلاثة** منفصل عن المنازل العشرية، إعادة ترقيم بموافقة وسجل تدقيق وخرائط رقم قديم/جديد ومنع كسر المراجع الخارجية، خيارات إظهار/إخفاء الحقول، سماح إخراج سالب وفق الدور والمخزون، شاشة إعدادات مهنية كاملة واختبارات تحويل.

## [4] التقارير: إغلاق الكتالوج وبناء محرك تقارير موحّد
اعتبر `ReportMasterCatalog.cs` عقد تغطية لا بيان شهادة جودة. افحص كتالوج جميع التقارير والـroutes، وسجّل لكل تقرير: ID، الاسم العربي، Dataset Source، Filters، Sort، Group، Summary، Currency، Permissions، API Route، View، Export، Tests، Status، P95. لا تصنّف Implemented إلا إذا أُثبت العرض والحساب والتصدير.

**تقارير غير منفذة معلنة تحتاج أولوية وموافقة على مصدرها:**
`GL-09`, `FIN-11`, `FIN-12`, `FX-08`, `CASH-04`, `INV-08`, `INV-17`, `SALES-08`, `SALES-09`, `BUY-06`, `CC-05`, `SYSTEM-01`, `SYSTEM-03`, `SYSTEM-04`, `SYSTEM-07`.
أصلح أيضًا 13 تقريرًا معلنًا Partial بعد إثبات الفجوة من كل مدخلة، واحرص على عدم إنشاء أرقام تقديرية عندما لا توجد ميزانية معتمدة (`FIN-12`) أو تصنيفات صريحة للتدفقات النقدية أو بيانات أصل ثابت. يمكن إظهار تقرير مؤجل ببيانات صحيحة جزئية ووسم صريح بدل اختلاق السطور.

### قواعد التقارير المطلوبة
- **دفتر الأستاذ:** كل الحسابات أو حساب/مجموعة وفق فلتر صريح، رصيد افتتاحي وحركة ورصيد جاري، سرد من مصدر القيد الحقيقي مع Drill-down للقيد والمستند؛ Paging ولا يحمل 100k إلى الذاكرة بلا داعٍ.
- **ميزان المراجعة:** تحديد حساب أب وإظهار الحساب وأبنائه بدقة، وعرض الافتتاح/الحركة/الإقفال، مستويات التجميع، منع double-count عند تضمين parent+children، مطابقة P&L وBalance Sheet.
- **كشف حساب الزبائن والموردين:** Party-only مع تواريخ واستحقاق وعملات ورصيد/دفعات وتسوية وربط مستند، لا يفتح تلقائيًا على كل حسابات الدليل.
- مالية: ميزانية عمومية، أرباح وخسائر، متاجرة، تشغيل، تدفق نقدي مباشر/غير مباشر بمصادر وتصنيف موثق، تغير حقوق الملكية، مقارنات فروع/سنوات/فترات، موازنات عند وجود بيانات فعلية.
- مبيعات/مشتريات: يوميات، تحليل مواد/أطراف/فروع/مندوبين إن توافرت، كلفة وربحية على أساس FIFO/WAC الصحيح بعد المردودات والتكاليف، حد ائتمان، استحقاقات.
- مخزون: حركة، أرصدة، قيمة، جرد وفروقات، وحدات بديلة، تقييم وإقفال، مواد راكدة وحرجة بحسب سياسة، روابط القيود.
- نظام: تدقيق، أحداث الترحيل والعكس، صلاحيات فعلية، مصادر السنوات، التغيير، مقارنة فتح، تقارير missing sources.

### معمارية وتشغيل التقارير
1. احتفظ بمحرك FastReport Open Source الموجود و`PdfSimple` و`ClosedXML` عند صلاحية الرخص والقدرات، مع تصميم تقارير مركزي مستقل عن UI وطبقة `ReportDefinition/Filters/Execution/Rendering/Export`؛ لا تدّعِ أن وظيفة محرر قوالب خارجي مدعومة إن لم تثبت.
2. لكل تقرير: شاشة معاملات خفيفة، فلتر تاريخ/حساب/فرع/عملة/مركز كلفة/مستودع حسب طبيعته، حفظ إعدادات المستخدم، التصفية المتقدمة بما فيها «يتضمن في البيان»، فرز وصادرات PDF/Excel وطباعة.
3. Header: شعار واسم شركة وفرع واسم التقرير والفترة؛ Footer: رقم صفحة/عدد صفحات/تاريخ طباعة/مستخدم/توقيع اختياري؛ صفوف/مجاميع فرعية/الإجمالي، تسوية اتجاه RTL، خط عربي مقروء، أرقام إنجليزية وإعداد فواصل مستقل، مقاس A4/A3 Portrait/Landscape.
4. دعم Drill-down: زر/نقرة على رصيد ⇒ قائمة حركات ⇒ القيد ⇒ المستند الأصلي؛ منع تعديل بيانات من شاشة عرض إلا بالصلاحية والعملية المعتمدة.
5. فصل عرض الشاشة عن التصدير: Data Virtualization + keyset/page لواجهة التصفح؛ Streaming/batched export للتقرير الكبير؛ الحفاظ على ترتيب ثابت، الرصيد الجاري الصحيح عبر الصفحات، snapshot/consistency وعدد الصفوف وحد أقصى واقعي.
6. جداول تلخيص مستمرة أو materialized snapshots للتقارير الساخنة **عند وجود حاجة مثبتة**؛ `watermark` وعمليات refresh وreconciliation وfallback إلى المصدر الحقيقي. لا تعتمد على eventually consistent aggregate لاتخاذ قرار تسوية مالية نهائي دون إظهار حالة التحديث.
7. Golden tests: عينات ذهبية رقمية لـSQL/CSV/PDF/Excel تشمل العربية والكسور والحسميات والتواريخ والـRTL والمجاميع وانقسام الصفحة وحسابات parent.

## [5] واجهات WPF: سهولة المحاسب قبل الزينة
اعتمد Design System واحد `Petra Classic Pro` مستلهمًا من مراجع الأمين 10 **ضمن الأدلة المتاحة**. اتبع `WINDOW_UNIFICATION_SPEC.md` و`SCREEN_MAPPING_AND_GAPS_AR.md` وعدم افتراض مرجع مفقود. ابدأ بالمخزون، ثم السندات، ثم المشتريات، ثم المبيعات للانتقال إلى قالب مستندات موحد؛ لا تكسّر اختصارات أو سلوك قائمًا.

### إطار البرنامج
- Ribbon/Top Horizontal Menu منظم: ملفات، حسابات، فواتير، مستودعات، صندوق وبنوك، تقارير، إعدادات، موديولات، مساعدة، مع بحث عالمي بالأوامر.
- Workspace MDI قابل لفتح نوافذ متعددة والتنقل بينها، تنشيط النافذة المقصودة، الاحتفاظ بسياق الشركة/السنة، إغلاق/استعادة موقعها وحجمها دون خروج عن الشاشة أو تراكم موارد.
- قوائم سياقية بالنقر الأيمن، ونوافذ Lookup موحدة، اختصار **زر المسطرة `/`** في كل حقل له قائمة اختيار، اختيار عنصر بـEnter، Tab/Shift+Tab، F2 للتعديل، Insert لسطر جديد، Ctrl+S للحفظ، Ctrl+N للجديد، Esc للإلغاء، F5 للتحديث، مع اختبار تعارض الاختصار مع حقول نصية تحتاج `/` حرفيًا؛ صمّم آلية سياق أو override للحروف ولا تكسر الإدخال الطبيعي.
- تصميم responsive لسطح المكتب WPF على شاشات مختلفة ودقة 100/125/150% مع قياس بالعربي؛ تجنب ازدحام 24–37 زرًا دائمًا، وحوّل الوظائف الثانوية لقائمة «المزيد».
- RTL وصيغ مبالغ وحقول واسعة متناسبة مع أحجامها؛ التركيز الأول عند فتح النافذة وفق الوظيفة، خط واضح، ألوان مهنية وعرض حالات بوضوح.

### قالب موحد للفواتير والسندات والمستودعات
شريط عنوان (نوع/رقم/حالة)؛ أزرار أساسية بحسب حالة المستند (جديد/تعديل/حفظ/اعتماد/ترحيل/طباعة) مع إظهار أسباب عدم توفر الأمر إن كان أساسيًا؛ ترويسة أساسية قصيرة؛ حقول متقدمة قابلة للطي؛ شبكة افتراضيًا 50 سطرًا **افتراضيًا بصريًا فقط** لا 50 كائنًا ثقيلًا أو استدعاء API لكل سطر؛ تذييل مجاميع مرئي؛ تنقل مستندات حقيقي؛ تنبيه حفظ عند Dirty State حقيقي بناءً على قيمة الحقول؛ ربط كل زر بأمر صريح يعمل وله اختبارات، ولا زر شكلي أو معطل بلا تفسير.

### محرك التبعية والمساعدة في الإدخال
- `FieldDependencyEngine` أو نمط موجود يعاد استعماله: اختيار مادة ⇒ تصفية وحداتها وربط سعرها وضريبتها وسياسة خصمها؛ اختيار طرف ⇒ حساب ذمته وشروطه دون تخمين؛ اختيار نمط ⇒ مستودعه وحساباته ونوعه؛ اختيار فرع ⇒ الحسابات والمستودعات المسموحة، مع دورة حل تبعيات لا نهائية ممنوعة.
- `LookupService` واحد مع Debounce + Cancellation + Keyboard shortcuts + search-by-prefix/fuzzy tolerant (بلا fuzzy غير مناسب لأكواد الحسابات) + page + add-new inline وإرجاع الكيان الجديد للحقل.
- مركزية قواعد Validation (Required/Conditional/ReadOnly/Permissions/MinMax/Precision/Unit) في Presentation Contract مشتق من قواعد الخادم ولا يتناقض معها. إظهار رسالة عربية مرتبطة بالحقل ورمز ثابت وسبب وطريقة تصحيح، لا `catch {}` صامت.
- لا تنفذ طلب الشبكة لكل ضغطة؛ Debounce بمدة مقاسة، request coalescing، show loading state مع عدم حظر Dispatcher، إلغاء طلب قديم عند إغلاق النافذة/تغيير السنة.
- في `SalesInvoiceCardViewModel.cs` عالج ابتلاع فشل تحميل المرجعيات الصامت. في `DirectoryViewModels.cs` عالج سقف 300 صف عند الحاجة، و`Rows.Clear/Add` المتكرر، وحدّث الصف عند تغير بياناته مع ثبات ID.
- ابنِ UI Automation smoke tests للنافذة والقوائم المنبثقة والاختصارات والمزامنة وDirty State وربط ComboBoxes وF2/Enter/Tab والتنقل والتصفح/الطباعة.

## [6] خطة إغلاق ملف الأداء — قياس ثم إصلاح ثم إعادة قياس
**ممنوع استخدام عبارات «100% أسرع» من دون قياسات ونسخة مرجعية.** أنشئ مشروع قياس ثابت ببيانات اصطناعية محاسبية واقعية على Windows + SQL Server 2022 Express الحالي، وLAN مع 5/10/25/50 مستخدمًا، وبروفايل جديد للقراءات/الكتابة. دون العتاد، عدد المستخدمين، النسخة، حجم القاعدة، الشبكة، الـGC، `P50/P95/P99`, `RPS`, `error rate`, `CPU`, `allocations`, `SQL commands`, `logical reads`, `lock waits`, `deadlock graph`, `UI frame time`, `startup/warmup`.

### [6.1] API وHTTP
- خذ Waterfall حقيقي لكل نافذة. افحص N+1 وبيانات المرجعيات الزائدة، وابتكر **Endpoint تجميعيًا مخصصًا للتهيئة** مثل `/ui/bootstrap/invoice-context` (الاسم النهائي وفق routing الحالي) يعطي فقط المرجعيات المفيدة للأذونات الحالية والسنة/الفرع. لا تحول API إلى God Endpoint ضخم؛ ادعم ETag/version وTTL بحسب نوع المرجع.
- احتفظ بـ`SocketsHttpHandler` pooling وHTTP/2 وdecompression الحالية. اضبط connection/stream limits وtimeouts وفق التجارب، لا تفتح HttpClient جديدًا لكل استعلام.
- استخدم DTO projections صغيرة و`System.Text.Json` مع source generation إذا كان profiling يظهر كلفة serialization؛ ضغط HTTP فقط عندما يربح فعليًا على LAN وبعد استثناء الأسرار الحساسة من ضغط قد يخلق مخاطر في سياقات مختلطة.
- Cache للقراءات المسموحة مع إبطال ذكي و`single-flight`, bounded size وversion، ووزّع حالات التغيير للمستخدمين النشطين عبر SignalR إذا ثبت أن polling يخلق حملًا. لا تستخدم Polling ثابتًا كل 5 ثوانٍ، ولا تجعل SignalR قناة الحقيقة المالية؛ عند إعادة الاتصال نفّذ refresh مصادقًا ومحددًا.
- استخدم `CancellationToken` من الـView حتى SQL، timeout budget لكل عملية، timeout مكتوب وآمن، `ProblemDetails` error codes، correlation عبر طبقات WPF/API/SQL، وOpenTelemetry traces/metrics/logs مع sampling لحماية الأداء والخصوصية. فعّل `Microsoft.Extensions.Http.Resilience` فقط إن كان متوافقًا مع سياسة retry وإعادة العمليات المالية.
- راجع حجم طلبات API وresponse_count لكل عملية قبل وبعد، مع contract tests OpenAPI؛ لا تغيّر أسماء العقود دون migration/version.

### [6.2] SQL Server وEF Core
- Query Store، `sys.dm_exec_query_stats` وWait Stats وactual plan و`STATISTICS IO/TIME`، المراقبة الدورية المضبوطة وإيضاح `top 20` حسب total duration/reads/CPU/locks.
- أصلح EF N+1 و`Include` المتشعب وcartesian explosion، لا تحمل جميع entities للتصفية محليًا. استخدم `AsNoTracking` للقراءة الفعلية، DTO projection، `AsSplitQuery` فقط عندما يقلل الكلفة الإجمالية، compiled queries فقط لـhot spots ثبتت كلفة ترجمتها؛ Parameterization للفلاتر، تجنب dynamic SQL غير المعلّم.
- راجع `DbContext pooling`: لا تفعّله تلقائيًا في بيئة Workspace متغيرة لأن إعادة استخدام سياق tenant/connection قد تسرّب حالة؛ تأكد من reset وscope لكل request، أو امتنع عنه. افصل SQL connection pool لكل connection string مع caps وحدود.
- فهارس Composite/Filtered/Covering بناء على `WHERE/JOIN/ORDER BY` لدفتر الأستاذ، حركات المادة، Open Items، فواتير الفرع، تاريخ القيود، الرقم والسنة؛ راقب كلفة الكتابة والـfragmentation/statistics ولا تُكثر الفهارس بلا قياس.
- فحص `SaveChanges` المتعددة في المعاملة، batching، bulk import عبر `SqlBulkCopy` + staging + validation + atomic merge على دفعات آمنة، TVP عند تقليل round trips، `ExecuteUpdate`/`ExecuteDelete` فقط عند عدم كسر audit/hooks/business rules، و `sp_getapplock` فقط لقفل نطاقي ضيق تم قياس جدواه.
- تحسين مسار 143–159 أمر SQL: رتب التكرارات حسب مساهمتها بالزمن، اجمع القراءات المكررة داخل UoW حيث لا تمس snapshot/validation، واعرض عدد الأوامر والخطة قبل/بعد. لا تضع هدفًا عدديًا لعدد SQL إذا كان تقليله يضر الاتساق.
- فحص فهارس/حجم `tempdb`, auto-growth, file storage, max degree parallelism, compatibility level, Query Store، وقيود SQL Server Express. **لا تعتمد على ميزة Enterprise عند وجود Express**.
- راجع SQL Server 2025 لاحقًا من حيث التكلفة والقيود والتوافق؛ **لا تهاجر تلقائيًا**. `Optimized Locking` في 2025 ليس متاحًا في Express بحسب مصفوفة الإصدارات؛ لا تقدمه كحل لهذه النسخة. قيّم OPPO/PSPO حسب مستوى توافق قاعدة البيانات وترخيص الإصدار فقط إذا ثبتت جدواها.

### [6.3] WPF وGC
- استخدم UI virtualization مع recycling وdata virtualization **معًا**؛ راجع `ScrollViewer.CanContentScroll`, `VirtualizingPanel`, `EnableRowVirtualization/ColumnVirtualization`, column templates، عرض النتائج على دفعات.
- اعزل أي `Dispatcher.Invoke`, `.Result`, `.Wait`, synchronous DB/network/IO على UI thread؛ استخدم `async/await`, `Dispatcher.InvokeAsync`, cancellation, immutable DTOs، وتجنب flood من `PropertyChanged`.
- فحص Memory Profiler/PerfView/dotnet-trace للـallocations، binding errors، visual tree، resource dictionaries، فتح/إغلاق 100 نافذة، تسرب event handlers/timers/subscriptions، وتحديث المجموعة diff/batch بدل Clear/Add الكثيف.
- اختبارات تجربة المستخدم تقيس من click حتى ظهور بيانات قابلة للاستخدام (`time to interactive`) وليس زمن API وحده؛ راجع CPU/GPU/Frame drops وscrolling 10k/100k صف مع backend paging.

### [6.4] تقارير ضخمة وخوارزميات بيانات
- `SqlProfessionalReportService.Ledger.cs`: لا تستخدم `ToListAsync` لكل دفتر الأستاذ قبل تطبيق الفلاتر. صمم query/filter أولًا، `keyset pagination` وفق ترتيب ثابت `(AccountingDate, PostingSequence, LineId)` وcursor مشفر/موقّع وقابل للتحقق من Workspace والفلاتر، أو Offset/Paging عند الحاجة إلى صفحة عشوائية صغيرة مع قياس.
- أحسب `OpeningBalance` مرة واحدة من مصدر مضبوط، والـrunning balance عبر Window Functions أو cumulative snapshots مع boundary cursor، وتأكد من تطابق الترتيب والقيمة عبر الصفحات وحالة عمليات الترحيل المتزامنة؛ اختر Snapshot Consistency أو as-of checkpoint بحسب السياسة.
- استخدم Streaming (`IAsyncEnumerable` أو DataReader) في إنشاء ملفات Excel/PDF إذا كانت مكتبة التصدير تدعم التدفق فعليًا؛ لا تزعم streaming إذا كان exporter يجمع كل الصفوف في RAM. تجاوز مكتبة التقرير عند الضرورة بخط تصدير متخصص مع اختبارات مطابقة، لا تكسر المعاينة.
- خفّض allocations وLOH/GC pressure وتفكيك DTOs الثقيلة؛ Bound queries, Max Export Size, background queue محلية للتقارير الكبيرة **مع حفظ صلاحية/Workspace وتمييز نتيجة التقرير من بيانات جديدة**، ورسالة واضحة وإلغاء آمن.

### [6.5] تقنية .NET الحالية
- المشروع يستهدف `.NET 10` و`EF Core 10` وWPF، و`global.json` يستخدم SDK `10.0.402`؛ أبقِ `net10.0` أساسًا إنتاجيًا. في 14-11-2026 توجد .NET 11 STS، لكن لا تبدل الإصدار دون compatibility+benchmark+package audit. حدّث .NET 10 إلى آخر patch آمن مع اختبار عقود وحزم؛ أحدث patch المعلن 10.0.15 (10 نوفمبر 2026) ويجب التحقق في بيئة التطوير.
- أجرِ BenchmarkDotNet على CPU-bound hotspots الفعلية، جرّب Tiered PGO/ReadyToRun حيث تدعم بيئة النشر وتثبت الفائدة؛ Native AOT ليس مسار ترقية افتراضيًا لتطبيق WPF/EF/Plugins المعتمد على Reflection. لا تُدخل C++/Rust أو cache موزع لأنهما «أسرع» دون اكتشاف hotspot.

## [7] أهداف الأداء ومعيار القبول المشروط
اعتمد العتاد والبيانات أولًا، ثم استهدف مبدئيًا (P95 على LAN بعد الإحماء، مع احترام النتائج المحاسبية):
- Lookup cached: ≤150ms من تفاعل المستخدم إلى النتيجة؛ Cache Miss مسجل منفصلًا.
- قائمة 100 صف: ≤350ms واجهة + API + عرض.
- فتح فاتورة موجودة: ≤500ms عند المرجعيات الدافئة.
- Save+Approve+Post فاتورة نموذجية: ≤800ms إن كان العتاد والبيانات يسمحان؛ إن تعذر، قدّم bottleneck والحد الواقعي والأدلة لا تقصّ اختبارات السلامة.
- أول صفحة Ledger كبير: ≤1000ms؛ تصدير 100k حركة مع سقف ذاكرة مقاس ومقبول لنوع التصدير، وتقدّم/إلغاء.
- 10/25/50 مستخدمًا: **صفر اختلاف في الأرصدة، صفر تكرار مالي أو رقم مستند، صفر فشل ترحيل نهائي غير مفسر**؛ قياس P99 والتعارضات المعاد تنفيذها، مع اختبار soak لا يقل عن ساعة في بيئة مرجعية قابلة للتكرار.
هذه أهداف تصميم وليست أرقامًا مثبتة للإصدار الحالي، ولا تجعل فشل زمن تحت عتاد أضعف سببًا لتقليل الضوابط المالية.

## [8] تجهيز Module Platform تجاري قابل للتركيب
**لا تبدأ Module Manager جديدًا**؛ حسّن الموجود في:
`src/PetraERP.Modules.Abstractions/*`, `src/PetraERP.Modules.Host/*`, `src/PetraERP.Modules.Packaging/*`, `src/PetraERP.Infrastructure/Modules/*`, `src/PetraERP.Api/Endpoints/ModuleEndpoints.cs`, `src/PetraERP.Modules.Sample/*`, والاختبارات المتصلة.

### [8.1] العقود والإصدارات
- افحص `ModuleIdentity/ModuleDescriptor/ModuleRequestContext`, `IModule`, `IModuleMenuContributor`, `IModulePermissionContributor`, `IModuleCommandHandler`, `IFinancialPostingGateway`, `IInventoryMovementGateway`, `IReferenceDataQuery`, `IAuditTrail`، وAPI الحالية.
- صمم SDK versioned ومستقر Semantic Versioning مع Compatibility Matrix بين إصدار Core وModule، Dependency Graph/DAG، الكشف عن cycles، وترتيب التفعيل/الإيقاف وإمكانية الاسترجاع عند فشل update.
- عقد `IModuleManifest`/package manifest: ID ثابت، Publisher identity، version، required core version، dependencies، features، menus، pages، reports، permissions، migrations، license requirement، checksum والتوقيع الرقمي القابل للتحقق بمفتاح عام موثوق. `SHA-256` يضمن تطابق المحتوى مع manifest فقط وليس **ثقة الناشر**؛ اسم Publisher موجود في قائمة موثوقين لا يكفي توقيعًا تشفيريًا. نفذ تحققًا من توقيع حقيقي وإبطال الثقة.
- حافظ على idempotent install/enable/disable/upgrade/rollback، مع حالات Installed/Enabled/Disabled/Failed/PendingMigration/NeedsRestart حسب الحاجة وسجلات Audit؛ تعطيل الموديول لا يمحو بياناته.

### [8.2] العزل الأمني والتشغيلي
- كود الموديول الحالي يعمل **in-process** عبر `AssemblyLoadContext`; هذا ليس Sandbox أمنيًا. اسمح بهذا فقط لموديولات الطرف الأول أو الناشرين الموثوقين حقًا. للموديولات الخارجية غير الموثوقة صمم **عملية منفصلة محدودة الصلاحيات** أو احظرها لحين توفر عزل مناسب، وتواصلًا محليًا مصادقًا ببوابات typed/IPC، دون وصول إلى Registry/connection strings/files الافتراضية.
- كل عملية تمر عبر `CompanyId + FiscalYearId + BranchId + UserId + ModuleId + Permission`; لا تقبل بيانات scope من جسم الطلب إذا خالفت الجلسة. لا تمنح الموديول `DbContext` أو بيانات اعتماد SQL أو استدعاء الترحيل بلا Gateway.
- كل موديول يضيف Permissions مع موافقة مدير، وFeature Flags، وحالة ترخيص حسب الشركة/الجهاز/الإصدار وفق السياسة النهائية؛ لا تتوقف المحاسبة عند تعطل ترخيص موديول.
- نفذ حدود CPU/memory/timeouts/queue/backpressure وcircuit isolation للعمليات الخارجية، وحماية من zip-slip/zip-bomb وDLL hijacking وdependency confusion عند التثبيت.

### [8.3] البيانات والمزامنة المستقبلية
- كل موديول يملك namespace/schema مستقلًا أو جداول باسم خاص داخل قاعدة الشركة/السنة حسب نوع البيانات، وMigration versioning واضح؛ لا يغيّر جداول `ledger` أو `inventory` مباشرة إلا بعقد core migration رسمي ومراجع.
- **افصل master data المستمرة عبر السنوات عن المعاملات السنوية** وفق سياسة معلنة: مثال ملف موظف/أصل ثابت قد يعيش لسنوات، بينما راتب/إهلاك/حركة تقيد في قاعدة سنة معينة. استخدم Registry/Company Master Store إن اقتضى الأمر مع نسخ snapshot سنوي واعتماد مصدر وحيد، لا تكرر هويات أو تفسد سجلات تاريخية.
- لا تنفذ Distributed Transaction عشوائية عبر قواعد الشركات/السنوات. للعمليات المتعددة استخدم `Transactional Outbox + Inbox + idempotent consumers + retry + reconciliation + compensating actions` عندما تحتاج نشر أحداث عبر قواعد، مع إبقاء قيد كل قاعدة ذرّيًا، والإفصاح عن eventual consistency وحالات المعالجة.
- أحداث typed/versioned مثل `JournalPosted`, `InvoicePosted`, `PaymentAllocated`, `StockMovementPosted`, `FiscalYearClosed`, `PartyChanged` مع OriginModule/SourceId/CorrelationId وOutbox durable؛ حماية من التكرار وترتيب الأحداث ونمو schema، ولها permissioned subscription. SignalR للواجهة فقط وليس استبدالًا للـOutbox.

### [8.4] واجهة الموديولات وتجربتها
- إضافة قائمة/صفحة/تقرير/بطاقة موديول بصورة declarative إلى Ribbon وMDI ومركز التقارير، عند التثبيت/التفعيل وإزالتها من الواجهة عند التعطيل **بلا حذف البيانات**.
- تصميم `Module Navigation Registry`, `Page Resolver`, `Report Registry`, `Settings Registry`, `Permission Registry`, `Module Health/Diagnostics`، مع انعكاس فوري بعد الصلاحية والمستودع وسنة العمل.
- صفحة Module Manager تعرض: الاسم والناشر وإصدار Core الأدنى والتوقيع/الثقة والاعتماديات والحقوق المطلوبة والترخيص والحالة والإصدار وتاريخ التثبيت والتحديث والأخطاء، أوامر تثبيت موثقة من واجهة الإدارة مع تحقق خادمي وقبول أو رفض المستخدم صاحب الصلاحية. يدعم تصدير diagnostics.

### [8.5] إثبات جاهزية منصة الموديولات
طوّر/حدّث `PetraERP.Modules.Sample` ليكون موديولًا تجريبيًا يضيف على الأقل: تبويبًا في القائمة، نافذة MDI، إعدادات خاصة، صلاحية، تقريرًا بسيطًا، schema/جدولًا خاصًا، lifecycle، حدثًا، أمر ترحيل بسيطًا عبر `IFinancialPostingGateway` مُختبرًا، وتعطيل/إعادة تمكين/ترقية/تراجع مع بقاء البيانات، دون تعديل Core وقت تثبيته. لا تنفذ نظام أصول أو HR كامل في هذه المرحلة؛ أنشئ **قالبين skeleton** مع المخططات والعقود فقط:
- `PetraERP.Module.FixedAssets`: AssetMaster (طويل الأجل)، AssetCategory، Acquisition، Capitalization، DepreciationPolicy، DepreciationRun، Disposal، Transfer، MaintenanceLink، Mapping لقيود الأصل/الإهلاك/مجمع الإهلاك عبر Gateway، صلاحيات وتقارير لاحقة.
- `PetraERP.Module.HRPayroll`: EmployeeMaster (طويل الأجل، حساس)، Contract/Department/Attendance/Leave/PayrollPeriod/PayElement/PayrollRun/Payment/Accrual، وPosting Gateway للرواتب والاقتطاعات والذمم، مع حماية معلومات الموظفين منفصلة عن دفتر الأستاذ.
مع اختبارات SDK compatibility وعدم تحميل موديول غير موقع أو غير مسموح.

## [9] الأمن، الصلاحيات، النسخ الاحتياطي، والاستيراد
- RBAC مع Deny-by-default وPolicy-based checks على API لكل endpoint وداخل Use Cases، وscope عبر الشركة/السنة/الفرع، وصلاحيات (عرض/إضافة/تعديل/اعتماد/ترحيل/عكس/إعادة ترقيم/طباعة/تصدير/إدارة موديولات) وSoD وفصل المهام في القيود الحساسة؛ اختبر تغيير GUID في URL والرأس وتبديل Workspace بين الطلبات.
- طبقة Authentication موجودة؛ راجع Rotation/Revocation/TLS وDPAPI/Credential Storage وحماية جلسة، Audit immutable/append-only حيث يلزم، masking لبيانات حساسة، rate limits قابلة للتخصيص مع استثناءات صحية، Backup encrypted وRestoration integrity.
- خطة Backup: لكل قاعدة شركة/سنة والـRegistry وقواعد الموديولات/المرفقات، نسخة كاملة واستعادة مجربة، retention وقياس RPO/RTO وأوامر واضحة، تحقق checksum/db integrity، محاكاة corrupt/failure منتصف عملية، استعادة إلى بيئة اختبار قبل ترقية schema.
- محرك استيراد الأمين لاحقًا يجب أن يمتلك Mapping/Preview/Dry-run/Idempotent Import/Resume/Reject File/Reconciliation مع دعم كامل أو تدريجي؛ لا تنفذه الآن إن لم تكن النواة مستقرة، لكن لا تغلق عقوده. حفظ legacy IDs وmapping للأطراف/الحسابات/المواد/المستودعات ونتيجة المبالغ والمخزون بلا فروقات.

## [10] اختبارات وظيفية ومالية لا يمكن تجاوزها
أنشئ Fixture محاسبي موثق بـ2025/2026 وشركتين وفرعين و3 عملات ومخزنين ومواد FIFO/WAC وحركات بيع/شراء/مردود/قبض/دفع/تسوية/إعادة تقييم/عكس/جرد/إقفال. نفذ الاختبارات التالية بقاعدة SQL حقيقية وليس mock فقط:
1. **Double Entry**: لكل قيد مرحّل مجموع المدين = الدائن بالعملة الأساسية، والفترة والفرع متطابقان، ولا orphan أو duplicate.
2. **Traceability**: من كل فاتورة إلى قيدها وحركات مخزونها وذممها وتقريرها، وعكسها/مردودها، دون تفاوت.
3. **AR/AP vs GL**: إجمالي Open Items والأرصدة حسب الطرف يساوي حسابات الرقابة، مع قائمة استثناءات معروفة ومعتمدة.
4. **Inventory vs GL**: قيمة المخزون والـCOGS وفق FIFO/WAC وبيع/شراء/جرد/عكس تطابق Ledger.
5. **Reports**: ميزان المراجعة، P&L، Balance Sheet، دفتر الأستاذ، أعمار الذمم، التسوية المصرفية، PDF/Excel تتفق مع Ground Truth.
6. **Fiscal Isolation**: 2025 و2026 مفتوحتان للقراءة، عدم تلوث cache، قفل القديمة ضد كتابة غير مصرح، rollover مطابق.
7. **Numbers**: 100 طلب متزامن لا يكرر رقمًا، وإعادة ترقيم مسجلة لا تكسر الروابط ولا audit.
8. **Idempotency + Retry**: طلبات مضاعفة/مفاتيح مختلفة وtimeout-after-commit وdeadlock 1205، أثر مرة واحدة.
9. **Security**: 403/404 عند عدم الإذن، التلاعب بالمحددات، عدم تسرّب البيانات أو الوصول للموديولات خارج scope.
10. **WPF UI**: فتح 20 نافذة، 50 سطرًا، Popup `/`، Enter/Tab، lookup add-new، حفظ/اعتماد/ترحيل/طباعة، أخطاء API، تغيير السنة أثناء طلب معلّق.
11. **Module SDK**: تثبيت وتفعيل وإيقاف وترقية/تراجع موديول Sample مع صلاحيات وMigration ورفض حزمة مخترقة وتكرار Command.
12. **Production Failure Modes**: تعطل API/SQL/الشبكة/الموديول/الطابعة، تفريغ الذاكرة تحت التصدير، Restart، Backup/Restore، ومقارنة checksum.

## [11] نظام توثيق الأدلة والمخرجات
أنشئ تحت `docs/closure/` (أو مسار مشروع موحد موجود) ملفات:
`EXECUTIVE_STATUS_AR.md`, `ACCOUNTING_FUNCTION_MATRIX.md`, `REPORT_COMPLETION_MATRIX.csv`, `UI_FIELD_COMMAND_MATRIX.csv`, `API_ROUTE_MAPPING.csv`, `WORKSPACE_CACHE_AUDIT.md`, `SQL_DEADLOCK_ROOT_CAUSE.md`, `SQL_QUERY_TOP20_BEFORE_AFTER.csv`, `PERFORMANCE_BEFORE_AFTER.md`, `MODULE_SDK_COMPATIBILITY.md`, `MIGRATION_BACKUP_PLAN.md`, `TEST_EXECUTION_EVIDENCE.md`, `RELEASE_GO_NO_GO.md`.
كل سطر في المصفوفة يحتوي `FeatureID`, شاشة/مسار, Code Owner, حالة تنفيذ, اختبار, نتيجة, وقت قياس, مستوى مخاطرة, commit, Evidence. لا تحذف بنود التقارير غير المنفذة حتى تصبح مغطاة باختبارات فعلية.

كل مرحلة تسلّم:
- الملفات التي تغيرت وأسباب التعديل والعقود التي تغيرت؛ Migration مع Up/Down أو خطة Rollback آمنة.
- أوامر Build/Test التي نُفذت فعليًا، ملخص Pass/Fail/Skipped/Blocked مع Evidence Paths وسجلات فشل قابلة لإعادة الإنتاج.
- Metrics قبل/بعد، عدد SQL queries، Lock Graph وHTTP waterfall عند صلة الموضوع، Memory Profile للتقارير.
- قائمة المخاطر المتبقية **لا تنقلها إلى خانة Done**، وقرار متابعة المرحلة التالية.

## [12] الترتيب الإلزامي للتنفيذ (Execution Gates)
**Gate A — Inventory/Build**: تثبيت خط الأساس وBuild Release أو BLOCKED صريح؛ Read-only source map؛ نسخ backup قبل migrations.

**Gate B — P0 Correctness**: Cache Scope/Generation، Idempotency/Retry، Deadlock graph/lock ordering، مصدر الحقيقة للسنة والفرع، اختبارات Finance 10/25/50 و0 اختلاف. **لا تبدأ تقارير تجميلية قبل اجتيازه**.

**Gate C — Performance**: HTTP waterfall، Bootstrap API، DTO/SQL N+1، Query Store/Index/Transaction profiling، Paging/Streaming/UX virtualization، اختبارات LAN مع before/after. لا تدمج تغييرًا أسرع لكنه خاطئ ماليًا.

**Gate D — Accounting Closure**: استكمال البطاقات والقيود والذمم والبيع والشراء والجرد والتسوية البنكية والعملات وترقيم المستندات والتدوير السنوي. كل Use Case له positive/negative/concurrency tests.

**Gate E — Reporting Closure**: معالجة 15 NotImplemented و13 Partial حسب الكتالوج، صحة وPDF/Excel/UI/permissions/P95، مع وسم العناصر المعتمدة على سياسات أو مصادر غير موجودة وعدم تزوير نتائج.

**Gate F — UX Closure**: توحيد WPF والقوائم والاختصارات والـMDI والتبعيات والحقول والتحقق والطباعة وUI automation.

**Gate G — Module Readiness**: توسيع العقود الموجودة، توقيع/عزل الحزم، استدامة البيانات والنسخ، Sample end-to-end، Skeleton Assets/HR، backward compatibility.

**Gate H — Final Release**: Full CI Release, SQL Integration, 10/25/50 concurrency, 60-minute soak, Backup/Restore, reports golden data/PDF, security audit, modules compatibility, signed installer إن كان موجودًا؛ إصدار `GO` فقط إذا انغلقت P0 والفروقات المالية جميعها = صفر، وإلا `NO-GO` مسبب.

## [13] معايير إنهاء نهائية — Definition of Done
تعتبر «نواة Petra ERP مكتملة ومهيأة للموديولات» **فقط** إذا:
1. جميع الوظائف الإلزامية في المحاسبة/الذمم/المستودعات/السنوات مثبتة في Integration tests حقيقية، ولا قيود غير متوازنة أو أرصدة مشتتة أو تكرار مستندات.
2. كل تقرير في الكتالوج إمّا يعمل بدقة (UI/API/PDF/Excel + tests) أو مصنّف صراحة Deferred بسبب اعتماد تجاري/مصدر غير موجود، مع عدم الادعاء باكتمال شامل في هذه الحالة.
3. لا فشل صامت في الواجهة، وكل زر ظاهر يؤدي وظيفة مصرحًا بها، والحقول والاختصارات والقوائم المنبثقة مختبرة.
4. إصلاح أصل مشكلة cache وتعارضات SQL وIdempotency والطلبات المتزامنة، وتوثيق أسباب أي retries، وعدم وجود ازدواج آثار مالية.
5. نشر metrics P50/P95/P99 مع بيئة وبيانات مرجعية قابلة لإعادة الإنتاج، ونشر before/after حقيقي.
6. تركيب موديول Sample وترقيته وتعطيله وإعادته واستعادة بياناته من Backup دون تعديل النواة، ومنع موديول غير موثوق من الوصول المباشر لقاعدة البيانات.
7. Build Release ينجح، وكل test إلزامي Pass؛ أي Skipped/Blocked يفتح استثناءً معتمدًا ويمنع GO حيث يتعلق بالسلامة المالية.
8. مصدر SQL/data/financial scope صحيح لكل شركة وفرع وسنة، ومقارنة سنة بأخرى لا تخلط cache أو تكتب في القديمة.
9. تسليم ملفات صيانة وتشغيل وMigration/Backup/Recovery وخريطة APIs وModular SDK واضحة بحيث يمكن بعدها بدء Fixed Assets وHRPayroll دون إعادة تصميم.

### أسلوب الرد بعد التنفيذ
بعد كل Gate أعطني **ملخصًا عربيًا تنفيذيًا قصيرًا**: (المنجز المثبت، الأخطاء التي عولجت وأماكنها، نتائج الاختبارات بالأرقام، الأداء قبل/بعد، المخاطر المتبقية، الملفات والـcommits). لا تكتفِ بعبارة «تم كل شيء»؛ أرفق الدليل. عند نهاية المسار قدم GO/NO-GO معللًا وفق بيانات فعلية.

**ابدأ الآن من Gate A ثم Gate B. نفّذ التعديلات في المشروع نفسه واستمر بالمراحل حسب ترتيبها دون تعطيل الأعمال بالأسئلة العامة. لا تعلن الاكتمال إلا بالأدلة.**
