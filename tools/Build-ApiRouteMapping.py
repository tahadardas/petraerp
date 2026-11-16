"""Derives docs/closure/API_ROUTE_MAPPING.csv from the committed OpenAPI contract and a static text search of the sources.

Columns added: endpoint file, handler, application service interface -> SQL implementation, desktop client wrapper, desktop consumers.
This is a text-based trace, not a call graph: every cell is "-" when nothing was found, never a guess, and a Confidence column
says which links were found. Run from the repository root: python tools/Build-ApiRouteMapping.py
"""
import csv
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
OUT = ROOT / "docs" / "closure" / "API_ROUTE_MAPPING.csv"


def read(path):
    return path.read_text(encoding="utf-8-sig", errors="ignore")


def files(pattern, base):
    return [p for p in base.rglob(pattern) if "obj" not in p.parts and "bin" not in p.parts]


endpoint_files = {p: read(p) for p in files("*.cs", SRC / "PetraERP.Api")}
infra_files = {p: read(p) for p in files("*.cs", SRC / "PetraERP.Infrastructure")}
desktop_files = {p: read(p) for p in files("*.cs", SRC / "PetraERP.Desktop")}
handwritten_client = {p: read(p) for p in files("*.cs", SRC / "PetraERP.ApiClient") if "Generated" not in p.parts}

# Interface -> implementation, from the DI registration.
registrations = {}
for text in endpoint_files.values():
    for interface, implementation in re.findall(r"AddScoped<\s*([\w.]+)\s*,\s*([\w.]+)\s*>", text):
        registrations[interface.split(".")[-1]] = implementation.split(".")[-1]

# Implementation class -> file (relative).
implementation_file = {}
for path, text in infra_files.items():
    for name in re.findall(r"(?:class|record)\s+(\w+)", text):
        implementation_file.setdefault(name, path.relative_to(SRC).as_posix())

spec = json.loads(read(SRC / "PetraERP.ApiClient" / "openapi" / "petraerp-v1.json"))
rows = []
for route, item in spec["paths"].items():
    for method, op in item.items():
        if method not in ("get", "post", "put", "delete", "patch"):
            continue
        op_id = op.get("operationId", "")
        rows.append((method.upper(), route, op_id, ",".join(op.get("tags", [])), op))

SERVICE = re.compile(r"\b(I[A-Z]\w*(?:Service|Gateway|Resolver|Evaluator|Factory|Store|Cache))\b")


def find_endpoint(op_id):
    needle = f'WithName("{op_id}")'
    for path, text in endpoint_files.items():
        index = text.find(needle)
        if index >= 0:
            return path, text, index
    return None, None, -1


def handler_and_services(text, index):
    window = text[max(0, index - 900):index]
    calls = list(re.finditer(r"Map(?:Get|Post|Put|Delete|Patch)\(", window))
    if not calls:
        return "(inline)", []
    statement = window[calls[-1].end():]
    named = re.match(r"\s*[^,()]+,\s*(\w+)\s*[,)]", statement)
    services = set()
    if named:
        handler = named.group(1)
        for match in re.finditer(r"\b" + re.escape(handler) + r"\s*\(([^)]*)\)", text):
            found = SERVICE.findall(match.group(1))
            if found:
                services.update(found)
                break
        return handler, sorted(services)
    services.update(SERVICE.findall(statement))
    return "(inline)", sorted(services)


def desktop_wrapper(op_id):
    needle = f".{op_id}Async("
    for path, text in desktop_files.items():
        if "Connection" not in path.parts:
            continue
        index = text.find(needle)
        if index < 0:
            continue
        head = text[:index]
        methods = re.findall(r"(?:public|internal)\s+(?:async\s+)?[\w<>?,\s.\[\]]*?\b(\w+Async)\s*\(", head)
        return path.relative_to(SRC).as_posix(), (methods[-1] if methods else None)
    return None, None


def static_tail(route):
    parts = [x for x in route.split("/") if x and not x.startswith("{")]
    return parts[-1] if parts else ""


def handwritten_method(route, method):
    """Hand-written ApiClient extensions call routes by URL text; match on the last static segments and the HTTP verb."""
    tail = static_tail(route)
    if not tail:
        return None
    for path, text in handwritten_client.items():
        for name_match in re.finditer(r"(?:public\s+)?(?:async\s+)?(?:Task|ValueTask)(?:<[^>]*>)?\s+(\w+Async)\s*\(", text):
            body = text[name_match.end():name_match.end() + 1600]
            if tail in body and (f"HttpMethod.{method.title()}" in body or method.lower() in body.lower()):
                return path.relative_to(SRC).as_posix(), name_match.group(1)
    return None


def direct_users(op_id):
    """View models that call the generated client directly (no wrapper in Connection)."""
    needle = f".{op_id}Async("
    return sorted(path.relative_to(SRC).as_posix() for path, text in desktop_files.items() if "Connection" not in path.parts and needle in text)


def consumers(wrapper):
    if not wrapper:
        return []
    names = set()
    needle = f".{wrapper}("
    for path, text in desktop_files.items():
        if "Connection" in path.parts:
            continue
        if needle in text:
            names.add(path.relative_to(SRC).as_posix())
    return sorted(names)


out_rows = []
mapped = 0
for method, route, op_id, tag, op in rows:
    path, text, index = find_endpoint(op_id)
    endpoint = path.relative_to(SRC).as_posix() if path else "-"
    handler, services = handler_and_services(text, index) if path else ("-", [])
    impls = []
    for service in services:
        impl = registrations.get(service)
        impls.append(f"{service}->{impl}({implementation_file.get(impl, '?')})" if impl else f"{service}->(unregistered/self)")
    client_file, wrapper = desktop_wrapper(op_id)
    via = ""
    if not client_file:
        hand = handwritten_method(route, method)
        if hand:
            via = f"hand-written {hand[0]}::{hand[1]} -> "
            client_file, wrapper = desktop_wrapper(hand[1].removesuffix("Async"))
    users = consumers(wrapper)
    if not client_file:
        direct = direct_users(op_id)
        if direct:
            client_file, wrapper, users = "(direct call from view model)", None, direct
    links = sum(bool(x) for x in (path, services, client_file, users))
    confidence = {0: "none", 1: "endpoint only", 2: "partial", 3: "good", 4: "full"}[links]
    if path:
        mapped += 1
    auth = "secured" if op.get("security") else ("anonymous" if "security" in op else "default")
    out_rows.append([method, route, op_id, tag, auth, "|".join(sorted(op.get("responses", {}))), endpoint, handler,
                     "; ".join(impls) or "-", (f"{via}{client_file}::{wrapper}" if wrapper else f"{via}{client_file}") if client_file else (f"{via.rstrip(' ->')}" if via else "-"), "; ".join(users) or "-", confidence])

OUT.parent.mkdir(parents=True, exist_ok=True)
with OUT.open("w", encoding="utf-8-sig", newline="") as stream:
    writer = csv.writer(stream)
    writer.writerow(["Method", "Route", "OperationId", "Tag", "Auth", "Responses", "EndpointFile", "Handler",
                     "ServiceInterface->SqlImplementation", "DesktopClientWrapper", "DesktopConsumers", "Confidence"])
    writer.writerows(out_rows)

from collections import Counter
print(len(out_rows), "operations;", mapped, "with an endpoint file;", dict(Counter(r[-1] for r in out_rows)))
sys.exit(0)
