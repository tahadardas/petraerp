from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from legacy_reconciliation import account_rollup, inventory_rollup, warning_codes  # noqa: E402


class ReconciliationTests(unittest.TestCase):
    def test_posted_lines_propagate_exactly_to_parent_accounts(self) -> None:
        accounts = [{"GUID": "root", "ParentGUID": None, "Debit": "0.3", "Credit": "0"},
                    {"GUID": "leaf", "ParentGUID": "root", "Debit": "0.3", "Credit": "0"}]
        headers = [{"GUID": "posted", "IsPosted": "1"}, {"GUID": "draft", "IsPosted": "0"}]
        lines = [{"ParentGUID": "posted", "AccountGUID": "leaf", "Debit": "0.1", "Credit": "0"},
                 {"ParentGUID": "posted", "AccountGUID": "leaf", "Debit": "0.2", "Credit": "0"},
                 {"ParentGUID": "draft", "AccountGUID": "leaf", "Debit": "9", "Credit": "0"}]
        result = account_rollup(accounts, headers, lines)
        self.assertEqual(0, result["debit_cache"]["exact_mismatches"])
        accounts[0]["ParentGUID"] = "leaf"
        with self.assertRaisesRegex(ValueError, "cycle"):
            account_rollup(accounts, headers, lines)

    def test_stock_rule_includes_bonus_and_rejects_ambiguous_direction(self) -> None:
        items = [{"GUID": "item", "Qty": "2.5"}]
        types = [{"GUID": "in", "bIsInput": "1", "bIsOutput": "0"},
                 {"GUID": "out", "bIsInput": "0", "bIsOutput": "1"}]
        headers = [{"GUID": "p", "IsPosted": "1", "TypeGUID": "in"},
                   {"GUID": "s", "IsPosted": "1", "TypeGUID": "out"}]
        lines = [{"ParentGUID": "p", "MatGUID": "item", "Qty": "3", "BonusQnt": "0.5"},
                 {"ParentGUID": "s", "MatGUID": "item", "Qty": "1", "BonusQnt": "0"}]
        result = inventory_rollup(items, headers, lines, types)
        self.assertEqual(0, result["item_snapshot_vs_posted_document_movements"]["exact_mismatches"])
        types[0]["bIsOutput"] = "1"
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            inventory_rollup(items, headers, lines, types)

    def test_warning_codes_find_less_than_zero_without_exposing_identifiers(self) -> None:
        rows = [{"c1": "AmnW0062: Product balance is less than zero", "g1": "private-id"},
                {"c1": "AmnW0062: Product balance is less than zero", "g1": "private-id"}]
        result = warning_codes(rows)
        self.assertEqual({"rows": 2, "distinct_nonzero_g1": 1}, result["AmnW0062"])
        self.assertNotIn("private-id", str(result))


if __name__ == "__main__":
    unittest.main()
