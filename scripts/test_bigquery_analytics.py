#!/usr/bin/env python3
"""Offline analytics contract/metric tests; no BigQuery runtime validation claim."""
import copy
import json
import math
import shutil
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

TEMPLATES = Path(__file__).resolve().parents[1] / 'DatabaseGenerator/Forge/Templates/free_gcp'
sys.path.insert(0, str(TEMPLATES / 'bqml'))
sys.path.insert(0, str(TEMPLATES / 'dbt_bigquery'))
import run_bqml
import run_dbt
sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_free_gcp_runtime as package_fixtures


class AnalyticsTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        (self.root / 'gcp').mkdir()
        (self.root / 'bqml').mkdir()
        shutil.copyfile(TEMPLATES / 'bqml/features.sql', self.root / 'bqml/features.sql')
        self.config = {'warehouse': 'bigquery', 'gcp': {'projectId': 'example-project', 'dataset': 'contoso_forge',
                       'location': 'US', 'maximumBytesBilled': 1000000}}
        (self.root / 'gcp/bigquery_config.json').write_text(json.dumps(self.config))
        self.order = {'gcp': copy.deepcopy(self.config['gcp']), 'warehouse': 'bigquery',
                      'runId': 'unit-test', 'workOrderId': 'id', 'datasetFingerprint': 'a'*64}
        (self.root / 'order.json').write_text(json.dumps(self.order))

    def tearDown(self):
        self.directory.cleanup()

    def test_dbt_binds_destination_and_run_prefix(self):
        values = run_dbt.configure(self.root, self.order)
        self.assertEqual(values['FORGE_GCP_PROJECT'], 'example-project')
        self.assertEqual(values['FORGE_BQ_PREFIX'], run_bqml.table_prefix(self.order))
        self.order['gcp']['dataset'] = 'different_dataset'
        with self.assertRaises(ValueError): run_dbt.configure(self.root, self.order)

    def test_bigquery_numeric_accepted_values_are_not_quoted_strings(self):
        try:
            import yaml
        except ImportError:
            self.skipTest('Parsed YAML regression runs with dbt/PyYAML installed; generated indentation is also checked by .NET')
        schema = yaml.safe_load((TEMPLATES / 'dbt_bigquery/models/staging/schema.yml').read_text())
        models = {model['name']: model for model in schema['models']}
        for model, column in (('stg_reviews', 'rating'), ('stg_support_tickets', 'satisfaction_score')):
            with self.subTest(model=model, column=column):
                spec = next(item for item in models[model]['columns'] if item['name'] == column)
                self.assertIn('not_null', spec['data_tests'])
                checks = [test['accepted_values'] for test in spec['data_tests'] if isinstance(test, dict) and 'accepted_values' in test]
                self.assertEqual(len(checks), 1)
                arguments = checks[0]['arguments']
                self.assertEqual(arguments['values'], [1, 2, 3, 4, 5])
                self.assertTrue(all(type(value) is int for value in arguments['values']))
                self.assertIs(arguments['quote'], False)
        operation = next(item for item in models['stg_customer_cdc']['columns'] if item['name'] == 'operation')
        string_check = next(test['accepted_values']['arguments'] for test in operation['data_tests'] if isinstance(test, dict))
        self.assertEqual(string_check['values'], ['I', 'U', 'D'])
        self.assertTrue(string_check.get('quote', True))

    def test_preview_has_no_execution_claim_and_explicit_feature_allowlist(self):
        plan = run_bqml.run(self.root, self.root / 'order.json', None, '2026-01-01T00:00:00Z')
        self.assertFalse(plan['cloudExecutionVerified'])
        sql = next((self.root / 'bqml').rglob('train.sql')).read_text()
        self.assertIn('CREATE MODEL', sql)
        self.assertNotIn('OR REPLACE', sql)
        self.assertIn("DATA_SPLIT_METHOD='CUSTOM'", sql)
        self.assertIn("split_name IN ('train','validation')", sql)
        for forbidden in ('refund_amount', 'returned_flag', 'average_review_rating', 'satisfaction_outcome', 'order_key'):
            self.assertNotIn(forbidden, plan['features'])
        with self.assertRaisesRegex(ValueError, 'pricing/quota'):
            run_bqml.run(self.root, self.root / 'order.json', None, '2026-01-01T00:00:00Z', execute=True)

    def test_cutoff_and_destination_cannot_inject_sql(self):
        for value in ("2026-01-01'; DROP TABLE x; --", '2026-01-01T00:00:00'):
            with self.assertRaises(ValueError): run_bqml.prepare(self.root, self.order, value, 'logistic')
        self.order['gcp']['projectId'] = 'other-project'
        with self.assertRaises(ValueError): run_bqml.prepare(self.root, self.order, '2026-01-01T00:00:00Z', 'logistic')

    def test_metrics_ties_match_known_aucs(self):
        tied = run_bqml.classification_metrics([{run_bqml.LABEL: y, 'probability': .5} for y in (0, 1, 0, 1)])
        self.assertEqual(tied['averagePrecision'], .5)
        self.assertEqual(tied['rocAuc'], .5)
        perfect = run_bqml.classification_metrics([{run_bqml.LABEL: 0, 'probability': .1}, {run_bqml.LABEL: 1, 'probability': .9}])
        self.assertEqual(perfect['averagePrecision'], 1)
        self.assertEqual(perfect['rocAuc'], 1)
        self.assertEqual(perfect['balancedAccuracy'], 1)
        self.assertAlmostEqual(perfect['brierScore'], .01)

    def test_gold_requires_all_models_truth_test_and_actual_kpis(self):
        results = {'results': [{'unique_id': 'model.contoso_forge_bigquery.' + name, 'status': 'success'}
                               for name in run_dbt.EXPECTED_MODELS]}
        with self.assertRaises(ValueError): run_dbt.validate_results(results)
        results['results'].append({'unique_id': 'test.contoso_forge_bigquery.reconcile_truth', 'status': 'pass'})
        run_dbt.validate_results(results)
        results['results'][0]['status'] = 'skipped'
        with self.assertRaises(ValueError): run_dbt.validate_results(results)
        self.assertEqual({'order_count': '60'}, run_dbt.compare_kpis([{'order_count': 60}], {'order_count': 60}))
        with self.assertRaises(ValueError): run_dbt.compare_kpis([{'order_count': 59}], {'order_count': 60})

    def test_invalid_probabilities_and_single_class_fail(self):
        for probability in (float('nan'), float('inf'), -0.1, 1.1):
            with self.assertRaises(ValueError):
                run_bqml.classification_metrics([{run_bqml.LABEL: 0, 'probability': probability}, {run_bqml.LABEL: 1, 'probability': .5}])
        with self.assertRaises(ValueError): run_bqml.classification_metrics([{run_bqml.LABEL: 1, 'probability': .9}])

    def test_package_binds_authored_analytics_and_excludes_prior_outputs(self):
        fixture = package_fixtures.WorkOrderTests()
        fixture.setUp()
        try:
            for directory in ('dbt_bigquery', 'bqml'):
                shutil.copytree(TEMPLATES / directory, fixture.root / directory)
            excluded = ('dbt_bigquery/target/compiled.sql', 'dbt_bigquery/logs/debug.sql',
                        'dbt_bigquery/dbt_packages/dependency.sql', 'bqml/forge_previous/train.sql',
                        'bqml/forge_previous/model_card.md', 'bqml/metrics.json')
            for relative in excluded:
                path = fixture.root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('Prior execution output: must not travel with authored inputs')
            order = package_fixtures.handoff.package(fixture.root, 'analytics-package',
                fixture.root / 'runs/analytics-order.json', fixture.root / 'runs/analytics.zip', now=fixture.now)
            with zipfile.ZipFile(fixture.root / 'runs/analytics.zip') as archive:
                names = set(archive.namelist())
                for relative in ('dbt_bigquery/profiles.yml', 'dbt_bigquery/run_dbt.py',
                                 'dbt_bigquery/macros/generate_alias_name.sql', 'dbt_bigquery/models/sources.yml',
                                 'bqml/features.sql', 'bqml/run_bqml.py'):
                    self.assertIn(relative, names)
                    self.assertEqual(order['packageFileSha256'][relative], package_fixtures.handoff.sha256(fixture.root / relative))
                self.assertEqual(24, len([name for name in names if name.startswith('dbt_bigquery/models/') and name.endswith('.sql')]))
                self.assertTrue(set(excluded).isdisjoint(names))
            (fixture.root / 'bqml/features.sql').write_text('SELECT changed_after_issue')
            with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
                package_fixtures.handoff.validate_order(fixture.root, order, now=fixture.now)
        finally:
            fixture.tearDown()


if __name__ == '__main__':
    unittest.main()
