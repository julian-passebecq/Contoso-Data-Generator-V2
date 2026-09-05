"""Small runtime utilities. Authored contracts and observed results stay distinct."""
import hashlib
import json
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(value, indent=2, sort_keys=True, default=str, allow_nan=False) + "\n"
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(text, encoding="utf-8")
    temporary.replace(path)


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def now():
    return datetime.now(timezone.utc).isoformat()


def identifier(value):
    import re
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", value):
        raise ValueError(f"Unsafe SQL identifier: {value}")
    return '"' + value + '"'


def literal(value):
    return "'" + str(value).replace("'", "''") + "'"


def compare_kpis(actual, truth, catalog):
    expected = truth["expectedKpis"]
    required = {k["id"] for k in catalog["kpis"]}
    if set(expected) != required or not required.issubset(actual):
        raise ValueError("KPI catalog, Gold and truth keys do not match")
    tolerance = Decimal(str(catalog["reconciliation"]["numericTolerance"]))
    comparisons = {}
    for key in sorted(required):
        if actual[key] is None:
            raise ValueError(f"Null Gold KPI: {key}")
        observed, target = Decimal(str(actual[key])), Decimal(str(expected[key]))
        matched = observed.is_finite() and abs(observed - target) <= tolerance
        comparisons[key] = {"actual": str(observed), "expected": str(target), "matched": matched}
    if not all(v["matched"] for v in comparisons.values()):
        raise ValueError("Gold truth reconciliation failed: " + json.dumps(comparisons))
    return comparisons
