"""Contract tests for the exporter's lossless nullable CSV format."""

from __future__ import annotations

import gzip
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from legacy_csv import ExportFormatError, iter_export_rows, read_export  # noqa: E402


class LegacyCsvTests(unittest.TestCase):
    def test_null_empty_arabic_and_escaped_values_remain_distinct(self) -> None:
        rows = list(iter_export_rows(io.StringIO('"a","b","c","d"\r\n,"","شركة البتراء","قال ""نعم"", صحيح"\r\n')))
        self.assertEqual([{"a": None, "b": "", "c": "شركة البتراء", "d": 'قال "نعم", صحيح'}], rows)

    def test_multiline_and_trailing_null_are_preserved(self) -> None:
        text = '"note","empty","null"\n"سطر أول\r\nسطر ثان","",\n'
        self.assertEqual("سطر أول\r\nسطر ثان", list(iter_export_rows(io.StringIO(text)))[0]["note"])
        self.assertIsNone(list(iter_export_rows(io.StringIO(text)))[0]["null"])

    def test_last_record_does_not_require_newline(self) -> None:
        self.assertEqual([{"id": "1"}], list(iter_export_rows(io.StringIO('"id"\n"1"'))))

    def test_empty_data_with_header_is_valid(self) -> None:
        self.assertEqual([], list(iter_export_rows(io.StringIO('"id","name"\n'))))

    def test_bad_format_is_rejected(self) -> None:
        for text in ('', '"id","id"\n', '"id"\n"x","y"\n', '"id"\n1\n',
                     '"id"\n"unterminated', '"id"\n"ok"oops\n', '"id"\n\n'):
            with self.subTest(text=text), self.assertRaises(ExportFormatError):
                list(iter_export_rows(io.StringIO(text)))

    def test_gzip_bom_is_removed_and_bad_unicode_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "data.csv.gz"
            with gzip.open(path, "wb") as stream:
                stream.write('"id","name"\n"1","حساب"\n'.encode("utf-8-sig"))
            self.assertEqual("حساب", list(read_export(path))[0]["name"])
            with gzip.open(path, "wb") as stream:
                stream.write(b'"id"\n"\xff"\n')
            with self.assertRaises(UnicodeDecodeError):
                list(read_export(path))


if __name__ == "__main__":
    unittest.main()
