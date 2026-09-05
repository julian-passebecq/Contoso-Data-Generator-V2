"""Canonical encoding, negative diagnostics, real adapter semantics and run binding tests."""
import argparse
import copy
from datetime import datetime, timezone, timedelta
from decimal import Decimal
import importlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
import pyarrow as pa
import pyarrow.parquet as pq

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "DatabaseGenerator/Forge/Templates/v15"))
from common import read, write, sha
from parity import canonical, compare_tables, compare_runs, read_table
from silver_contract import column, arrow_schema, contract, sources

GOVERNED = {"key": ["id"], "unique": True, "columns": [column("id", "int64"), column("text", "string", True),
    column("amount", "decimal(18,2)"), column("time", "timestamp_utc"), column("flag", "boolean"),
    column("lag", "float64", decimalPlaces=9)]}
ROWS = [{"id": 1, "text": None, "amount": Decimal("1.20"), "time": datetime(2024, 1, 1, tzinfo=timezone.utc), "flag": False, "lag": 1 / 3},
        {"id": 2, "text": "é|🙂", "amount": Decimal("0.00"), "time": datetime(2024, 1, 2, tzinfo=timezone.utc), "flag": True, "lag": -0.0}]
GATE = None
DIAGNOSTICS = None


def table(rows=None, governed=GOVERNED):
    return pa.Table.from_pylist(ROWS if rows is None else rows, schema=arrow_schema(governed["columns"]))


class CanonicalTests(unittest.TestCase):
    def compare(self, changed, governed=GOVERNED, original=None):
        return compare_tables({"duckdb": table(governed=governed) if original is None else original, "candidate": changed}, governed, 2)

    def test_identical_values_with_reverse_row_and_column_order_match(self):
        self.assertTrue(self.compare(table(list(reversed(ROWS))).select(list(reversed(table().column_names))))["matched"])

    def test_different_parquet_bytes_same_logical_values_match(self):
        with tempfile.TemporaryDirectory() as folder:
            a, b = Path(folder) / "a.parquet", Path(folder) / "b.parquet"
            pq.write_table(table(), a, compression="NONE", use_dictionary=False)
            pq.write_table(table(list(reversed(ROWS))), b, compression="zstd", use_dictionary=True)
            self.assertNotEqual(sha(a), sha(b))
            self.assertTrue(self.compare(pq.read_table(b), original=pq.read_table(a))["matched"])

    def test_value_mutation_fails_and_identifies_key_and_column(self):
        rows = copy.deepcopy(ROWS); rows[0]["amount"] += Decimal("0.01")
        result = self.compare(table(rows))
        self.assertFalse(result["matched"])
        sample = result["comparisons"]["candidate"]["samples"][0]
        self.assertEqual({"id": 1}, sample["key"])
        self.assertIn("amount", sample["columns"])
        if DIAGNOSTICS: write(DIAGNOSTICS / "value-mismatch.json", result)

    def test_key_mutation_reports_missing_and_extra(self):
        rows = copy.deepcopy(ROWS); rows[0]["id"] = 3
        full = self.compare(table(rows))
        if DIAGNOSTICS: write(DIAGNOSTICS / "key-mismatch.json", full)
        result = full["comparisons"]["candidate"]
        self.assertEqual((1, 1), (result["missingKeyCount"], result["extraKeyCount"]))

    def test_missing_row_fails(self):
        self.assertFalse(self.compare(table(ROWS[:1]))["matched"])

    def test_extra_row_fails(self):
        self.assertFalse(self.compare(table(ROWS + [dict(ROWS[0], id=3)]))["matched"])

    def test_null_to_empty_fails_with_null_counts(self):
        rows = copy.deepcopy(ROWS); rows[0]["text"] = ""
        result = self.compare(table(rows))
        self.assertFalse(result["matched"])
        self.assertEqual(1, result["engines"]["duckdb"]["nullCounts"]["text"])
        self.assertEqual(0, result["engines"]["candidate"]["nullCounts"]["text"])
        if DIAGNOSTICS: write(DIAGNOSTICS / "null-empty-mismatch.json", result)

    def test_schema_type_mismatch_fails_even_equal_numeric_values(self):
        altered = table().set_column(0, "id", pa.array([1, 2], type=pa.int32()))
        result = self.compare(altered)
        self.assertFalse(result["matched"])
        self.assertFalse(result["engines"]["candidate"]["schemaMatched"])
        if DIAGNOSTICS: write(DIAGNOSTICS / "schema-mismatch.json", result)

    def test_missing_column_fails(self):
        self.assertFalse(self.compare(table().drop(["flag"]))["matched"])

    def test_extra_column_fails(self):
        self.assertFalse(self.compare(table().append_column("extra", pa.array([1, 2])))["matched"])

    def test_duplicate_keys_fail_even_when_both_engines_agree(self):
        duplicate = table(ROWS + ROWS)
        result = self.compare(duplicate, original=duplicate)
        self.assertFalse(result["matched"])
        self.assertEqual(2, result["engines"]["duckdb"]["duplicateKeyCount"])

    def test_legal_duplicates_have_deterministic_total_order(self):
        governed = dict(GOVERNED, unique=False)
        a = table(ROWS + [dict(ROWS[0], text="another")])
        b = a.take(pa.array([2, 1, 0]))
        self.assertTrue(self.compare(b, governed, a)["matched"])
        self.assertFalse(self.compare(table(ROWS), governed, a)["matched"])

    def test_null_key_fails_even_if_equal(self):
        a = table([dict(ROWS[0], id=None)])
        self.assertFalse(self.compare(a, original=a)["matched"])

    def test_nonnullable_value_fails_even_if_equal(self):
        a = table([dict(ROWS[0], flag=None)])
        self.assertFalse(self.compare(a, original=a)["matched"])

    def test_bounded_diagnostics(self):
        a = table([dict(ROWS[0], id=i) for i in range(100)])
        b = table([dict(ROWS[0], id=i, text="x" * 10000) for i in range(100)])
        result = self.compare(b, original=a)["comparisons"]["candidate"]
        self.assertEqual(100, result["mismatchRowCount"])
        self.assertEqual(2, len(result["samples"]))
        self.assertLess(len(json.dumps(result)), 2000)

    def test_null_empty_zero_and_false_are_distinct(self):
        self.assertEqual(4, len({canonical(None, column("x", "string")), canonical("", column("x", "string")),
            canonical(0, column("x", "int64")), canonical(False, column("x", "boolean"))}))

    def test_length_framed_unicode_strings_are_unambiguous(self):
        c = column("x", "string")
        self.assertNotEqual(canonical("a", c) + canonical("bc", c), canonical("ab", c) + canonical("c", c))
        self.assertEqual(b"S\x00\x00\x00\x00\x00\x00\x00\x02\xc3\xa9", canonical("é", c))

    def test_fixed_scale_decimal_and_signed_zero(self):
        c = column("x", "decimal(18,2)")
        self.assertEqual(canonical(Decimal("1.2"), c), canonical(Decimal("1.20"), c))
        self.assertEqual(canonical(Decimal("-0.00"), c), canonical(Decimal("0"), c))
        with self.assertRaises(ValueError): canonical(Decimal("1.234"), c)

    def test_utc_and_naive_timestamp_normalize_at_microseconds(self):
        c = column("x", "timestamp_utc")
        a = datetime(2024, 1, 1, 1, 0, 0, 123456, tzinfo=timezone(timedelta(hours=1)))
        self.assertEqual(canonical(a, c), canonical(datetime(2024, 1, 1, microsecond=123456), c))

    def test_submicrosecond_timestamp_rejected(self):
        import pandas as pd
        with self.assertRaises(ValueError): canonical(pd.Timestamp("2024-01-01T00:00:00.000000001Z"), column("x", "timestamp_utc"))

    def test_float_policy_rounding_signed_zero_and_nonfinite(self):
        c = column("lag", "float64", decimalPlaces=9)
        self.assertEqual(canonical(-0., c), canonical(0., c))
        self.assertEqual(canonical(float("nan"), c), canonical(float("nan"), c))
        self.assertNotEqual(canonical(float("inf"), c), canonical(float("-inf"), c))
        self.assertEqual(canonical(1 / 3, c), canonical(.3333333331, c))
        self.assertNotEqual(canonical(1 / 3, c), canonical(.333333335, c))
        with self.assertRaises(ValueError): canonical(1.2, column("x", "float64"))

    def test_integer_type_and_range_enforced(self):
        for value in (True, 2**31, 1.2):
            with self.assertRaises(ValueError): canonical(value, column("x", "int32"))

    def test_typed_empty_tables_match(self):
        self.assertTrue(self.compare(table([]), original=table([]))["matched"])

    def test_missing_parquet_fails_closed(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError): read_table(Path(folder))

    def test_materialization_guard_precedes_parquet_read(self):
        with tempfile.TemporaryDirectory() as folder:
            pq.write_table(table(), Path(folder) / "part.parquet")
            with self.assertRaisesRegex(ValueError, "budget"): read_table(folder, max_bytes=1)


class AdapterTests(unittest.TestCase):
    def test_explicit_csv_parsing_preserves_na_strings_decimals_and_nulls(self):
        import pandas_silver, polars_silver
        cols = [column("id", "int64"), column("text", "string", True), column("price", "decimal(18,2)"), column("time", "timestamp_utc"), column("flag", "boolean")]
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "typed.csv"
            path.write_text('id,text,price,time,flag\n1,NA,12345678901234.12,2024-01-01T00:00:00Z,true\n2,,0.00,2024-01-02T00:00:00Z,false\n', encoding="utf-8")
            pandas = pa.Table.from_pandas(pandas_silver.read_source(path, cols), schema=arrow_schema(cols), preserve_index=False)
            polars = polars_silver.scan_source(path, cols).collect(engine="streaming").to_arrow()
            self.assertTrue(compare_tables({"pandas": pandas, "polars": polars}, dict(columns=cols, key=["id"], unique=True))["matched"])
            self.assertEqual(["NA", None], pandas["text"].to_pylist())

    def test_header_mismatch_is_rejected_by_both_adapters(self):
        import pandas_silver, polars_silver
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "bad.csv"; path.write_text("wrong\n1\n")
            for adapter in (pandas_silver.read_source, polars_silver.scan_source):
                with self.assertRaisesRegex(ValueError, "header"): adapter(path, [column("id", "int64")])


@unittest.skipUnless(GATE, "Use --gate for completed real pipelines")
class RunBindingTests(unittest.TestCase):
    def runs(self):
        return [{"engine": e, "root": GATE / e, "state": GATE / e / ".forge/v15/v16"} for e in ("duckdb", "polars", "pandas")]

    def test_all_thirteen_tables_match_real_engines(self):
        result = read(GATE / "engine_parity.json")
        self.assertTrue(result["matched"])
        self.assertEqual(13, len(result["tables"]))
        self.assertEqual({"duckdb", "polars", "pandas"}, set(result["runs"]))
        self.assertTrue(all(t["matched"] for t in result["tables"].values()))

    def test_reused_state_or_engine_is_rejected(self):
        runs = self.runs()
        with self.assertRaisesRegex(ValueError, "distinct"): compare_runs([runs[0], runs[0]], Path("unused"), "test")
        runs[1]["state"] = runs[0]["state"]
        with self.assertRaisesRegex(ValueError, "isolated"): compare_runs(runs, Path("unused"), "test")

    def test_incomplete_run_does_not_emit_parity(self):
        original = read(self.runs()[0]["state"] / "run_evidence.json")
        original["status"] = "running"
        with tempfile.TemporaryDirectory() as folder, patch("parity.read", return_value=original):
            output = Path(folder) / "engine_parity.json"
            with self.assertRaisesRegex(ValueError, "completed"): compare_runs(self.runs(), output, "test")
            self.assertFalse(output.exists())

    def test_real_parquet_mutation_emits_failed_bounded_diagnostics(self):
        # Copy only one isolated state; never mutate retained successful evidence.
        with tempfile.TemporaryDirectory() as folder:
            state = Path(folder) / "state"
            runs = self.runs()
            shutil.copytree(runs[1]["state"], state, ignore=shutil.ignore_patterns("node_modules", "build"))
            runs[1]["state"] = state
            path = state / "lake/silver/orders/part-00000.parquet"
            data = pq.read_table(path); rows = data.to_pylist(); rows[0]["OrderStatus"] = "MUTATED"
            pq.write_table(pa.Table.from_pylist(rows, schema=data.schema), path)
            result = compare_runs(runs, Path(folder) / "engine_parity.json", "negative-test", 2)
            self.assertFalse(result["matched"])
            self.assertGreater(result["integrityErrorCount"], 0)
            self.assertFalse(result["tables"]["orders"]["matched"])
            self.assertIn("OrderStatus", result["tables"]["orders"]["comparisons"]["polars"]["samples"][0]["columns"])
            if DIAGNOSTICS: write(DIAGNOSTICS / "persisted-tamper-mismatch.json", result)

    def adapter_scenario(self, change):
        from pandas_silver import read_source
        import pandas as pd
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / "input"
            shutil.copytree(GATE / "duckdb/models", root / "models")
            shutil.copytree(GATE / "duckdb/data/source", root / "data/source")
            shutil.copyfile(GATE / "duckdb/truth_manifest.json", root / "truth_manifest.json")
            change(root)
            states = {}
            for engine in ("duckdb", "polars", "pandas"):
                state = Path(folder) / engine; state.mkdir()
                # No DuckDB transformation can hide behind either DataFrame adapter.
                if engine == "duckdb": importlib.import_module(engine + "_silver").transform(root, state)
                else:
                    with patch("duckdb.connect", side_effect=AssertionError("DataFrame adapter invoked DuckDB")):
                        importlib.import_module(engine + "_silver").transform(root, state)
                states[engine] = state
            results = {}
            for name, governed in contract(root)["tables"].items():
                frames = {engine: read_table(state / "lake/silver" / name) for engine, state in states.items()}
                result = compare_tables(frames, governed)
                self.assertTrue(result["matched"], name + ": " + json.dumps(result, default=str)[:3000])
                results[name] = frames["duckdb"].to_pylist()
            return results

    def test_all_adapters_handle_empty_optional_tables_without_inference(self):
        def change(root):
            for name in ("customer_cdc", "reviews", "returns", "shipments", "shipment_events", "support_tickets"):
                path = root / "data/source" / (name + ".csv")
                path.write_text(path.read_text().splitlines()[0] + "\n", encoding="utf-8")
        results = self.adapter_scenario(change)
        self.assertEqual([], results["quality_issues"])
        self.assertTrue(all(r["IsCurrent"] and r["ValidTo"] is None for r in results["customer_scd2"]))

    def test_all_adapters_match_cdc_order_and_duplicate_replay(self):
        import csv
        def change(root):
            for name in ("orders", "order_rows", "customer_cdc", "shipment_events"):
                path = root / "data/source" / (name + ".csv")
                with path.open(newline="", encoding="utf-8") as stream: rows = list(csv.reader(stream))
                with path.open("w", newline="", encoding="utf-8") as stream:
                    csv.writer(stream, lineterminator="\n").writerows([rows[0], *reversed(rows[1:]), *rows[1:4]])
        results = self.adapter_scenario(change)
        self.assertTrue(any(r["IsDeleted"] for r in results["customer_scd2"]))
        self.assertTrue(any(r["IsLateArrival"] for r in results["shipment_events"]))

    def test_all_adapters_match_whitespace_quarantine_and_lag_boundary(self):
        import csv
        def change(root):
            path = root / "data/source/shipments.csv"
            with path.open(newline="", encoding="utf-8") as stream:
                reader = csv.DictReader(stream); fields = reader.fieldnames; rows = list(reader)
            rows[0]["TrackingNumber"] = "   "
            with path.open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fields, lineterminator="\n"); writer.writeheader(); writer.writerows(rows)
            path = root / "data/source/shipment_events.csv"
            with path.open(newline="", encoding="utf-8") as stream:
                reader = csv.DictReader(stream); fields = reader.fieldnames; rows = list(reader)
            for row in rows:
                row["IngestedAt"] = (datetime.fromisoformat(row["EventTime"].replace("Z", "+00:00")) + timedelta(hours=24)).strftime("%Y-%m-%dT%H:%M:%SZ")
            with path.open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fields, lineterminator="\n"); writer.writeheader(); writer.writerows(rows)
        results = self.adapter_scenario(change)
        self.assertTrue(all(r["IngestionLagHours"] == 24 and not r["IsLateArrival"] for r in results["shipment_events"]))
        self.assertTrue(any(r["BadValue"] == "   " for r in results["quality_issues"]))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--gate", type=Path); parser.add_argument("--diagnostics", type=Path)
    args, remaining = parser.parse_known_args()
    GATE = args.gate.resolve() if args.gate else None
    DIAGNOSTICS = args.diagnostics.resolve() if args.diagnostics else None
    RunBindingTests.__unittest_skip__ = GATE is None
    unittest.main(argv=[sys.argv[0], *remaining])
