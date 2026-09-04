#!/usr/bin/env python3
"""Offline adversarial evidence tests; synthetic fixtures are never live cloud proof."""
from __future__ import annotations

import copy
import sys
import types
import unittest
from datetime import timedelta
from pathlib import Path
from unittest.mock import patch

import test_free_gcp_runtime as legacy
from test_free_gcp_runtime import (FakeClient, API, ApiError, CONFIG, TEMPLATES, handoff, runtime)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        legacy.WorkOrderTests.setUp(self)
        self.legacy_order = self.order
        self.legacy_result = legacy.WorkOrderTests.make_result(self)
        (self.root / 'colab/work_order.json').unlink()
        handoff.write_json(self.root / 'colab/spark_config.json', {
            'contractVersion': '1.3', 'sparkApiMode': 'classic',
            'sparkVersionPolicy': 'colab-native', 'sparkVersion': '4.0.4'})
        for name in ('spark_session.py', 'storage_adapter.py', 'bootstrap_runtime.py'):
            path = self.root / 'colab' / name
            if not path.exists():
                path.write_text('# fixture runtime helper\n')
        self.order = handoff.package(self.root, 'v13-fixture', now=self.now)
        self.silver = self.root / 'lake/silver/orders/part-00000.parquet'
        self.silver.parent.mkdir(parents=True)
        self.silver.write_bytes(b'measured parquet fixture')
        self.instant = self.now + timedelta(minutes=3)

    def tearDown(self):
        self.temp.cleanup()

    def measured(self, mode='classic', hosted=False):
        result = {field: copy.deepcopy(self.order[field]) for field in (
            'workOrderId', 'runId', 'datasetFingerprint', 'truthManifestSha256', 'packageFileSha256', 'sourceFileSha256')}
        result.update(contractVersion='1.3', status='succeeded',
                      executionRuntime='google-colab-interactive' if hosted else 'local-python',
                      startedAt=(self.now + timedelta(seconds=10)).isoformat(),
                      completedAt=(self.now + timedelta(minutes=1)).isoformat(),
                      pythonVersion='3.13.5', javaVersion='openjdk 17.0.16', pysparkVersion='4.0.4', sparkVersion='4.0.4',
                      requestedSparkApiMode=self.order['requestedSparkApiMode'], actualSparkApiMode=mode,
                      sparkVersionPolicy=self.order['sparkVersionPolicy'], requestedSparkVersion=self.order['requestedSparkVersion'],
                      sparkSessionClass='pyspark.sql.connect.session.SparkSession' if mode.startswith('connect-') else 'pyspark.sql.session.SparkSession',
                      isRemote=mode.startswith('connect-'), masterOrRemote='local[2]', fallbackReason=None,
                      cpuCount=2, memorySummary={'physicalBytes': 10_000_000_000}, inputTransport='work-package-zip',
                      inputFingerprint=self.order['datasetFingerprint'], sourceRowCounts={'orders': 1},
                      bronzeRowCounts={'orders': 1}, silverRowCounts={'orders': 1}, truthReconciled=True,
                      bronzeFileSha256={'orders/part-00000.parquet': 'b' * 64},
                      silverFileSha256={'orders/part-00000.parquet': handoff.sha256(self.silver)},
                      dataframeSmoke={'dataframe': True, 'window': True, 'dedup': True, 'parquetRoundTrip': True})
        for layer in ('bronze', 'silver'):
            result[layer + 'Fingerprint'] = handoff.hash_mapping(result[layer + 'FileSha256'])
        return result

    def spark_result(self, evidence=None):
        return handoff.spark_result(self.root, self.order, evidence or self.measured(), self.instant)

    def import_result(self, result, output='validation/evidence.json', instant=None):
        returned = self.root / 'returned.json'
        handoff.write_json(returned, result)
        return handoff.import_evidence(self.root, self.root / 'colab/work_order.json', returned,
                                       self.root / output, instant or self.instant)

    def set_mode(self, mode):
        config = handoff.read_json(self.root / 'colab/spark_config.json')
        config['sparkApiMode'] = mode
        handoff.write_json(self.root / 'colab/spark_config.json', config)
        (self.root / 'colab/work_order.json').unlink()
        self.order = handoff.package(self.root, 'v13-' + mode, now=self.now)

    def test_local_spark_import_never_promotes_hosted_or_cloud_gates(self):
        result = self.spark_result()
        report = self.import_result(result)
        self.assertEqual(report['runtimeStatus']['colab-spark-classic'], 'validated-local-runtime')
        self.assertEqual(report['runtimeStatus']['bigquery-sandbox'], 'pending')
        self.assertEqual(report['overallArchitectureStatus'], 'pending-live-gates')
        self.assertEqual(report['resultManifestSha256'], handoff.sha256(self.root / 'returned.json'))
        self.assertEqual(report['workOrderSha256'], handoff.sha256(self.root / 'colab/work_order.json'))
        self.assertEqual(handoff.read_json(self.root / 'returned.json'), result)

    def test_spark_only_package_needs_no_real_gcp_project(self):
        config = copy.deepcopy(CONFIG)
        config['gcp']['projectId'] = 'your-gcp-project'
        handoff.write_json(self.root / 'gcp/bigquery_config.json', config)
        path = self.root / 'spark-only/work_order.json'
        order = handoff.package(self.root, 'spark-only', path, self.root / 'spark-only/package.zip', now=self.now, scope='spark')
        self.assertEqual(order['executionScope'], 'spark')
        with self.assertRaisesRegex(ValueError, 'actual GCP project'):
            handoff.package(self.root, 'bad-full', self.root / 'bad/order.json', now=self.now)

    def test_hosted_classic_gate_does_not_imply_connect_or_bigquery(self):
        report = self.import_result(self.spark_result(self.measured(hosted=True)))
        self.assertEqual(report['runtimeStatus']['colab-spark-classic'], 'validated-user-runtime')
        self.assertEqual(report['runtimeStatus']['colab-spark-connect-local'], 'pending')
        self.assertEqual(report['runtimeStatus']['bigquery-sandbox'], 'pending')
        self.assertEqual(report['verificationKind'], 'offline-contract-and-truth-reconciliation')

    def test_connect_class_and_remote_flag_and_smoke_are_required(self):
        self.set_mode('connect-local')
        for field, value in (('isRemote', False), ('sparkSessionClass', 'pyspark.sql.session.SparkSession'),
                             ('dataframeSmoke', {'dataframe': True}), ('sparkVersion', '3.5.9')):
            with self.subTest(field=field):
                measured = self.measured(mode='connect-local')
                measured[field] = value
                with self.assertRaises(ValueError):
                    self.spark_result(measured)
        report = self.import_result(self.spark_result(self.measured(mode='connect-local', hosted=True)))
        self.assertEqual(report['runtimeStatus']['colab-spark-connect-local'], 'validated-user-runtime')

    def test_explicit_fallback_only_validates_actual_classic(self):
        self.set_mode('connect-local')
        measured = self.measured(hosted=True)
        with self.assertRaisesRegex(ValueError, 'fallback'):
            self.spark_result(measured)
        measured['fallbackReason'] = 'Operator explicitly selected classic after Connect failed'
        report = self.import_result(self.spark_result(measured))
        self.assertEqual(report['runtimeStatus']['colab-spark-classic'], 'validated-user-runtime')
        self.assertEqual(report['runtimeStatus']['colab-spark-connect-local'], 'pending')

    def test_missing_exact_runtime_identity_versions_and_hashes_fail(self):
        for field in ('runId', 'workOrderId', 'truthManifestSha256', 'packageFileSha256', 'sourceFileSha256',
                      'pythonVersion', 'javaVersion', 'pysparkVersion', 'sparkVersion', 'sparkVersionPolicy', 'requestedSparkVersion', 'isRemote', 'masterOrRemote',
                      'cpuCount', 'memorySummary', 'inputFingerprint', 'bronzeFingerprint', 'silverFileSha256',
                      'bronzeRowCounts', 'silverRowCounts', 'truthReconciled'):
            with self.subTest(field=field):
                measured = self.measured()
                del measured[field]
                with self.assertRaises(ValueError):
                    self.spark_result(measured)

    def test_runtime_hash_count_and_time_tampering_is_rejected(self):
        for field, value in (('bronzeFingerprint', 'e' * 64), ('silverRowCounts', {'orders': 2}),
                             ('packageFileSha256', {'colab/run_spark.py': 'a' * 64}),
                             ('startedAt', (self.now - timedelta(seconds=1)).isoformat()),
                             ('completedAt', (self.now + timedelta(days=2)).isoformat())):
            with self.subTest(field=field):
                measured = self.measured()
                measured[field] = value
                with self.assertRaises(ValueError):
                    self.spark_result(measured)

    def test_expired_order_can_import_a_timely_result_but_not_execute(self):
        result = self.spark_result()
        later = self.now + timedelta(days=2)
        with self.assertRaisesRegex(ValueError, 'expired'):
            handoff.reconcile(self.root, self.order, result, later)
        report = self.import_result(result, instant=later)
        self.assertEqual(report['status'], 'evidence-imported')

    def test_import_cannot_overwrite_issued_source_or_another_run(self):
        result = self.spark_result()
        for path in ('colab/work_order.json', 'returned.json', 'truth_manifest.json', 'colab/run_spark.py'):
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, 'separate'):
                self.import_result(result, path)
        handoff.write_json(self.root / 'validation/evidence.json', {'workOrderId': 'other', 'runId': 'other'})
        with self.assertRaisesRegex(ValueError, 'another issued run'):
            self.import_result(result)

    def test_legacy_import_stays_runtime_unverified(self):
        # Preserve the old issued package hashes by restoring the unchanged legacy
        # order; adding a Spark configuration does not alter its hashed members.
        handoff.write_json(self.root / 'legacy_order.json', self.legacy_order)
        handoff.write_json(self.root / 'legacy_result.json', self.legacy_result)
        report = handoff.import_evidence(self.root, self.root / 'legacy_order.json', self.root / 'legacy_result.json',
                                        self.root / 'validation/legacy.json', self.instant)
        self.assertTrue(report['legacyRuntimeUnverified'])
        self.assertTrue(all(value == 'pending' for value in report['runtimeStatus'].values()))

    def test_new_order_cannot_accept_legacy_result_or_cloud_claim_on_spark_scope(self):
        with self.assertRaisesRegex(ValueError, 'completed 1.3'):
            handoff.reconcile(self.root, self.order, self.legacy_result, self.instant)
        result = self.spark_result()
        result['loadJobs'] = {'orders': {'jobId': 'fake'}}
        with self.assertRaisesRegex(ValueError, 'cannot claim warehouse'):
            self.import_result(result)

    def test_spark_partial_import_cannot_complete_the_full_manual_checkpoint(self):
        result = self.spark_result()
        with self.assertRaisesRegex(ValueError, 'cannot complete'):
            handoff.reconcile(self.root, self.order, result, self.instant)
        report = self.import_result(result)
        self.assertEqual(report['resultScope'], 'spark')
        self.assertEqual(report['runtimeStatus']['bigquery-sandbox'], 'pending')

    def test_schemas_accept_legacy_and_new_contracts(self):
        try:
            import jsonschema
        except ImportError:
            self.skipTest('jsonschema is optional')
        orders = jsonschema.Draft202012Validator(handoff.read_json(TEMPLATES / 'colab/work_order.schema.json'))
        results = jsonschema.Draft202012Validator(handoff.read_json(TEMPLATES / 'colab/result_manifest.schema.json'))
        orders.validate(self.legacy_order)
        results.validate(self.legacy_result)
        orders.validate(self.order)
        results.validate(self.spark_result())

    def test_bigquery_execution_uses_measured_files_jobs_and_preserves_injected_origin(self):
        test = self
        class Client(FakeClient):
            def get_dataset(self, _):
                return types.SimpleNamespace(location='US', default_table_expiration_ms=5_184_000_000)
            def get_table(self, _):
                raise ApiError(404)
            def details(self, job):
                job.project, job.location = 'example-project', 'US'
                job.created = job.started = test.now + timedelta(minutes=1, seconds=1)
                job.ended = test.now + timedelta(minutes=2)
                job.total_bytes_processed = job.total_bytes_billed = 100
                return job
            def load_table_from_file(self, *args, **kwargs):
                return self.details(super().load_table_from_file(*args, **kwargs))
            def query(self, *args, **kwargs):
                return self.details(super().query(*args, **kwargs))
        handoff.write_json(self.root / 'colab/spark_runtime.json', self.measured(hosted=True))
        pq = types.ModuleType('pyarrow.parquet')
        pq.ParquetFile = lambda _: types.SimpleNamespace(metadata=types.SimpleNamespace(num_rows=1))
        pa = types.ModuleType('pyarrow')
        pa.parquet = pq
        result_path = self.root / 'colab/result_manifest.json'
        with patch.dict(sys.modules, {'pyarrow': pa, 'pyarrow.parquet': pq}), \
             patch.object(handoff, 'utcnow', return_value=self.instant), \
             patch.object(runtime, 'utcnow', return_value=self.instant):
            client = Client()
            result = runtime.execute(self.root, self.root / 'lake/silver', self.root / 'colab/work_order.json', result_path, client, API)
        self.assertEqual(len(client.submissions), 1)
        self.assertEqual(result['bigQueryEvidence']['queryJobs']['kpis']['totalBytesBilled'], 100)
        report = self.import_result(result)
        self.assertEqual(report['runtimeStatus']['bigquery-sandbox'], 'reconciled-non-hosted-evidence')
        for field, value in (('state', 'RUNNING'), ('totalBytesBilled', 1_000_001), ('projectId', 'another-project')):
            corrupt = copy.deepcopy(result)
            corrupt['bigQueryEvidence']['queryJobs']['kpis'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.import_result(corrupt)
        corrupt = copy.deepcopy(result)
        corrupt['loadJobs']['orders']['inputSha256'] = 'c' * 64
        with self.assertRaisesRegex(ValueError, 'measured Silver'):
            self.import_result(corrupt)


class SandboxPreflightTests(unittest.TestCase):
    def test_wrong_location_and_external_target_fail_before_upload(self):
        client = types.SimpleNamespace(get_dataset=lambda _: types.SimpleNamespace(location='EU'))
        with self.assertRaisesRegex(ValueError, 'location mismatch'):
            runtime.preflight(client, CONFIG)
        client.get_dataset = lambda _: types.SimpleNamespace(location='US')
        client.get_table = lambda _: types.SimpleNamespace(table_type='EXTERNAL', external_data_configuration=object())
        with self.assertRaisesRegex(ValueError, 'not a native table'):
            runtime.preflight(client, CONFIG, ['example-project.contoso_forge.test'])

    def test_missing_dataset_requires_explicit_creation_and_auth_error_is_sanitized(self):
        def missing(_):
            raise ApiError(404)
        client = types.SimpleNamespace(get_dataset=missing)
        with self.assertRaisesRegex(ValueError, 'dataset is missing'):
            runtime.preflight(client, CONFIG)
        created = []
        client.create_dataset = lambda dataset, exists_ok: created.append(dataset) or dataset
        api = types.SimpleNamespace(Dataset=lambda _: types.SimpleNamespace())
        evidence = runtime.preflight(client, CONFIG, create_dataset=True, api=api)
        self.assertEqual(created[0].default_table_expiration_ms, 5_184_000_000)
        self.assertEqual(evidence['transport'], 'local-file-upload')
        self.assertFalse(evidence['billingAccountInspected'])
        def forbidden(_):
            raise ApiError(403)
        client.get_dataset = forbidden
        with self.assertRaisesRegex(RuntimeError, 'authentication/access'):
            runtime.preflight(client, CONFIG)


if __name__ == '__main__':
    unittest.main(verbosity=2)
