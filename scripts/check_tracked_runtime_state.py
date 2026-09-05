#!/usr/bin/env python3
"""Reject live state in the Git index without opening files or reading credentials."""
from __future__ import annotations

import argparse
from pathlib import Path, PurePosixPath
import subprocess


def forbidden_reason(path: str) -> str | None:
    """Match known live paths; authored templates and *.example files stay valid."""
    parts = PurePosixPath(path.replace("\\", "/")).parts
    lower = tuple(part.casefold() for part in parts)
    if any(part in {".forge-live", ".forge-runtime", ".kube"} for part in lower):
        return "user runtime or Kubernetes state directory"
    if any(lower[index:index + 2] == (".config", "gcloud") for index in range(len(lower) - 1)):
        return "Google Cloud user configuration directory"
    if any(lower[index] == ".forge" and lower[index + 1] in {"state", "v15"} for index in range(len(lower) - 1)):
        return "generated pipeline run state directory"
    if lower and lower[0] in {"out", "artifacts"}:
        return "generated output or local execution evidence directory"
    if lower and lower[0] == "lake" and lower[-1] not in {".gitkeep", ".contoso-forge-lake"}:
        return "local execution lake data"
    if lower and lower[-1] in {"application_default_credentials.json", "credentials.db", "access_tokens.db"}:
        return "authentication state filename"
    if len(lower) >= 2 and lower[-2] == "colab" and lower[-1] in {
            "work_order.json", "result_manifest.json", "spark_result_manifest.json", "spark_runtime.json", "work_package.zip"}:
        return "issued Colab work package or runtime result"
    if len(lower) == 1 and lower[0] in {".env", ".env.local", "kubeconfig"}:
        return "local environment or Kubernetes configuration"
    return None


def tracked_violations(repository: Path) -> list[tuple[str, str]]:
    result = subprocess.run(["git", "-C", str(repository), "ls-files", "--cached", "-z"],
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True)
    paths = result.stdout.decode("utf-8", errors="surrogateescape").split("\0")
    return [(path, reason) for path in sorted(set(paths)) if path and (reason := forbidden_reason(path))]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        violations = tracked_violations(args.repo)
    except (OSError, subprocess.CalledProcessError):
        parser.exit(2, "Cannot inspect the repository Git index.\n")
    if violations:
        print("Tracked live state is forbidden; remove these paths from the index and preserve local files:")
        for path, reason in violations:
            print(f"  {path}: {reason}")
        return 1
    print("Tracked runtime state guard passed (Git index checked; no file contents read).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
