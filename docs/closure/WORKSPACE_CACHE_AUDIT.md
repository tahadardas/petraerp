# تدقيق كاش الـWorkspace (Gate B / 2.1)

## العيوب التي أُثبتت في الشيفرة القديمة (`ReferenceDataCache.cs`)
1. المفتاح `companyId:category:key` — سنتان ماليتان أو Workspaceان للشركة نفسها يتشاركان الإدخال.
2. `Task.Run(..., cancellationToken)` ثم `factory(cancellationToken)` برمز أول طالب: إلغاؤه يلغي الجلب لكل المنتظرين.
3. الإدخال يُكتب بعد اكتمال الجلب دون فحص إبطال: جلب بدأ قبل `InvalidateCategory` يُعيد كتابة البيانات القديمة بعده.
4. `InvalidateAll` يمسح `inFlight` بينما تبقى مهام تعمل وتكتب.
5. `Task.Run` غير لازم لـI/O async.
6. `PatternShortcutLoader` استعمل الكاش بمفتاح الشركة فقط ولا يعرف الـWorkspace.

## الحل (مثبت باختبارات)
- `CacheScope(CompanyId, FiscalYearId, WorkspaceId, BranchId?, PermissionFingerprint?)` جزء من المفتاح؛ `ContextEpoch` يزداد عند تبديل الـWorkspace وتسجيل الخروج (`DesktopSession`).
- Generation رباعي (سياق، Workspace، نوع، Workspace+نوع)؛ الجلب لا يُخزَّن إن تغيّر أي منها، والمنتظر يتلقى `StaleWorkspaceResultException` (يُعامَل كإلغاء) بدل عرض نتيجة سنة قديمة.
- Single-flight بعدّاد منتظرين: إلغاء منتظر لا يلغي الجلب، وإلغاء آخر منتظر يلغيه؛ الجلب يبدأ خارج القفل وبلا `Task.Run`.
- `InvalidateCategory(scope)`, `InvalidateWorkspace`, `InvalidateReferenceType`, `InvalidateAll`, `AdvanceContext`.
- `DesktopSession.CachedAsync/InvalidateCached`: عند عدم وجود Workspace محدد يُتجاوز الكاش (لا مفتاح مشترك).
- `PatternShortcutLoader`: أُزيل الكاش (لا هوية Workspace هنا)؛ يُعاد في طبقة العميل أو عبر Bootstrap لاحقًا (Gate C).

## الاختبارات (`ReferenceDataCacheTests`، 10 اختبارات، 3 تكرارات)
سنتان لنفس الشركة، الفرع/بصمة الصلاحيات في المفتاح، 100 طالب = جلب واحد (cache stampede)، إلغاء طالب لا يلغي الباقين، إلغاء الأخير يلغي الجلب، إبطال أثناء الجلب، تبديل Workspace أثناء الجلب، إبطال Workspace/نوع/الكل، TTL وعدم تخزين الفاشل.

## متبقٍ
سيناريوهات: انقطاع الشبكة، تغيير مستخدم/فرع عبر الواجهة الكاملة، دفع SignalR، وإبطال عند تغيّر صلاحيات المستخدم أثناء الجلسة (يوجد فقط عند تسجيل الخروج). لم تُفحَص بقية الـViewModels التي تحتفظ بقوائم مرجعية خاصة بها خارج هذا الكاش.
