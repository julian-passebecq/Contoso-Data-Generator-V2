"""Fail-closed logical Silver comparison. See docs/v1.6-canonical-encoding.md."""
import argparse
from collections import defaultdict
from datetime import datetime, timezone
from decimal import Decimal, ROUND_HALF_EVEN, localcontext
import hashlib
import json
import math
from pathlib import Path
import struct
import pyarrow as pa
import pyarrow.parquet as pq
from common import read, write, sha, now
from silver_contract import VERSION, contract


def frame(value):
    return struct.pack(">Q", len(value)) + value


def canonical(value, column):
    if value is None:
        return b"N"
    kind = column["type"]
    if kind == "string":
        if not isinstance(value, str): raise ValueError("Expected string")
        return b"S" + frame(value.encode("utf-8"))
    if kind == "boolean":
        if not isinstance(value, bool): raise ValueError("Expected boolean")
        return b"B1" if value else b"B0"
    if kind in ("int32", "int64"):
        bits = 32 if kind == "int32" else 64
        if type(value) is not int or not -(2 ** (bits - 1)) <= value < 2 ** (bits - 1):
            raise ValueError("Integer outside governed type")
        return b"I" + frame(str(value).encode("ascii"))
    if kind == "decimal(18,2)":
        if not isinstance(value, Decimal) or not value.is_finite(): raise ValueError("Expected finite decimal")
        quantized = value.quantize(Decimal("0.01"), rounding=ROUND_HALF_EVEN)
        if quantized != value or abs(value) >= Decimal("1e16"): raise ValueError("Decimal outside governed precision/scale")
        return b"D" + frame(format(abs(quantized) if quantized == 0 else quantized, ".2f").encode("ascii"))
    if kind == "timestamp_utc":
        if not isinstance(value, datetime): raise ValueError("Expected timestamp")
        # Arrow ns timestamps may retain sub-microsecond digits through pandas scalars.
        if getattr(value, "nanosecond", 0): raise ValueError("Timestamp exceeds microsecond precision")
        utc = value.replace(tzinfo=timezone.utc) if value.tzinfo is None else value.astimezone(timezone.utc)
        text = utc.isoformat(timespec="microseconds").replace("+00:00", "Z")
        return b"T" + frame(text.encode("ascii"))
    if kind == "float64":
        if "decimalPlaces" not in column: raise ValueError("Float requires a per-column policy")
        number = float(value)
        if math.isnan(number): text = "nan"
        elif math.isinf(number): text = "+inf" if number > 0 else "-inf"
        else:
            places = column["decimalPlaces"]
            with localcontext() as context:
                context.prec = 400
                rounded = Decimal(str(number)).quantize(Decimal(1).scaleb(-places), rounding=ROUND_HALF_EVEN)
            text = format(abs(rounded) if rounded == 0 else rounded, f".{places}f")
        return b"F" + frame(text.encode("ascii"))
    raise ValueError("Unknown governed type: " + kind)


def logical_type(t):
    if pa.types.is_string(t) or pa.types.is_large_string(t): return "string"
    if pa.types.is_timestamp(t): return "timestamp_utc"
    if pa.types.is_int32(t): return "int32"
    if pa.types.is_int64(t): return "int64"
    if pa.types.is_boolean(t): return "boolean"
    if pa.types.is_float64(t): return "float64"
    if pa.types.is_decimal(t): return f"decimal({t.precision},{t.scale})"
    return str(t)


def display(value):
    if value is None or isinstance(value, (bool, int)): return value
    if isinstance(value, float) and math.isfinite(value): return value
    text = str(value)
    return text if len(text) <= 256 else text[:256] + "…"


def snapshot(table, governed, limit=10):
    """Read physical types before canonicalization; never cast a mismatch into a match."""
    columns = governed["columns"]
    names = [c["name"] for c in columns]
    actual = {f.name: logical_type(f.type) for f in table.schema}
    expected = {c["name"]: c["type"] for c in columns}
    schema_ok = actual == expected and len(table.column_names) == len(names)
    observed_schema = [{"name": name, "type": actual.get(name, "missing")} for name in names]
    observed_schema += [{"name": name, "type": actual[name]} for name in sorted(actual.keys() - expected.keys())]
    result = {"rowCount": table.num_rows, "schema": observed_schema, "schemaMatched": schema_ok,
              "key": governed["key"], "uniqueRequired": governed["unique"], "canonicalSha256": None,
              "nullCounts": {n: table[n].null_count for n in names if n in actual}, "errors": []}
    groups = defaultdict(list)
    if not schema_ok:
        result["errors"].append("Logical schema differs from governed columns/types")
        return result, groups
    result["nullViolations"] = {c["name"]: result["nullCounts"][c["name"]] for c in columns
                                if not c["nullable"] and result["nullCounts"][c["name"]]}
    null_keys = 0
    rows = []
    for row in table.select(names).to_pylist():
        null_keys += any(row[k] is None for k in governed["key"])
        try:
            values = {c["name"]: canonical(row[c["name"]], c) for c in columns}
            key = b"".join(frame(values[k]) for k in governed["key"])
            encoded = b"".join(frame(values[n]) for n in names)
            item = (encoded, row, values)
            groups[key].append(item)
            rows.append((key, encoded))
        except (ValueError, TypeError, ArithmeticError) as error:
            if len(result["errors"]) < limit: result["errors"].append(str(error))
    for values in groups.values(): values.sort(key=lambda v: v[0])
    duplicate_groups = [values for values in groups.values() if len(values) > 1]
    result.update(keyUnique=not duplicate_groups, duplicateKeyCount=len(duplicate_groups), nullKeyCount=null_keys,
        duplicateKeySamples=[{k: display(values[0][1][k]) for k in governed["key"]} for values in duplicate_groups[:limit]])
    if result["errors"]: return result, groups
    schema_bytes = json.dumps({"version": VERSION, **governed}, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    digest = hashlib.sha256(b"CONTOSO-LOGICAL\x00" + frame(schema_bytes))
    for key, encoded in sorted(rows): digest.update(frame(encoded))
    result["canonicalSha256"] = digest.hexdigest()
    return result, groups


def compare_tables(tables, governed, limit=10):
    if not 1 <= limit <= 100: raise ValueError("Diagnostic limit must be 1-100")
    if len(tables) < 2: raise ValueError("At least two engines are required")
    observations, groups = {}, {}
    for name, table in tables.items(): observations[name], groups[name] = snapshot(table, governed, limit)
    reference = next(iter(tables))
    base = observations[reference]
    differences = {}
    valid = lambda x: (x["schemaMatched"] and not x["errors"] and not x.get("nullViolations")
        and x.get("nullKeyCount") == 0 and (not governed["unique"] or x.get("keyUnique")))
    matched = valid(base)
    for engine, observed in observations.items():
        if engine == reference: continue
        missing = sorted(groups[reference].keys() - groups[engine].keys())
        extra = sorted(groups[engine].keys() - groups[reference].keys())
        samples = []
        mismatch_rows = 0
        for key in sorted(groups[reference].keys() & groups[engine].keys()):
            left, right = groups[reference][key], groups[engine][key]
            if len(left) != len(right):
                mismatch_rows += abs(len(left) - len(right))
                if len(samples) < limit: samples.append({"key": {k: display(left[0][1][k]) for k in governed["key"]}, "rowMultiplicity": [len(left), len(right)]})
            for a, b in zip(left, right):
                if a[0] != b[0]:
                    mismatch_rows += 1
                    if len(samples) < limit:
                        samples.append({"key": {k: display(a[1][k]) for k in governed["key"]},
                            "columns": {c["name"]: {reference: display(a[1][c["name"]]), engine: display(b[1][c["name"]])}
                                for c in governed["columns"] if a[2][c["name"]] != b[2][c["name"]]}})
        same = (valid(observed) and valid(base) and observed["canonicalSha256"] == base["canonicalSha256"]
                and observed["rowCount"] == base["rowCount"] and observed["nullCounts"] == base["nullCounts"]
                and not missing and not extra)
        def key_samples(keys, group):
            return [{k: display(group[key][0][1][k]) for k in governed["key"]} for key in keys[:limit]]
        differences[engine] = {"matched": same, "missingKeyCount": len(missing), "extraKeyCount": len(extra),
            "missingKeySamples": key_samples(missing, groups[reference]), "extraKeySamples": key_samples(extra, groups[engine]),
            "mismatchRowCount": mismatch_rows, "samples": samples}
        matched = matched and same
    return {"matched": matched, "reference": reference, "governed": governed, "engines": observations, "comparisons": differences}


def read_table(directory, max_bytes=512 * 1024 * 1024):
    files = sorted(Path(directory).rglob("*.parquet"))
    if not files: raise ValueError("Missing Silver Parquet: " + str(directory))
    # Educational local comparator: reject unbounded decompression/materialization.
    size = sum(sum(pq.ParquetFile(p).metadata.row_group(i).total_byte_size
                   for i in range(pq.ParquetFile(p).metadata.num_row_groups)) for p in files)
    if size > max_bytes: raise ValueError("Silver table exceeds comparator materialization budget")
    return pa.concat_tables([pq.ParquetFile(p).read() for p in files])


def compare_runs(runs, output, revision, limit=10):
    from run import identity, verify_artifacts
    if len(runs) < 2 or len({r["engine"] for r in runs}) != len(runs):
        raise ValueError("Provide at least two distinct real engines")
    if len({str(Path(r["state"]).resolve()) for r in runs}) != len(runs):
        raise ValueError("Engine runs must have isolated state directories")
    bindings, contracts, source_identity = {}, [], None
    integrity_errors = []
    for r in runs:
        engine, root, state = r["engine"], Path(r["root"]).resolve(), Path(r["state"]).resolve()
        evidence = read(state / "run_evidence.json")
        if evidence["status"] != "succeeded" or evidence["stages"].get("silver", {}).get("status") != "succeeded":
            raise ValueError("No completed real run for " + engine)
        if evidence.get("engine", {}).get("name") != engine:
            raise ValueError("Engine identity mismatch: " + engine)
        if engine != "spark":
            settings = read(root / "resolved_project.json")["settings"]
            expected_stages = [a["operation"][8:] for a in read(root / "local_plan.json")["activities"]]
            if settings["engine"] != engine or evidence["stages"]["silver"].get("result", {}).get("adapter") != engine:
                raise ValueError("Compiled/executed engine differs: " + engine)
            if any(evidence["stages"].get(s, {}).get("status") != "succeeded" for s in expected_stages):
                raise ValueError("Incomplete compiled pipeline: " + engine)
        current = identity(root)
        if current != evidence["identity"]: raise ValueError("Run input identity changed: " + engine)
        truth = read(root / "truth_manifest.json")
        shared = {"datasetFingerprint": truth["datasetFingerprint"], "sourceFileSha256": truth["sourceFileSha256"],
                  "sourceModelSha256": sha(root / "models/source_model.json")}
        if source_identity is not None and shared != source_identity: raise ValueError("Source identities differ")
        source_identity = shared
        governed = contract(root)
        if read(state / "silver_contract.json") != governed: raise ValueError("Run Silver contract differs")
        contracts.append(governed)
        for stage in evidence["stages"].values():
            try: verify_artifacts(state, stage)
            except ValueError as error: integrity_errors.append({"engine": engine, "error": str(error)})
        # Detect unrecorded extra Parquet files as well as changes to existing files.
        recorded = {p for stage in evidence["stages"].values() for p in stage.get("artifacts", {}) if p.startswith("lake/silver/") and p.endswith(".parquet")}
        actual = {p.relative_to(state).as_posix() for p in (state / "lake/silver").rglob("*.parquet")}
        if recorded != actual: integrity_errors.append({"engine": engine, "error": "Silver artifact set changed"})
        bindings[engine] = {"engine": evidence["engine"], "runId": evidence["runId"], "identity": current,
            "root": str(root), "state": str(state),
            "executionScope": evidence.get("executionScope", "full-local-factory"),
            "startedAt": evidence["startedAt"], "completedAt": evidence["completedAt"],
            "runEvidenceSha256": sha(state / "run_evidence.json"), "runtimeVersions": evidence["runtimeVersions"],
            "silverFileSha256": {p: sha(state / p) for p in sorted(actual)}}
    if any(c != contracts[0] for c in contracts): raise ValueError("Governed contracts differ")
    tables = {}
    for table, governed in contracts[0]["tables"].items():
        try:
            tables[table] = compare_tables({r["engine"]: read_table(Path(r["state"]) / "lake/silver" / table) for r in runs}, governed, limit)
        except (ValueError, pa.ArrowException) as error:
            tables[table] = {"matched": False, "error": str(error)[:1024]}
    matched = all(t["matched"] for t in tables.values()) and not integrity_errors
    result = {"contractVersion": VERSION, "repositoryCommit": revision, "createdAt": now(),
        "source": source_identity, "runs": bindings, "canonicalPolicy": {k: v for k, v in contracts[0].items() if k != "tables"},
        "matched": matched, "status": "matched" if matched else "mismatch", "diagnosticLimit": limit,
        "integrityErrors": integrity_errors[:limit], "integrityErrorCount": len(integrity_errors), "tables": tables,
        "sparkParity": "compared" if "spark" in bindings else "not-executed"}
    write(output, result)
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", nargs=3, action="append", metavar=("ENGINE", "ROOT", "STATE"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--limit", type=int, default=10)
    args = parser.parse_args()
    result = compare_runs([dict(zip(("engine", "root", "state"), r)) for r in args.run], args.output, args.revision, args.limit)
    print(f"logical-parity:{result['status']} tables={len(result['tables'])} evidence={args.output}")
    if not result["matched"]: raise SystemExit(1)


if __name__ == "__main__": main()
