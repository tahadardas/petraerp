"""Read-only, source-evidenced reconciliation diagnostics; never repair records."""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter, defaultdict
from decimal import Decimal, localcontext
from pathlib import Path

from legacy_checks import Row, identity, missing, number
from legacy_csv import read_export
from legacy_integrity import safe_file, sha256

TOLERANCE_DIAGNOSTIC = Decimal("0.0001")  # Reporting bucket only, never acceptance.
SOURCE_OBJECTS = ("prcEntry_post", "prcBill_post", "fnMaterial_GetUnitList",
                  "prcCheckDB_mt_Units", "trg_ms000_CheckBalance", "trg_en000_checkConstraints")


def deviation_counts(deltas: list[Decimal]) -> dict[str, int]:
    return {"compared": len(deltas), "exact_mismatches": sum(d != 0 for d in deltas),
            "mismatches_over_0_0001": sum(abs(d) > TOLERANCE_DIAGNOSTIC for d in deltas)}


def account_rollup(accounts: list[Row], headers: list[Row], lines: list[Row]) -> dict[str, object]:
    cards = {identity(row["GUID"]): row for row in accounts}
    posted = {identity(row["GUID"]) for row in headers if row["IsPosted"] == "1"}
    totals = {guid: [Decimal(0), Decimal(0)] for guid in cards}
    with localcontext() as context:
        context.prec = 200
        for row in lines:
            if identity(row["ParentGUID"]) not in posted:
                continue
            guid = identity(row["AccountGUID"])
            visited = set()
            while not missing(guid):
                if guid in visited:
                    raise ValueError("Account hierarchy contains a cycle.")
                if guid not in cards:
                    raise ValueError("Account hierarchy contains a missing parent.")
                visited.add(guid)
                totals[guid][0] += number(row, "Debit")
                totals[guid][1] += number(row, "Credit")
                guid = identity(cards[guid]["ParentGUID"])
        debits = [number(row, "Debit") - totals[guid][0] for guid, row in cards.items()]
        credits = [number(row, "Credit") - totals[guid][1] for guid, row in cards.items()]
    return {"accounts": len(accounts), "debit_cache": deviation_counts(debits),
            "credit_cache": deviation_counts(credits),
            "meaning": "Posted raw line sums plus ancestor propagation, following prcEntry_post. No acceptance tolerance or repairs."}


def inventory_rollup(items: list[Row], headers: list[Row], lines: list[Row], types: list[Row]) -> dict[str, object]:
    documents = {identity(row["GUID"]): row for row in headers}
    patterns = {identity(row["GUID"]): row for row in types}
    totals: dict[str | None, Decimal] = defaultdict(Decimal)
    with localcontext() as context:
        context.prec = 200
        for row in lines:
            header = documents[identity(row["ParentGUID"])]
            if header["IsPosted"] != "1":
                continue
            pattern = patterns[identity(header["TypeGUID"])]
            if (pattern["bIsInput"], pattern["bIsOutput"]) not in (("1", "0"), ("0", "1")):
                raise ValueError("Document stock direction is ambiguous.")
            direction = 1 if pattern["bIsInput"] == "1" else -1
            # prcBill_post adds raw Qty+BonusQnt; unit factors affect prices,
            # not this quantity update. Do not infer units from names alone.
            totals[identity(row["MatGUID"])] += direction * (number(row, "Qty") + number(row, "BonusQnt"))
        deltas = [number(item, "Qty") - totals[identity(item["GUID"])] for item in items]
    return {"item_snapshot_vs_posted_document_movements": deviation_counts(deltas),
            "negative_reconstructed_item_balances": sum(value < 0 for value in totals.values()),
            "meaning": "Observed prcBill_post quantity rule only. Store/branch chronology, other movement sources and opening policies remain separate migration checks."}


def unit_checks(items: list[Row], lines: list[Row]) -> dict[str, int]:
    cards = {identity(row["GUID"]): row for row in items}
    bad_default = absent_unit = bad_fixed_factor = missing_variable_qty = 0
    for item in items:
        default = item.get("DefUnit")
        bad_default += default not in ("1", "2", "3") or not item.get(
            "Unity" if default == "1" else f"Unit{default}")
        for suffix in ("2", "3"):
            if item.get(f"Unit{suffix}") and item.get(f"Unit{suffix}FactFlag") == "0":
                bad_fixed_factor += number(item, f"Unit{suffix}Fact") <= 0
    for line in lines:
        item = cards[identity(line["MatGUID"])]
        unit = line.get("Unity")
        if unit not in ("1", "2", "3") or not item.get("Unity" if unit == "1" else f"Unit{unit}"):
            absent_unit += 1
        for suffix in ("2", "3"):
            if item.get(f"Unit{suffix}FactFlag") not in (None, "0") and number(line, f"Qty{suffix}") == 0:
                missing_variable_qty += 1
    return {"invalid_item_default_units": bad_default, "configured_fixed_units_nonpositive_factor": bad_fixed_factor,
            "lines_selecting_missing_or_unknown_unit": absent_unit,
            "missing_variable_unit_quantity_occurrences": missing_variable_qty}


def warning_codes(rows: list[Row]) -> dict[str, object]:
    counts: Counter[str] = Counter()
    references: dict[str, set[str]] = defaultdict(set)
    for row in rows:
        codes = set(re.findall(r"\bAmn[EW]\d{4}\b", " ".join(row.get(c) or "" for c in ("c1", "c2"))))
        for code in codes:
            counts[code] += 1
            if not missing(row.get("g1")):
                references[code].add(identity(row["g1"]))
    return {code: {"rows": count, "distinct_nonzero_g1": len(references[code])}
            for code, count in sorted(counts.items())}


def object_bodies(text: str) -> dict[str, str]:
    pattern = r"^-- OBJECT: \[dbo\]\.\[([^]]+)\]"
    markers = list(re.finditer(pattern, text, re.M))
    return {marker[1].casefold(): text[marker.end():markers[i + 1].start() if i + 1 < len(markers) else len(text)].strip()
            for i, marker in enumerate(markers)}


def analyze(root: Path, output: Path) -> dict[str, object]:
    root, output = root.resolve(strict=True), output.resolve()
    if output.is_relative_to(root):
        raise ValueError("Reconciliation output must remain outside evidence.")
    results = []
    baseline = None
    for dataset in sorted(root.glob("DB_[0-9][0-9][0-9][0-9]")):
        expected = {}
        for line in safe_file(dataset, "META/ALL_FILES_SHA256.txt").read_text(encoding="utf-8-sig").splitlines():
            digest, relative = line.split("  ", 1)
            expected[safe_file(dataset, relative)] = digest.upper()

        def load(table: str) -> list[Row]:
            path = safe_file(dataset, f"DATA/dbo__{table}.csv.gz")
            if sha256(path) != expected[path]:
                raise ValueError("Source hash mismatch.")
            rows = list(read_export(path))
            if sha256(path) != expected[path]:
                raise ValueError("Source changed during reconciliation.")
            return rows

        path = safe_file(dataset, "SCHEMA/22_module_definitions.sql")
        if sha256(path) != expected[path]:
            raise ValueError("Routine definition hash mismatch.")
        bodies = object_bodies(path.read_text(encoding="utf-8-sig"))
        selected = {name: bodies[name.casefold()] for name in SOURCE_OBJECTS}
        if baseline is None:
            baseline = selected
        elif selected != baseline:
            raise ValueError("Reviewed source rules differ between annual exports; review before comparing.")
        accounts, headers, entries = load("ac000"), load("ce000"), load("en000")
        items, bills, lines, types = load("mt000"), load("bu000"), load("bi000"), load("bt000")
        results.append({"dataset": dataset.name,
                        "account_rollup": account_rollup(accounts, headers, entries),
                        "inventory_rollup": inventory_rollup(items, bills, lines, types),
                        "unit_checks": unit_checks(items, lines), "warning_codes": warning_codes(load("ErrorLog"))})
    if not results:
        raise ValueError("No datasets found.")
    report = {"scope": "Source-evidenced exploratory reconciliation; no import approval or source mutation.",
              "reviewed_source_rules_identical_across_exports": True, "datasets": results}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--legacy-root", type=Path, default=Path(__file__).resolve().parents[1] / "legacy-data")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / ".artifacts/phase1/reconciliation.json")
    args = parser.parse_args()
    try:
        result = analyze(args.legacy_root, args.output)
        print(json.dumps(result, ensure_ascii=True, indent=2))
    except (OSError, ValueError, KeyError) as error:
        print(f"FAIL: {type(error).__name__}; reconciliation incomplete.")
        raise SystemExit(1) from error
