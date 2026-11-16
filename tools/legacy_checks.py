"""Evidence diagnostics, not an implementation of PetraERP's accounting engine."""

from __future__ import annotations

from collections import Counter, defaultdict
from decimal import Decimal, InvalidOperation, localcontext
from typing import TypeAlias

Row: TypeAlias = dict[str, str | None]
ZERO_GUID = "00000000-0000-0000-0000-000000000000"


def identity(value: str | None) -> str | None:
    return value.casefold() if value else value


def missing(value: str | None) -> bool:
    return value in (None, "", ZERO_GUID)


def number(row: Row, column: str) -> Decimal:
    value = row.get(column)
    if value is None or value == "":
        raise ValueError("Required diagnostic amount is NULL or empty.")
    try:
        result = Decimal(value)
    except InvalidOperation as error:
        raise ValueError("Diagnostic amount is not a decimal.") from error
    if not result.is_finite():
        raise ValueError("Diagnostic amount is not finite.")
    if len(result.as_tuple().digits) > 60 or abs(result.as_tuple().exponent) > 60:
        raise ValueError("Diagnostic decimal exceeds the supported exact-analysis bounds.")
    return result


def reference_check(rows: list[Row], column: str, parents: list[Row]) -> dict[str, int]:
    ids = {identity(row["GUID"]) for row in parents if not missing(row.get("GUID"))}
    counts: Counter[str] = Counter({"rows": len(rows), "null": 0, "empty": 0,
                                  "zero_guid": 0, "orphan_nonzero": 0})
    for row in rows:
        value = identity(row.get(column))
        if value is None:
            counts["null"] += 1
        elif value == "":
            counts["empty"] += 1
        elif value == ZERO_GUID:
            counts["zero_guid"] += 1
        elif value not in ids:
            counts["orphan_nonzero"] += 1
    return dict(counts)


def duplicate_numbers(rows: list[Row], scope: tuple[str, ...]) -> dict[str, int]:
    numbers: Counter[tuple[str | None, ...]] = Counter()
    absent = 0
    for row in rows:
        if row.get("Number") in (None, ""):
            absent += 1
        else:
            numbers[tuple(row.get(column) for column in scope + ("Number",))] += 1
    return {"duplicate_groups": sum(count > 1 for count in numbers.values()),
            "extra_rows": sum(max(0, count - 1) for count in numbers.values()),
            "missing_numbers": absent}


def journal_diagnostics(headers: list[Row], lines: list[Row]) -> dict[str, object]:
    with localcontext() as context:
        # Inputs are bounded to 60 digits/exponent by number(); 200-digit sums
        # cover both extremes plus the bounded export row count without float.
        context.prec = 200
        balances: dict[str | None, Decimal] = defaultdict(Decimal)
        debits: dict[str | None, Decimal] = defaultdict(Decimal)
        credits: dict[str | None, Decimal] = defaultdict(Decimal)
        currencies: dict[str | None, set[str | None]] = defaultdict(set)
        both_sides = 0
        negative_sides = 0
        for line in lines:
            parent = identity(line.get("ParentGUID"))
            debit, credit = number(line, "Debit"), number(line, "Credit")
            balances[parent] += debit - credit
            debits[parent] += debit
            credits[parent] += credit
            currencies[parent].add(identity(line.get("CurrencyGUID")))
            both_sides += debit > 0 and credit > 0
            negative_sides += debit < 0 or credit < 0
        deltas = [abs(value) for value in balances.values() if value != 0]
        header_mismatches = 0
        for header in headers:
            key = identity(header.get("GUID"))
            header_mismatches += (number(header, "Debit") != debits[key]
                                  or number(header, "Credit") != credits[key])
        return {
            "header_rows": len(headers), "line_rows": len(lines),
            "groups_with_lines": len(balances), "raw_nonzero_deltas": len(deltas),
            "raw_deltas_over_0_0001": sum(d > Decimal("0.0001") for d in deltas),
            "raw_deltas_over_0_01": sum(d > Decimal("0.01") for d in deltas),
            "mixed_currency_groups": sum(len(values) > 1 for values in currencies.values()),
            "headers_without_lines": sum(identity(h.get("GUID")) not in balances for h in headers),
            "header_line_total_mismatches": header_mismatches,
            "headers_debit_credit_differ": sum(number(h, "Debit") != number(h, "Credit") for h in headers),
            "lines_with_both_positive_sides": both_sides,
            "lines_with_negative_side": negative_sides,
            "interpretation": "RAW_DIAGNOSTIC_ONLY: currency denomination and legacy rounding need reconciliation; do not label all deltas invalid posted journals.",
        }


def core_diagnostics(core: dict[str, list[Row]], year: int) -> dict[str, object]:
    relations = (
        ("en000", "ParentGUID", "ce000"), ("en000", "AccountGUID", "ac000"),
        ("bi000", "ParentGUID", "bu000"), ("bi000", "MatGUID", "mt000"),
        ("bu000", "TypeGUID", "bt000"), ("bu000", "CustGUID", "cu000"),
        ("bu000", "CustAccGUID", "ac000"), ("bu000", "StoreGUID", "st000"),
        ("en000", "CostGUID", "co000"), ("en000", "CurrencyGUID", "my000"),
        ("bu000", "CurrencyGUID", "my000"), ("bi000", "CurrencyGUID", "my000"),
        ("ms000", "MatGUID", "mt000"), ("ms000", "StoreGUID", "st000"),
    )
    refs = {f"{child}.{column}->{parent}.GUID": reference_check(core[child], column, core[parent])
            for child, column, parent in relations}
    bill_parents = {identity(row.get("ParentGUID")) for row in core["bi000"]}
    accounts = {identity(row["GUID"]): row for row in core["ac000"]}
    invalid_rates = {}
    for table in ("en000", "ce000", "bu000", "bi000", "my000"):
        invalid_rates[table] = {
            "missing": sum(row.get("CurrencyVal") in (None, "") for row in core[table]),
            "nonpositive": sum(number(row, "CurrencyVal") <= 0 for row in core[table]
                               if row.get("CurrencyVal") not in (None, "")),
        }
    dates = {}
    for table in ("en000", "ce000", "bu000"):
        dates[table] = sum(bool(row.get("Date")) and not row["Date"].startswith(f"{year:04d}-")
                           for row in core[table])
    return {
        "references": refs,
        "journal": journal_diagnostics(core["ce000"], core["en000"]),
        "duplicate_journal_numbers_by_type_branch": duplicate_numbers(core["ce000"], ("TypeGUID", "Type", "Branch")),
        "duplicate_invoice_numbers_by_type_branch": duplicate_numbers(core["bu000"], ("TypeGUID", "Branch")),
        "invoices_without_lines": sum(identity(row.get("GUID")) not in bill_parents for row in core["bu000"]),
        "negative_inventory_snapshot_rows": sum(number(row, "Qty") < 0 for row in core["ms000"]),
        "negative_item_snapshot_rows": sum(number(row, "Qty") < 0 for row in core["mt000"]),
        "negative_invoice_quantities": sum(number(row, "Qty") < 0 for row in core["bi000"]),
        "zero_invoice_quantities": sum(number(row, "Qty") == 0 for row in core["bi000"]),
        "negative_invoice_prices": sum(number(row, "Price") < 0 for row in core["bi000"]),
        "zero_invoice_prices": sum(number(row, "Price") == 0 for row in core["bi000"]),
        "zero_qty_nonzero_discount_extra_or_vat": sum(number(row, "Qty") == 0 and any(
            number(row, column) != 0 for column in ("Discount", "Extra", "VAT")) for row in core["bi000"]),
        "rates": invalid_rates,
        "business_dates_outside_named_calendar_year": dates,
        "cost_flag_1_lines_missing_cost_candidate": sum(
            accounts.get(identity(row.get("AccountGUID")), {}).get("ForceCostCenter") == "1"
            and missing(row.get("CostGUID")) for row in core["en000"]),
        "account_cost_flag_distribution": dict(Counter(row.get("ForceCostCenter") for row in core["ac000"])),
        "manual_edit_assessment": "UNKNOWN: free-text logs cannot establish who changed posted financial records or whether changes were authorized.",
    }
