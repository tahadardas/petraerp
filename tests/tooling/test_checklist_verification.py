"""Checks that checklist evidence cannot turn missing, skipped or stale tests into completion."""
import importlib.util
import csv
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("checklist_verification", ROOT / "tools/checklist_verification.py")
TOOL = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(TOOL)


class ChecklistVerificationTests(unittest.TestCase):
    def test_current_arabic_checklist_and_unmapped_ux_keep_original_criteria(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); reports = root / 'reports'; reports.mkdir(); self.write_reports(reports)
            rows = [dict(ID='PT-081', **{'حالة المصدر':'🟠 جزئي', 'المهمة':'مقبوضات الزبون', 'معيار القبول':'هوية ونطاق', 'دليل المصدر':'دليل عربي'}),
                    dict(ID='UX-017', **{'حالة المصدر':'🟠 جزئي', 'المهمة':'مشتريات المورد', 'معيار القبول':'تحميل مرقم', 'دليل المصدر':'قيد العمل'})]
            with (root / 'CHECKLIST.csv').open('w', encoding='utf-8-sig', newline='') as f:
                writer = csv.DictWriter(f, fieldnames=list(rows[0])); writer.writeheader(); writer.writerows(rows)
            manifest = root / 'docs/implementation/verification-map.json'; manifest.parent.mkdir(parents=True)
            manifest.write_text(json.dumps({'requirements':[{'id':'PT-081','tests':['Api.*']}, {'id':'UX-017','gaps':['No complete mapped evidence yet']}]}), encoding='utf-8')
            result = TOOL.create_report(root, reports, 0)
            self.assertEqual('CHECKLIST.csv', result['checklist_path']); self.assertEqual(64, len(result['checklist_sha256']))
            self.assertEqual('هوية ونطاق', result['requirements'][0]['acceptance'])
            self.assertEqual('not_mapped', result['requirements'][1]['result'])

    def test_legacy_english_checklist_is_supported_when_live_file_is_absent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); path = root / 'PetraERP_PromptPack/PetraERP_IMPLEMENTATION_CHECKLIST.csv'; path.parent.mkdir()
            path.write_text('ID,Status,Task,Acceptance,Evidence\nPT-001,Partial,Task,Original criterion,Proof\n', encoding='utf-8')
            selected, rows = TOOL.load_checklist(root)
            self.assertEqual(path, selected); self.assertEqual('Original criterion', rows[0]['Acceptance'])

    def test_live_checklist_has_priority_and_bad_columns_never_silently_fall_back(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); legacy = root / 'PetraERP_PromptPack/PetraERP_IMPLEMENTATION_CHECKLIST.csv'; legacy.parent.mkdir()
            legacy.write_text('ID,Status,Task,Acceptance,Evidence\nPT-001,Partial,Old,Old,Old\n', encoding='utf-8')
            current = root / 'CHECKLIST.csv'; current.write_text('ID,Status,Task,Acceptance,Evidence\nPT-002,Partial,Current,Current,Current\n', encoding='utf-8')
            selected, rows = TOOL.load_checklist(root)
            self.assertEqual(current, selected); self.assertEqual('PT-002', rows[0]['ID'])
            current.write_text('ID,Unknown\nPT-002,x\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Unsupported checklist columns'):
                TOOL.load_checklist(root)

    def test_new_continuation_ids_still_require_explicit_manifest_entries(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); reports = root / 'reports'; reports.mkdir(); self.write_reports(reports)
            (root / 'CHECKLIST.csv').write_text('ID,Status,Task,Acceptance,Evidence\nUX-017,Partial,Task,Criterion,Proof\n', encoding='utf-8')
            manifest = root / 'docs/implementation/verification-map.json'; manifest.parent.mkdir(parents=True)
            manifest.write_text(json.dumps({'requirements':[]}), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'IDs must match exactly'):
                TOOL.create_report(root, reports, 0)

    def test_passing_checks_are_not_requirement_acceptance(self):
        result = TOOL.assess({"id": "PT-001", "tests": ["Core.*"]}, [{"name": "Core.case", "status": "passed"}])
        self.assertEqual("checks_passed", result["result"])
        self.assertNotIn("complete", result)

    def test_unmapped_requirement_is_a_gap(self):
        self.assertEqual("not_mapped", TOOL.assess({"id": "PT-001"}, [])['result'])

    def test_missing_required_test_is_partial_even_when_another_passes(self):
        row = {"id": "PT-001", "tests": ["Core.*", "Sql.*"]}
        result = TOOL.assess(row, [{"name": "Core.test", "status": "passed"}])
        self.assertEqual("partial", result['result']); self.assertEqual(["Sql.*"], result['missing_tests'])

    def test_failure_takes_priority_over_passing_checks(self):
        result = TOOL.assess({"id": "PT-001", "tests": ["Core.*"]}, [{"name": "Core.a", "status": "passed"}, {"name": "Core.b", "status": "failed"}])
        self.assertEqual("failed", result['result'])

    def test_skipped_tests_and_manual_gaps_remain_partial(self):
        for row, tests in [({"id": "PT-001", "tests": ["Core.*"]}, [{"name": "Core.a", "status": "skipped"}]),
                           ({"id": "PT-001", "tests": ["Core.*"], "gaps": ["Native acceptance missing"]}, [{"name": "Core.a", "status": "passed"}])]:
            self.assertEqual("partial", TOOL.assess(row, tests)['result'])

    def test_report_set_must_include_all_five_projects(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "five project"):
                TOOL.load_reports(Path(directory), 0)

    def test_stale_report_cannot_be_used(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); self.write_reports(folder)
            with self.assertRaisesRegex(ValueError, "Stale"):
                TOOL.load_reports(folder, 2000)

    def test_report_outcomes_are_reconciled_instead_of_trusting_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); self.write_reports(folder)
            self.assertEqual(5, len(TOOL.load_reports(folder, 0)))
            path = folder / "PetraERP.Api.Tests.json"; data = json.loads(path.read_text())
            data['results']['summary']['passed'] = 0; path.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "Inconsistent"):
                TOOL.load_reports(folder, 0)

    @staticmethod
    def write_reports(folder):
        for project in ("Domain", "Application", "Infrastructure", "Api", "Desktop"):
            report = {"reportFormat": "CTRF", "reportId": project, "results": {"summary": {"start": 1000000, "tests": 1, "passed": 1},
                      "tests": [{"name": project + ".case", "status": "passed"}]}}
            (folder / f"PetraERP.{project}.Tests.json").write_text(json.dumps(report))
