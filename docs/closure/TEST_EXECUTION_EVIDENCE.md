# أدلة تنفيذ الاختبارات (Gate A/B)

مسار الأدوات: `DOTNET_ROOT=D:\PetraERP\.tools\dotnet\10.0.402`. اختبارات xUnit v3 تُشغَّل بملف `.exe` الناتج (`dotnet test` يرفض في SDK 10 لهذا المستودع). اختبارات SQL الصريحة: `PETRAERP_SQL_TEST_SERVER=.\SQLEXPRESS`, `PETRAERP_SQL_TEST_TRUST_CERTIFICATE=1`, `--explicit on`.

| المجموعة | الأمر | النتيجة |
|---|---|---|
| Build Release | `dotnet build PetraERP.slnx -c Release` (بعد `restore --locked-mode`) | PASS (0/0) |
| Domain / Application / Reporting / Modules | exe الاختبارات | 217 / 94 / 40 / 30 — PASS |
| Infrastructure (افتراضي) | exe | 120 PASS، 285 صريح غير مُشغَّل في التشغيل الافتراضي |
| Api (افتراضي) | exe | 50 PASS، 102 صريح غير مُشغَّل افتراضيًا |
| Desktop | exe | **661** (632 + 10 كاش + 19 إعادة) — 652 PASS، **9 SKIPPED** (تحتاج SQL حيًّا، لم تُشغَّل) |
| SQL صريح (مشغَّل فعليًا هذه الجلسة) | `--explicit on` | PostingSql 12، IndependentAccounting 21، Inventory 5، FifoWarehouse 3، InventoryOpening 1، BulkDocumentPosting 5، ModuleFinancialPostingGateway 6، ModuleInventoryMovementGateway 1، ApiSecurity 5، ExternalModuleHttp 1، InventoryDocumentHttp 1، **LedgerManagementHttp 40: 38 PASS، 2 FAIL قبل الإصلاح** (Concurrent و Soak) — **بعد الإصلاح كلاهما PASS منفردَين** |
| LedgerManagementHttp كاملة بعد كل إصلاحات Gate B (28 د 19 ث) | `--filter-class *.LedgerManagementHttpSqlTests --explicit on` | **40/40 PASS** (تشمل Concurrent 1/5/10/25/50 وSoak 120ث ودفعات الترحيل والمبيعات والمشتريات والسندات) — `.artifacts/gate-b/ledger-management-full-after-fixes.txt` |
| تسجيل حزمة موديول موقّعة عبر HTTP (توقيع ECDSA فعلي) | `--filter-class *.ExternalModuleHttpSqlTests` | PASS |
| الحزم الافتراضية بعد كل التعديلات | exe | Domain 217، Application 94، Modules **37**، Reporting 40، Infrastructure 120/405، Api 50/152، Desktop **664** (655 PASS، 9 SKIPPED) |

## BLOCKED / غير مُشغَّل
- بقية مجموعات SQL الصريحة في Infrastructure/Api (≈ 250 اختبارًا) لم تُشغَّل هذه الجلسة.
- UI Automation الحقيقية، Soak ساعة (شُغّل 120ث)، BenchmarkDotNet، Query Store، Extended Events: لم تُنفَّذ.
- اختبار واحد في Desktop (`AccountDirectoryListsControlAccountsInTheTreeAndTheirChildrenAsCards`) يعتمد `Task.Delay(100)` ويفشل أحيانًا تحت ضغط الجهاز؛ ينجح منفردًا (متذبذب معروف، لم يُصلَح).

## دفتر الأستاذ — ترقيم بالمفتاح (Keyset) — 2026-11-15
- التنفيذ: `SqlLedgerQueryService.AccountLedger.cs` — ترتيب ثابت (تاريخ، PostingSequence، JournalId، LineId)، مؤشر موقَّع HMAC-SHA256 صالح 30 دقيقة يحمل الرصيد الجاري والمجاميع وعلامة اللقطة (أعلى PostingSequence)، يُرفض عند التلاعب أو اختلاف الحساب/المدى/الفرع. بلا موقِّع يبقى الترقيم بالصفحات كما كان.
- API: معامل `cursor` + `NextCursor` + `SnapshotPostingSequence`؛ العقد والعميل مُولَّدان (`Update-ApiContract -Check` = PASS).
- النتائج: `LedgerKeysetPagingSqlTests` + `LedgerReportSqlTests` (SQL حقيقي) 5/5؛ API 154/154؛ Desktop 664/664.
- غير منفَّذ: استعمال الشاشة للمؤشر (ما زالت تتصفح بالصفحات)، وقياس زمن الصفحة 1000 على بيانات كبيرة.
- قياس (`MeasureDeepPageByNumberVersusByCursor`، SQL Express محلي، 1000 قيد، الصفحة 20 بحجم 50، وسيط 5 تشغيلات): بالرقم **49.6ms** (أدنى 47.3) مقابل بالمؤشر **12.8ms** (أدنى 12.4). حجم صغير وجهاز مثقل؛ يدل على الاتجاه فقط ولا يثبت هدف ≤1000ms على ملايين الأسطر.
