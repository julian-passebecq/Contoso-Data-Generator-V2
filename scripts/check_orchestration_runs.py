"""Revalidate real DagRuns, including refusal to adopt another run's artifacts."""
import argparse
from pathlib import Path
import sys

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--expected-runs", required=True, type=int)
    args = parser.parse_args()
    root = args.root.resolve()
    sys.path.insert(0, str(root / "factory"))
    from common import read
    from orchestration import finalize_execution, validate_cosmos, require
    states = sorted((root / ".forge/v15").glob("*/cosmos_execution.json"))
    require(len(states) == args.expected_runs, "incorrect number of completed runs")
    invocations, nonces, run_ids = set(), set(), set()
    for path in states:
        state = path.parent
        run_id = read(path)["runId"]
        previous = read(path)
        require(finalize_execution(root, state, run_id, persist=False) == previous, "retained execution evidence differs")
        invocations.add(previous["dbtInvocationId"])
        nonces.add(read(state / "cosmos/attempt.json")["nonce"])
        run_ids.add(run_id)
        try:
            validate_cosmos(root, state, "different-DagRun")
        except ValueError:
            pass
        else:
            raise AssertionError("A different DagRun adopted previous evidence")
        print(run_id, previous["status"], previous["executedModels"], previous["executedTests"], previous["runResultsSha256"])
    require(len(invocations) == len(nonces) == len(run_ids) == args.expected_runs, "DagRuns reused invocation identity")
