# PetraERP engineering contract

The current project is PetraERP, an Arabic/English commercial accounting ERP.
The user's attached PetraERP specification supersedes the earlier Medical ID
project instructions. See `docs/requirements/PROJECT_SPECIFICATION.txt` for the
supplied specification. Its final text is truncated after `PHASE 13 / Re`;
do not invent the missing requirements.

- Application stack: C#, stable .NET 10 LTS, ASP.NET Core, EF Core, SQL Server
  2025, WPF with MVVM. Python is used only for repository evidence tooling.
- The API is a primary product interface for Android, web, WPF and authorized
  integrations. Apply ADR-0002: versioned OpenAPI contracts, decimal strings,
  server-authoritative accounting and central LAN deployment. Build and test
  each implemented slice; do not describe API design as implemented endpoints.
- Follow `docs/ROADMAP.md` sequentially, one phase at a time. Analyze legacy
  data before writing ERP business logic. Clearly label incomplete gates.
- Everything inside `legacy-data/` is read-only evidence. Never rewrite,
  delete, normalize, execute its SQL, upload it, or include it in Git/releases.
  Write analysis outputs only outside that directory.
- Preserve Arabic Unicode and distinguish SQL NULL from an empty string.
  Financial values use decimal, never binary floating point.
- Use a modular monolith with feature directories inside each layer. Domain
  depends on no persistence, HTTP, or UI code. UI never accesses SQL directly.
- Deliver user features through database, domain, application, API, generated
  typed client, ViewModel, UI, validation, and automated tests. No fake commands.
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

## Cross-agent handoff

- During the owner's current sequential prompts 01–12 series, read
  `docs/ai/SEQUENTIAL_EXECUTION_RULES.md` with HANDOFF. The explicit owner rule
  defers all builds/tests/SQL/UI/performance execution and new test cases until
  prompt 12; prompts 01–11 use static source review only. This takes precedence
  over intermediate verification instructions above. Keep changes
  `UNVERIFIED UNTIL FINAL TESTS` and refresh `docs/implementation/API_UI_PARITY_MATRIX.md`.
- Read `docs/ai/HANDOFF.md` before continuing implementation, then inspect the
  current source and Git state. The handoff is the canonical continuation entry
  point, not a substitute for fresh evidence. Older phase reports are historical.
- Update `docs/ai/HANDOFF.md` after every task with exact commands/results,
  changed files, remaining blockers and the next bounded task. Reconcile
  `docs/FEATURE_MATRIX.md`, the active phase status and `PROJECT_PROGRESS_AR.md`
  whenever their implementation claims change.
- Preserve failing gates and distinguish source presence, executed verification
  and usable workflows. Never claim recovery from Git without actual history,
  or Phase-4 completion while Desktop/session workflows remain missing.
