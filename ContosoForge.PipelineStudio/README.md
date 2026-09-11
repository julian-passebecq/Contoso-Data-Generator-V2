# Contoso Forge Pipeline Studio V1.5

The ten product sections now follow Business → Data → Pipeline mode → Architecture → Orchestration → dbt → ML design → BI & Validation → Run → Monitor / Results. Business is the initial section. Open `examples/v15-local-ml.project.json` for the executable ML factory, or `examples/v15-local-bi.project.json` for BI without ML. Apply edits, Plan, choose a Python executable with `factory/requirements.txt` installed, then Run local into a new folder. Build Evidence records a separate real npm build; Open Evidence report serves it on loopback HTTP. Measured results are separate from historical planner evidence.

V1.5 preserves the editor workflow below. Use `--factory-smoke-output <empty-directory>` with the ML example to test the ten sections, pending data/product edits, ML export graph changes, save/load/compile and actual WPF rendering. The legacy `--smoke-output` remains available unchanged. [Full execution commands, evidence and limitations](../docs/v1.5.md).

Optional Windows WPF architecture planner and editor targeting .NET 8. It references the existing generator, uses the shared `PlanBuilder` and edits `PipelineDefinition` and `StudioProjectSpec` directly. The cross-platform solution and all existing command-line backends remain independent of this project.

From the repository root on Windows with the .NET SDK and .NET 8 Desktop Runtime:

```powershell
dotnet run --project ContosoForge.PipelineStudio --configuration Release -- --project examples/free-gcp-lab.project.json
```

Add `--pipeline out/free-gcp/pipeline.json` to open a generated neutral pipeline alongside its project. Open project also accepts an existing V1 business project and wraps it in the architecture envelope without changing the source business configuration.

1. Open the project, then its existing pipeline if available. Choose a business scenario separately from an architecture preset and cost profile, and select **Apply scenario / architecture**. `free-gcp-lab` retains classic Spark; `free-gcp-connect` explicitly selects Connect-local 4.0.4. Changing architecture preserves source-generation settings. Explicitly switching to the ML scenario applies the catalog's 1,200-order, 365-day learning profile; reselecting the same scenario preserves customized quantities.
2. Select **Plan** to call the same offline C# planner used by `forge plan`. The resolved canvas shows actual stages and edges, engine/runtime, reference evidence badges and manual checkpoints. The Architecture panel explains storage, formats, warehouse, orchestration, credentials, costs and readiness. A new plan always reports this project as not executed; stage badges describe scoped implementation history. Reference providers remain clearly labeled.
3. Use **Overrides** to edit optional architecture fields as JSON and apply them through the shared resolver. Use Destination for the warehouse, BigQuery project/dataset/location/cost guard and existing dataset path/table/connection bindings. Invalid combinations are rejected before the project is changed. An untouched default graph follows new preset/runtime choices. A graph with authoring changes is preserved and validated against the new architecture.
4. Switch to **Edit pipeline** to select and edit an activity's name, kind, implementation, source/sink, engine/runtime, Spark API mode/version, dataset bindings, connection reference, parameters, retry count or timeout. Apply activity changes commits that panel to the in-memory contract. Empty optional fields inherit project defaults. Add activities from the toolbox; new activities depend on the selected node. Edit dependency IDs to connect them; remove selected also removes its incoming/outgoing edges. Unsupported executable mappings remain explicit in the plan.
5. Apply each edited panel before planning, saving, compiling, validating or navigating to another graph. Applying one panel preserves pending text in the others. Pending edits immediately invalidate the plan and compiled previews. A diagnostic preserves entered text; **Discard pending edits** explicitly restores the last applied values. Save bundle writes the neutral pipeline and sibling `project.json`, refusing to replace an unrelated companion project. The shared parser rejects malformed null structures and raw credentials even for incomplete drafts. Connection fields contain reference IDs only.
6. **Save plan** saves the current shared contract to an explicitly chosen JSON path and refuses to overwrite the open project or pipeline. **Compile** requires a current, reviewed Plan and writes `plan/resolved_plan.json` alongside the existing generated artifacts, with the plan included in manifest hashes. An unplanned or stale revision fails before output is written. Airflow, IaC, resolved project, plan JSON and run-manifest tabs show actual outputs. Ordinary legacy CLI generation/compilation does not gain this optional plan artifact.

Compilation performs no deployment or runtime execution. Hosted Colab, BigQuery, and Minikube validation still require their separate execution evidence. The graph uses a simple fixed layout; advanced diagram manipulation and a connection/dataset creation wizard are outside this MVP. Existing connections, datasets, activity fields, parameters, and annotations are preserved on round-trip.

Run the deterministic Windows UI smoke without displaying a desktop window:

```powershell
dotnet build ContosoForge.PipelineStudio --configuration Release
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/free-gcp-lab.project.json --smoke-output artifacts/studio-smoke
```

Use an empty smoke output directory. Optional `--pipeline` exercises an existing generated pipeline. The smoke retains every V1.3 editor assertion: add/remove, BigQuery/Connect edits, pending text protection, companion-file collision protection, malformed contract rejection, save/reload, cycles, credentials and existing compiler previews. It also exercises Plan, actual CLI/core JSON parity, independent scenario/preset selection, reference and manual badges, resolved edges, stale-plan protection, atomic invalid overrides, authored graph preservation and compilation of the current plan. It renders the real hidden WPF visual tree at 1,500 and 1,150 pixel widths. `smoke-report.json` and `planner/planner-smoke-report.json` record assertions and leave cloud execution unverified. The dedicated Windows CI workflow runs this independently of `ContosoDGV2.sln`.

## Local run history (S001)

Results now has **List local runs**, **Inspect selected**, **Import folder**, and **Locate moved folder**. Select a V1.7 generated root or its `.forge/v15/<run-directory>` state. Imported results are read-only: Studio reads JSON and hashes, and does not invoke bundle scripts, rebuild reports, resume stages, or change project files. Applying another editor revision clears the selected result; its files and catalog locator remain available for reopening. An owned run captures its input revision, graph, run ID, root and validated interpreter before execution.

The per-user locator index is `%LOCALAPPDATA%/ContosoForge/runs.json` (schema version 1). It stores root, run ID, label and registration time, never authoritative outcomes. Writers use a bounded exclusive sibling lock, reread/merge, durable temporary write and atomic replacement. Duplicate IDs at different roots remain separate entries. A damaged or unsupported catalog is left untouched and folder import still works. Preserve that file for diagnosis; moving it aside explicitly allows Studio to start a new index. Locating a moved folder adds its locator and validates existing identities without repairing hashes.

History displays recorded run/build outcomes and verification diagnostics. A persisted `running` status means **last recorded running; live execution unconfirmed**. A successful report preview requires matching run and snapshot identities, root manifest/source hashes, report contract and input hashes, the recorded artifact location, and its index hash. Linked paths are rejected, including links in otherwise unhashed preview assets. The build receipt binds `index.html`; other built assets are not hash-verified. Local receipts are unsigned and do not protect against coordinated rewriting of all evidence.

Owned V1.5/V1.6 previews retain their version-specific snapshot contract. History import supports V1.7 only. Closing while a run/build or preview startup is active is blocked; switching results or closing an active preview stops the owned loopback server. Execution cancellation remains unavailable.

Run the actual asynchronous desktop tests with fresh outputs:

```powershell
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-kpi-duckdb.project.json --execution-smoke-output out/s001-kpi
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-specific-ml.project.json --execution-smoke-output out/s001-ml
$run = Get-Content out/s001-kpi/execution-smoke.json -Raw | ConvertFrom-Json
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --reopen-smoke-state $run.firstState --history-smoke-output artifacts/s001-restart
dotnet test DatabaseGenerator.Tests --configuration Release --filter FullyQualifiedName~StudioRunTests
```

Smoke entrypoints isolate interpreter preferences and catalog writes below their output directory. They exercise the shared button APIs, real WPF rendering, subprocesses and loopback HTTP; native folder-picker and default-browser interaction remain separate manual checks. The Windows workflow runs Bronze with only DuckDB/PyArrow, plus full KPI and ML desktop/report/restart jobs. Optional V1.7 package versions are recorded as `unavailable` when metadata is absent; missing required imports or selected-engine metadata fail preflight before generation. Report packaging also requires pandas through the shared CSV helper.

History validates each selected locator before changing selection or stopping its preview. Invalid IDs or rejected paths produce a status diagnostic and preserve the previous selection; catalog and evidence bytes are not repaired automatically. Corrupt catalog JSON remains recoverable through explicit folder import.

Runtime requirements combine compiled operations with the Silver engine and analysis kind/runtime independently. Included local Spark ML requires PySpark even with DuckDB Silver; Colab export or a stop before analysis does not add that requirement. Shared export imports and mandatory engine metadata remain checked.

The Windows KPI gate also runs `--reopen-smoke-state <built-state> --repair-smoke-output <fresh-output>` to exercise actual invalid-locator handlers, preview preservation, corrupt-catalog recovery, real missing-PySpark preflight, pending edits, export/early-stop controls and a recovered Bronze run. This negative test requires an interpreter without PySpark and isolates all preferences/catalog changes.
