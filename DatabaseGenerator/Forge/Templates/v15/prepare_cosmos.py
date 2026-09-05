"""Generate the optional Cosmos parse manifest explicitly, before deploying the DAG."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    root = parser.parse_args().root.resolve()
    project = root / "factory/dbt"
    dbt = Path(sys.executable).parent / ("dbt.exe" if os.name == "nt" else "dbt")
    subprocess.run([str(dbt), "parse", "--project-dir", str(project), "--profiles-dir", str(project), "--no-partial-parse"], check=True)
    shutil.copyfile(project / "target/manifest.json", root / "factory/dbt_manifest.json")
    launcher = root / ".forge/cosmos/dbt"
    launcher.parent.mkdir(parents=True, exist_ok=True)
    launcher.write_text(f"#!{sys.executable}\nimport sys\nsys.path.insert(0, {str(root / 'factory')!r})\nfrom orchestration import dbt_guard\nsys.exit(dbt_guard(sys.argv[1:]))\n", encoding="utf-8")
    launcher.chmod(0o755)
    print("Parsed dbt manifest for Cosmos. No dbt model, Airflow DAG or cloud job has executed.")
