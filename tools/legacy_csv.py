"""Strict streaming reader for the supplied UTF-8 nullable CSV export contract."""

from __future__ import annotations

import csv
import gzip
import re
from collections.abc import Iterator
from pathlib import Path
from typing import TextIO

FIELD = re.compile(r'"(?:[^"]|"")*"|[^",\r\n]*')
MAX_RECORD_CHARACTERS = 32 * 1024 * 1024


class ExportFormatError(ValueError):
    """A record violates the supplied export format; never repair it silently."""


class _RecordLines:
    def __init__(self, stream: TextIO) -> None:
        self.stream = stream
        self.parts: list[str] = []
        self.characters = 0

    def __iter__(self) -> _RecordLines:
        return self

    def __next__(self) -> str:
        line = self.stream.readline(MAX_RECORD_CHARACTERS + 1)
        if not line:
            raise StopIteration
        self.characters += len(line)
        if self.characters > MAX_RECORD_CHARACTERS:
            raise ExportFormatError("CSV record exceeds the analysis size limit.")
        self.parts.append(line)
        return line

    def take(self) -> str:
        record = "".join(self.parts)
        self.parts.clear()
        self.characters = 0
        return record


def _quoted_fields(record: str) -> list[bool]:
    flags = []
    position = 0
    while True:
        match = FIELD.match(record, position)
        if match is None:
            raise ExportFormatError("Invalid CSV field syntax.")
        token = match[0]
        quoted = token.startswith('"')
        if token and not quoted:
            raise ExportFormatError("Non-NULL export values must be quoted.")
        flags.append(quoted)
        position = match.end()
        if position == len(record):
            return flags
        if record[position] in "\r\n":
            if record[position:] not in ("\n", "\r\n", "\r"):
                raise ExportFormatError("Unexpected content after CSV record.")
            return flags
        if record[position] != ",":
            raise ExportFormatError("Unexpected content after CSV field.")
        position += 1


def iter_export_rows(stream: TextIO) -> Iterator[dict[str, str | None]]:
    csv.field_size_limit(MAX_RECORD_CHARACTERS)
    source = _RecordLines(stream)
    reader = csv.reader(source, strict=True)
    columns: list[str] | None = None
    try:
        for record in reader:
            quoted = _quoted_fields(source.take())
            if len(record) != len(quoted) or not record:
                raise ExportFormatError("Invalid or blank CSV record.")
            if columns is None:
                if not all(record) or not all(quoted) or len(record) != len(set(record)):
                    raise ExportFormatError("CSV header must contain unique quoted names.")
                columns = record
                continue
            if len(record) != len(columns):
                raise ExportFormatError("CSV record width differs from its header.")
            yield dict(zip(columns, (value if flag else None
                                     for value, flag in zip(record, quoted)), strict=True))
    except csv.Error as error:
        raise ExportFormatError("Malformed CSV record.") from error
    if columns is None:
        raise ExportFormatError("CSV header is missing.")


def read_export(path: Path) -> Iterator[dict[str, str | None]]:
    with gzip.open(path, "rt", encoding="utf-8-sig", errors="strict", newline="") as stream:
        yield from iter_export_rows(stream)
