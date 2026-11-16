# PERFORMANCE BOTTLENECKS & ROOT CAUSE ANALYSIS — PETRA ERP

## 1. Overview of Critical Bottlenecks

Through static code analysis, execution tracing, and profiling of Petra ERP, seven major categories of performance bottlenecks were identified across the stack:

```
[UI Layer: Un-debounced Keystroke Storms & ObservableCollection Resets]
                                  │
                                  ▼
      [HTTP Layer: Missing SocketsHttpHandler Pooling & No Compression]
                                  │
                                  ▼
[API Layer: Repeated Lookup Round-Trips & Missing Reference Caching]
                                  │
                                  ▼
    [Database Layer: N+1 Settlement Queries & Bloated Count Joins]
                                  │
                                  ▼
  [Reporting Engine: Full-Chart Scans & 8-Way Union Source Filtering]
```

---

## 2. Detailed Bottleneck Analysis by Layer

### 2.1 WPF UI Layer Bottlenecks

#### Bottleneck 1: Un-debounced Search Keystrokes in Directories and Views
- **Locations**:
  - `src/PetraERP.Desktop/Shell/DirectoryViewModels.cs:93` (`OnSearchTextChanged`)
  - `src/PetraERP.Desktop/Sales/SalesInvoicesViewModel.cs:320` (`OnSearchChanged`)
  - `src/PetraERP.Desktop/Inventory/ItemsViewModel.cs:161` (`OnSearchChanged`)
  - `src/PetraERP.Desktop/Parties/PartiesViewModel.cs:117` (`OnSearchChanged`)
- **Root Cause**:
  Whenever a user types in a search box (e.g. typing "101-Dell"), an event fires on every single keystroke. Without debouncing, the ViewModel immediately initiates an asynchronous operation or HTTP call. In `ItemDirectoryViewModel`, typing 10 characters fires 10 consecutive network requests to `client.ListItemsAsync(...)`, saturating network buffers and triggering multiple database queries.
- **Impact**: UI lag, high CPU on both client and API server, out-of-order response hazard.

#### Bottleneck 2: Excessive ObservableCollection Resets & Layout Thrashing
- **Locations**:
  - `src/PetraERP.Desktop/Shell/DirectoryViewModels.cs:86-88`
- **Root Cause**:
  ```csharp
  Rows.Clear();
  foreach (var row in rows.Take(RowLimit)) Rows.Add(row);
  ```
  Calling `Rows.Clear()` fires `NotifyCollectionChangedAction.Reset`. Then adding up to 300 rows individually fires 300 separate `NotifyCollectionChangedAction.Add` events on the UI dispatcher. The WPF `ListBox` or `DataGrid` recalculates visual layout 300 times in rapid succession.
- **Impact**: Noticeable stuttering when opening directories or typing search terms.

---

### 2.2 HTTP & API Communication Layer Bottlenecks

#### Bottleneck 3: Unpooled Transport & Disabled Decompression
- **Location**: `src/PetraERP.Desktop/Connection/DesktopSession.cs:23`
- **Root Cause**:
  `DesktopSession` initializes `new HttpClient(new HttpClientHandler())`. It omits `SocketsHttpHandler` with `PooledConnectionLifetime` and `PooledConnectionIdleTimeout`. Furthermore, `AutomaticDecompression` is left at `DecompressionMethods.None`.
- **Impact**: Over local networks (LAN), large JSON payloads (such as 1,000 journal lines or 500 items) are transferred uncompressed in full ASCII/UTF-8 JSON, wasting network bandwidth and increasing serialization/transfer latency.

#### Bottleneck 4: Lack of Reference Data Caching & Sequential Lookup Requests
- **Locations**:
  - `src/PetraERP.Desktop/Sales/SalesInvoiceCardViewModel.cs:1031-1090`
  - `src/PetraERP.Desktop/Purchases/PurchasesViewModel.cs:396-435`
- **Root Cause**:
  Every time an invoice card or purchase document is opened, `LoadLookupsAsync` executes 8 to 12 HTTP requests from scratch. Furthermore, `runnablePatterns.Select(async p => await patternsClient.GetPatternAsync(...))` queries pattern configurations in a loop over HTTP (N+1 network requests).
- **Impact**: Opening a simple invoice card incurs 100-300ms of unnecessary network round trips even though patterns, warehouses, currencies, and units rarely change.

---

### 2.3 SQL Server & EF Core Layer Bottlenecks

#### Bottleneck 5: N+1 Settlement Queries in Purchase Invoicing
- **Location**: `src/PetraERP.Infrastructure/Purchases/SqlPurchaseInvoiceService.cs:41-49`
- **Root Cause**:
  ```csharp
  var creditSupplierIds = rawItems.Where(...).Select(i => i.SupplierId).Distinct().ToList();
  foreach (var supplierId in creditSupplierIds)
  {
      var supplierInvoices = await PetraERP.Infrastructure.Treasury.SettlementQueries.LoadInvoicesAsync(
          context, companyId, branchId, VoucherKind.Payment, supplierId, null, null, cancellationToken);
      // ...
  }
  ```
  For every distinct supplier on a purchase invoice page, a separate query is executed against SQL Server.
- **Impact**: If a page has 50 distinct suppliers, 51 SQL queries are executed instead of 1 batched query.

#### Bottleneck 6: Eager Navigation Includes in Count Query
- **Location**: `src/PetraERP.Infrastructure/Sales/SqlSalesInvoiceService.cs:232-242`
- **Root Cause**:
  `dbQuery` includes `PatternRevision`, `Customer`, and `Warehouse` before executing `await dbQuery.CountAsync(...)`. In EF Core, this joins 4 tables just to compute `COUNT(*)`.
- **Impact**: Unnecessary table joins and increased logical reads for list count queries.

---

### 2.4 Reporting Engine Bottlenecks

#### Bottleneck 7: Redundant 8-Way UNION All Evaluation in Report Filters
- **Location**: `src/PetraERP.Infrastructure/Reports/Professional/SqlProfessionalReportService.Sources.cs:36, 67`
- **Root Cause**:
  `Sources(scope)` builds an 8-way UNION ALL query across sales, purchases, vouchers, inventory, and settlements. When all 16 report sources are checked (which is the default when running Ledger or Trial Balance), `FilterLines` still appends:
  `query = query.Where(l => links.Any(s => s.JournalId == l.JournalId && sources.Contains(s.Source)) || ...);`
  This forces SQL Server to evaluate an `EXISTS` subquery containing 8 unions for every single journal line candidate.
- **Impact**: Substantial query latency on large journals when all document sources are included.
