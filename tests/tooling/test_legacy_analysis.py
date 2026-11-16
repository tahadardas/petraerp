"""Independent synthetic checks for migration evidence calculations."""

from __future__ import annotations

import gzip
import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from legacy_checks import duplicate_numbers, journal_diagnostics, number, reference_check  # noqa: E402
from legacy_profile import log_provenance, profile_table  # noqa: E402
from analyze_legacy import analyze, log_keyword_diagnostics  # noqa: E402


class AnalysisTests(unittest.TestCase):
    def test_journal_diagnostic_sums_decimal_fractions_exactly(self) -> None:
        headers = [{"GUID": "a", "Debit": "0.3", "Credit": "0.3"}]
        lines = [
            {"ParentGUID": "a", "CurrencyGUID": "x", "Debit": "0.1", "Credit": "0"},
            {"ParentGUID": "a", "CurrencyGUID": "x", "Debit": "0.2", "Credit": "0"},
            {"ParentGUID": "a", "CurrencyGUID": "x", "Debit": "0", "Credit": "0.3"},
        ]
        result = journal_diagnostics(headers, lines)
        self.assertEqual(0, result["raw_nonzero_deltas"])
        self.assertEqual(0, result["header_line_total_mismatches"])
        lines[-1]["Credit"] = "0.299999999999999999999999999999"
        self.assertEqual(1, journal_diagnostics(headers, lines)["raw_nonzero_deltas"])

    def test_invalid_amounts_never_become_zero(self) -> None:
        for value in (None, "", "not-a-number", "NaN", "Infinity", "1e9999"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                number({"Debit": value}, "Debit")

    def test_reference_diagnostics_distinguish_optional_sentinels(self) -> None:
        rows = [{"Ref": value} for value in (None, "", "00000000-0000-0000-0000-000000000000", "A", "b")]
        result = reference_check(rows, "Ref", [{"GUID": "a"}])
        self.assertEqual({"rows": 5, "null": 1, "empty": 1, "zero_guid": 1, "orphan_nonzero": 1}, result)

    def test_numbers_are_unique_within_scope_not_globally(self) -> None:
        rows = [{"Type": "sale", "Number": "1"}, {"Type": "purchase", "Number": "1"}]
        self.assertEqual(0, duplicate_numbers(rows, ("Type",))["extra_rows"])
        rows.append({"Type": "sale", "Number": "1"})
        self.assertEqual(1, duplicate_numbers(rows, ("Type",))["extra_rows"])

    def test_profiles_null_empty_numeric_and_duplicate_keys(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "dbo__test.csv.gz"
            with gzip.open(path, "wt", encoding="utf-8-sig", newline="") as stream:
                stream.write('"GUID","Name","Debit","Date"\r\n"a",,"0.10","2025-01-01T00:00:00"\r\n'
                             '"a","","-0.20","2025-12-31T00:00:00"\r\n')
            result, _ = profile_table(path, [["GUID"]])
        self.assertEqual(2, result["rows"])
        self.assertEqual(1, result["null_counts"]["Name"])
        self.assertEqual(1, result["empty_string_counts"]["Name"])
        self.assertEqual(1, result["primary_keys"][0]["duplicate_rows"])
        self.assertEqual(1, result["numeric"]["Debit"]["negative"])
        self.assertEqual("2025-12-31", result["dates"]["Date"]["max"])

    def test_log_append_is_explained_without_rewriting_source(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            dataset = Path(folder)
            prefix = b"schema exported\r\n"
            suffix = b"[2026-10-07 08:48:16] FINISHED database export. Tables=1, OK=1, COUNT_MISMATCH=0, ERROR=0\r\n"
            path = dataset / "DATABASE_LOG.txt"
            path.write_bytes(prefix + suffix)
            result = log_provenance(dataset, hashlib.sha256(prefix).hexdigest().upper())
            self.assertEqual("APPENDED_SUFFIX", result["status"])
            self.assertTrue(result["suffix_is_completion_log"])
            self.assertEqual(prefix + suffix, path.read_bytes())

    def test_warning_counts_are_scoped_to_message_fields_and_not_deduplicated(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            dataset = Path(folder)
            (dataset / "DATA").mkdir()
            texts = {
                "DBLog": '"Notes","HostName"\n"ok","Cost Center"\n',
                "ErrorLog": '"c1","c2"\n"Missing Cost Job",""\n"Missing Cost Job",""\n',
                "MaintenanceLogItem000": '"Notes"\n"رصيد سالب"\n',
            }
            for table, text in texts.items():
                with gzip.open(dataset / "DATA" / f"dbo__{table}.csv.gz", "wt", encoding="utf-8-sig") as stream:
                    stream.write(text)
            result = log_keyword_diagnostics(dataset)
        self.assertEqual(0, result["DBLog"]["cost_text_candidates"])
        self.assertEqual(2, result["ErrorLog"]["cost_text_candidates"])
        self.assertEqual(1, result["MaintenanceLogItem000"]["negative_text_candidates"])

    def test_analysis_cannot_write_into_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with self.assertRaisesRegex(ValueError, "outside evidence"):
                analyze(root, root / "report.json")
            self.assertFalse((root / "report.json").exists())


if __name__ == "__main__":
    unittest.main()
