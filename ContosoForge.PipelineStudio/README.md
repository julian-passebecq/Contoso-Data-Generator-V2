# Contoso Forge Pipeline Studio

Optional Windows WPF editor targeting .NET 8. It references the existing generator and edits `PipelineDefinition` and `StudioProjectSpec` directly. The cross-platform solution and all existing command-line backends remain independent of this project.

From the repository root on Windows with the .NET SDK and .NET 8 Desktop Runtime:

```powershell
dotnet run --project ContosoForge.PipelineStudio --configuration Release -- --project examples/free-gcp-lab.project.json
```

Add `--pipeline out/free-gcp/pipeline.json` to open a generated neutral pipeline alongside its project. Open project also accepts an existing V1 business project and wraps it in the architecture envelope without changing the source business configuration.

1. Open the project, then its existing pipeline if available. The canvas renders activities and both supported dependency representations.
2. Select an activity and edit its name, kind, implementation, source/sink, engine/runtime, Spark API mode/version, dataset bindings, connection reference, parameters, retry count, or timeout. Apply activity changes commits that panel to the in-memory contract. Empty optional fields inherit project defaults.
3. Use Destination for the warehouse, BigQuery project/dataset/location/cost guard, and existing dataset path/table/connection bindings. Apply destination validates the architecture before changing it.
4. Add activities from the toolbox. New activities depend on the selected node. Edit dependency IDs to connect them; remove selected also removes its incoming/outgoing edges. Unsupported executable mappings remain explicit in the compiler plan.
5. Apply each edited panel before saving, compiling, validating, or navigating to another graph. Applying one panel preserves pending text in the others. A pending-edit diagnostic preserves the entered text; Discard pending edits explicitly restores the last applied values. Save bundle writes the neutral pipeline and sibling `project.json` in the chosen directory, and refuses to replace an unrelated companion project. The shared parser rejects malformed null structures and raw credentials even when saving an incomplete draft. Connection fields contain reference IDs only.
6. Validate invokes the existing compiler against the resolved architecture. Compile writes generated artifacts into an empty directory or an existing Forge output. Airflow, IaC, resolved project, validation/plan, and run-manifest tabs show those actual artifacts. Edits invalidate previews until compilation is repeated.

Compilation performs no deployment or runtime execution. Hosted Colab, BigQuery, and Minikube validation still require their separate execution evidence. The graph uses a simple fixed layout; advanced diagram manipulation and a connection/dataset creation wizard are outside this MVP. Existing connections, datasets, activity fields, parameters, and annotations are preserved on round-trip.

Run the deterministic Windows UI smoke without displaying a desktop window:

```powershell
dotnet build ContosoForge.PipelineStudio --configuration Release
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/free-gcp-lab.project.json --smoke-output artifacts/studio-smoke
```

Use an empty smoke output directory. Optional `--pipeline` exercises an existing generated pipeline. The smoke executes the real WPF editor actions: add/remove, edit BigQuery destination and Connect mode, preserve pending text across panel changes, block unapplied save/compile/navigation, protect unrelated companion files, reject malformed null contracts without replacing the current graph, save/reload, reject cycles and raw credential values, compile through the existing compiler, and render `pipeline-studio.png` from the WPF visual tree. `smoke-report.json` records assertions and explicitly leaves cloud execution unverified. The dedicated Windows CI workflow runs this independently of `ContosoDGV2.sln`.
