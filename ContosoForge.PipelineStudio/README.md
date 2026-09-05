# Contoso Forge Pipeline Studio V1.4.0

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
