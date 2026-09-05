"""Release gate driver: invoke the existing compiler/neutral runner for isolated engines."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--build-evidence", action="store_true")
    parser.add_argument("--revision", default=None)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists(): raise ValueError("Gate output must be fresh: " + str(output))
    output.mkdir(parents=True)
    revision = args.revision or subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip())
    (output / "checkout.json").write_text(json.dumps({"headCommit": revision, "workingTreeDirty": dirty}, indent=2) + "\n", encoding="utf-8")
    runs = []
    env = {**os.environ, "OMP_NUM_THREADS": "2", "OPENBLAS_NUM_THREADS": "2", "POLARS_MAX_THREADS": "4", "PYTHONUTF8": "1"}

    def execute(command, log):
        print("Executing:", " ".join(map(str, command)), flush=True)
        with (output / log).open("w", encoding="utf-8") as stream:
            subprocess.run(list(map(str, command)), env=env, stdout=stream, stderr=subprocess.STDOUT, check=True)

    for engine in ("duckdb", "polars", "pandas"):
        root = output / engine
        execute(["dotnet", "run", "--project", "DatabaseGenerator", "--configuration", "Release", "--no-build", "--",
                 "forge", "generate", "--project", f"examples/v16-local-{engine}.project.json", "--output", root], f"{engine}-generate.log")
        execute([sys.executable, "scripts/validate_studio_artifacts.py", "--project", root], f"{engine}-schema.log")
        execute([sys.executable, root / "pipeline/run_local.py", "--root", root, "--run-id", "v16"], f"{engine}-pipeline.log")
        state = root / ".forge/v15/v16"
        execute([sys.executable, "scripts/test_v15.py", "--root", root, "--state", state, "-v"], f"{engine}-v15-tests.log")
        if args.build_evidence:
            execute([sys.executable, root / "factory/build_evidence.py", "--state", state], f"{engine}-evidence.log")
        runs.extend(["--run", engine, root, state])
    execute([sys.executable, output / "duckdb/factory/parity.py", *runs, "--revision", revision,
             "--output", output / "engine_parity.json"], "parity.log")
    print(json.dumps({"parity": str(output / "engine_parity.json"), "status": "matched", "revision": revision}), flush=True)


if __name__ == "__main__": main()
