"""Offline table profiling. No SQL execution, source changes or customer-row output."""

from __future__ import annotations

import csv
import gzip
import hashlib
import re
from collections import Counter, defaultdict
from datetime import datetime
from decimal import Decimal, InvalidOperation
from pathlib import Path

from legacy_csv import read_export
from legacy_integrity import csv_rows, safe_file, sha256

MEASURES = re.compile(r"^(?:Debit|Credit|InitDebit|InitCredit|Qty[23]?|Price|CurrencyVal|"
                      r"Total|TotalDisc|TotalExtra|Discount|BonusQnt|BonusDisc|Extra|"
                      r"Unit[23]Fact|PartPrecision|AvgPrice|LastPrice|PurchaseVal|VAT|VATRatio)$", re.I)
DATE = re.compile(r"^(?:Date|CDate|CreateDate|PostDate|CheckDate|ExpireDate|ProductionDate)$", re.I)
MOJIBAKE = re.compile(r"(?:[ØÙ][\x80-\xBF]){2,}")
CORE = {"ac000", "bu000", "bi000", "ce000", "en000", "cu000", "mt000", "bt000",
        "co000", "st000", "my000", "ms000"}


def key_groups(rows: list[dict[str, str]], field: str) -> dict[str, list[list[str]]]:
    groups: dict[tuple[str, str], list[tuple[int, str]]] = defaultdict(list)
    for row in rows:
        if row["IsPrimaryKey"] == "1":
            groups[(f"{row['SchemaName']}.{row['TableName']}", row[field])].append(
                (int(row["KeyOrdinal"]), row["ColumnName"]))
    result: dict[str, list[list[str]]] = defaultdict(list)
    for (table, _), columns in groups.items():
        result[table].append([name for _, name in sorted(columns)])
    return dict(result)


def profile_table(path: Path, primary_keys: list[list[str]]) -> tuple[dict[str, object], list[dict[str, str | None]]]:
    with gzip.open(path, "rt", encoding="utf-8-sig", errors="strict", newline="") as stream:
        columns = next(csv.reader(stream, strict=True))
    for key in primary_keys:
        if not set(key) <= set(columns):
            raise ValueError("Declared primary-key columns are absent from the CSV header.")
    nulls: Counter[str] = Counter()
    empty: Counter[str] = Counter()
    unicode_counts: Counter[str] = Counter()
    numerics: dict[str, dict[str, object]] = {}
    dates: dict[str, dict[str, object]] = {}
    for column in columns:
        if MEASURES.fullmatch(column):
            numerics[column] = {"values": 0, "invalid": 0, "negative": 0, "zero": 0,
                                "min": None, "max": None, "max_exported_scale": 0}
        if DATE.fullmatch(column):
            dates[column] = {"values": 0, "invalid": 0, "min": None, "max": None}
    key_sets: list[set[tuple[str | None, ...]]] = [set() for _ in primary_keys]
    duplicate_keys = [0] * len(primary_keys)
    null_keys = [0] * len(primary_keys)
    fingerprints: set[bytes] = set()
    row_duplicates = 0
    rows = 0
    retained = []
    for row in read_export(path):
        rows += 1
        # repr(tuple) preserves None/empty and field boundaries before SHA256.
        fingerprint = hashlib.sha256(repr(tuple(row.values())).encode("utf-8")).digest()
        row_duplicates += fingerprint in fingerprints
        fingerprints.add(fingerprint)
        for column, value in row.items():
            if value is None:
                nulls[column] += 1
                continue
            if value == "":
                empty[column] += 1
                continue
            unicode_counts["replacement_character_fields"] += "\ufffd" in value
            unicode_counts["mojibake_suspect_fields"] += bool(MOJIBAKE.search(value))
            if column in numerics:
                stat = numerics[column]
                try:
                    number = Decimal(value)
                    if not number.is_finite():
                        raise InvalidOperation
                    stat["values"] += 1
                    stat["negative"] += number < 0
                    stat["zero"] += number == 0
                    stat["min"] = number if stat["min"] is None else min(stat["min"], number)
                    stat["max"] = number if stat["max"] is None else max(stat["max"], number)
                    stat["max_exported_scale"] = max(stat["max_exported_scale"], -number.as_tuple().exponent)
                except InvalidOperation:
                    stat["invalid"] += 1
            if column in dates:
                stat = dates[column]
                try:
                    date = datetime.fromisoformat(value).date().isoformat()
                    stat["values"] += 1
                    stat["min"] = date if stat["min"] is None else min(stat["min"], date)
                    stat["max"] = date if stat["max"] is None else max(stat["max"], date)
                except ValueError:
                    stat["invalid"] += 1
        for index, key in enumerate(primary_keys):
            identity = tuple(row[column].casefold() if row[column] and "guid" in column.lower()
                             else row[column] for column in key)
            null_keys[index] += any(value is None for value in identity)
            duplicate_keys[index] += identity in key_sets[index]
            key_sets[index].add(identity)
        if path.name[5:-7] in CORE:
            retained.append(row)
    return {
        "rows": rows, "columns": columns, "column_types": "UNKNOWN: column-definition export absent",
        "null_counts": dict(nulls), "empty_string_counts": dict(empty),
        "row_fingerprint_duplicates": row_duplicates,
        "primary_keys": [{"columns": key, "duplicate_rows": duplicate_keys[i], "null_rows": null_keys[i]}
                         for i, key in enumerate(primary_keys)],
        "unicode": dict(unicode_counts), "numeric": numerics, "dates": dates,
    }, retained


def log_provenance(dataset: Path, expected: str) -> dict[str, object]:
    raw = safe_file(dataset, "DATABASE_LOG.txt").read_bytes()
    actual = hashlib.sha256(raw).hexdigest().upper()
    if actual == expected:
        return {"status": "MATCH"}
    for newline in re.finditer(b"\n", raw):
        end = newline.end()
        if hashlib.sha256(raw[:end]).hexdigest().upper() == expected:
            suffix = raw[end:]
            return {"status": "APPENDED_SUFFIX", "prefix_bytes": end,
                    "suffix_bytes": len(suffix), "suffix_lines": suffix.count(b"\n"),
                    "suffix_is_completion_log": bool(re.fullmatch(
                        rb"\[\d{4}-\d{2}-\d{2} [\d:]+\] FINISHED database export\. Tables=\d+, OK=\d+, COUNT_MISMATCH=\d+, ERROR=\d+\r?\n", suffix))}
    return {"status": "UNEXPLAINED_MISMATCH"}


def profile_dataset(dataset: Path) -> tuple[dict[str, object], dict[str, list[dict[str, str | None]]]]:
    if dataset.is_symlink() or dataset.is_junction():
        raise ValueError("Linked datasets are not accepted.")
    dataset = dataset.resolve(strict=True)
    keys = csv_rows(safe_file(dataset, "SCHEMA/05_primary_unique_keys.csv"))
    primary = key_groups(keys, "ConstraintName")
    exports = csv_rows(safe_file(dataset, "META/TABLE_EXPORT_MANIFEST.csv"))
    hashes = {}
    for line in safe_file(dataset, "META/ALL_FILES_SHA256.txt").read_text(encoding="utf-8-sig").splitlines():
        digest, relative = line.split("  ", 1)
        path = safe_file(dataset, relative)
        if path in hashes or re.fullmatch(r"[0-9A-Fa-f]{64}", digest) is None:
            raise ValueError("Invalid or duplicate checksum manifest entry.")
        hashes[path] = digest.upper()
    checksum = safe_file(dataset, "META/ALL_FILES_SHA256.txt")
    actual = {safe_file(dataset, str(path.relative_to(dataset)))
              for path in dataset.rglob("*") if path.is_file()}
    if actual != set(hashes) | {checksum}:
        raise ValueError("Checksum coverage differs from the dataset inventory.")
    # Inspect metadata only after checking its publisher-supplied digest.
    metadata_mismatches = [str(path.relative_to(dataset)) for path, digest in hashes.items()
                           if path.parent.name != "DATA" and path.name != "DATABASE_LOG.txt"
                           and sha256(path) != digest]
    if metadata_mismatches:
        raise ValueError("Schema/meta integrity mismatch; analysis stopped.")
    tables = {}
    retained = {}
    for exported in exports:
        path = safe_file(dataset, f"DATA/{exported['DataFile']}")
        if sha256(path) != exported["SHA256"].upper() or hashes.get(path) != exported["SHA256"].upper():
            raise ValueError("Data integrity mismatch; analysis stopped.")
        table_name = f"{exported['SchemaName']}.{exported['TableName']}"
        if table_name in tables:
            raise ValueError("Duplicate table export metadata.")
        profile, data = profile_table(path, primary.get(table_name, []))
        if sha256(path) != exported["SHA256"].upper():
            raise ValueError("Source data changed during profiling; analysis stopped.")
        profile["exported_rows"] = int(exported["ExportedRowCount"])
        profile["count_match"] = (profile["rows"] == profile["exported_rows"]
                                  == int(exported["PreExactRowCount"])
                                  == int(exported["PostExactRowCount"]))
        profile["export_status"] = exported["Status"]
        tables[table_name] = profile
        if exported["TableName"] in CORE:
            retained[exported["TableName"]] = data
    inventory = {}
    for path in sorted((dataset / "SCHEMA").glob("*.csv")):
        rows = csv_rows(path)
        inventory[path.name] = {"rows": len(rows)}
    objects = csv_rows(safe_file(dataset, "SCHEMA/10_objects.csv"))
    return {
        "dataset": dataset.name, "tables": tables, "schema_inventory": inventory,
        "object_type_counts": dict(Counter(row["ObjectType"] for row in objects)),
        "source_manifest_sha256": sha256(safe_file(dataset, "META/ALL_FILES_SHA256.txt")),
        "source_acceptance": "NOT_ACCEPTED: original log hash mismatch is not waived",
        "log_provenance": log_provenance(dataset, hashes[safe_file(dataset, "DATABASE_LOG.txt")]),
        "column_definition_export_present": (dataset / "SCHEMA/04_columns.csv").exists(),
        "unique_constraint_groups": len({(r["SchemaName"], r["TableName"], r["ConstraintName"]) for r in keys}),
        "declared_foreign_keys": csv_rows(safe_file(dataset, "SCHEMA/06_foreign_keys.csv")),
    }, retained
