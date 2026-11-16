# برومبت مطابقة واجهات Petra ERP مع الأمين ERP 10 — مرجع بصري موثق

> **حالة العمل: مواصفات وبرومبت فقط. يمنع تعديل أي ملف برمجي أو قاعدة بيانات قبل إصدار إذن تنفيذ منفصل.**
>
> **منع التخمين:** لا تُستخدم أي صورة على أنها مرجع لنافذة مختلفة. إذا لم توجد صورة واضحة للنافذة المطلوبة فأنشئ إدخالًا باسم `REFERENCE_MISSING` ولا تصمّم تخطيطًا مفترضًا.

## الهوية والحدود

أنت مهندس C# WPF متخصص في ERP Desktop Applications وMDI Docking. المطلوب مطابقة طريقة عمل ونمط واجهات Petra ERP (محاسبة ومستودعات) مع **الأمين ERP الإصدار 10 فقط**. ليست المهمة تصميم واجهة جديدة أو تحديثًا بصريًا أو إعادة هيكلة المحاسبة.

### قيود ملزمة

1. لا تغيّر أي API Endpoint أو قاعدة SQL Server أو Domain / Application / Infrastructure أو ترحيل واحتساب الأرصدة أو الصلاحيات أو قيود الفواتير أو منطق التقارير.
2. احتفظ ببنية C# / .NET 10 / WPF / XAML / CommunityToolkit.Mvvm الحالية وبطبقاتها، وافحصها قبل اقتراح تعديلات.
3. لا تستبدل `MdiWorkspaceHost` أو `MdiLayoutEngine` ولا تستورد مكونات خارجية دون إثبات أن المطلوب غير قابل للتحقيق بها، مع مراجعة الترخيص والتوافق مسبقًا.
4. لا تستحدث أسلوب بطاقات Dashboard في أماكن البطاقات المحاسبية التقليدية. ابتعد عن Glassmorphism وNeumorphism والزوايا المبالغ فيها والمؤثرات والحركات التجميلية غير الموثقة.
5. لا تستخدم صور الأمين 9 أو تصميمات من مواقع أخرى كمرجع للنسخة 10. **الأمين 10 يوفّر في نافذة التقرير تخطيط «قياسي» أفقي وتخطيطًا بديلًا «أمين» عموديًا شبيهًا بالتاسع؛ الافتراضي هو القياسي حسب المدونة**.
6. الواجهة عربية RTL، أرقام 0–9 إنكليزية، مع بقاء تجميع الآلاف وسياسة العملات من إعدادات Petra كما هي.
7. لا تُعد تسمية زر أو خيار أو خانة إن كانت الصورة والوثائق تحسم الاسم؛ ولا تُضف تبويبات أو أدوات لمجرد أنها شائعة في ERP.
8. الشيفرة الأصلية وطبقة المعالجة المحاسبية خط أحمر؛ التطوير يقتصر على تموضع الواجهات، أساليب العرض، التنقل، التركيز، حجم النوافذ، تحجيم الجداول، اختصارات النوافذ وتجربة الإدخال.

## أولًا: أسماء التقنيات والأشكال المطلوبة داخل WPF

| الاسم الدقيق | الغرض | المقابل التنفيذي المرشح في WPF |
|---|---|---|
| Ribbon UI / Tabbed Command Bar | قوائم الشريط الرئيسية للمجموعات الوظيفية | `TabControl` + Grouped `ItemsControl` + `ICommand` أو Ribbon مرخّص عند الحاجة |
| Quick Access Toolbar | أوامر متكررة أعلى النافذة | `ToolBar` / `ItemsControl` |
| MDI Workspace | فتح النوافذ داخليًا مع Z-order وتحريك وتحجيم | الموجود `MdiWorkspaceHost : Canvas` |
| Floating Child Window | نافذة حرة داخل MDI | MDI child chrome + Thumb + bindings |
| Dockable Tool Window | أدلة قابلة للإرساء | Docking layout service + `Grid`/`DockPanel` |
| Auto-Hide Dock Panel | إخفاء الدليل على الحافة | auto-hide tab strip + hover/activation |
| Tabbed Dock Group | جمع عدة أدلة ضمن تبويبات | `TabControl` داخل docking group |
| Docking Guide / Compass | أهداف الإرساء عند السحب | adorner overlay / drop targets |
| Split View / Split Pane | دليل بقسمين أفقي أو عمودي | `GridSplitter` + 2 `TreeView`s |
| Hierarchical Tree / TreeGrid | شجرة الحسابات والمواد | `TreeView` / hierarchical grid مع Virtualization عند الحاجة |
| Record Navigator | أول/سابق/التالي/آخر ورقم السجل | reusable user control مع RelayCommands |
| Side Tabs / Category Navigator | أبواب بطاقة الحساب أو المادة | `ListBox` أو `TabControl` مخصص |
| Standard Command Bar | إضافة/تعديل/جديد/حذف/معاينة/طباعة/إغلاق | shared `UserControl` مرتبط بأوامر موجودة |
| Lookup Dialog / Selector | اختيار الحساب/الزبون/المادة/المستودع | `DialogHost` + searchable data grid |
| Searchable ComboBox | البحث أثناء الكتابة | `ComboBox`/TextBox + dropdown results |
| Editable DataGrid | فواتير وسندات متعددة الأسطر | WPF `DataGrid` with explicit keyboard navigation |
| Context Menu | خيارات الزر الأيمن على الأشجار والجداول | WPF `ContextMenu` |
| Report Parameters Window | خيارات قبل المعاينة | shared report parameter control |
| Report Preview Grid | عرض النتيجة مع فرز وتجميع | existing report grid + appropriate toolbar |
| Modal Dialog | تأكيدات أو خيارات يجب إغلاقها للعودة | owned `Window.ShowDialog` أو dialog host |
| Status Bar | الشركة/السنة/العملة/المستخدم والحالة | WPF `StatusBar` |

**مهم:** هذه **أسماء الأنماط/التقنيات المقترحة في Petra** وليست ادعاءً بمعرفة مكتبات الأمين الداخلية.

## ثانيًا: بروتوكول القراءة الملزم للمراجع

لكل صورة في السجل أدناه: (1) افتح رابط المقال الرسمي، (2) افتح رابط ملف الصورة المباشر دون تغيير، (3) اعرضها واقرأ توزيع المساحات والحقول والأزرار، (4) اربطها فقط بالشاشة المطابقة، (5) سجل ما هو ظاهر فعلًا وما لا يظهر، (6) أضف أبعاد نافذة مقترحة فقط إذا كان يمكن الاستدلال عليها نسبةً إلى صورة موثوقة، ولا تخترع قياسًا مطلقًا من لقطة شاشة مضغوطة.

لا يجوز إنشاء أي XAML لشاشة لا تتوفر لها صورة مطابقة ومقروءة إلا لتنفيذ إصلاح سلوكي مثبت من الشيفرة، مع الإشارة `BEHAVIOR_ONLY`.

## ثالثًا: مكتبة الصور الرسمية — **الصور ظاهرة داخل البرومبت**

> الصور أدناه مستضافة في النطاقات الرسمية `s3.alameensoft.com` أو `docs.alameensoft.com`؛ لكل مرجع رابط المقال الأصلي ورابط الصورة المباشر. الصور مرجع للتصميم لا لإعادة نشر الأصول المحمية.

### UI-01 — الشريط الرئيسي Ribbon والقوائم الأفقية
- **القسم:** رئيسية.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/d05e9dcd-2cd4-41d1-88eb-b2d0e46c15ce/original.png
- **المحتوى المثبت:** صورة إصدار 10 تضم تبويبات القوائم، المجموعات، شريط أوامر البرامج.
- **نطاق استخدامها:** رأس نافذة PetraERP والقوائم العليا؛ ليست مرجعًا لتفاصيل فاتورة.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-01 — الشريط الرئيسي Ribbon والقوائم الأفقية](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/d05e9dcd-2cd4-41d1-88eb-b2d0e46c15ce/original.png)

### UI-02 — لوحة الأوامر والبحث السريع
- **القسم:** رئيسية.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/1f369c2f-749e-4292-9c4d-28b1337af165/original.png
- **المحتوى المثبت:** صورة قائمة الأوامر مع حقول بحث وتصنيفات.
- **نطاق استخدامها:** لوحة البحث والأوامر فقط.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-02 — لوحة الأوامر والبحث السريع](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/1f369c2f-749e-4292-9c4d-28b1337af165/original.png)

### UI-03 — نوافذ الأدلة العائمة داخل سطح المكتب
- **القسم:** نوافذ.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/display-charts
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/61c26128-0fd0-4bf4-bb28-57d442a15b3b/original.png
- **المحتوى المثبت:** أربع نوافذ أدلة متراكبة داخل مساحة العمل.
- **نطاق استخدامها:** Floating MDI، تفعيل النافذة وطبقات الظهور.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-03 — نوافذ الأدلة العائمة داخل سطح المكتب](https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/61c26128-0fd0-4bf4-bb28-57d442a15b3b/original.png)

### UI-04 — إرساء الأدلة على الحافة Auto-Hide
- **القسم:** نوافذ.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/display-charts
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/adbde131-305b-46a2-ae2d-ea39c3bcdb46/original.png
- **المحتوى المثبت:** تبويبات أدلة مخفية على حافة سطح العمل.
- **نطاق استخدامها:** Docking Auto-Hide، وليس نافذة فاتورة.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-04 — إرساء الأدلة على الحافة Auto-Hide](https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/adbde131-305b-46a2-ae2d-ea39c3bcdb46/original.png)

### UI-05 — تجميع أدلة متعددة في تبويبات
- **القسم:** نوافذ.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/display-charts
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/4f024d45-b301-461e-9042-4194adffaad8/original.png
- **المحتوى المثبت:** دليل مواد وحسابات ومراكز كلفة كعلامات تبويب بأسفل إطار واحد.
- **نطاق استخدامها:** Tabbed Dock Groups.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-05 — تجميع أدلة متعددة في تبويبات](https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/4f024d45-b301-461e-9042-4194adffaad8/original.png)

### UI-06 — مؤشر توجيه الإرساء
- **القسم:** نوافذ.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/display-charts
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/74f6098d-f5bd-45e5-a189-e9fdd04dc985/original.png
- **المحتوى المثبت:** مؤشر توزيع بإمكانيات أعلى/أسفل/يمين/يسار/وسط.
- **نطاق استخدامها:** Docking Compass / Drop Targets.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-06 — مؤشر توجيه الإرساء](https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/74f6098d-f5bd-45e5-a189-e9fdd04dc985/original.png)

### UI-07 — دليل الحسابات ذو التخطيط العمودي
- **القسم:** أدلة.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/display-charts
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/b901c4fd-4984-41b5-897e-a2c46a0dd6af/original.png
- **المحتوى المثبت:** قائمة الحسابات الرئيسة وأبنائها على جزأين فوق بعضهما.
- **نطاق استخدامها:** TreeView + GridSplitter vertical panels.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-07 — دليل الحسابات ذو التخطيط العمودي](https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/b901c4fd-4984-41b5-897e-a2c46a0dd6af/original.png)

### UI-08 — دليل الحسابات ذو التخطيط الأفقي
- **القسم:** أدلة.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/display-charts
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/832f306b-23ff-44b7-a943-0e68c41cb9d7/original.png
- **المحتوى المثبت:** قائمتان في الدليل متجاورتان يمينًا ويسارًا.
- **نطاق استخدامها:** TreeView + GridSplitter horizontal panels.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-08 — دليل الحسابات ذو التخطيط الأفقي](https://s3.alameensoft.com/blogs/posts/cmrl2hsu4068z01rvm8u3hwz2/832f306b-23ff-44b7-a943-0e68c41cb9d7/original.png)

### UI-09 — بطاقة مادة مع تبويبات وصورة
- **القسم:** بطاقات.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/ed3f109b-d05f-4572-b3d8-4ca41b540c03/original.png
- **المحتوى المثبت:** نموذج بطاقة مادة مع رقم وتنقل وتبويبات جانبية وبيانات.
- **نطاق استخدامها:** ItemCardView؛ لا يُعمم تصميم الصورة على كل البطاقات.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-09 — بطاقة مادة مع تبويبات وصورة](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/ed3f109b-d05f-4572-b3d8-4ca41b540c03/original.png)

### UI-10 — بطاقة حساب كاملة
- **القسم:** بطاقات.
- **المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/cards/accounts-tab/account-card/
- **رابط الصورة المباشر:** https://docs.alameensoft.com/assets/images/Account-Card2-c40b1a215198cc5588f785301328c11c.png
- **المحتوى المثبت:** صورة بطاقة حساب: رمز واسم، تبويبات جانبية، أزرار قياسية أسفل النافذة.
- **نطاق استخدامها:** AccountCardView.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-10 — بطاقة حساب كاملة](https://docs.alameensoft.com/assets/images/Account-Card2-c40b1a215198cc5588f785301328c11c.png)

### UI-11 — فاتورة مبيعات كاملة
- **القسم:** فواتير.
- **المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/operations/invoice-operations/sales-invoice/
- **رابط الصورة المباشر:** https://docs.alameensoft.com/assets/images/sales-invoice2-4afee3e891c9d9e6bb0eea6bb3e7d066.png
- **المحتوى المثبت:** صورة فاتورة مبيعات تضم حقول الرأس، جدول المواد والحسم والإضافة والمجاميع والأزرار.
- **نطاق استخدامها:** SalesInvoiceCardView؛ المرجع الأساس لترتيب فاتورة المبيعات.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-11 — فاتورة مبيعات كاملة](https://docs.alameensoft.com/assets/images/sales-invoice2-4afee3e891c9d9e6bb0eea6bb3e7d066.png)

### UI-12 — سند يومية كامل
- **القسم:** سندات.
- **المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/operations/entries-operations/journal-voucher/
- **رابط الصورة المباشر:** https://docs.alameensoft.com/assets/images/Journal-voucher2-482c2851305a049cd96ceb65134c2636.png
- **المحتوى المثبت:** صورة سند يومية بمبالغ المدين/الدائن وألوان صفوف الجدول والمجاميع.
- **نطاق استخدامها:** DailyVoucherView؛ سند يومية تحديدًا.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-12 — سند يومية كامل](https://docs.alameensoft.com/assets/images/Journal-voucher2-482c2851305a049cd96ceb65134c2636.png)

### UI-13 — نافذة خيارات البرنامج
- **القسم:** إعدادات.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/6e01a92a-12de-4365-a79a-05493fba50ee/original.png
- **المحتوى المثبت:** واجهة بخانة بحث أعلى، قائمة أقسام يمينًا وخيارات تفصيلية.
- **نطاق استخدامها:** ProgramOptionsWindow.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-13 — نافذة خيارات البرنامج](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/6e01a92a-12de-4365-a79a-05493fba50ee/original.png)

### UI-14 — نافذة طلب تقرير كشف حساب زبون
- **القسم:** تقارير.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/report-options-options-dialog
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/65252c41-c5e5-4c19-b87b-a99b4a5cc279/original.jpg
- **المحتوى المثبت:** صورة تقرير الزبون بعناصر مرشحات وأزرار معاينة/ملخصات/طباعة/تصدير.
- **نطاق استخدامها:** Customer Account Statement report parameters فقط.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-14 — نافذة طلب تقرير كشف حساب زبون](https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/65252c41-c5e5-4c19-b87b-a99b4a5cc279/original.jpg)

### UI-15 — تخطيط التقرير قياسي في الإصدار 10
- **القسم:** تقارير.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/report-options-options-dialog
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/305c5135-d5be-40ef-a191-badaada23958/original.jpg
- **المحتوى المثبت:** صورة نافذة جرد المواد بأزرار الإجراءات أفقياً أسفل.
- **نطاق استخدامها:** النمط القياسي في الأمين 10.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-15 — تخطيط التقرير قياسي في الإصدار 10](https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/305c5135-d5be-40ef-a191-badaada23958/original.jpg)

### UI-16 — تخطيط أمين المشابه للإصدار 9
- **القسم:** تقارير.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/report-options-options-dialog
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/eb49c342-c847-4e70-9f6c-ebabf8c25370/original.jpg
- **المحتوى المثبت:** صورة نافذة جرد المواد بأزرار المعاينة والطباعة والتصدير عموديًا.
- **نطاق استخدامها:** خيار بديل في الأمين 10، لا يُستخدم كمرجع افتراضي.
- **حالة التحقق:** تم فتح الصورة الأصلية والتحقق من محتواها بصريًا بتاريخ 2026-11-02.

![UI-16 — تخطيط أمين المشابه للإصدار 9](https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/eb49c342-c847-4e70-9f6c-ebabf8c25370/original.jpg)

### UI-17 — تخطيط محتوى التقرير تبويبي
- **القسم:** تقارير.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/report-options-options-dialog
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/82a7cbcd-f68c-4853-b364-265307a5b86c/original.jpg
- **المحتوى المثبت:** نافذة مرشحات مع تبويبات: عام، مصدر، حالة طلبية، شروط.
- **نطاق استخدامها:** Tabbed Report Parameters.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-17 — تخطيط محتوى التقرير تبويبي](https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/82a7cbcd-f68c-4853-b364-265307a5b86c/original.jpg)

### UI-18 — تخطيط محتوى التقرير تبويب موسع
- **القسم:** تقارير.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/report-options-options-dialog
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/0fe71ff0-0367-426a-8edc-e5b131e50d60/original.jpg
- **المحتوى المثبت:** نافذة مرشحات بعلامات تبويب متعددة في صفين.
- **نطاق استخدامها:** Expanded tabbed Report Parameters.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-18 — تخطيط محتوى التقرير تبويب موسع](https://s3.alameensoft.com/blogs/posts/cmrl1emv003l201rvwae1if4i/0fe71ff0-0367-426a-8edc-e5b131e50d60/original.jpg)

### UI-19 — شبكة معاينة تقرير مع أوامر تحليلية
- **القسم:** تقارير.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/84e29f6e-83d8-4056-a29e-378052e057d1/original.png
- **المحتوى المثبت:** صورة جدول نتيجة تقرير مع فرز وتجميع ومجاميع.
- **نطاق استخدامها:** Report Preview Grid، وليست قالب PDF جاهز.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-19 — شبكة معاينة تقرير مع أوامر تحليلية](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/84e29f6e-83d8-4056-a29e-378052e057d1/original.png)

### UI-20 — نافذة تقرير ولوحة خيارات جانبية
- **القسم:** تقارير.
- **المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/reports/report-window/
- **رابط الصورة المباشر:** https://docs.alameensoft.com/en/assets/images/report-window12-4fad243fac9026e335162804b50d3bcb.png
- **المحتوى المثبت:** صورة خيارات التقرير على جانب الجدول وعناصر فلترة.
- **نطاق استخدامها:** Report Grid with dockable options.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-20 — نافذة تقرير ولوحة خيارات جانبية](https://docs.alameensoft.com/en/assets/images/report-window12-4fad243fac9026e335162804b50d3bcb.png)

### UI-21 — لوحة البداية / الاختصارات
- **القسم:** رئيسية.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/b71d178c-e33a-468f-856d-d6a48eb35b2c/original.png
- **المحتوى المثبت:** مربعات اختصارات وظيفية وإحصاءات، من الإصدار 10.
- **نطاق استخدامها:** Home screen shortcuts فقط؛ لا تعمم كمظهر للبطاقات.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-21 — لوحة البداية / الاختصارات](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/b71d178c-e33a-468f-856d-d6a48eb35b2c/original.png)

### UI-22 — نافذة الحقول المخصصة
- **القسم:** إعدادات.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/c98b55de-5ade-4e2f-bb80-d42b4c1b8bbd/original.png
- **المحتوى المثبت:** نافذة إعدادات خلفية مع بطاقة حقول مخصصة متقدمة في المقدمة.
- **نطاق استخدامها:** Dialog overlays / sub dialogs.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-22 — نافذة الحقول المخصصة](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/c98b55de-5ade-4e2f-bb80-d42b4c1b8bbd/original.png)

### UI-23 — شريط الحالة
- **القسم:** رئيسية.
- **المصدر الرسمي:** https://blogs.alameensoft.com/blogs/alameensoft/new-features-10
- **رابط الصورة المباشر:** https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/2ce2af6d-cb6e-4eb3-b194-0f1977c421ae/original.png
- **المحتوى المثبت:** شريط أسفل واجهة الأمين يعرض بيانات الملف والمستخدم.
- **نطاق استخدامها:** Shell status bar.
- **حالة التحقق:** رابط من الموقع الرسمي؛ يلزم فحص الصورة الأصلية قبل التنفيذ.

![UI-23 — شريط الحالة](https://s3.alameensoft.com/blogs/posts/cmrl0pthw01sh01rvghtgyxo0/2ce2af6d-cb6e-4eb3-b194-0f1977c421ae/original.png)

## رابعًا: مراجع إضافية موثقة مباشرة من الأمين 10

هذه الصور مراجع أصلية تكمّل المكتبة وتزيل الاعتماد على التخمين لبعض النوافذ الأساسية:

### UI-24 — بطاقة زبون/مورد — النموذج الكامل
- **صفحة المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/cards/customers-tab/customer-card/
- **رابط الصورة الأصلي:** https://docs.alameensoft.com/assets/images/2-d542e61b63029e6f4c72406c9bca1cc8.png
- **الوصف المرئي:** بطاقة زبون/مورد تضم شريط تنقل أعلى وتبويبات جانبية وأوامر أسفل النافذة
- **الاستخدام المسموح:** Parties/PartyCardView.xaml، لا تعمم على بطاقة حساب
- **التحقق:** صورة أصلية فُتحت وتحققت بصريًا 2026-11-02

![UI-24 — بطاقة زبون/مورد — النموذج الكامل](https://docs.alameensoft.com/assets/images/2-d542e61b63029e6f4c72406c9bca1cc8.png)

### UI-25 — فاتورة مشتريات — النافذة الكاملة
- **صفحة المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/operations/invoice-operations/purchase-invoice/
- **رابط الصورة الأصلي:** https://docs.alameensoft.com/assets/images/purchase-invoice2-21770d78c38b5de9f6597c1fa1dbff19.png
- **الوصف المرئي:** فاتورة مشتريات مع رأس وحقول المورد والمستودع والشبكة والمجاميع وشريط الأوامر
- **الاستخدام المسموح:** Sales/SalesInvoiceCardView.xaml عند نمط شراء فقط؛ حافظ على نمط الفاتورة الحالي
- **التحقق:** صورة أصلية فُتحت وتحققت بصريًا 2026-11-02

![UI-25 — فاتورة مشتريات — النافذة الكاملة](https://docs.alameensoft.com/assets/images/purchase-invoice2-21770d78c38b5de9f6597c1fa1dbff19.png)

### UI-26 — ميزان المراجعة المصنّف — مرشحات الطلب
- **صفحة المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/reports/all-reports/financial-tab/classified-trial-balance/
- **رابط الصورة الأصلي:** https://docs.alameensoft.com/assets/images/classified-trial-balance-d8ff3cb5798630b44573c2ec55ffb611.png
- **الوصف المرئي:** نافذة طلب ميزان مراجعة مصنّف وتبويب مالية وأزرار المعاينة والطباعة
- **الاستخدام المسموح:** مرجع خاص بالمصنّف فقط؛ لا يُعد صورة ميزان المراجعة العادي
- **التحقق:** صورة أصلية فُتحت وتحققت بصريًا 2026-11-02

![UI-26 — ميزان المراجعة المصنّف — مرشحات الطلب](https://docs.alameensoft.com/assets/images/classified-trial-balance-d8ff3cb5798630b44573c2ec55ffb611.png)

### UI-27 — دفتر الأستاذ — نافذة طلب التقرير
- **صفحة المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/reports/all-reports/accounts-tab/books/ledger/
- **رابط الصورة الأصلي:** https://docs.alameensoft.com/assets/images/ledger-877ffdf73d2f557d9c93817847939bc4.png
- **الوصف المرئي:** نافذة خيارات دفتر الأستاذ مع اختيار الحساب والمصادر والمدة والمستوى
- **الاستخدام المسموح:** Accounting/AccountLedgerView.xaml، لمرشحات دفتر الأستاذ فقط
- **التحقق:** صورة أصلية فُتحت وتحققت بصريًا 2026-11-02

![UI-27 — دفتر الأستاذ — نافذة طلب التقرير](https://docs.alameensoft.com/assets/images/ledger-877ffdf73d2f557d9c93817847939bc4.png)

### UI-28 — بطاقة زبون/مورد — تبويب الحسابات
- **صفحة المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/cards/customers-tab/customer-card/
- **رابط الصورة الأصلي:** https://docs.alameensoft.com/assets/images/accounts-9a2633f21d2ed4ddf4505f5e235cef2a.png
- **الوصف المرئي:** حقول رصيد حالي والحساب الرئيسي ونسبة الحسم وشروط الدفع
- **الاستخدام المسموح:** Parties/PartyCardView.xaml تبويب الحسابات وحده
- **التحقق:** صورة أصلية فُتحت وتحققت بصريًا 2026-11-02

![UI-28 — بطاقة زبون/مورد — تبويب الحسابات](https://docs.alameensoft.com/assets/images/accounts-9a2633f21d2ed4ddf4505f5e235cef2a.png)

### UI-29 — دفتر الأستاذ لعدة سنوات — مرشحات منفصلة
- **صفحة المصدر الرسمي:** https://docs.alameensoft.com/amn10/references/reports/all-reports/accounts-tab/books/ledger-over-years/
- **رابط الصورة الأصلي:** https://docs.alameensoft.com/assets/images/ledger-over-years-781626491b218027475f0b4ef0348011.png
- **الوصف المرئي:** مرجع لدفتر أستاذ متعدد السنوات فقط وإظهار ترشيحات السنوات
- **الاستخدام المسموح:** لا يُستخدم لفرض وظيفة دفتر السنوات على دفتر الأستاذ الحالي
- **التحقق:** صورة أصلية فُتحت وتحققت بصريًا 2026-11-02

![UI-29 — دفتر الأستاذ لعدة سنوات — مرشحات منفصلة](https://docs.alameensoft.com/assets/images/ledger-over-years-781626491b218027475f0b4ef0348011.png)

## خامسًا: ربط الواجهات الحالية بالصور والملفات

| نافذة Petra | ملف المصدر | الصور المسموح اعتمادها |
|---|---|---|
| سطح المكتب وشريط القوائم | `src/PetraERP.Desktop/DesktopWindow.xaml` | UI-01, UI-02, UI-21, UI-23 |
| محرك النوافذ الداخلية | `src/PetraERP.Desktop/Controls/MdiWorkspaceHost.cs`, `Shell/MdiLayoutEngine.cs` | UI-03, UI-04, UI-05, UI-06 |
| دليل الحسابات | `src/PetraERP.Desktop/Accounting/ChartOfAccountsView.xaml` | UI-07, UI-08, UI-03 |
| دليل المواد | `src/PetraERP.Desktop/Inventory/ItemsView.xaml` | UI-03, UI-05؛ تفاصيل الأعمدة بحاجة صورة إضافية |
| بطاقة حساب | `src/PetraERP.Desktop/Accounting/AccountCardView.xaml` | UI-10 |
| بطاقة مادة | `src/PetraERP.Desktop/Inventory/ItemCardView.xaml` | UI-09 |
| فاتورة مبيعات/مشتريات | `src/PetraERP.Desktop/Sales/SalesInvoiceCardView.xaml` | UI-11 للمبيعات، UI-25 للمشتريات حسب نوع العملية |
| سند يومية | `src/PetraERP.Desktop/Shell/DailyVoucherView.xaml` | UI-12 |
| نافذة الخيارات | `src/PetraERP.Desktop/Shell/ProgramOptionsWindow.xaml` | UI-13, UI-22 |
| التقارير العامة | `src/PetraERP.Desktop/Reports/CommercialReportsView.xaml` + ملفات عارض التقارير | UI-14 إلى UI-20، حسب نوع التقرير |
| ميزان المراجعة/دفتر الأستاذ | `Accounting/TrialBalanceView.xaml`, `Accounting/AccountLedgerView.xaml` | **UI-27 لدفتر الأستاذ**، UI-26 للمصنّف فقط؛ ميزان المراجعة العادي REFERENCE_MISSING |
| بطاقة زبون/مورد وسند قبض/دفع | افحص ملفات المشروع أولًا | **بطاقة زبون/مورد: UI-24 وUI-28. سند قبض/دفع: REFERENCE_MISSING ضمن النواة المالية (لا تستخدم مرجع سندات POS)** |

## سادسًا: سلوكيات النوافذ التي يجب مطابقتها

**الشريط الرئيسي:** استعمل تقسيمًا بحسب النظام كما في UI-01، مع مجموعات أوامر داخل كل تبويب وتسميات عربية واضحة وتدرج أيقونات مشابه بصريًا؛ احتفظ بأسماء الأوامر الحالية ولا تتضمن القوائم وحدات لم تُنفذ في النواة.

**إدارة النوافذ:** افتح كل شاشة كنافذة MDI child ضمن سطح المكتب، لا كصفحة تحل محل كل مساحة العمل. تحقق من bring-to-front، active title، focus، minimize/restore/maximize، إغلاق النافذة الواحدة، إغلاق الكل، حفظ مكان النافذة وحجمها، ومنع فتح نسخة ثانية لنفس الشاشة ما لم تسمح وظائفها. الأدلة فقط هي المرشحة لنمط docking + auto-hide + tabbed grouping كما تثبت UI-03 إلى UI-08؛ لا تجعل الفواتير قابلة للإرساء استنتاجًا من صور الأدلة.

**نموذج البطاقة:** رقم البطاقة وأزرار تنقل علوية، حقول ثابتة، تبويبات فرعية بحسب البطاقة، أزرار الإجراءات بالأسفل. لا تُجزئ البطاقة إلى عدة صفحات أو تعرضها كلوحة Dashboard. يجب أن يكون ترتيب الأزرار والحقول نابعًا من الصورة الخاصة بالبطاقة.

**فاتورة المبيعات:** رأس النافذة كما في UI-11، جدول أسطر بنمط الأمين، تفاصيل الحسميات/الإضافات، مجاميع وتذييل وأزرار أسفل النافذة. استمر في دعم 50 سطرًا افتراضيًا حسب مواصفات Petra، واسمح بالتمرير/virtualization بدل جعل النافذة عملاقة. `Enter` أو `F3` على مادة جزئية يفتح نافذة اختيار مادة كما تؤكد وثائق الأمين 10. `Tab` ينتقل للسطر والحقل التاليين حسب ترتيب مرئي ومدروس. لا تنسخ رقم فاتورة أو بيانات تجريبية من الصورة.

**السندات:** طبّق على سند اليومية شكل UI-12، وأبقِ حقول المدين والدائن وأزرار اختيار الحساب والمجاميع كما هي. لا تعمم تخطيط سند اليومية حرفيًا على القبض والدفع قبل رؤية مرجع خاص بكل نوع.

**التقارير:** قبل المعاينة افتح نافذة خيارات؛ يعتمد تخطيط «قياسي» UI-15 كافتراضي لـ10، ويظل تخطيط «أمين» UI-16 خيارًا فقط إذا كان مدعومًا بإعداد موجود. إذا كان محتوى التقرير مقسمًا استخدم UI-17/UI-18 وفق المرجع، ولا تُجبر كل تقرير على التبويب. شاشة المعاينة UI-19/UI-20 مع فرز وتجميع ومجاميع وخيارات تصدير عند توفرها وظيفيًا. **لا تغيّر منطق احتساب أي تقرير ولا تدّعِ مطابقة تصميم الطباعة النهائية دون صورة مطبوعة موثقة.**

**البحث المنبثق:** أي حقل مرجعي له زر بحث يعمل مع الماوس ولوحة المفاتيح وفق وظيفة الحقل: تحديد الحساب/الزبون/المادة/المستودع/مركز الكلفة. يجب أن يعود الاختيار للحقل الأصلي ويحافظ على السطر النشط، ويضبط التركيز بالكيبورد. `Esc` يغلق اختيارًا دون تغيير الحقل، ولا يجب أن يغلق المستند الأم.

**RTL والأرقام:** واجهة عربية RTL كاملة، مع الأرقام دائمًا غربية 0–9 وحساب الأصفار العشرية وفق الإعدادات، بدون تحويل الحقول الرقمية إلى أرقام هندية. لا تُكسر المحاذاة أو عناوين الأعمدة.

## سابعًا: خطوات العمل المطلوبة عند إعطاء إذن التنفيذ لاحقًا

1. جرد جميع ملفات XAML والشاشات ونقاط الفتح في `DesktopWindow`, `NavigationService`, `WorkspaceDocuments` و`MdiWorkspaceHost`.
2. بناء **Reference Matrix** بأعمدة: الشاشة، صورة الأمين، رابط المقال، رابط مباشر، درجة ثقة، حالة الملف، الفروق المرئية، الفروق السلوكية، الحكم (مطابق/جزئي/مفقود).
3. تنفيذ **وحدة Shell وMDI** أولًا، ثم الأدلة، ثم البطاقات، ثم الفواتير والسندات، ثم خيارات التقارير والمعاينة.
4. قبل تعديل كل ملف، اكتب فرقًا محددًا بين كود Petra والصورة، واسم عنصر XAML المراد تغييره. نفذ أصغر Patch ممكن، دون نسخ تصميم إلى صفحات أخرى بلا سند.
5. تنفيذ UI Tests لفتح/إغلاق/إعادة فتح/تحجيم/سحب/إرساء/إخفاء/استعادة/تركيز، وKeyboard Tests (`Enter`,`Tab`,`Esc`,`F3`,`Ctrl+F`) لكل نوع متاح.
6. أخذ لقطة Petra بعد التعديل بنفس أبعاد العرض التي اختُبرت؛ إنشاء لوحة مقارنة **المرجع بجانب النتيجة** وتحديد الفروق المرئية الموضوعية، دون إعلان نسبة 100% إذا تعذرت المقارنة المرئية.
7. إصدار تقرير UI-only يشرح الملفات التي عُدلت، الأدلة المصورة، الاختبارات، وما لم يثبت مرجعه.

## ثامنًا: شروط توقف إلزامية / ممنوعات

- إذا لم تُفتح صورة أصلية أو كانت ضبابية: **STOP_REFERENCE_UNVERIFIED**، ولا تُصمّم من الذاكرة أو من صورة بحث عامة.
- إذا كانت الصورة للإصدار 9 أو صفحة ويب تسويقية مختلفة: **STOP_WRONG_VERSION**.
- إذا كانت الصورة لبطاقة مادة وأنت تعمل على بطاقة حساب: **STOP_WRONG_SCREEN**.
- إذا احتاجت المطابقة تغييرًا في SQL أو API أو حساب الأرصدة: **STOP_SCOPE_VIOLATION** وبلّغ المستخدم فقط.
- إذا حدث تعارض بين التخطيط الموثق ومتطلبات الإلزام مثل RTL والرقم الغربي و50 سطرًا، احتفظ بالمتطلبات الإلزامية، وسجّل التباين بدل إخفائه.
- إذا لم تتوفر بيئة Windows/.NET SDK لتشغيل WPF، لا تدّع نجاح البناء أو المطابقة الفعلية؛ أصدِر فحص XAML فقط واذكر عدم توفر الاختبار المرئي.

### معايير قبول نهائية

لا تُقبل أي نافذة لم تُربط بصورة محددة مناسبة، مع جرد واضح للحقول/الأزرار ومصفوفة اختلاف، وسلوك فتح/إغلاق قابل للاختبار. يجب ألا يظهر أي تغير غير مقصود على منطق الفواتير والقيود والصلاحيات والتقارير. الألوان والخطوط ليست بديلًا عن صحة Layout، وقابلية اختيار الحقول والنوافذ ليست بديلًا عن صحة الواجهة.

## وثائق رسمية مكمّلة لوصف الوظائف (تُستخدم لفهم السلوك، لا كصور مخترعة)

- فاتورة المبيعات: https://docs.alameensoft.com/amn10/references/operations/invoice-operations/sales-invoice/
- بطاقة الحساب: https://docs.alameensoft.com/amn10/references/cards/accounts-tab/account-card/
- بطاقة المادة: https://docs.alameensoft.com/amn10/references/cards/materials-tab/material-card/
- سند اليومية: https://docs.alameensoft.com/amn10/references/operations/entries-operations/journal-voucher/
- أنماط الفواتير: https://docs.alameensoft.com/amn10/references/operations/invoice-operations/invoices-forms/
- أنماط السندات: https://docs.alameensoft.com/amn10/references/operations/entries-operations/entries-forms/
- دليل الحسابات: https://docs.alameensoft.com/amn10/references/charts/accounts-tab/account-chart/
- مركز وثائق الأمين 10: https://docs.alameensoft.com/amn10/intro/

### تعليمات خاصة بالصور المحلية وتدقيقها
- افتح `ALAMEEN10_OFFICIAL_IMAGE_LINKS.json` للروابط الحرفية لكل صورة.
- يحتوي مجلد `images` على `README` فقط عند التسليم؛ **الصور الأصلية غير مضمّنة في ZIP**. يمكن جلبها عبر `Download-OfficialImages.ps1` بعد فك الحزمة إلى مجلد المشروع. ملف HTML يعرض الصور مباشرة من المواقع الرسمية عند وجود الإنترنت.
- لا تعتمد صورة في وضع `رابط ضمن مجموعة صور المصدر الرسمي` قبل معاينتها والتأكد من تطابق نافذتها.
- الملفات المحلية والأصول الرقمية مقتصرة على المرجعية الداخلية؛ لا تستخدم صور الأمين كأصول تطبيق Petra أو شعار له.

**الختام:** لا تبدأ التنفيذ تلقائيًا. المخرج المطلوب من البرومبت عند التفعيل هو أولًا تقرير Reference Matrix وخطة ملفات، وبعد إذن تنفيذ مستقل تبدأ تغييرات UI المحدودة.
