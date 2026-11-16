"""Trace checklist requirements to fresh executed CTRF results; never auto-complete requirements."""
from __future__ import annotations

import argparse
import csv
import fnmatch
import hashlib
import json
import math
from pathlib import Path


def load_reports(directory: Path, not_before: float) -> list[dict]:
    if not math.isfinite(not_before) or not_before < 0:
        raise ValueError("A finite run start timestamp is required.")
    expected = {f"PetraERP.{name}.Tests.json" for name in ("Domain", "Application", "Infrastructure", "Api", "Desktop")}
    paths = {path.name: path for path in directory.glob("PetraERP.*.Tests.json")}
    if set(paths) != expected:
        raise ValueError("Need exactly the five project reports from one complete verification run.")
    tests: list[dict] = []
    identities: set[str] = set()
    for name in sorted(paths):
        report = json.loads(paths[name].read_text(encoding="utf-8-sig"))
        if report.get("reportFormat") != "CTRF" or not report.get("reportId") or report.get("reportId") in identities:
            raise ValueError(f"Invalid or duplicate report: {name}")
        identities.add(report["reportId"])
        result = report["results"]
        if result["summary"]["start"] < not_before * 1000:
            raise ValueError(f"Stale test report: {name}")
        rows = result["tests"]
        if not rows or result["summary"]["tests"] != len(rows):
            raise ValueError(f"Incomplete test report: {name}")
        for status in ("passed", "failed", "skipped", "pending", "other"):
            if sum(row["status"] == status for row in rows) != result["summary"].get(status, 0):
                raise ValueError(f"Inconsistent outcomes: {name}")
        if any(row.get("status") not in {"passed", "failed", "skipped", "pending", "other"} or not row.get("name") for row in rows):
            raise ValueError(f"Unrecognized outcome: {name}")
        tests.extend(rows)
    return tests


def assess(requirement: dict, tests: list[dict]) -> dict:
    patterns = requirement.get("tests", [])
    matched = [test for test in tests if any(fnmatch.fnmatchcase(test["name"], pattern) for pattern in patterns)]
    missing = [pattern for pattern in patterns if not any(fnmatch.fnmatchcase(test["name"], pattern) for test in tests)]
    blockers = requirement.get("gaps", [])
    result = "checks_passed"
    if any(test["status"] == "failed" for test in matched):
        result = "failed"
    elif not patterns:
        result = "not_mapped"
    elif missing or blockers or any(test["status"] != "passed" for test in matched):
        result = "partial"
    return {"id": requirement["id"], "result": result, "passed": sum(test["status"] == "passed" for test in matched),
            "matched": len(matched), "missing_tests": missing, "gaps": blockers,
            "test_names": [test["name"] for test in matched]}


def load_checklist(root: Path) -> tuple[Path, list[dict]]:
    """Read the live continuation checklist; keep old archive compatibility explicit."""
    path = root / "CHECKLIST.csv"
    if not path.is_file():
        path = root / "PetraERP_PromptPack/PetraERP_IMPLEMENTATION_CHECKLIST.csv"
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        fields = set(reader.fieldnames or [])
        english = {"ID": "ID", "Status": "Status", "Task": "Task", "Acceptance": "Acceptance", "Evidence": "Evidence"}
        arabic = {"ID": "ID", "Status": "حالة المصدر", "Task": "المهمة", "Acceptance": "معيار القبول", "Evidence": "دليل المصدر"}
        mapping = english if set(english.values()) <= fields else arabic
        if not set(mapping.values()) <= fields:
            raise ValueError(f"Unsupported checklist columns: {path}")
        rows = [{key: row[column] for key, column in mapping.items()} for row in reader]
    return path, rows


def create_report(root: Path, report_directory: Path, not_before: float) -> dict:
    checklist_path, checklist = load_checklist(root)
    manifest = json.loads((root / "docs/implementation/verification-map.json").read_text(encoding="utf-8"))
    ids = [row["ID"] for row in checklist]
    mapped_ids = [row["id"] for row in manifest["requirements"]]
    if len(set(ids)) != len(ids) or len(set(mapped_ids)) != len(mapped_ids) or set(ids) != set(mapped_ids):
        raise ValueError("Checklist and verification-map IDs must match exactly, without duplicates.")
    tests = load_reports(report_directory, not_before)
    results = [assess(row, tests) for row in manifest["requirements"]]
    source_rows = {row["ID"]: row for row in checklist}
    for row in results:
        source = source_rows[row["id"]]
        row["listed_status"] = source["Status"]
        row["task"] = source["Task"]
        row["acceptance"] = source["Acceptance"]
        row["source_evidence"] = source["Evidence"]
    return {"notice": "Passing mapped checks is evidence, not automatic acceptance or commercial readiness. Unmapped/gap rows stay explicit.",
            "tests": len(tests), "failed": sum(test["status"] == "failed" for test in tests),
            "not_before_unix": not_before,
            "checklist_path": str(checklist_path.relative_to(root)),
            "checklist_sha256": hashlib.sha256(checklist_path.read_bytes()).hexdigest(),
            "report_hashes": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(report_directory.glob("PetraERP.*.Tests.json"))},
            "manifest_sha256": hashlib.sha256((root / "docs/implementation/verification-map.json").read_bytes()).hexdigest(),
            "requirements": results}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reports", type=Path, required=True)
    parser.add_argument("--not-before-unix", type=float, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    report = create_report(root, args.reports, args.not_before_unix)
    (args.reports / "checklist-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = ["# Executed checklist evidence", "", report["notice"], "", "| ID | Listed status | Mapped checks | Passed / matched | Gaps |", "| --- | --- | --- | --- | --- |"]
    for row in report["requirements"]:
        gaps = "; ".join(row["gaps"] + row["missing_tests"]).replace("|", "/").replace("\n", " ")
        lines.append(f"| {row['id']} | {row['listed_status']} | {row['result']} | {row['passed']} / {row['matched']} | {gaps} |")
    (args.reports / "checklist-verification.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Checklist evidence: {len(report['requirements'])} requirements, {report['tests']} executed test cases; passing checks do not auto-complete rows.")


if __name__ == "__main__":
    main()
