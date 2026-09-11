# Test strategy and execution contract

**Current S001 follow-up:** use the additional cases and rerun scope in [repair 01](sprints/S001-repair-01.md). The [lead review](reviews/S001-lead.md) resolves native/remote/provider coverage for local acceptance. The original matrix below remains the general contract; do not repeat its expensive unchanged-runtime gates solely for the specified C# repair.

The developer writes and runs focused regressions while implementing; light QA independently runs the final acceptance matrix and maintains evidence. The lead audits logic and test adequacy. Use tests that establish observable behavior and invariants, including failures; do not merely assert source strings or mirror the implementation under test.

## S001 acceptance matrix

| Test ID / acceptance | Scenario and independent oracle | Required level |
| --- | --- | --- |
| T01 / AC01 | Malformed editor JSON and valid-but-unsupported ML leave project/graph/pending text unchanged; valid edits save/reload; authored graph retained | Existing WPF journey smoke plus focused additions if touched |
| T02 / AC02 | Valid built receipt accepted; alter index bytes, run ID, contract hash, immutable report input or path one at a time; expect rejection before server launch | Receipt-service tests; actual WPF preview negative test. Use independent SHA-256 computations |
| T03 / AC03 | Complete run A; change applied stop stage or generation settings; inspect A and run B. Assert explicit historical/current labeling and no stale build action | WPF state/interaction regression |
| T04 / AC04 | Required engine/import missing, nonexistent interpreter and paths with spaces; expect preflight rejection before output. Simulate absent unused metadata on a real bounded V1.7 stage; expect documented nonfailure | Focused tests + small real runtime; independent dependency consistency check |
| T05 / AC05 | Child emits early stdout and >100 KB stderr; output visible before exit and complete logs retained. Block concurrent run/build/switch; preview startup failure/close and active-preview close release owned resources | Owned-process and WPF asynchronous regression with bounded timeouts |
| T06 / AC06 | Catalog save/load in a new instance; missing/corrupt/truncated JSON; duplicate entry; two writers/interleaving; write interruption preserves last valid index or explicit recoverable state | Service tests using fresh temporary user-data directory, no real preference corruption |
| T07 / AC07 | Import V1.7 success, failure, export-only, unknown version, malformed evidence, missing/moved path, stale running state, wrong root/run ID, path traversal and conflicting duplicate ID | Service + WPF integration. Hash all fixture evidence before/after and assert unchanged; sentinel script must never execute |
| T08 / AC08 | Fresh KPI and ML-only run/build/preview/restart/reopen; data and labels agree with stored reconciliation/metrics. Bronze produces no report action | Real generated runs and strict reports; WPF async path plus visual/HTTP inspection |
| T09 / AC01/09 | .NET contracts, deterministic/legacy compatibility; original WPF variants and new history/runtime suite | Full .NET once after integration; affected WPF gates |
| T10 / AC09/10 | Inspect workflow triggers, commands, timeouts and uploaded diagnostics; verify final CI revision when available; review diff/track-state hygiene | QA source/workflow review and exact-revision evidence |

If a test case is not yet implemented, record NOT_RUN and the missing test; a planned test is never a pass. A successful mocked report-build failure test does not prove a real report builds. A loopback HTTP check or folder-selection seam does not prove native picker/default-browser interaction.

## Commands verified against this repository

Run from the repository root. On this host use profile-free PowerShell (`login:false` for command tools); its normal profile currently emits unrelated Conda import errors. Verify required tools without reconfiguring global environments. Use a fresh short output parent each QA run; `out/qa-s001a` below is an example to change if it already exists.

```powershell
dotnet test ContosoDGV2.sln --configuration Release --logger 'trx;LogFileName=s001.trx' --results-directory artifacts/s001-qa/dotnet
dotnet build ContosoForge.PipelineStudio --configuration Release
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-kpi-duckdb.project.json --journey-smoke-output artifacts/s001-qa/journey
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/free-gcp-lab.project.json --smoke-output artifacts/s001-qa/legacy
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v15-local-ml.project.json --factory-smoke-output artifacts/s001-qa/factory
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v16-local-polars.project.json --factory-smoke-output artifacts/s001-qa/polars
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v16-local-pandas.project.json --factory-smoke-output artifacts/s001-qa/pandas
```

Existing real desktop execution entrypoints (validate the interpreter first; these can be expensive and build npm packages):

```powershell
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-kpi-duckdb.project.json --execution-smoke-output out/qa-s001a
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-specific-ml.project.json --execution-smoke-output out/qa-s001b
```

Read `firstState` in each `execution-smoke.json` to locate the actual fresh state and its generated root. Run report-template tests with `--root` pointing to that **generated root**, not the `.forge/v15/<runId>` directory. The developer must add documented entrypoints for new S001 history/receipt tests; no such command exists at bootstrap.

Independent small report fixture generation, when a real execution is unnecessary:

```powershell
dotnet run --project DatabaseGenerator --configuration Release --no-build -- forge generate --project examples/v17-kpi-duckdb.project.json --output out/qa-s001-report
& .tools/v15/Scripts/python.exe scripts/test_studio_report.py --root out/qa-s001-report -v
```

S001 adds `dotnet test DatabaseGenerator.Tests --configuration Release --filter FullyQualifiedName~StudioRunTests` for receipt/path/catalog tests, and `python scripts/test_studio_dependencies.py --root <fresh-generated-root> -v` for real Verify/Bronze execution with absent optional metadata. Run that Python test in a minimal environment containing only the pinned DuckDB and PyArrow distributions as well as the ordinary runtime. Its new run IDs preserve existing evidence.

For a separate Studio process reopening a completed report, read `firstState` from the KPI/ML desktop smoke, then run `dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --reopen-smoke-state <firstState> --history-smoke-output <fresh-output>`. This tests read-only reopening, real preview HTTP, startup-close blocking, injected startup failure cleanup and active-close cleanup. It writes `history-smoke.json` and a WPF render. Smoke preferences/catalogs are isolated under the smoke output. Review native pickers/default-browser behavior separately.

`.tools/v15/Scripts/python.exe` is an existing candidate on this host, not a portable requirement. Verify it exists and imports the required modules; elsewhere select a working interpreter and record its path/version. The application preflight controls can validate/save a candidate. Keep test-specific preference changes isolated or restore previous user settings. Never install packages globally or overwrite a user's interpreter choice just to make a test green.

## Broaden tests according to changed behavior

| Changed area | Additional required checks |
| --- | --- |
| V1.7 stage dispatch, dependencies, status or identity (expected in S001 A) | Fresh V1.7 journey matrix with stop boundaries, all three local engines and `test_v17.py`; its receipt/parity checks. Missing distribution cases need a negative fixture, not just a fully installed environment |
| Shared Silver/layers/encoding or generated engine code | V1.6 DuckDB/Polars/pandas 13-table parity; actual Spark gate if Spark/shared representation affected |
| Shared report/build helper or V1.5/V1.6 overlay impact | Historical artifact compatibility plus affected V1.5/V1.6 actual report builds; preserve expected fixture hashes |
| dbt/Cosmos orchestration, adoption or invocation identity | `test_orchestration.py` and actual DagRuns with single-build/callback custody, distinct invocation/run IDs |
| ML features/partitions/selection/threshold logic | Temporal leakage/embargo/validation-only selection and metric artifact checks; actual affected estimator/framework execution |
| External adapter or provider-auth logic | Offline contract/negative tests first; explicit account-backed execution if required for the claim. Export tests alone never certify a provider |

Existing full V1.7 runner: `scripts/run_v17.py --output <fresh-root> --automl-python <validated-automl-python> --build-evidence`, followed by `scripts/test_v17.py --gate <fresh-root> -v`. Add `--spark` only with the supported prepared runtime or when the required CI gate supplies it. Consult `.github/workflows/journeys-v17.yml` for current pinned setup and full invocation. Do not invent environment-specific executables. No `pytest` conversion is required; current Python suites primarily use `unittest`.

For S001, runtime dependency/status changes make the local V1.7 matrix mandatory. Full AutoML/Spark/orchestration can use exact-revision CI when that gate is available; otherwise record the missing required coverage and call the lead instead of claiming completion. Pure management-document changes do not require rerunning product suites.

## Evidence and stopping rules

Each run records date, role, branch, full HEAD SHA, dirty status, hash manifest of all changed tracked/untracked source, test ID, exact command, interpreter/tool versions where relevant, exit code, passed/failed/skipped counts, duration and artifact path. Verify the source snapshot did not change during QA. Store concise summaries in TEST-REGISTER and the QA packet; raw logs/screenshots/TRX belong in ignored `artifacts/`, generated bundles in ignored `out/`.

Use unique output paths; do not delete evidence or reuse old generated templates as if they came from the current source. Include full command output in a log and preserve the child exit code when piping through PowerShell. Redact actual credentials before a summary is tracked. Fixture hashes and run IDs establish provenance, not a substitute for correctness checks.

During development, run focused tests. After integration, run the required matrix once. After a fix, rerun affected tests and any integration gates whose result could change. Broaden only for a relevant change, failure, release requirement or unresolved concern. A test blocked by unavailable infrastructure is BLOCKED with reason, never PASSED or silently SKIPPED.

Clean-checkout release capture (`capture_v17_evidence.py` and version-specific capture scripts) has stronger requirements than local dirty-worktree QA. Do not bypass its cleanliness checks. A local sprint can reach lead review with a transparent dirty diff, but it cannot inherit a clean release ledger or a different commit's CI success.
