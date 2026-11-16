# تعيين مراجع الأمين على شاشات Petra — مع منع نقل المراجع خطأ

| ملف Petra | مرجع الأمين المباشر | القرار |
|---|---|---|
| `src/PetraERP.Desktop/DesktopWindow.xaml` | UI-01, UI-02, UI-23 | Ribbon / Command panel / StatusBar |
| `src/PetraERP.Desktop/Controls/MdiWorkspaceHost.cs` + `Shell/MdiLayoutEngine.cs` | UI-03..UI-06 | النوافذ العائمة وإرساء الأدلة فقط |
| `src/PetraERP.Desktop/Accounting/ChartOfAccountsView.xaml` | UI-07, UI-08 | تخطيط شجرة الدليل |
| `src/PetraERP.Desktop/Accounting/AccountCardView.xaml` | UI-10 | بطاقة الحساب |
| `src/PetraERP.Desktop/Inventory/ItemCardView.xaml` | UI-09 | بطاقة المادة (الجزء الظاهر فقط) |
| `src/PetraERP.Desktop/Parties/PartyCardView.xaml` | UI-24, UI-28 | بطاقة الزبون/المورد وتبويب الحسابات |
| `src/PetraERP.Desktop/Sales/SalesInvoiceCardView.xaml` | UI-11 وUI-25 | بيع / شراء؛ لا تفرض حقول نمط على آخر |
| `src/PetraERP.Desktop/Shell/DailyVoucherView.xaml` | UI-12 | سند يومية فقط |
| سند قبض/دفع مالي | REFERENCE_MISSING | لا تنسخ سند يومية أو سند POS مكانه |
| `src/PetraERP.Desktop/Accounting/AccountLedgerView.xaml` | UI-27 | نافذة خيارات دفتر الأستاذ |
| `src/PetraERP.Desktop/Accounting/TrialBalanceView.xaml` | REFERENCE_MISSING | UI-26 **مصنّف** وليس تلقائيًا ميزان المراجعة العادي |
| `src/PetraERP.Desktop/Reporting/ProfessionalReportParametersView.xaml` | UI-14..UI-18 | حدود كل نافذة حسب ما تظهره الصورة |
| `src/PetraERP.Desktop/Reporting/PetraInteractiveReportGrid.xaml` | UI-19..UI-20 | معاينة التقرير لا إعداد PDF النهائي |
| `src/PetraERP.Desktop/Shell/ProgramOptionsWindow.xaml` | UI-13 | خيارات البرنامج |

## ما لا يجوز ادعاؤه من الصور

- استخدام شركة الأمين لمكتبة AvalonDock أو مكتبة Ribbon بعينها.
- تطابق واجهة ميزان المراجعة العادي مع «ميزان المراجعة المصنّف».
- تصميم قبض/دفع مالي من شكل سند يومية أو مستند POS.
- تصميم قوالب الطباعة PDF من صورة شبكة المعاينة.
- أن الصورة المرئية تثبت قواعد العمل المحاسبية أو الصلاحيات.

أية نافذة غير موثّقة بالصورة: أوقف المطابقة الشكلية لتلك النافذة، وصنّفها `REFERENCE_MISSING`، لكن يمكن معالجة خلل سلوكي مثبت في الكود فقط بتوثيق `BEHAVIOR_ONLY` عندما يبدأ التنفيذ لاحقًا.
