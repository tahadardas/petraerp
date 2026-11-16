# PETRA ERP - قياسات الأداء الحقيقية (2026-11-04)

> هذا الملف هو المرجع الوحيد للأرقام المقاسة فعليًا على SQL Server حقيقي (SQL Server 2022 Express محلي، قواعد اختبار مؤقتة معزولة).
> أرقام `PERFORMANCE_BENCHMARKS.md` (3 نوفمبر) مأخوذة من كائنات وهمية في الذاكرة (`AccountingFixture`) ولا تمثل أداء النظام.

## 1. بيئة القياس
- جهاز تطوير (ليس بيئة إنتاج/LAN مرجعية). SQL Server 2022 Express على نفس الجهاز، لا شبكة بين العميل والخادم.
- .NET SDK 10.0.402، Release. تشغيل واحد لكل قياس (لا P95/P99 إحصائيًا للتقرير الثقيل) -> الفروق الزمنية الصغيرة ضمن الضجيج.

## 2. ما أُضيف: قياس الطلبات (Observability)
- `RequestTimingMiddleware` (Api): زمن كل طلب + زمن SQL + عدد أوامر SQL، يُرجَع في ترويسة `Server-Timing` ويُسجَّل (Warning إذا >= 500ms، Debug غير ذلك).
  لا يُسجَّل إلا: Method، قالب المسار (وليس القيم)، الحالة، الأزمنة. لا سلاسل استعلام ولا أجسام ولا رموز.
- `SqlTimingInterceptor` + `RequestMetrics` (Infrastructure): يجمع زمن أوامر EF Core ضمن سياق الطلب (زمن وصول القارئ/النتيجة، لا قراءة كل الصفوف).
- مرتبط بـ `X-Correlation-Id` الموجود أصلًا عبر نطاق السجل.

## 3. الأرقام المقاسة (قبل التعديل)
### 3.1 تقرير دفتر الحساب (SQL حقيقي، 1 تشغيل)
| الصفوف | استعلام كامل (ms) | مرشّح (ms) | ذاكرة مخصصة | ذروة العملية |
|---|---|---|---|---|
| 10,000 | 1,308.8 | 629.9 | 185.8 MB | 559 MB |
| 100,000 | 5,851.0 | 3,340.8 | 1,577 MB | 665 MB |

### 3.2 قائمة فواتير المبيعات / التسويات (20 تشغيلًا)
| العملية | صفوف | أوامر SQL | P50 ms | P95 ms |
|---|---|---|---|---|
| SettlementQueries.LoadInvoicesAsync (كل الزبائن) | 5,000 | 5 | 93.3 | 129.4 |
| قائمة فواتير المبيعات (صفحة 100، 3 زبائن آجلين) | 100 | 7 | 126.8 | 169.9 |

### 3.3 أزمنة HTTP+SQL من اختبارات التكامل (لا تعتبر معيارًا: بدء بارد + تزامن اختبارات)
ملاحظات تستحق متابعة مستقلة بقياس مخصص بعد الإحماء:
- `POST /auth/login`: P50 ~3.4s بينما SQL ~38ms (تكلفة المعالجة = تجزئة كلمة المرور وبدء بارد؛ لم تُعدَّل لأسباب أمنية).
- `POST /ledger/journals/{id}/post`: P50 ~3.0s، متوسط 42 أمر SQL للطلب.
- `POST .../sales/invoices/save-and-process`: أحيانًا 143 أمر SQL (متوسط) و P50 ~0.83s.
- `POST /purchases/invoices/{id}/post`: متوسط 169 أمر SQL.

## 4. التعديلات المنفذة والنتيجة
| الملف | التعديل | الأثر المقاس |
|---|---|---|
| `Reports/Professional/SqlProfessionalReportService.Ledger.cs` | حساب «الحساب المقابل» قبل حلقة الصفوف وإنشاء كل DTO مرة واحدة (كان يُنشأ ثم يُستبدل بنسخة `with`) | ذاكرة مخصصة: 10k: 185.8 -> 93.9 MB (-49.5%)؛ 100k: 1,577 -> 842.6 MB (-46.6%). الزمن: 5,851 -> 5,423 ms (100k) و1,309 -> 1,303 ms (10k)؛ فروق الزمن ضمن الضجيج (تشغيل واحد). الاختبار نفسه ينجح ويتحقق من عدد الصفوف والأرصدة العشرية. |
| `Api/Hosting/RequestTimingMiddleware.cs`، `Infrastructure/Diagnostics/*`، `PetraPipeline.cs`، `PetraServiceRegistration.cs`، `AccountingDbContextFactory.cs`، `SqlFiscalDatabaseResolver.cs` | قياس الطلبات و SQL | لا أثر وظيفي؛ البناء 0 تحذيرات/0 أخطاء |

## 5. نتائج الاختبارات بعد التعديل (Release)
| المجموعة | النتيجة |
|---|---|
| Domain 217، Application 94، Modules 25، Reporting 40 | كلها ناجحة |
| Infrastructure (غير الصريحة) 385 | 117 ناجح، 268 متخطّى (صريحة SQL) |
| Infrastructure الصريحة: ProfessionalReportingScale + SettlementQueriesMeasurement | 3/3 ناجحة (قبل التعديل)، 2/2 ناجحة (بعد تعديل تقرير الدفتر) |
| Api (بـ SQL صريح) | فشل 1: `ApiContractTests.OpenApiDocumentMatchesTheCommittedContract` (انحراف عقد OpenAPI؛ عدد المسارات 233 والمخططات 458 متطابق، وتعديلي لا يضيف نقاط/مخططات). لم يُتحقق منه مقابل شجرة نظيفة |
| Desktop 581 | 568 ناجح، 9 متخطّى، 4 فاشلة: `NoTextBoxTakesARawIdentifier` (ItemCardView.xaml)، `FiscalYearIsExplicitAndUnsupportedFieldsCannotBeSelected`، `NoRouteAppearsTwiceAndEveryNavigationRouteIsReachableFromTheMenu` (46 متوقع/45 فعلي)، `InvalidGeometryFallsBackWithoutNonfiniteOrExtremeNativeCoordinates` — لا علاقة لها بمسار HTTP؛ لم تُقارن بشجرة نظيفة |
| `tools\Verify-PetraERP-Full.ps1` | يتوقف عند فحص `git diff --check` (مسافات زائدة في سكربتات `tools/` غير ملتزمة) قبل تشغيل أي اختبار |

## 6. ما لم يُنفذ / غير مثبت
- لا P95/P99 لمسارات HTTP بعد إحماء؛ لا اختبار متعدد المستخدمين؛ لا اختبار ذاكرة طويل (ساعة)؛ لا قياس على LAN/عتاد مرجعي.
- لم يُقاس أثر `ReferenceDataCache` (الواجهة) على SQL/HTTP فعليًا.
- لم تُراجع الفهارس بخطط تنفيذ فعلية (Query Store/Actual Plans) بعد.
- لا تغيير على: ترقيم الخادم (القوائم الرئيسية مرقّمة أصلًا)، Dapper، SignalR، DbContext pooling.

## 7. الخطوة التالية المقترحة (مرتبة بالأثر)
1. قياس HTTP مُحمّى بتكرار (P50/P95) لـ: save-and-process (143 أمر SQL)، post (42-169 أمر)، login.
2. فحص أوامر SQL المتكررة داخل save-and-process / purchases post (احتمال N+1 أو تحميل متكرر للمراجع).
3. إعادة تصميم تقرير الدفتر ليدعم ترقيم/تدفق بدل تجميع كل الصفوف (سقف `MaximumRows` موجود).
4. مراجعة الفهارس على `ledger.JournalLines` بخطة تنفيذ فعلية عند 100k+ صف.

## 8. الرجوع
كل التعديلات على الفرع `perf/optimization`؛ التراجع: `git checkout` للملفات المذكورة أو حذف الفرع. لا تغييرات مخطط قاعدة بيانات.

## 9. حفظ + ترحيل فاتورة مبيعات عبر HTTP حقيقي (اختبار صريح جديد، إعداد Debug - الزمن غير ممثل)
الاختبار: `tests/PetraERP.Api.Tests/LedgerManagementHttpSqlTests.Performance.cs` (`MeasureWarmSalesSaveAndPostLatencyAndSqlCommandCount`)، 3 إحماء + 20 قياسًا، فاتورة بسطرين، وضع الترحيل ApproveAndPost.
تشغيل: `PetraERP.Api.Tests.exe --explicit only --filter-method '*MeasureWarmSalesSaveAndPostLatencyAndSqlCommandCount'` مع `PETRAERP_SQL_TEST_SERVER` و `PETRAERP_BENCH_OUT_HTTP`.

| المقياس | P50 | P95 |
|---|---|---|
| زمن الطلب الكلي (ms) | 899.7 | 1025.1 |
| زمن SQL المسجل (ms) | 234.1 | 309.2 |
| أوامر SQL للطلب | 143 | 159 |

- التشغيل الأول تأثر بعملية اختبار قديمة تستهلك CPU (P95=5.6s) فاستُبعد؛ الجدول من التشغيل الثاني.
- ≈74% من الزمن خارج SQL، لكن البناء Debug فلا يجوز استنتاج شيء قبل إعادة القياس بـ Release.
- تكرار داخل الطلب الواحد (تنفيذات/23 طلبًا ≈ لكل طلب): فحص نطاق السنة المالية ×6 + ×6، بحث ActorReferences ×6، تحميل الزبون ×5، حساب تعرّض الائتمان/التسويات (فاتورة + تخصيصات سندات + تطبيقات تسوية + بنود مفتوحة) ×4، كل منها بأزمنة ≈0.1-0.4ms للتنفيذ.
- لم يُعدَّل شيء هنا: التكرار قد يكون مقصودًا (إعادة التحقق عند كل مرحلة Save/Approve/Post وفق قاعدة عدم الاعتماد على حالة قديمة في العمليات المالية). أي تقليص يحتاج مراجعة محاسبية وتغطية اختبار قبله.
- ملاحظة: أغلى العبارات إجمالًا في القياس هي استعلامات فحص المخطط `sys.*` من بنية الاختبار نفسها (32 تنفيذًا) وليست من التطبيق.

## 10. تحديث لاحق (نفس اليوم) - إعادة القياس بـ Release وتسجيل الدخول
### 10.1 حفظ + ترحيل فاتورة مبيعات (Release، بعد إنهاء عملية اختبار قديمة كانت تستهلك CPU)
| المقياس | P50 | P95 |
|---|---|---|
| زمن الطلب الكلي (ms) | 1,286.2 | 1,805.3 |
| زمن SQL (ms) | 425.0 | 649.4 |
| أوامر SQL | 143 | 159 |
- Release أبطأ من تشغيل Debug السابق (900ms) رغم أن عدد الأوامر ثابت؛ **ضجيج الجهاز أكبر من أي فرق بين البناءين** (SQL Express محدود الموارد، عمليات أخرى). لا يجوز استنتاج تحسن/تراجع من تشغيل واحد؛ المعتمد من هذه التجربة هو **عدد الأوامر (143) فقط**. لإعادة القياس: جهاز هادئ، 3+ تشغيلات متتالية، ومقارنة الوسيط.

### 10.2 تسجيل الدخول
| القياس | النتيجة |
|---|---|
| تجزئة/تحقق كلمة المرور (PBKDF2-SHA512، 210,000 تكرار، .NET 10) | P50 344.4ms، P95 388.1ms |
| تسجيل دخول HTTP مُحمّى (10 تشغيلات) | P50 355.8ms، P95 375.2ms |
| أول تسجيل دخول بعد تشغيل الخادم (بارد) | 3,839.7ms |
- الاستنتاج: كلفة الدخول المُحمّى تساوي كلفة التجزئة تقريبًا (مقصودة أمنيًا؛ لم تُغيَّر). الـ3.4 ثانية المرصودة سابقًا هي **أول طلب بارد (JIT/أول استخدام)**.
- مقترح غير منفذ (يحتاج قياسًا على النشر الفعلي): ReadyToRun/TieredPGO لتطبيق الـAPI، وخدمة إحماء عند الإقلاع تمرّ على مسار الدخول والنماذج. لم تنفذ لأن بنية الاختبار تبني النموذج مسبقًا فلا يمكن قياس أثرها هناك.

### 10.3 تحقق انحدار لتعديل تقرير الدفتر
- اختبارات التقارير على SQL حقيقي (`*Report*`، صريحة): **17/17 ناجحة** (تشمل LedgerReportSqlTests وProfessionalReportSqlTests وAnalyticalReportSqlTests).
- اختبارا المقياس 10k/100k: ناجحان مع بقاء أرصدة العشري مطابقة.

### 10.4 اختبارات القياس المضافة (كلها Explicit، لا تعمل افتراضيًا)
- `tests/PetraERP.Api.Tests/LedgerManagementHttpSqlTests.Performance.cs`: `MeasureWarmSalesSaveAndPostLatencyAndSqlCommandCount`، `MeasureColdAndWarmLoginLatency`
- `tests/PetraERP.Infrastructure.Tests/PasswordHashingMeasurementTests.cs`
- متغيرات الإخراج: `PETRAERP_BENCH_OUT_HTTP`، `PETRAERP_BENCH_OUT_LOGIN`، `PETRAERP_BENCH_OUT_HASH`

### 10.5 حالة معايير الإغلاق (المستند الأصلي، القسم 20)
محقق جزئيًا: قياس الأداء (9)، Benchmarks حقيقية (15 جزئيًا)، ترقيم القوائم الكبيرة (3: القوائم الرئيسية مرقّمة أصلًا). **غير محقق/غير مثبت:** 5، 6، 11-14، 16، 17 (اختبارات Api/Desktop فيها 5 إخفاقات غير مقارنة بشجرة نظيفة)، 18-20. **لا يُعلن قبول نهائي.**

## 11. Concurrency and deadlock analysis (measured, SQL Server 2022 Express)

Test: `MeasureConcurrentSalesPostingAndLedgerIntegrity` (10 and 25 concurrent users, POST `sales/invoices/save-and-process`).

| Metric | Result |
|---|---|
| Requests | 82 |
| Succeeded (2xx) | 79 |
| Server errors | 3, all `503 server.transient_failure` (SQL error 1205, deadlock victim) |
| Ledger imbalance (SUM BaseDebit - SUM BaseCredit) | 0 |
| Duplicate invoice numbers | 0 |
| Throughput (dev machine) | about 1 to 1.7 req/s |

Deadlock graphs were read from `system_health*.xel` (the ring buffer was empty). `sys.dm_os_performance_counters` reports 3 deadlocks for this server run; the files contain 6 graphs in total, so some come from earlier runs.

Lock resources found in the 6 graphs:

| Resource | Occurrences | Mode |
|---|---|---|
| `audit.ActorReferences` PK | 4 | U |
| `sales.Invoices` PK | 5 | RangeS-U, RangeX-X, RangeS-S |
| `ledger.JournalEntries` IX_JournalEntries_CompanyBranchStateDate | 2 | RangeS-S |
| `ledger.JournalAudit` UQ_JournalAudit_JournalRevision | 1 | RangeS-S |

Observed pattern: one session holds SERIALIZABLE range locks taken by a read (`SettlementQueries.LoadInvoicesAsync`, the credit-exposure query, or the `ActorReferences` UPDLOCK/HOLDLOCK lookup in `VerifyWriteActorAsync`) and then needs an insert or update lock on the same range. A second session holds the opposite combination. This is lock-order inversion, not a missing index and not a slow query.

What already exists in the code: `LockSalesScopeAsync` takes `UPDLOCK,HOLDLOCK` on the company and branch rows, which serializes posting. The deadlocks therefore come from paths that read without that lock (credit snapshot in `GetInvoiceAsync`, the actor lookup order across the save steps of one request).

What the API does today: `ApiExceptionHandler` returns 503 with `Retry-After: 1` for SQL 1205/-2. The victim transaction is rolled back, so a retry with the same idempotency key is safe. A search of the Desktop sources found no automatic retry on this response.

Not changed in this session: no isolation level, lock order or index was changed. Doing so touches accounting and posting code and needs its own review. Candidate options, not applied and not measured:

1. Retry on 503 `server.transient_failure` with the same idempotency key (client or API middleware). Lowest risk, no accounting change.
2. Take the company-row lock before any credit-snapshot read that runs under SERIALIZABLE, so readers queue behind posting. Needs a re-run of the 17 report tests and the concurrency test.
3. Take the `ActorReferences` lock first in every write path to fix the order.

Open items: integrity query strengthening (journal-line counts per invoice state; the earlier figure of 4 journals vs 82 invoices is still unexplained), soak test not run (1 hour is not feasible in one session), baseline comparison of the 5 failing tests not done.

## 12. Change applied: automatic replay of deadlock-victim responses (Desktop)

- New src/PetraERP.Desktop/Connection/TransientRetryHandler.cs, wired inside DesktopSession (resource client only, not the login client).
- Replays a response of 503 with code server.transient_failure at most 2 times (400 ms, then 1200 ms). Only for GET/HEAD, or for requests carrying an idempotency key (header Idempotency-Key or body property idempotencyKey). A request without a key is never replayed.
- No accounting, locking, isolation or posting code was touched. The server contract (503 + Retry-After) is unchanged.
- Tests: TransientRetryHandlerTests (4 tests, all pass): keyed POST replayed with the same body; unkeyed POST not replayed; stops after 2 replays; other failures not replayed.
- Desktop full run: 5 failures. With this change reverted, 4 of the same 5 classes still fail (DocumentKeyboardContract, WindowPlacement, ProfessionalReportingWorkflow, TopMenuRegistry), so they are pre-existing. AmeenDirectoryTests passes when run alone and failed once in the full run, so it depends on test order. One WPF render test (WpfFoundationTests.RealWpfResourcesRender...) hung for over 2 minutes in the full run and was stopped.
- Not measured: how often the replay turns a real deadlock into a success. The concurrency test calls the API directly, not through the Desktop client, so its 3 x 503 are unchanged.
