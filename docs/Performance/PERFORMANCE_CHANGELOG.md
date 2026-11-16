# PERFORMANCE CHANGELOG — PETRA ERP

## Comprehensive List of Modified & Added Files

This document tracks all files modified or added as part of the enterprise performance engineering program.

---

### 1. Added Files

1. **`src/PetraERP.Desktop/Caching/ReferenceDataCache.cs`**
   - **Rationale**: Implements thread-safe, tenant-isolated in-memory cache with single-flight request coalescing, TTL-based eviction, and category-level cache invalidation for static master data (Chart of Accounts tree, Item Groups, Document Patterns, Units). Volatile balances are strictly excluded.

2. **`tests/PetraERP.Desktop.Tests/PetraPerformanceBenchmarkTests.cs`**
   - **Rationale**: Provides repeatable, high-precision performance benchmarking for the 6 core scenarios (Chart of Accounts, Item Directory, Invoice List, Invoice Entry, Save & Post, and Ledger) under realistic 1,000-record dataset sizes.

3. **`docs/Performance/PERFORMANCE_BASELINE.md`**
   - **Rationale**: Pre-modification diagnostic baseline, profiling matrix, root causes, and latency measurements.

4. **`docs/Performance/PERFORMANCE_BOTTLENECKS.md`**
   - **Rationale**: Deep architectural breakdown of bottlenecks across UI, HTTP, API, EF Core, and SQL Server.

5. **`docs/Performance/PERFORMANCE_FIXES.md`**
   - **Rationale**: Engineering explanations of all applied fixes and architectural remediations.

6. **`docs/Performance/API_SQL_OPTIMIZATION.md`**
   - **Rationale**: Database queries, EF Core projections, N+1 query elimination, and network transport compression.

7. **`docs/Performance/WPF_PERFORMANCE_AUDIT.md`**
   - **Rationale**: WPF UI virtualization, container layouts, dispatcher threading, and debounced input handling.

8. **`docs/Performance/PERFORMANCE_BENCHMARKS.md`**
   - **Rationale**: Before and after latency measurements, improvement percentages, and acceptance evaluation.

9. **`docs/Performance/PERFORMANCE_REGRESSION_TESTS.md`**
   - **Rationale**: Full regression test suite results and financial correctness verification.

10. **`docs/Performance/PERFORMANCE_CHANGELOG.md`**
    - **Rationale**: Detailed tracking of all modified components.

---

### 2. Modified Files

1. **`src/PetraERP.Desktop/Shell/DirectoryViewModels.cs`**
   - **Changes**: Added 80ms debounced search using `CancellationTokenSource`; added identity sequence equality check in `RefreshRowsAsync` to avoid unnecessary collection resets.

2. **`src/PetraERP.Desktop/Sales/SalesInvoicesViewModel.cs`**
   - **Changes**: Added 80ms debounced search to `OnSearchChanged` to eliminate network request flooding during typing.

3. **`src/PetraERP.Desktop/Controls/LookupPicker.cs`**
   - **Changes**: Replaced blocking synchronous `Dispatcher.Invoke` calls in completion/error blocks with non-blocking `_ = Dispatcher.InvokeAsync`.

4. **`src/PetraERP.Desktop/Connection/DesktopSession.cs`**
   - **Changes**: Configured high-performance `SocketsHttpHandler` with `PooledConnectionLifetime = 15m`, `PooledConnectionIdleTimeout = 2m`, `AutomaticDecompression = DecompressionMethods.All`, and `EnableMultipleHttp2Connections = true`.

5. **`src/PetraERP.Api/Hosting/PetraServiceRegistration.cs`**
   - **Changes**: Added `services.AddResponseCompression()` for HTTPS JSON responses.

6. **`src/PetraERP.Api/Hosting/PetraPipeline.cs`**
   - **Changes**: Added `app.UseResponseCompression()` to middleware pipeline prior to routing.

7. **`src/PetraERP.Infrastructure/Purchases/SqlPurchaseInvoiceService.cs`**
   - **Changes**: Eliminated N+1 query loop over distinct credit suppliers by passing `partyIds: creditSupplierIds` to `SettlementQueries.LoadInvoicesAsync` in a single query.

8. **`src/PetraERP.Infrastructure/Sales/SqlSalesInvoiceService.cs`**
   - **Changes**: Separated unjoined `baseQuery.CountAsync()` from paged navigation `.Include()` projections, removing redundant table joins from COUNT queries.

9. **`src/PetraERP.Infrastructure/Reports/Professional/SqlProfessionalReportService.Sources.cs`**
   - **Changes**: Added short-circuit check `includesAllSources` to bypass the expensive 8-way UNION ALL subquery in SQL when all report sources are selected.

10. **`src/PetraERP.Desktop/Patterns/PatternShortcutLoader.cs`**
    - **Changes**: Integrated `ReferenceDataCache` to cache runnable pattern configurations with tenant isolation, preventing repeated N+1 HTTP calls.

11. **`src/PetraERP.Desktop/Sales/SalesInvoiceCardViewModel.cs`**
    - **Changes**: Reused `PatternShortcutLoader.LoadAsync` in `LoadLookupsAsync`, eliminating sequential loop of `GetPatternAsync` round trips.

12. **`src/PetraERP.Desktop/Connection/AccountingClients.cs`**
    - **Changes**: Cached `GetAccountTreeAsync` via `ReferenceDataCache` with automatic invalidation upon account creation, update, activation, or deletion.

13. **`src/PetraERP.Desktop/Connection/InventoryClient.cs`**
    - **Changes**: Cached `ListItemGroupsAsync` and `ListUnitsAsync` via `ReferenceDataCache` with automatic invalidation upon group/unit mutations.
