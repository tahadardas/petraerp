# PetraERP engineering contract

The current project is PetraERP, an Arabic/English commercial accounting ERP.
The owner's PetraERP specification supersedes the earlier Medical ID project
instructions. Its text is truncated after `PHASE 13 / Re` and is no longer kept
in this repository (the former `docs/requirements/` and `docs/ROADMAP.md` paths
were removed); ask the owner for it instead of guessing, and do not invent the
missing requirements. The current plan, evidence and decisions live in
`docs/closure/` (start at `00_CURRENT_STATE.md` and `RELEASE_GO_NO_GO.md`), the
gate checklist is `docs/PetraERP_EXECUTION_GATES_CHECKLIST.csv`, and the UX
direction is under `docs/UX_AUDIT/`, `docs/UX_REDESIGN/` and `docs/AlAmeen10-UI/`.

- Application stack: **Backend** C#, stable .NET 10 LTS, ASP.NET Core Web API, EF
  Core, SQL Server 2025 (unchanged). **Official UI** (owner directive, 19 Nov
  2026): React + TypeScript (strict) + Vite + Tauri 2 on Windows, in
  `frontend/petra-desktop/`; see `docs/adr/ADR-0003-wpf-to-react-tauri-desktop-migration.md`.
  **WPF (`src/PetraERP.Desktop`) is Legacy / Frozen / Pending Retirement**: no new
  WPF features, only fixes that block the legacy build; it stays in Git as the
  functional reference until parity is proven. Python is used only for
  repository evidence tooling.
- The API is a primary product interface for Android, web, WPF and authorized
  integrations. Apply the ADR-0002 rules (the ADR file itself is not in this
  snapshot): versioned OpenAPI contracts, decimal strings, server-authoritative
  accounting and central LAN deployment. Build and test each implemented slice;
  do not describe API design as implemented endpoints.
- Follow the gate order of `docs/PetraERP_EXECUTION_GATES_CHECKLIST.csv`, one
  gate at a time. Analyze legacy data before writing ERP business logic.
  Clearly label incomplete gates.
- Everything inside `legacy-data/` is read-only evidence. Never rewrite,
  delete, normalize, execute its SQL, upload it, or include it in Git/releases.
  Write analysis outputs only outside that directory.
- Preserve Arabic Unicode and distinguish SQL NULL from an empty string.
  Financial values use decimal, never binary floating point.
- Use a modular monolith with feature directories inside each layer. Domain
  depends on no persistence, HTTP, or UI code. UI never accesses SQL directly.
- Deliver user features through database, domain, application, API, generated
  typed client (`frontend/petra-desktop/src/services/api`, generated from the
  OpenAPI contract), UI, validation, and automated tests. No fake commands.
  The UI never recomputes balances, totals, numbering, posting or approvals:
  they come from the API.
- Posting is atomic, balanced, idempotent, authorized, scoped by company and
  branch, audited, and rejected for closed periods or missing required mappings.
  Posted records are immutable; corrections use linked reversals.
- Stock movements and journal lines are authoritative; balances are rebuildable.
  Negative stock defaults to blocked; any override needs permission and reason.
- Use reviewed EF migrations, foreign keys, unique/check constraints, explicit
  precision, concurrency controls, and atomic server document numbering.
- Plan for independent customer installations and separate customer databases.
  Never silently infer a customer's company scope from untrusted request data.
- Do not add proprietary components or open-source license declarations without
  verifying commercial distribution rights. No DevExpress dependency is assumed.
- End each task with changed files, verification, tables, migrations, endpoints,
  completed modules, and remaining work. Do not call scaffolding a finished ERP.

## Frontend migration (WPF to React + Tauri 2)

- This section supersedes every earlier instruction that required building or
  extending WPF screens (including "do not create duplicate screens over existing
  ones" as applied to WPF): new UI work happens only in React.
- Ownership: Claude Code owns the Backend (Domain/Application/Infrastructure/API,
  OpenAPI, security, posting, reports, integration tests). Antigravity owns the
  Frontend (React screens, design system "Petra Calm Enterprise", input engine,
  windows, shortcuts, visual and interaction tests). Each agent works on its own
  branch or Git worktree, does not edit the other's files without coordination,
  and nothing unverified is merged into `main`.
- Legacy WPF stays buildable (`PetraERP.slnx`) but is not a development target.
  Backend-only work uses `PetraERP.Backend.slnx`, which excludes WPF and its
  tests. Do not delete WPF code, shared libraries (reporting, API client,
  contracts) or Backend services because WPF used them. Deleting WPF from the
  base branch is a separate proposal after functional parity is proven.
- Business rules do not change because the UI changed. Do not touch `legacy-data/`.
  Restore point: tag `legacy/wpf-final-2026-11-19`.

## Cross-agent handoff

- Execution policy while the owner's batch is open ("Batch Implementation, then
  one Final Verification"): do not build, run tests, SQL integration or
  benchmarks after each edit; do not add new tests while implementing, record
  the needed cases in `docs/ai/HANDOFF.md` and add the regression tests in the
  final verification phase; do static reviews while working; commit in small
  reversible steps on a work branch; run ONE final campaign (Release build,
  needed tests, integration, performance); after a failure fix the cause and
  rerun only the failed group. Everything changed before that campaign is
  `UNVERIFIED`. Financial changes are not merged into the approved branch
  until the final verification passes. Outside an open batch the usual rule
  applies: build and test each slice.
- Read `docs/ai/HANDOFF.md` before continuing implementation, then inspect the
  current source and Git state. The handoff is the canonical continuation entry
  point, not a substitute for fresh evidence. `docs/closure/` holds the dated
  evidence; older reports are historical.
- Update `docs/ai/HANDOFF.md` after every task with exact commands/results,
  changed files, remaining blockers and the next bounded task. Reconcile
  `docs/closure/RELEASE_GO_NO_GO.md` and the gate checklist whenever an
  implementation claim changes. (The former `docs/FEATURE_MATRIX.md`,
  `PROJECT_PROGRESS_AR.md` and `docs/implementation/` files no longer exist;
  do not recreate them as a second source of truth.)
- Preserve failing gates and distinguish source presence, executed verification
  and usable workflows. Never claim recovery from Git without actual history,
  or completion of any phase while its Desktop/session workflows are missing.
  Do not create duplicate screens over existing ones.
