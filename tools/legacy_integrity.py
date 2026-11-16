"""Verify offline export evidence without opening a database or changing sources."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path, PureWindowsPath


class EvidenceError(ValueError):
    """The supplied evidence is incomplete, unsafe, or internally inconsistent."""


def safe_file(root: Path, relative: str) -> Path:
    """Accept exporter Windows paths, but reject escape and linked evidence."""
    windows_path = PureWindowsPath(relative)
    if windows_path.drive or windows_path.root or ".." in windows_path.parts:
        raise EvidenceError("Evidence path must remain inside its dataset.")
    if not windows_path.parts or ":" in relative:
        raise EvidenceError("Invalid evidence path.")
    candidate = root.joinpath(*windows_path.parts)
    current = root
    for part in windows_path.parts:
        current = current / part
        if current.is_symlink() or current.is_junction():
            raise EvidenceError("Linked evidence paths are not accepted.")
    resolved = candidate.resolve(strict=True)
    if not resolved.is_relative_to(root.resolve()) or not resolved.is_file():
        raise EvidenceError("Evidence path is not a local dataset file.")
    return resolved


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def csv_rows(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", errors="strict", newline="") as stream:
        reader = csv.DictReader(stream, strict=True)
        if not reader.fieldnames or len(reader.fieldnames) != len(set(reader.fieldnames)):
            raise EvidenceError("Invalid CSV header.")
        rows = list(reader)
        if any(None in row or any(value is None for value in row.values()) for row in rows):
            raise EvidenceError("CSV row width does not match its header.")
        return rows


def verify_dataset(dataset: Path) -> dict[str, object]:
    if dataset.is_symlink() or dataset.is_junction():
        raise EvidenceError("Linked dataset directories are not accepted.")
    dataset = dataset.resolve(strict=True)
    checksum_file = safe_file(dataset, "META/ALL_FILES_SHA256.txt")
    expected_hashes: dict[Path, str] = {}
    for line in checksum_file.read_text(encoding="utf-8-sig", errors="strict").splitlines():
        match = re.fullmatch(r"([0-9A-Fa-f]{64})  (.+)", line)
        if match is None:
            raise EvidenceError("Malformed checksum manifest line.")
        path = safe_file(dataset, match[2])
        if path in expected_hashes or path == checksum_file:
            raise EvidenceError("Duplicate or self-referencing checksum entry.")
        expected_hashes[path] = match[1].upper()
    if not expected_hashes:
        raise EvidenceError("Empty checksum manifest.")
    actual_files = {safe_file(dataset, str(p.relative_to(dataset)))
                    for p in dataset.rglob("*") if p.is_file()}
    if actual_files != set(expected_hashes) | {checksum_file}:
        raise EvidenceError("Checksum manifest does not cover the complete dataset.")
    mismatches = [str(path.relative_to(dataset)) for path, expected in expected_hashes.items()
                  if sha256(path) != expected]
    if mismatches:
        raise EvidenceError(f"SHA256 mismatch: {', '.join(mismatches)}")

    tables = csv_rows(safe_file(dataset, "META/TABLE_EXPORT_MANIFEST.csv"))
    summaries = csv_rows(safe_file(dataset, "META/DATABASE_EXPORT_SUMMARY.csv"))
    if not tables or len(summaries) != 1:
        raise EvidenceError("Missing table entries or ambiguous database summary.")
    summary = summaries[0]
    seen_tables: set[tuple[str, str]] = set()
    seen_data: set[Path] = set()
    total_rows = 0
    total_bytes = 0
    for table in tables:
        identity = (table["SchemaName"], table["TableName"])
        if not all(identity) or identity in seen_tables:
            raise EvidenceError("Empty or duplicate table identity.")
        seen_tables.add(identity)
        data_file = safe_file(dataset, f"DATA/{table['DataFile']}")
        if data_file.parent != dataset / "DATA" or data_file in seen_data:
            raise EvidenceError("Duplicate or non-local table data file.")
        seen_data.add(data_file)
        counts = [int(table[key]) for key in
                  ("PreExactRowCount", "ExportedRowCount", "PostExactRowCount")]
        if min(counts) < 0 or len(set(counts)) != 1:
            raise EvidenceError("Export row counts disagree.")
        if (table["Status"] != "OK" or table["CountMatch"].lower() != "true"
                or table["Error"] or table["DatabaseName"] != summary["DatabaseName"]):
            raise EvidenceError("Exporter did not confirm a consistent table export.")
        if expected_hashes.get(data_file) != table["SHA256"].upper():
            raise EvidenceError("Table manifest hash disagrees with checksum manifest.")
        if data_file.stat().st_size != int(table["SizeBytes"]):
            raise EvidenceError("Table compressed file size disagrees with manifest.")
        total_rows += counts[1]
        total_bytes += data_file.stat().st_size
    if seen_data != {p for p in actual_files if p.parent == dataset / "DATA"}:
        raise EvidenceError("Table manifest does not cover all data files.")
    expected_totals = {
        "TotalTables": len(tables), "OKTables": len(tables),
        "CountMismatchTables": 0, "ErrorTables": 0,
        "TotalExportedRows": total_rows, "TotalCompressedBytes": total_bytes,
    }
    if any(int(summary[key]) != value for key, value in expected_totals.items()):
        raise EvidenceError("Database summary disagrees with table manifest.")
    return {
        "dataset": dataset.name,
        "checksum_manifest_sha256": sha256(checksum_file),
        "files_verified": len(expected_hashes),
        "tables": len(tables),
        "exporter_declared_rows": total_rows,
        "compressed_data_bytes": total_bytes,
        "status": "PASS",
        "scope": "File integrity and exporter metadata; no CSV row or accounting validation.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--legacy-root", type=Path,
                        default=Path(__file__).resolve().parents[1] / "legacy-data")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        root = args.legacy_root.resolve(strict=True)
        if args.output and args.output.resolve().is_relative_to(root):
            raise EvidenceError("Reports must be written outside legacy-data.")
        datasets = sorted(p for p in root.iterdir() if re.fullmatch(r"DB_\d{4}", p.name))
        if not datasets:
            raise EvidenceError("No DB_YYYY datasets found.")
        results = []
        for dataset in datasets:
            try:
                results.append(verify_dataset(dataset))
            except (EvidenceError, OSError, ValueError, KeyError, csv.Error) as error:
                results.append({"dataset": dataset.name, "status": "FAIL", "reason": str(error)})
        passed = all(result["status"] == "PASS" for result in results)
        report = {
            "checked_at_utc": datetime.now(timezone.utc).isoformat(),
            "status": "PASS" if passed else "FAIL",
            "datasets": results,
        }
        encoded = json.dumps(report, ensure_ascii=False, indent=2)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(encoded + "\n", encoding="utf-8")
        print(encoded)
        return 0 if passed else 1
    except (EvidenceError, OSError, ValueError, KeyError, csv.Error) as error:
        # Output has only structural diagnostics, never customer row contents.
        print(json.dumps({"status": "FAIL", "reason": str(error)}, ensure_ascii=False))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
