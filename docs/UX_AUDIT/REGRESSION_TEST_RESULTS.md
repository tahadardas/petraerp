# نتائج الاختبارات — 2026-11-12

ما شُغّل فعليًا على هذا الجهاز (Windows، ‎.NET SDK 10.0.402 المحمول). لا يوجد هنا أي نتيجة لاختبار لم يُشغّل.

## Desktop.Tests (`scratch\test_desktop.ps1`، بناء Release)

| التشغيل | المجموع | ناجح | فاشل | متخطّى | ملاحظة |
|---|---|---|---|---|---|
| 1 (بعد الإصلاحات) | 602 | 592 | 1 | 9 | الفاشل: `ProfessionalReportingWindowsTests.FourReportWindows…` |
| 2 | 602 | 592 | 1 | 9 | نفس الاختبار |
| 3 (الأخير) | 602 | 593 | 0 | 9 | — |

- الاختبارات الجديدة `UxIntegrityTests`: 9 من 9 ناجحة (شُغّلت منفردة ثم ضمن المجموعة).
- المتخطّاة التسعة: اختبارات SQL الطرفية (`DesktopEndToEndSqlTests`) التي تعمل بطلب صريح فقط.

### الاختبار المتذبذب

`ProfessionalReportingWindowsTests.FourReportWindowsRenderBothThemesAtWideAndCompactWidthsAndPreviewReturnsToExactParameters`
فشل في تشغيلين كاملين من ثلاثة (عند العرض 860 بقي التخطيط العريض)، ونجح منفردًا ونجح في التشغيل الثالث.
لم تُعدَّل ملفات التقارير في هذه الجولة. السبب **غير مشخّص**؛ المرجّح اعتماده على حالة عامة مشتركة (`ReportDisplayOptionsHub`) أو على توقيت التخطيط عند تشغيله مع اختبارات متوازية.
أُضيفت إلى رسالة الفشل قيمة خيار التخطيط وعرض النافذة الفعلي ليظهر السبب عند تكراره.

## ما لم يُشغّل

- `Application.Tests`، `Infrastructure.Tests`، `Api.Tests`: لم تُشغّل في هذه الجولة (لم يتغير كودها). آخر نتيجة معروفة لها من 2026-11-05.
- اختبارات SQL الحقيقية (فواتير ← قيد ← مخزون ← تقارير): لم تُشغّل.
- لا اختبار واجهة آلي (UI Automation) ولا تشغيل للتطبيق مقابل خادم.
- لا قياس أداء قبل/بعد.
- لا تحقق بصري من تغييرات XAML في هذه الجولة (قائمة وحدات المشتريات، حقل حساب العميل، إخفاء الصفحات).

## الجولة الأخيرة: حساب الفاتورة، عمود الوحدة، سياسة حساب الأب (2026-11-12)
تم البناء بصفر أخطاء (SDK 10.0.402 من C:\Users\BIG\.dotnet). النتائج الفعلية:
- Application 94/94، Domain 217/217، Modules 25/25، Reporting 40/40.
- Api: 50 نجح، 0 فشل، 100 متخطى (SQL صريح).
- Infrastructure: 117 نجح، 0 فشل، 278 متخطى (SQL صريح). عُدّل اختبار قائمة الترحيلات ليشمل InvoiceAccountAndPartyParents (49 ترحيلًا).
- Desktop: 597 نجح، 0 فشل، 9 متخطى (من 606).
- SQL حقيقي (SQLEXPRESS، قواعد مؤقتة، PETRAERP_SQL_TEST_TRUST_CERTIFICATE=1): SalesPostingSqlTests + PartyAccountPolicySqlTests = 85/85 نجحت.
لم تُشغَّل بقية اختبارات SQL الصريحة (Ledger/Posting/ApiSecurity/Backup...) في هذه الجولة؛ ولا اختبارات الشاشات الحية على Windows بالنقر اليدوي.

## تتبع التعديلات بالقيمة: المشتريات والسندات وعمليات المستودع (2026-11-12)
Desktop: 611 اختبارًا، 602 نجح، 0 فشل، 9 متخطى (أضيفت 5 اختبارات في ValueDirtyTrackingTests). لم تتغير بقية الحزم في هذه الجولة (لم تُمس مشاريعها).

## 2026-11-14 — تشغيل SQL الحقيقي الكامل لـ Infrastructure (جزئي)
- شُغّل بـ --explicit on على SQLEXPRESS. اكتمل 350 من 395 قبل إيقافه يدويًا (اختباران ثقيلان: TenThousandInvoicesSort... تجاوز 16 دقيقة، وMeasure* للقياس). لم يُحسب كنجاح كامل.
- فشلان: (1) AlAmeenRealDataTrialTests: يحتاج PETRAERP_ALAMEEN_TRIAL_SOURCE (ليس عيبًا). (2) AmeenCardsSqlTests.PartyProfile...: عيب حقيقي — حقل Profile لم يكن موصولًا في CreateParty/UpdateParty/GetPartyById/LookupParties.
- الإصلاح: SqlPartyService.cs يستدعي ApplyProfileAsync في الإنشاء والتعديل، ويعيد Profile في التفاصيل، ويستبعد HideInSearch من البحث.
- بعد الإصلاح: البناء 0 أخطاء؛ AmeenCardsSqlTests + PartySqlTests = 9/9 ناجحة (ArabicPartySearch... وحده ~5.5 دقيقة، بسبب زرع 1000 طرف).
- لم يُعاد تشغيل: TenThousandInvoicesSort... وبقية الاختبارات الثقيلة بعد الإيقاف؛ لا ادعاء جاهزية إنتاج.
