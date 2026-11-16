"""Enforce assembly dependency direction before feature implementations arrive."""

from __future__ import annotations

import json
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class FoundationBoundaryTests(unittest.TestCase):
    def test_layer_references_do_not_invert_dependencies(self) -> None:
        allowed = {
            "PetraERP.Domain": set(),
            "PetraERP.Contracts": set(),
            "PetraERP.Application": {"PetraERP.Domain"},
            "PetraERP.Infrastructure": {"PetraERP.Domain", "PetraERP.Application", "PetraERP.Modules.Abstractions", "PetraERP.Modules.Packaging", "PetraERP.Modules.Host"},
            "PetraERP.Modules.Abstractions": set(),
            "PetraERP.Modules.Packaging": {"PetraERP.Modules.Abstractions"},
            "PetraERP.Modules.Host": {"PetraERP.Modules.Abstractions", "PetraERP.Modules.Packaging"},
            # Phase 4 (ADR-0003): the API composes the server layers; the desktop reaches the
            # server only through the generated typed client, never Application/Infrastructure/SQL.
            "PetraERP.Api": {"PetraERP.Domain", "PetraERP.Application", "PetraERP.Infrastructure",
                             "PetraERP.Contracts"},
            "PetraERP.ApiClient": set(),
            "PetraERP.Desktop": {"PetraERP.ApiClient", "PetraERP.Reporting.OpenSource"},
            # ADR-0004: immutable presentation/export component, no access to financial layers.
            "PetraERP.Reporting.OpenSource": set(),
        }
        projects = list((ROOT / "src").rglob("*.csproj"))
        self.assertTrue(projects, "No source projects found.")
        for project in projects:
            with self.subTest(project=project.name):
                self.assertIn(project.stem, allowed, "New layers need an architecture decision.")
                tree = ET.parse(project)
                for reference in tree.findall(".//ProjectReference"):
                    target = (project.parent / reference.attrib["Include"]).resolve()
                    self.assertTrue(target.is_file(), "Project reference does not exist.")
                    self.assertTrue(target.is_relative_to(ROOT / "src"))
                    self.assertIn(target.stem, allowed[project.stem])
                if project.stem in {"PetraERP.Domain", "PetraERP.Contracts", "PetraERP.ApiClient"}:
                    self.assertEqual([], tree.findall(".//PackageReference"),
                                     "Core contracts/domain and the generated client must stay framework independent.")
                if project.stem == "PetraERP.Desktop":
                    packages = {node.attrib["Include"] for node in tree.findall(".//PackageReference")}
                    forbidden = {p for p in packages if p.startswith(("Microsoft.EntityFrameworkCore",
                                                                       "Microsoft.Data.SqlClient",
                                                                       "System.Data.SqlClient"))}
                    self.assertEqual(set(), forbidden, "The desktop client must never access SQL Server.")

    def test_desktop_transitive_packages_and_source_do_not_access_sql(self) -> None:
        lock = json.loads((ROOT / "src/PetraERP.Desktop/packages.lock.json").read_text(encoding="utf-8"))
        for packages in lock["dependencies"].values():
            self.assertFalse(any(name.startswith(("Microsoft.EntityFrameworkCore", "Microsoft.Data.SqlClient", "System.Data.SqlClient"))
                                 for name in packages), "SQL libraries must not enter Desktop transitively.")
        for source in (ROOT / "src/PetraERP.Desktop").rglob("*.cs"):
            if "obj" in source.parts or "bin" in source.parts:
                continue
            text = source.read_text(encoding="utf-8-sig")
            self.assertNotIn("using Microsoft.EntityFrameworkCore", text)
            self.assertNotIn("using Microsoft.Data.SqlClient", text)
            self.assertNotIn("using System.Data.SqlClient", text)

    def test_solution_references_existing_source_projects_once(self) -> None:
        solution = ET.parse(ROOT / "PetraERP.slnx")
        paths = [node.attrib["Path"] for node in solution.findall(".//Project")]
        self.assertEqual(len(paths), len(set(paths)))
        expected = {p.resolve() for folder in ("src", "tests")
                    for p in (ROOT / folder).rglob("*.csproj")}
        self.assertEqual(expected,
                         {(ROOT / path).resolve() for path in paths})


if __name__ == "__main__":
    unittest.main()
