"""Produce private, repeatable phase-1 evidence from offline exports only."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from decimal import Decimal
from pathlib import Path

from legacy_checks import core_diagnostics
from legacy_csv import read_export
from legacy_integrity import safe_file
from legacy_profile import profile_dataset


def log_keyword_diagnostics(dataset: Path) -> dict[str, dict[str, int]]:
    results = {}
    for table in ("DBLog", "ErrorLog", "MaintenanceLogItem000"):
        counts = {"rows": 0, "cost_text_candidates": 0, "negative_text_candidates": 0}
        for row in read_export(safe_file(dataset, f"DATA/dbo__{table}.csv.gz")):
            text = " ".join(row.get(column) or "" for column in ("Notes", "c1", "c2"))
            counts["rows"] += 1
            counts["cost_text_candidates"] += bool(re.search(
                r"cost\s*(?:job|center)|مركز.{0,12}تكلف|مراكز.{0,12}تكلف", text, re.I))
            counts["negative_text_candidates"] += bool(re.search(r"negative|سالب", text, re.I))
        results[table] = counts
    return results


def declared_foreign_keys(dataset: Path, definitions: list[dict[str, str]]) -> list[dict[str, object]]:
    grouped = {}
    for definition in definitions:
        key = (definition["ParentSchema"], definition["ParentTable"], definition["ForeignKeyName"])
        grouped.setdefault(key, []).append(definition)
    results = []
    for (schema, table, name), columns in grouped.items():
        columns.sort(key=lambda row: int(row["ColumnOrder"]))
        parent = columns[0]
        target = safe_file(dataset, f"DATA/{parent['ReferencedSchema']}__{parent['ReferencedTable']}.csv.gz")
        keys = {tuple(row[col["ReferencedColumn"]] for col in columns) for row in read_export(target)}
        checked = orphans = skipped = 0
        for row in read_export(safe_file(dataset, f"DATA/{schema}__{table}.csv.gz")):
            identity = tuple(row[col["ParentColumn"]] for col in columns)
            if any(value is None for value in identity):
                skipped += 1
                continue
            checked += 1
            orphans += identity not in keys
        results.append({"table": f"{schema}.{table}", "constraint": name,
                        "checked_nonnull_rows": checked, "null_rows_skipped": skipped,
                        "orphan_rows": orphans, "disabled": parent["IsDisabled"],
                        "not_trusted": parent["IsNotTrusted"]})
    return results


def analyze(root: Path, output: Path) -> dict[str, object]:
    root = root.resolve(strict=True)
    output = output.resolve()
    if output.is_relative_to(root):
        raise ValueError("Analysis outputs must remain outside evidence.")
    datasets = sorted(path for path in root.glob("DB_[0-9][0-9][0-9][0-9]") if path.is_dir())
    if not datasets:
        raise ValueError("No datasets found.")
    results = []
    for dataset in datasets:
        print(f"Profiling {dataset.name} ...", flush=True)
        profile, core = profile_dataset(dataset)
        profile["diagnostics"] = core_diagnostics(core, int(dataset.name[-4:]))
        profile["log_keyword_diagnostics"] = log_keyword_diagnostics(dataset)
        profile["declared_fk_checks"] = declared_foreign_keys(dataset, profile["declared_foreign_keys"])
        results.append(profile)
        print(f"Completed {dataset.name}: {len(profile['tables'])} tables, "
              f"{sum(t['rows'] for t in profile['tables'].values())} actual CSV rows.", flush=True)
    common = set(results[0]["tables"])
    for result in results[1:]:
        common &= set(result["tables"])
    drift = [name for name in sorted(common)
             if any(result["tables"][name]["columns"] != results[0]["tables"][name]["columns"]
                    for result in results[1:])]
    report = {"generated_at_utc": datetime.now(timezone.utc).isoformat(),
              "scope": "Offline exploratory profiling. Not import acceptance or financial reconciliation.",
              "datasets": results, "shared_tables_with_column_order_or_name_drift": drift}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2,
                                 default=lambda value: str(value) if isinstance(value, Decimal) else _unsupported(value)) + "\n",
                      encoding="utf-8")
    return report


def _unsupported(value: object) -> str:
    raise TypeError(f"Unsupported analysis output type: {type(value).__name__}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--legacy-root", type=Path, default=Path(__file__).resolve().parents[1] / "legacy-data")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / ".artifacts/phase1/profile.json")
    args = parser.parse_args()
    try:
        analyze(args.legacy_root, args.output)
        print("PASS: exploratory profiling completed; see the report for source and quality failures.")
        return 0
    except (OSError, ValueError, KeyError, TypeError, EOFError) as error:
        print(f"FAIL: {type(error).__name__}: analysis incomplete; no source was changed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
