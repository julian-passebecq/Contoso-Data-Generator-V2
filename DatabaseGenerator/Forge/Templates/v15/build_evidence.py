"""Explicit local Evidence rendering; no cloud deployment or BI logic."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
from common import read, write, sha, now


def build(state):
    state = state.resolve()
    report = state / "bi/evidence"
    contract = read(state / "bi/report_contract.json")
    if contract["status"] != "package-generated": raise ValueError("A measured BI package is required")
    from run import verify_artifacts
    verify_artifacts(report, {"artifacts": contract["reportFileHashes"]})
    npm = shutil.which("npm.cmd" if os.name == "nt" else "npm")
    if npm is None: raise RuntimeError("Install Node.js/npm to render Evidence")
    result = {"status": "running", "startedAt": now(), "runId": contract["runId"], "reportContractSha256": sha(state / "bi/report_contract.json")}
    write(state / "bi/build_evidence.json", result)
    try:
        for command in ([npm, "install", "--no-audit", "--no-fund"], [npm, "run", "sources"], [npm, "run", "build"]):
            label = "install" if command[1] == "install" else command[-1]
            with (state / "bi" / (label + ".log")).open("w", encoding="utf-8") as log:
                subprocess.run(command, cwd=report, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=1800)
        built = report / "build/index.html"
        if not built.is_file(): raise RuntimeError("Evidence returned no build/index.html")
        result.update(status="built", completedAt=now(), artifact="bi/evidence/build/index.html", sha256=sha(built),
                      packageLockSha256=sha(report / "package-lock.json"),
                      npmVersion=subprocess.check_output([npm, "--version"], text=True).strip(),
                      nodeVersion=subprocess.check_output([shutil.which("node"), "--version"], text=True).strip())
    except Exception as error:
        result.update(status="failed", error=str(error), completedAt=now())
        raise
    finally:
        write(state / "bi/build_evidence.json", result)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--state", type=Path, required=True)
    print(build(parser.parse_args().state))
