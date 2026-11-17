# Petra ERP — مصفوفة الوضع الحالي والمستهدف (AS-IS / TO-BE Matrix)
## مواصفة إعادة التأهيل الشاملة وفق أسلوب الأمين 10 ومنع التكرار (Single Screen Ownership)
**التاريخ:** 2026-11-15  
**المرجع التنفيذي:** `PetraERP_ANTIGRAVITY_AMEEN10_NO_DUPLICATE_UX_MASTER_20261115_AR.md`  
**حالة الحوكمة:** الإحلال داخل المكان — لا شاشات مكررة — مطابقة سلوك المحاسب

---

## 1. جدول حوكمة الشاشات والملكية الفردية (Single Screen Ownership Table)

| FeatureId | RouteId | MenuId | View XAML | ViewModel | Factory/DI | EntryPoints | Permissions | Client / API | Tests |
|---|---|---|---|---|---|---|---|---|---|
| `vouchers.daily` | `daily-vouchers` | `vouchers.daily` | `Shell/DailyVoucherView.xaml` | `DailyVoucherViewModel` | Singleton DI | القائمة العلوية، البحث السريع Ctrl+K، اختصار السندات | `Ledger.Journals.View` | `IGeneralLedgerClient`, `ILedgerReportsClient` | `FiftyRowEntryContractTests`, `DesktopMenuNavigationTests` |
| `vouchers.journals` | `journals` | `vouchers.journals` | `GeneralLedger/JournalsView.xaml` (قائمة) / `GeneralLedger/JournalDocumentView.xaml` (سند) | `GeneralLedgerViewModel` | `JournalDocumentFactory` (مستند) / Singleton (قائمة) | القائمة، شريط الأوامر، رابط المستند الأصلي، Ctrl+K | `Ledger.Journals.View` | `IGeneralLedgerClient`, `IFiscalCalendarClient` | `GeneralLedgerViewModelTests`, `FiftyRowEntryContractTests` |
| `vouchers.treasury` | `treasury-vouchers` | `vouchers.treasury` | `Treasury/VouchersView.xaml` | `VouchersViewModel` | Singleton DI | القائمة العلوية، البحث السريع، كشف الحساب | `Treasury.Vouchers.View` | `ITreasuryVouchersClient` | `TreasuryWorkstationTests`, `FiftyRowEntryContractTests` |
| `invoices.sales` | `sales-invoices` / `sales-invoice-card` | `invoices.sales` | `Sales/SalesInvoicesView.xaml` (قائمة) / `Sales/SalesInvoiceCardView.xaml` (بطاقة) | `SalesInvoicesViewModel` / `SalesInvoiceCardViewModel` | `SalesInvoiceCardFactory` | القائمة، البحث السريع، زر جديد، بطاقة العميل | `Sales.Invoices.View` | `ISalesInvoicesClient`, `IPartiesClient` | `SalesInvoiceWorkstationTests`, `FiftyRowEntryContractTests` |
| `invoices.purchases` | `purchase-invoices` | `invoices.purchases` | `Purchases/PurchasesView.xaml` | `PurchasesViewModel` | Singleton DI | القائمة العلوية، البحث السريع، بطاقة المورد | `Purchases.Invoices.View` | `IPurchasesClient`, `IPartiesClient` | `PurchaseWorkstationTests`, `FiftyRowEntryContractTests` |
| `inventory.operations` | `inventory-operations` | `inventory.operations` | `Inventory/InventoryOperationsView.xaml` | `InventoryOperationsViewModel` | Singleton DI | قائمة المواد، البحث السريع، بطاقة المستودع | `Inventory.Movements.View` | `IInventoryOperationsClient` | `InventoryDocumentsFixture` |
| `accounts.chart` | `accounts` | `accounts.chart` | `Accounting/ChartOfAccountsView.xaml` | `ChartOfAccountsViewModel` | Singleton DI | قائمة حسابات، شجرة الحسابات، Ctrl+F11 | `Ledger.Accounts.View` | `IGeneralLedgerClient` | `AccountingWorkstationTests` |
| `inventory.items` | `items` | `inventory.items` | `Inventory/ItemsView.xaml` (دليل) / `ItemCardView.xaml` (بطاقة) | `ItemsViewModel` / `ItemCardViewModel` | `ItemCardFactory` | قائمة المواد، F7، البحث السريع، من شبكة الفاتورة | `Inventory.Items.View` | `IInventoryItemsClient` | `ItemWorkstationTests`, `AmeenItemCardTests` |
| `parties.directory` | `parties` | `parties.directory` | `Parties/PartiesView.xaml` (دليل) / `PartyCardView.xaml` (بطاقة) | `PartiesViewModel` / `PartyCardViewModel` | `PartyCardFactory` | قائمة زبائن، F5، البحث السريع، من رأس الفاتورة | `Parties.Directory.View` | `IPartiesClient` | `PartyWorkstationTests` |
| `accounts.ledger` | `account-ledger` | `accounts.ledger` | `Reporting/ProfessionalReportParametersView.xaml` (خيارات) / `PetraInteractiveReportGrid.xaml` (معاينة) | `ProfessionalReportPageViewModel` | Singleton DI | قائمة الحسابات، Ctrl+L، بطاقة الحساب | `Ledger.Reports.View` | `ILedgerReportsClient` | `ProfessionalReportingWindowsTests` |

---

## 2. مصفوفة التحليل المقارن للشاشات الرئيسة (AS-IS vs TO-BE)

### 1) سند اليومية (Daily Voucher — `DailyVoucherView.xaml` / `DailyVoucherViewModel.cs`)
- **التصنيف المرجعي:** `OFFICIAL_REFERENCE` (الأمين 10: UI-12) + `PETRA_REQUIREMENT` (50 سطرًا افتراضيًا).
- **الوضع الحالي (AS-IS) `CONFIRMED_CODE`:**
  - عدد صفوف البدء: صف واحد فقط يتم إنشاؤه عبر `ResetRows()` و `AddRow()`.
  - آلية إضافة الصف: النقر اليدوي على زر "إضافة سطر" أو الضغط على مفتاح `Insert`.
  - الحفظ: يتجاهل الصفوف الفارغة عبر `Rows.Where(r => !r.IsBlank)`.
  - التنقل بالكيبورد: `PetraAccountingDataGrid` مفعّل بـ `EntryMode="True"` ولكن `AutoGrowCommand` غير مربوط، مما يوقف المستخدم عند نفاد الأسطر.
  - اختصار البحث: لا يدعم `F3` في `LookupPicker` لاختيار حساب الطرف المقابل.
  - الأخطاء: تظهر في نص أعلى الشاشة عند عدم توازن أو نقص البيانات.
- **الوضع المستهدف (TO-BE):**
  - عدد صفوف البدء: **50 سطرًا منطقيًا جاهزًا للإدخال فور فتح السند أو تفريغه** عبر `EnsureRowBuffer(50)`.
  - آلية إضافة الصف: استمرار تلقائي `AutoGrow(10)` عند الوصول لآخر صف دون الحاجة للمس الفأرة.
  - الحفظ: لا يُحفظ أي صف فارغ في Payload أو قاعدة البيانات؛ مع كشف أي صف مدخل جزئيًا وتحديده برسالة واضحة.
  - التنقل بالكيبورد: ربط `AutoGrowCommand` في `DailyVoucherView.xaml`؛ تفعيل `F3` ومفتاح `*` لفتح دليل الحسابات مباشرة داخل الخلية.
  - المجاميع: شريط المجاميع المباشر (إجمالي القبض، إجمالي الدفع، الرصيد النهائي للصندوق) متطابق مع بطاقات الأمين الزرقاء.

---

### 2) سند القيد المحاسبي (General Ledger Journal — `JournalDocumentView.xaml` / `GeneralLedgerViewModel.cs`)
- **التصنيف المرجعي:** `OFFICIAL_REFERENCE` (الأمين 10: سند قيد) + `CONFIRMED_CODE`.
- **الوضع الحالي (AS-IS) `CONFIRMED_CODE`:**
  - عدد صفوف البدء: 50 صفًا جاهزًا عبر `EnsureLineBuffer(50)`.
  - آلية إضافة الصف: `AutoGrowCommand` مرتبط بـ `AutoGrowLines(10)` ومفتاح `Insert`.
  - الحفظ: يحفظ فقط الصفوف المكتملة عبر `CompleteLines`، ولا يرسل صفوفًا فارغة.
  - التحقق: يمنع ترحيل السند غير المتوازن، ويعرض إجمالي المدين والدائن والفرق = صفر.
  - التنقل بالكيبورد: Enter ينتقل أفقياً ثم للسطر التالي. ينقصه دعم `F3` المباشر في خلايا الحسابات ومراكز الكلفة.
- **الوضع المستهدف (TO-BE):**
  - دعم `F3` و `*` على كل حقل مرجعي داخل الشبكة.
  - تحسين مظهر الترويسة المضغوطة لتوفير 65% من المساحة لشبكة القيود.
  - إبقاء القيد متوازنًا لحظيًا مع تلوين حالة التوازن (أخضر عند التوازن، تحذير عند الفرق).

---

### 3) سندات الصندوق — القبض والدفع (Treasury Vouchers — `VouchersView.xaml` / `VouchersViewModel.cs`)
- **التصنيف المرجعي:** `OFFICIAL_REFERENCE` (الأمين: سند قبض/دفع) + `CONFIRMED_CODE`.
- **الوضع الحالي (AS-IS) `CONFIRMED_CODE`:**
  - النمط الفردي (طرف واحد): حقل طرف واحد ومبلغ رئيسي.
  - النمط الموزع (`IsDistributionPayment`): شبكة `LineRows` فيها 50 سطرًا جاهزًا بـ `EnsureDistributionBuffer(50)`.
  - النقص: `AutoGrowCommand` غير معرّف كأمر في ViewModel وغير مربوط في شبكة `LineRows` في `VouchersView.xaml`.
  - خلط شاشات التسوية: أسطر تسوية الفواتير `AllocationRows` منفصلة في الكود ولكنها تحتاج تنظيمًا بصريًا أوضح لعدم إرباك المحاسب.
- **الوضع المستهدف (TO-BE):**
  - تعريف `AutoGrowCommand` صراحة في `VouchersViewModel.Editor.cs` وربطه بالشبكة.
  - تفعيل `EntryMode="True"` وشريط المجاميع المباشر المتطابق.
  - دعم `F3` في حقول الحسابات والأطراف ومراكز الكلفة.
  - إبقاء السند الفردي فائق السرعة دون فرض تعبئة جداول، مع تقديم السند الموزع للمدفوعات والمقبوضات متعددة الحسابات.

---

### 4) فاتورة المبيعات وفاتورة المشتريات (Sales & Purchases Invoices)
- **التصنيف المرجعي:** `OFFICIAL_REFERENCE` (الأمين 10: UI-11, UI-25) + `PETRA_REQUIREMENT` (البيان بعد السعر مباشرة).
- **الوضع الحالي (AS-IS) `CONFIRMED_CODE`:**
  - فواتير المبيعات (`SalesInvoiceCardView.xaml`): مجهزة بـ 50 صفًا، وتحتوي عمود البيان بعد السعر.
  - فواتير المشتريات (`PurchasesView.xaml`): مجهزة بـ 50 صفًا مع `AutoGrowCommand`.
  - المشكلة: التباين في شريط الأوامر (أسماء أوامر مختلفة مثل `BeginNewCommand` مقابل `NewCommand`).
  - البحث: F2 يعمل، بينما المحاسب يبحث تلقائيًا بـ `F3` أو كتابة أول حرفين.
- **الوضع المستهدف (TO-BE):**
  - توحيد شريط الأوامر `DocumentCommandBar` المرتبط بعقد `IDocumentScreen`.
  - دعم `F3` و `*` لاختيار المادة والوحدة والمستودع.
  - الحساب المحلي الفوري للإجماليات، الضريبة، الحسميات، وصافي الفاتورة بدون أي طلبات شبكة عند التنقل بين الخلايا.

---

### 5) عمليات المستودع والجرد (Inventory Operations & Stocktaking)
- **التصنيف المرجعي:** `OFFICIAL_REFERENCE` (أمين 10: جرد ومناقلات) + `DESIGN_PROPOSAL`.
- **الوضع الحالي (AS-IS) `CONFIRMED_CODE`:**
  - الواجهة تجمع قائمة المستندات مع شبكة الإدخال في مساحة رأسية ضيقة (160 DIP).
  - الأزرار مبعثرة في `WrapPanel` بدون أنماط موحدة.
- **الوضع المستهدف (TO-BE):**
  - فصل وضع القائمة عن وضع المستند/المحرر أو تطبيق القالب الموحد (`DocumentCommandBar` + ترويسة + شبكة موسعة).
  - دعم 50 صفًا في المناقلات والتسويات المخزنية.

---

## 3. مصفوفة تصنيف مراجع الأمين 10 (Feature Parity Matrix)

| رقم المرجع | اسم المرجع في الأمين 10 | الشاشة المقابلة في Petra | حالة التوافق في Petra | الأثر المالي / المخزني | الإجراء في هذه الدورة |
|---|---|---|---|---|---|
| UI-01 | الشريط الرئيسي والقوائم الأفقية | `DesktopWindow.xaml` | `AMEEN_REFERENCED` (موجود ومطابق) | لا يوجد | تدقيق القوائم ومطابقة الترتيب |
| UI-02 | لوحة الأوامر والبحث السريع Ctrl+K | `DialogHost.xaml` / Palette | `AMEEN_REFERENCED` (موجود) | لا يوجد | تسريع الاقتراحات وإبقاء التركيز |
| UI-03..06 | سطح العمل MDI والأدلة العائمة | `Controls/MdiWorkspaceHost.cs` | `AMEEN_REFERENCED` (موجود ومختبر) | لا يوجد | الحفاظ على MDI وتعدد النوافذ |
| UI-07..08 | دليل الحسابات والتخطيط المزدوج | `Accounting/ChartOfAccountsView.xaml` | `AMEEN_REFERENCED` (موجود) | GL Balance | دعم F3 والبحث الفوري |
| UI-09 | بطاقة المادة | `Inventory/ItemCardView.xaml` | `AMEEN_REFERENCED` (موجود) | مخزون وتسعير | الحفاظ على الوحدات والأسعار |
| UI-10 | بطاقة الحساب | `Accounting/AccountCardView.xaml` | `AMEEN_REFERENCED` (موجود) | شجرة الحسابات | تكامل التبويبات والأرصدة |
| UI-11 | فاتورة المبيعات الكاملة | `Sales/SalesInvoiceCardView.xaml` | `AMEEN_REFERENCED` (مطابق بنسبة 90%) | ذمم ومبيعات ومخزون | تفعيل F3 والبيان بعد السعر |
| UI-12 | سند اليومية | `Shell/DailyVoucherView.xaml` | `AMEEN_REFERENCED` (يحتاج 50 صف) | قيد يومية نقدي | استبدال الصف الواحد بـ 50 صفًا وAutoGrow |
| UI-13 | خيارات البرنامج المتقدمة | `Shell/ProgramOptionsWindow.xaml` | `AMEEN_REFERENCED` (موجود) | إعدادات عامة | مطابقة التبويبات والبحث |
| UI-14..18 | مرشحات التقارير والنمط القياسي | `ProfessionalReportParametersView.xaml` | `AMEEN_REFERENCED` (موجود) | تقارير مالية | أزرار أفقية أسفل النافذة |
| UI-19..20 | شبكة معاينة التقرير والفرز | `PetraInteractiveReportGrid.xaml` | `AMEEN_REFERENCED` (موجود) | استعلامات فقط | تثبيت الرؤوس وتصدير Excel/PDF |
| UI-23 | شريط الحالة وبيانات الملف | `DesktopWindow.xaml` (StatusBar) | `AMEEN_REFERENCED` (موجود ومطابق) | سياق الشركة والسنة | إظهار اسم الشركة، الفرع، المستخدم |
| UI-24, 28 | بطاقة الزبون والمورد وتبويب الحسابات | `Parties/PartyCardView.xaml` | `AMEEN_REFERENCED` (موجود) | ذمم وأرصدة | الحفاظ على خيارات الائتمان والأسعار |
| UI-25 | فاتورة المشتريات | `Purchases/PurchasesView.xaml` | `AMEEN_REFERENCED` (موجود) | موردين ومخزون | توحيد شريط الأوامر وF3 |
| UI-27 | مرشحات دفتر الأستاذ | `Accounting/AccountLedgerView.xaml` | `AMEEN_REFERENCED` (موجود) | كشف حساب GL | مطابقة خيارات التصفية التاريخية |
| — | سند قبض/دفع مالي متعدد البنود | `Treasury/VouchersView.xaml` | `CONFIRMED_CODE` (موجود للموزع) | حركات نقدية وذمم | تفعيل AutoGrow وربط الأوامر |

---

## 4. خطة التحقق والقياس (Verification & Acceptance Gates)

1. **Gate UX-0:** إنجاز مصفوفة الملكية وتوثيق الوضع الحالي وتأكيد خلو النظام من النوافذ المزدوجة. (تم بنجاح).
2. **Gate UX-1:** تطوير المكونات المشتركة (`GridEntry.cs`, `LookupPicker.cs`, `PetraAccountingDataGrid.cs`) لدعم `F3`, `*`, `AutoGrowCommand`، واختبارها باختبارات وحدة عازلة.
3. **Gate UX-2:** حل أزمة السندات في `DailyVoucher` (50 سطرًا افتراضيًا، منع حفظ الفارغ، AutoGrow)، وتحديث سندات الصندوق `VouchersViewModel`.
4. **Gate UX-3:** مواءمة فواتير المبيعات والمشتريات وشبكات المواد.
5. **Gate UX-4:** تحسين شاشات المستودعات وبطاقات الأدلة.
6. **Gate UX-5:** تقارير ومعاينات الطباعة.
7. **Gate UX-6:** التحقق النهائي الشامل وسيناريوهات المحاسب الثمانية.
