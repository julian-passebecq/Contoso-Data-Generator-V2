"""Generate an orchestration fixture through the existing C# compiler."""
import argparse
import json
from pathlib import Path
import subprocess

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--downstream", required=True, choices=("ml", "bi", "export"))
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.output.exists(): raise ValueError("Use a fresh output directory")
    project = json.loads(Path("examples/v16-local-cosmos.project.json").read_text(encoding="utf-8-sig"))
    if args.downstream == "bi": project["businessScenario"] = "retail.customer_satisfaction"
    if args.downstream == "export": project["product"]["mlTarget"] = "colab-sklearn"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    source = args.output.parent / (args.output.name + "-project.json")
    source.write_text(json.dumps(project, indent=2) + "\n", encoding="utf-8")
    subprocess.run(["dotnet", "run", "--project", "DatabaseGenerator", "--configuration", "Release", "--no-build", "--",
                    "forge", "generate", "--project", str(source), "--output", str(args.output)], check=True)
