# PERFORMANCE FIXES & ARCHITECTURAL REMEDIATIONS — PETRA ERP

## Executive Summary
This document details all performance fixes engineered across the desktop client, communication transport, Web API, caching layer, and SQL persistence layers. Every change was implemented while preserving all business rules, accounting invariants, and UI aesthetics.

---

## 1. Summary of Applied Fixes

### 1.1 UI Debouncing & Collection Thrashing Elimination
- **File**: `src/PetraERP.Desktop/Shell/DirectoryViewModels.cs`
  - **Issue**: Keystroke storms firing on every character typed in the search box; `Rows.Clear()` and 300 sequential `Rows.Add()` operations causing repeated UI re-layouts.
  - **Remediation**:
    1. Added an 80ms debounced execution loop using `CancellationTokenSource` with previous task cancellation (`latest request wins`).
    2. Implemented identity sequence check: if the newly filtered items match the existing collection items by ID, visual collection resets are skipped entirely.
- **File**: `src/PetraERP.Desktop/Sales/SalesInvoicesViewModel.cs`
  - **Issue**: `OnSearchChanged` triggering immediate `RunSearch()` network reloads on every character typed.
  - **Remediation**: Added 80ms debounce with cancellation tokens to ensure typing "INV-0005" generates 1 network call instead of 8 calls.

### 1.2 Elimination of UI Thread Synchronous Blocking
- **File**: `src/PetraERP.Desktop/Controls/LookupPicker.cs`
  - **Issue**: Background thread completion and error blocks invoked synchronous `Dispatcher.Invoke(() => ...)` calls, blocking the thread pool worker and risking deadlock during UI rendering.
  - **Remediation**: Converted to non-blocking `_ = Dispatcher.InvokeAsync(() => ...)`.

### 1.3 High-Performance Transport & Response Compression
- **File**: `src/PetraERP.Desktop/Connection/DesktopSession.cs`
  - **Issue**: Used unconfigured `HttpClientHandler` without connection pooling or HTTP response decompression.
  - **Remediation**: Replaced with `SocketsHttpHandler` configured with:
    - `PooledConnectionLifetime = TimeSpan.FromMinutes(15)`
    - `PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)`
    - `AutomaticDecompression = DecompressionMethods.All` (GZip, Deflate, Brotli)
    - `EnableMultipleHttp2Connections = true`
- **Files**: `src/PetraERP.Api/Hosting/PetraServiceRegistration.cs` & `PetraPipeline.cs`
  - **Issue**: Web API returned uncompressed JSON across LAN.
  - **Remediation**: Registered `services.AddResponseCompression(options => options.EnableForHttps = true)` and added `app.UseResponseCompression()` to the middleware pipeline before routing.

### 1.4 Elimination of SQL Server N+1 Query in Purchase Invoices
- **File**: `src/PetraERP.Infrastructure/Purchases/SqlPurchaseInvoiceService.cs`
  - **Issue**: `foreach (var supplierId in creditSupplierIds) { LoadInvoicesAsync(...) }` executed N queries in a loop.
  - **Remediation**: Batched the supplier settlement query into a single call:
    `SettlementQueries.LoadInvoicesAsync(..., partyIds: creditSupplierIds)`
    Reducing database queries from $1 + N$ to $1$.

### 1.5 Count Query Optimization
- **File**: `src/PetraERP.Infrastructure/Sales/SqlSalesInvoiceService.cs`
  - **Issue**: `CountAsync()` was executed on an `IQueryable` containing eager `.Include()` joins to 4 tables.
  - **Remediation**: Separated `baseQuery.CountAsync(...)` from the paged `.Include(...)` navigation projection, reducing SQL Server logical reads and eliminating redundant joins.

### 1.6 Reporting Engine 8-Way Union Bypass
- **File**: `src/PetraERP.Infrastructure/Reports/Professional/SqlProfessionalReportService.Sources.cs`
  - **Issue**: When all 16 report sources are checked (the default for General Ledger and Trial Balance), an 8-way UNION ALL query was evaluated via `EXISTS` on every candidate journal line.
  - **Remediation**: Added short-circuit check `includesAllSources`: when all sources are selected and unclassified is included, the redundant UNION subquery filter is bypassed in SQL, dramatically accelerating report generation.

### 1.7 Intelligent Reference Data Cache
- **Files**:
  - `src/PetraERP.Desktop/Caching/ReferenceDataCache.cs`
  - `src/PetraERP.Desktop/Patterns/PatternShortcutLoader.cs`
  - `src/PetraERP.Desktop/Connection/AccountingClients.cs`
  - `src/PetraERP.Desktop/Connection/InventoryClient.cs`
  - `src/PetraERP.Desktop/Sales/SalesInvoiceCardViewModel.cs`
- **Issue**: Master data (Chart of Accounts tree, Item Groups, Document Patterns, Units) was repeatedly re-downloaded on every window opening.
- **Remediation**:
  1. Built thread-safe `ReferenceDataCache` with tenant isolation (`CompanyId:FiscalYearId:DataType:Version`) and single-flight request coalescing.
  2. Applied caching to:
     - `ChartOfAccountsClient.GetAccountTreeAsync` (15m TTL, invalidated on account create/update/delete)
     - `InventoryClient.ListItemGroupsAsync` (15m TTL, invalidated on item group create/update/delete)
     - `InventoryClient.ListUnitsAsync` (30m TTL, invalidated on unit create)
     - `PatternShortcutLoader.LoadAsync` (15m TTL, invalidated on pattern changes)
  3. Reused `PatternShortcutLoader` in `SalesInvoiceCardViewModel.LoadLookupsAsync` instead of sequential N+1 HTTP calls.
  4. Enforced rule: Sensitive volatile balances (Cash, Bank, Balances) are NEVER stored in the cache.
