# V1.4.0 planning implementation evidence

The audited baseline is `407c1d250addb7b3f0cc3f9ce21f5ae676c2132d`.
This records the local planning audit before publication as V1.4.0 in
`ec1ea2d6bb2e408480277ef11eef46333de00939`. The baseline GitHub results below
are historical; see [the V1.4.0 release-fix report](v1.4-release-fix.md) for the
subsequent compatibility diagnosis and focused validation.

## Baseline GitHub checks

The public repository's `main` branch matched the baseline when inspected.
All three workflows completed successfully:

| Workflow | Actual baseline run |
| --- | --- |
| Core tests, deterministic generation and Compose checks | [validate](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33929693982) |
| Windows editor build, round trip and rendering | [pipeline-studio-windows](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33929694066) |
| Contracts, classic/Connect Spark execution, dbt parse, infrastructure validation and DAG parse | [free-gcp-contracts](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33929693984) |

Local baseline Python checks passed all 69 tests, including the three optional
real-Parquet/native Google-client configuration tests. Those tests translate SQL
against local Silver and use fake warehouse transport; they are not cloud jobs.
The local logs are under `artifacts/v131-plan/baseline-python/`.

## Local runtime and toolchain rechecks

Fresh isolated runs reused the baseline's deterministic generated source package.
Their evidence is under `artifacts/v131-plan/runtime/`; no prior issued work
directory was regenerated or overwritten. The final ordinary `forge generate`
output was then compared with every baseline hash: **all 152 files were
byte-for-byte identical**. `artifacts/v131-plan/default-compatibility.json` records
that comparison, binding the runtime inputs to the final code's unchanged
default output.

| Check | Actual result | Evidence |
| --- | --- | --- |
| Classic PySpark 4.0.4 | Executed; requested/actual `classic`, `isRemote=false`; Silver reconciled | `spark-classic-summary.json`, `spark-classic/` |
| True local Spark Connect 4.0.4 | Executed; requested/actual `connect-local`, `isRemote=true`; Silver reconciled | `spark-connect-local-summary.json`, `spark-connect-local/` |
| dbt-bigquery 1.10.3 / dbt-core 1.10.15 | Parsed 24 models and 121 tests; no cloud build | `dbt-parse-report.json`, `dbt-parse.log` |
| OpenTofu 1.12.6 | Formatting, isolated backend-free initialization and validation passed | `infrastructure.json` |
| Terraform 1.13.4 | Formatting, independent backend-free initialization and validation passed | `infrastructure.json` |
| Helm 3.19.0 / Airflow chart 1.22.0 | Lint and rendering passed, including Airflow 3 and GitSync | `infrastructure.json` |
| Airflow 3.2.2 DagBag | Parsed the exact generated DAG and all four tasks | `airflow-parse-report.json`, `airflow-parse.log` |

The Spark runs used WSL Ubuntu with Java 17 and native PySpark 4.0.4. Their
runtime evidence explicitly identifies local execution; hosted Colab and
BigQuery remain separate historical evidence. The Airflow parser ran in a
separate Python process in the existing scheduler, with isolated temporary
copies of the generated DAG and helper. It verified their exact hashes and
removed its scratch directory. No DAG-directory, state-PVC, deployment or
database mutation was requested.

The post-change Python run passed all 73 tests with no skips: the existing 69
plus four live-state guard tests. Its optional Parquet checks used Silver from
the fresh classic run. An intermediate schema JSON syntax error was caught by
the existing schema-reference test, fixed, and that failed suite rerun; both
the initial failure and successful result remain under
`artifacts/v131-plan/final-python/`.

The final CLI also passed scenario/preset listing, deterministic repeated Connect
planning, the 1,200-order/365-day ML selection, the new compile alias, and
`local-fast` planning with `git:null`. Four emitted plans passed the resolved-plan
JSON schema. HTTPS repository strings containing control characters or Helm
template expressions were rejected before writing plan output. The explicit
Connect preset generated and statically validated 21 JSON files, 15 Python files
and one notebook. Exact CLI logs and contracts are in
`artifacts/v131-plan/cli-checks/`.

## Live state cleanup

The baseline tracked `.forge-live/free-gcp-live.project.json`. It was removed
from the Git index with `git rm --cached`; its local bytes were preserved and
the before/after SHA-256 values match. `.forge-live/` and `.forge-runtime/` are
now ignored. The cleanup report is
`artifacts/v131-plan/live-state-cleanup.json`.

`scripts/check_tracked_runtime_state.py` examines tracked **paths only** using
the Git index. It rejects known live output, runtime-state, authentication and
issued Colab artifact paths without opening their contents. Authored examples,
schemas, notebooks and lake sentinels remain allowed. CI invokes the guard and
its isolated-index regression tests. This guard does not claim to scan arbitrary
file contents for secrets or erase previously published history.

## Public GitHub to GitSync remains pending

The existing `contoso-forge-v13` Minikube profile was Running, and the four
Airflow/PostgreSQL pods were Ready. Airflow release revision 3 uses chart 1.22.0
and Airflow 3.2.2. Scheduler and DAG processor GitSync containers read the local
read-only repository's `hosted-full` branch. The successful V1.3 issued work
orders, hosted result adoption and run histories remain intact.

The public repository had only `main`, with no complete generated source package
or neutral compiled `contoso_forge_pipeline.py`. Its authored reference DAG is
not that generated payload. Repointing the existing release to public `main`
would fail the configured generated project path and would not prove a matching
work-order identity.

The full public GitHub → GitSync → exact generated DAG/work order → manual result
adoption → reconciliation gate is therefore **pending publication of a reviewed
credential-free generated fixture**. No fixture was published and no cluster
configuration was changed in this pass. A future execution should use a separate
namespace/release or cluster and a fresh run ID, preserving the existing release.

The successful earlier local-GitSync cycle and hosted BigQuery jobs remain
documented in [V1.3 Minikube evidence](v1.3-minikube-live.md). They are not promoted
to public-GitHub evidence. No new BigQuery load, cloud infrastructure apply or
BigQuery ML training is implied by the V1.4.0 offline planner checks.
