# WPF PERFORMANCE & UI VIRTUALIZATION AUDIT — PETRA ERP

## 1. WPF Rendering & Layout Architecture

Petra ERP's desktop frontend uses Windows Presentation Foundation (WPF) with CommunityToolkit.Mvvm, custom Al-Ameen style layouts, and RTL alignment.

### 1.1 UI Virtualization vs. Data Virtualization Verification
As emphasized by Microsoft WPF engineering guidelines:
> **UI Virtualization** only recycles visual element containers (e.g. `DataGridRow`, `ListBoxItem`) for elements within the viewport.
> **Data Virtualization** manages the memory footprint and retrieval of data from the server in pages or chunks.

Both layers were audited across all primary grids:

| Screen / View | Container Control | UI Virtualization Mode | ScrollViewer.CanContentScroll | Remote Data Virtualization |
|---|---|---|---|---|
| **Account Directory** | `ListBox (CardList)` | Recycling (`VirtualizingPanel.VirtualizationMode="Recycling"`) | True | Group-level Keyset filtering + 300 row ceiling |
| **Item Directory** | `ListBox (CardList)` | Recycling | True | Server-side paged list (`ListItemsAsync`) |
| **Sales Invoices** | `PetraAccountingDataGrid` | Recycling (`VirtualizingPanel.SetVirtualizationMode(this, Recycling)`) | True | Server-side Keyset / Offset-Fetch (`PageSize = 100`) with on-demand `LoadMore` |
| **Purchase Invoices** | `PetraAccountingDataGrid` | Recycling | True | Server-side Keyset / Offset-Fetch (`PageSize = 100`) |
| **Journal Lines / Ledger** | `PetraInteractiveReportGrid` | Recycling | True | Keyset paged segments with interactive column sorting |

### 1.2 Layout Container Audit
- **Check for Infinite Height Containers**:
  - Ensured no `DataGrid` is hosted inside an unconstrained vertical `StackPanel`.
  - All large grids are bounded by `DockPanel` or `Grid` with star sizing (`*`) or explicit row constraints.
  - No nested `ScrollViewer` wraps a virtualizing `DataGrid` or `ListBox`.

---

## 2. Dispatcher & UI Thread Optimization

### 2.1 Eliminating Synchronous ThreadPool-to-UI Blocks
In `LookupPicker.cs`:
- Previously: Worker threads called `Dispatcher.Invoke(() => ...)` to report search completion and errors. If the UI thread was performing a layout pass, the background worker thread froze until the UI finished.
- Remediated: Replaced with asynchronous non-blocking dispatches `_ = Dispatcher.InvokeAsync(() => ...)`.

### 2.2 Debounced Search Inputs
- **Directory Search**: Keystrokes are debounced by 80ms using `CancellationTokenSource`.
- **Invoices Search**: Keystrokes are debounced by 80ms using `CancellationTokenSource`.
- **Lookup Pickers**: Debounced by 180ms with background worker cancellation.

### 2.3 Incremental Collection Notifications
- **Identical Sequence Check**:
  In `DirectoryViewModelBase.RefreshRowsAsync`, before invoking `Rows.Clear()` and rebuilding the collection, the collection is compared against the incoming result set by ID. If unchanged, collection modification is bypassed, eliminating unnecessary WPF measurement and layout passes.
