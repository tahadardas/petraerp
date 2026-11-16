"""Synthetic evidence only: never use company data as a test fixture."""

from __future__ import annotations

import csv
import hashlib
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[2] / "tools" / "legacy_integrity.py"
SPEC = importlib.util.spec_from_file_location("legacy_integrity", TOOL)
assert SPEC is not None and SPEC.loader is not None
integrity = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(integrity)


def write_csv(path: Path, rows: list[dict[str, str]]) -> None:
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def rehash(dataset: Path) -> None:
    manifest = dataset / "META" / "ALL_FILES_SHA256.txt"
    lines = []
    for path in sorted(dataset.rglob("*")):
        if path.is_file() and path != manifest:
            digest = hashlib.sha256(path.read_bytes()).hexdigest().upper()
            relative = str(path.relative_to(dataset)).replace("/", "\\")
            lines.append(f"{digest}  {relative}")
    manifest.write_text("\n".join(lines) + "\n", encoding="utf-8")


class IntegrityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.dataset = self.root / "DB_2025"
        (self.dataset / "META").mkdir(parents=True)
        (self.dataset / "DATA").mkdir()
        # Binary evidence is intentionally opaque to this phase's verifier.
        self.data = self.dataset / "DATA" / "dbo__accounts.csv.gz"
        self.data.write_bytes(b"synthetic compressed evidence")
        self.tables = [{
            "DatabaseName": "شركة الاختبار", "SchemaName": "dbo", "TableName": "accounts",
            "DataFile": self.data.name, "PreExactRowCount": "2", "ExportedRowCount": "2",
            "PostExactRowCount": "2", "CountMatch": "True", "OrderedByPrimaryKey": "True",
            "SizeBytes": str(self.data.stat().st_size),
            "SHA256": hashlib.sha256(self.data.read_bytes()).hexdigest().upper(),
            "Status": "OK", "Error": "",
        }]
        self.summary = [{
            "DatabaseName": "شركة الاختبار", "TotalTables": "1", "OKTables": "1",
            "CountMismatchTables": "0", "ErrorTables": "0", "TotalExportedRows": "2",
            "TotalCompressedBytes": str(self.data.stat().st_size),
        }]
        self.table_path = self.dataset / "META" / "TABLE_EXPORT_MANIFEST.csv"
        self.summary_path = self.dataset / "META" / "DATABASE_EXPORT_SUMMARY.csv"
        self.save()

    def save(self) -> None:
        write_csv(self.table_path, self.tables)
        write_csv(self.summary_path, self.summary)
        rehash(self.dataset)

    def test_valid_arabic_metadata_is_read_without_modifying_any_source(self) -> None:
        before = {p: p.read_bytes() for p in self.dataset.rglob("*") if p.is_file()}
        result = integrity.verify_dataset(self.dataset)
        self.assertEqual("PASS", result["status"])
        self.assertEqual(2, result["exporter_declared_rows"])
        self.assertEqual(3, result["files_verified"])
        self.assertEqual("شركة الاختبار", integrity.csv_rows(self.summary_path)[0]["DatabaseName"])
        self.assertEqual(before, {p: p.read_bytes() for p in self.dataset.rglob("*") if p.is_file()})

    def test_tampering_fails(self) -> None:
        self.data.write_bytes(b"changed")
        with self.assertRaisesRegex(integrity.EvidenceError, "SHA256 mismatch"):
            integrity.verify_dataset(self.dataset)

    def test_missing_file_fails(self) -> None:
        self.data.unlink()
        with self.assertRaises(OSError):
            integrity.verify_dataset(self.dataset)

    def test_unlisted_file_fails(self) -> None:
        (self.dataset / "extra.txt").write_text("unexpected", encoding="utf-8")
        with self.assertRaisesRegex(integrity.EvidenceError, "complete dataset"):
            integrity.verify_dataset(self.dataset)

    def test_parent_absolute_drive_and_stream_paths_fail(self) -> None:
        for path in ("../outside", "..\\outside", "C:\\outside", "/outside", "DATA/file:secret"):
            with self.subTest(path=path), self.assertRaises(integrity.EvidenceError):
                integrity.safe_file(self.dataset, path)

    def test_duplicate_hash_entry_fails(self) -> None:
        path = self.dataset / "META" / "ALL_FILES_SHA256.txt"
        with path.open("a", encoding="utf-8") as stream:
            stream.write(path.read_text(encoding="utf-8").splitlines()[0] + "\n")
        with self.assertRaisesRegex(integrity.EvidenceError, "Duplicate"):
            integrity.verify_dataset(self.dataset)

    def test_inconsistent_row_counts_fail_even_when_hashes_match(self) -> None:
        self.tables[0]["PostExactRowCount"] = "3"
        self.save()
        with self.assertRaisesRegex(integrity.EvidenceError, "counts disagree"):
            integrity.verify_dataset(self.dataset)

    def test_export_failure_is_rejected(self) -> None:
        self.tables[0]["Status"] = "ERROR"
        self.save()
        with self.assertRaisesRegex(integrity.EvidenceError, "consistent table export"):
            integrity.verify_dataset(self.dataset)

    def test_duplicate_table_is_rejected(self) -> None:
        self.tables.append(dict(self.tables[0]))
        self.save()
        with self.assertRaisesRegex(integrity.EvidenceError, "duplicate table"):
            integrity.verify_dataset(self.dataset)

    def test_independent_manifests_must_agree_on_hash(self) -> None:
        self.tables[0]["SHA256"] = "0" * 64
        self.save()
        with self.assertRaisesRegex(integrity.EvidenceError, "hash disagrees"):
            integrity.verify_dataset(self.dataset)

    def test_summary_totals_must_reconcile(self) -> None:
        self.summary[0]["TotalExportedRows"] = "999"
        self.save()
        with self.assertRaisesRegex(integrity.EvidenceError, "summary disagrees"):
            integrity.verify_dataset(self.dataset)

    def test_output_cannot_be_written_into_evidence(self) -> None:
        target = self.dataset / "report.json"
        result = subprocess.run(
            [sys.executable, str(TOOL), "--legacy-root", str(self.root), "--output", str(target)],
            capture_output=True, check=False,
        )
        self.assertEqual(1, result.returncode)
        self.assertFalse(target.exists())

    def test_empty_input_returns_failure(self) -> None:
        with tempfile.TemporaryDirectory() as empty:
            result = subprocess.run([sys.executable, str(TOOL), "--legacy-root", empty],
                                    capture_output=True, check=False)
        self.assertEqual(1, result.returncode)

    def test_failed_report_preserves_findings_for_every_dataset(self) -> None:
        other = self.root / "DB_2026"
        shutil.copytree(self.dataset, other)
        self.data.write_bytes(b"damaged first dataset")
        (other / "DATA" / self.data.name).write_bytes(b"damaged second dataset")
        # The report directory must be outside the entire evidence root.
        with tempfile.TemporaryDirectory() as output_root:
            report_path = Path(output_root) / "private-report.json"
            result = subprocess.run(
                [sys.executable, str(TOOL), "--legacy-root", str(self.root),
                 "--output", str(report_path)], capture_output=True, check=False,
            )
            report = json.loads(report_path.read_text(encoding="utf-8"))
        self.assertEqual(1, result.returncode)
        self.assertEqual("FAIL", report["status"])
        self.assertEqual(["DB_2025", "DB_2026"], [r["dataset"] for r in report["datasets"]])
        self.assertTrue(all(r["status"] == "FAIL" for r in report["datasets"]))


if __name__ == "__main__":
    unittest.main()
