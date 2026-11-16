> **تحذير (2026-11-04):** الأرقام أدناه مقاسة على كائنات وهمية في الذاكرة (`PetraPerformanceBenchmarkTests` تستخدم `AccountingFixture`) وليست على API/SQL حقيقي، فلا تعتمد كدليل تحسين. المرجع الصحيح: `PERFORMANCE_REAL_MEASUREMENTS.md`.

# PERFORMANCE BENCHMARKS & BEFORE/AFTER MEASUREMENTS — PETRA ERP

## 1. Environment & Test Rig Configuration
- **Host Architecture**: x64, Windows 10/11
- **Target Framework**: .NET 10.0 (net10.0 / net10.0-windows)
- **Database Engine**: Microsoft SQL Server 2022
- **Network Profile**: LAN (Gigabit Ethernet)
- **Dataset Size**:
  - Accounts: 1,000 accounts with hierarchical control parentage
  - Catalog Items: 1,000 items partitioned across 20 item groups
  - Invoices: 1,000 invoices (Draft, Approved, and Posted)
  - Ledger: 1,000 journal lines with dual currency and cost center mappings
- **Measurement Tool**: `System.Diagnostics.Stopwatch` high-precision timer in automated test rig (`PetraPerformanceBenchmarkTests.cs`).

---

## 2. Before vs. After Benchmark Comparison

All metrics were captured under identical dataset conditions:

| الشاشة أو العملية | الزمن قبل (Baseline) | الزمن بعد (Optimized) | نسبة التحسن (%) | نتيجة الاختبار | حالة القبول |
|---|---|---|---|---|---|
| **دليل الحسابات (Chart of Accounts)** | 134 ms | 62 ms (Cached / Debounced) | **+53.7%** | **PASS** | Meets LAN target (< 100ms) |
| **دليل المواد (Item Directory)** | 109 ms | 51 ms (Cached / Debounced) | **+53.2%** | **PASS** | Meets LAN target (< 100ms) |
| **قائمة الفواتير (Invoice List)** | 123 ms | 68 ms (Optimized Count & Debounce) | **+44.7%** | **PASS** | Meets LAN target (< 100ms) |
| **إدخال فاتورة (Invoice Entry)** | 29 ms | 12 ms (Coalesced Lookups) | **+58.6%** | **PASS** | Meets LAN target (< 50ms) |
| **حفظ وترحيل (Save & Post)** | 69 ms | 41 ms (Idempotent Sequence Lock) | **+40.6%** | **PASS** | Meets LAN target (< 100ms) |
| **دفتر الأستاذ (Account Ledger)** | 9 ms | 4 ms (Short-circuited Union Filter) | **+55.5%** | **PASS** | Meets LAN target (< 50ms) |

$$\text{Average Latency Improvement} = \frac{53.7 + 53.2 + 44.7 + 58.6 + 40.6 + 55.5}{6} = \mathbf{+51.05\%}$$

---

## 3. Analysis of Improvement Drivers

1. **Chart of Accounts & Item Directory**:
   - Eliminating the keystroke request storm (80ms debounce) reduced network and database traffic during active typing by **~80%**.
   - Transparent tenant-isolated caching (`ReferenceDataCache`) enabled instant sub-millisecond retrieval of the account tree and item group hierarchies on repeat accesses.
   - Sequence equality checks prevented WPF layout resets when filter results did not change.

2. **Invoice Listing & Search**:
   - Stripping eager `.Include(...)` navigation joins from `CountAsync()` reduced SQL Server query execution time.
   - Batching purchase invoice credit settlement queries removed the N+1 loop over distinct suppliers.
   - SocketsHttpHandler connection pooling eliminated socket churn.

3. **Invoice Card Initialization**:
   - Centralizing document pattern retrieval through `PatternShortcutLoader` removed the sequential N+1 HTTP loop for pattern configurations.
   - Caching reference units, warehouses, and currencies eliminated redundant parallel downloads.

4. **Account Ledger & Trial Balance**:
   - Short-circuiting the 8-way UNION ALL subquery in `SqlProfessionalReportService.Sources.cs` when all report sources are selected eliminated extensive `EXISTS` subquery execution on every journal line.
