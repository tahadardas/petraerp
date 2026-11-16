"""Read-only source inventory. No application, compiler, SQL or test execution.

This is a conservative lexical index, not a C#/XAML semantic compiler. Explicit
method edges and unresolved bindings are retained separately; never interpret a
file-level candidate as an executed or proven user workflow.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import defaultdict
from pathlib import Path
from typing import Any

STATUS = "UNVERIFIED UNTIL FINAL TESTS"
EXCLUDED = {"bin", "obj", "Generated"}


def mask(text: str) -> str:
    # Preserve offsets/newlines while hiding comments and C# strings/chars.
    pattern = r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    return re.sub(pattern, lambda m: re.sub(r"[^\n]", " ", m[0]), text)


def close_at(code: str, start: int, left: str = "(", right: str = ")") -> int:
    depth = 0
    for index in range(start, len(code)):
        if code[index] == left:
            depth += 1
        elif code[index] == right:
            depth -= 1
            if depth == 0:
                return index
    raise ValueError(f"Unclosed {left} at {start}")


def line_at(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def reference(path: str, line: int, label: str | None = None) -> str:
    return f"[{label or Path(path).name + ':' + str(line)}](../../{path}#L{line})"


def cell(value: Any) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def table(headers: list[str], rows: list[list[Any]]) -> str:
    return "\n".join(["| " + " | ".join(headers) + " |", "| " + " | ".join("---" for _ in headers) + " |"]
                     + ["| " + " | ".join(cell(v) for v in row) + " |" for row in rows]) + "\n"


def inventory(root: Path) -> dict[str, Any]:
    sources: dict[str, str] = {}
    for path in sorted((root / "src").rglob("*")):
        if path.suffix in {".cs", ".xaml", ".csproj"} and not EXCLUDED.intersection(path.parts):
            sources[path.relative_to(root).as_posix()] = path.read_text(encoding="utf-8-sig")
    types: list[dict[str, Any]] = []
    methods: list[dict[str, Any]] = []
    commands: list[dict[str, Any]] = []
    variables: dict[str, dict[str, set[str]]] = defaultdict(lambda: defaultdict(set))
    type_parts: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for path, text in sources.items():
        if not path.endswith(".cs"):
            continue
        code = mask(text)
        spans: list[tuple[int, int, str]] = []
        for match in re.finditer(r"\b(class|interface|enum|struct|record(?:\s+(?:class|struct))?)\s+(\w+)", code):
            tail_end = code.find("{", match.end())
            semi = code.find(";", match.end())
            end = close_at(code, tail_end, "{", "}") if tail_end >= 0 and (semi < 0 or tail_end < semi) else semi
            if end < 0:
                continue
            header = text[match.start():tail_end if tail_end >= 0 and tail_end < end else end]
            item = {"name": match[2], "kind": match[1], "path": path, "line": line_at(text, match.start()),
                    "header": header.strip(), "start": match.start(), "end": end}
            if "/PetraERP.Contracts/" in path:
                item["contract_source"] = text[match.start():end + 1]
            types.append(item)
            type_parts[match[2]].append(item)
            spans.append((match.start(), end, match[2]))

        def owner(position: int) -> str | None:
            candidates = [span for span in spans if span[0] <= position <= span[1]]
            return min(candidates, key=lambda s: s[1] - s[0])[2] if candidates else None

        decl = r"(?m)^\s*(?:(?:public|private|protected|internal|static|sealed|override|virtual|async|partial|new)\s+)+[^\n=;{}]*?\b(\w+)(?:<[^\n()]+>)?\s*\("
        for match in re.finditer(decl, code):
            cls = owner(match.start())
            if not cls or re.search(r"\b(class|interface|record|struct)\s", match[0]):
                continue
            opening = code.find("(", match.start(), match.end())
            closing = close_at(code, opening)
            after = closing + 1
            while after < len(code) and code[after].isspace():
                after += 1
            if code[after:after + 2] == "=>":
                end = code.find(";", after)
            elif code[after:after + 1] == "{":
                end = close_at(code, after, "{", "}")
            else:
                continue
            signature = text[match.start():closing + 1].strip()
            body = text[after:end + 1]
            node = {"owner": cls, "name": match[1], "path": path, "line": line_at(text, match.start()),
                    "signature": signature, "body": body}
            methods.append(node)
            preceding = text[max(0, match.start() - 200):match.start()]
            if re.search(r"\[RelayCommand(?:\([^\]]*\))?\]\s*$", preceding):
                name = match[1].removesuffix("Async") + "Command"
                commands.append(dict(node, name=name, generated_from=match[1], construction="[RelayCommand]"))
        command_pattern = r"\b(\w+Command)\s*(?:=\s*|=>\s*\w+\s*\?\?=\s*)new\s*(?:(?:Async)?RelayCommand(?:<[^;\n]+?>)?\s*)?\("
        for match in re.finditer(command_pattern, code):
            cls = owner(match.start())
            if cls is None:
                continue
            opening = code.find("(", match.start(), match.end())
            closing = close_at(code, opening)
            commands.append({"owner": cls, "name": match[1], "path": path, "line": line_at(text, match.start()),
                             "body": text[opening + 1:closing], "construction": text[match.start():closing + 1]})
        for start, end, cls in spans:
            # Receiver type hints are candidates only; aliases/dynamic contexts are not guessed.
            for match in re.finditer(r"\b([A-Z]\w*(?:<[^;{}()\n]+>)?)\??\s+([a-z]\w*)\b", code[start:end]):
                variables[cls][match[2]].add(match[1].split("<")[0])

    permission_text = sources["src/PetraERP.Application/Security/PermissionCatalog.cs"]
    permissions = {m[1]: m[2] for m in re.finditer(r'const string (\w+) = "([^"]+)"', permission_text)}
    scopes = {m[1]: m[2] for m in re.finditer(r"new\((\w+), PermissionScope\.(\w+)\)", permission_text)}
    endpoints: list[dict[str, Any]] = []
    groups: dict[tuple[str, str], str] = {}
    endpoint_files = {p: t for p, t in sources.items() if "/Endpoints/" in p}
    for path, text in endpoint_files.items():
        groups[path, "api"] = "/api/v1"
        # Extension route groups used by other endpoint registration methods.
        for parameter in re.finditer(r"static void (\w+)\(this RouteGroupBuilder (\w+)\)", text):
            for other_path, other_text in endpoint_files.items():
                if other_path == path:
                    continue
                caller = re.search(r"\b(\w+)\." + parameter[1] + r"\(\)", other_text)
                if caller:
                    # Resolve parent declaration from its source, including its root api group.
                    parent_decl = re.search(r'var\s+' + caller[1] + r'\s*=\s*(\w+)\.MapGroup\("([^"]*)"\)', other_text)
                    if parent_decl:
                        base = re.search(r'var\s+' + parent_decl[1] + r'\s*=\s*api.MapGroup\("([^"]*)"\)', other_text)
                        groups[path, parameter[2]] = "/api/v1" + (base[1] if base else "") + parent_decl[2]
        for group in re.finditer(r'var\s+(\w+)\s*=\s*(\w+)\.MapGroup\("([^"]*)"\)', text):
            groups[path, group[1]] = groups.get((path, group[2]), "UNRESOLVED_GROUP") + group[3]
        policy_vars = {m[1]: permissions.get(m[2], m[2]) for m in re.finditer(r"var\s+(\w+)\s*=\s*PetraPolicies.Permission\(PermissionCatalog\.(\w+)\)", text)}
        for match in re.finditer(r'\b(\w+)\.Map(Get|Post|Put|Patch|Delete)\(\s*("[^"]*"|string.Empty)\s*,', text):
            opening = text.find("(", match.start())
            closing = close_at(mask(text), opening)
            argument = text[match.end():closing].strip()
            named_handler = re.fullmatch(r"\w+", argument)
            lambda_handler = re.search(r"=>\s*(\w+)\(", argument)
            handler_name = argument if named_handler else lambda_handler[1] if lambda_handler else "INLINE_LAMBDA_REVIEW"
            finish = text.find(";", closing)
            chain = text[match.start():finish + 1]
            name = re.search(r'WithName\("([^"]+)"\)', chain)
            handler = next((m for m in methods if m["path"] == path and m["name"] == handler_name), None)
            perms = [permissions.get(p, p) for p in re.findall(r"PermissionCatalog\.(\w+)", chain)]
            for var, value in policy_vars.items():
                if re.search(r"RequireAuthorization\([^)]*\b" + var + r"\b", chain):
                    perms.append(value)
            policies = re.findall(r"PetraPolicies\.(\w+)", chain)
            if not perms:
                if "AllowAnonymous" in chain:
                    policies.append("Anonymous")
                elif "RequireAuthorization()" in chain:
                    policies.append("Authenticated")
                else:
                    group_decl = re.search(r"var\s+" + match[1] + r"\s*=[\s\S]*?;", text)
                    policies += re.findall(r"PetraPolicies\.(\w+)", group_decl[0]) if group_decl else []
                    if group_decl:
                        perms += [permissions.get(p, p) for p in re.findall(r"PermissionCatalog\.(\w+)", group_decl[0])]
            request = re.findall(r"Validate<([^>]+)>", chain)
            if handler:
                request += re.findall(r"\b(\w+Request)\s+\w+", handler["signature"])
            response = re.findall(r"\.Produces<(.+?)>\(", chain)
            body = handler["body"] if handler else ""
            services = re.findall(r"\b(I\w+(?:Service|Queries))\s+(\w+)", handler["signature"]) if handler else []
            calls = []
            for service_type, variable in services:
                calls += [f"{service_type}.{method}" for method in re.findall(r"\b" + variable + r"\.(\w+)\(", body)]
            # Explicit handler-level conditional authorization must not be hidden by route metadata.
            conditional = sorted(set(permissions.get(p, p) for p in re.findall(r"PermissionCatalog\.(\w+)", body)) - set(perms))
            endpoints.append({"operation": name[1] if name else "UNNAMED", "verb": match[2].upper(),
                              "route": groups.get((path, match[1]), "UNRESOLVED_GROUP") + (match[3][1:-1] if match[3].startswith('"') else ""),
                              "path": path, "line": line_at(text, match.start()), "handler": handler_name,
                              "request": sorted(set(request)), "response": response or ["No content / see handler"],
                              "permissions": sorted(set(perms)), "policies": sorted(set(policies) - {"Permission"}),
                              "conditional_permissions": conditional, "services": calls, "registration": chain})

    # This source registers five real routes through one parameterized helper.
    link_path = "src/PetraERP.Api/Endpoints/SalesDocumentLinkEndpoints.cs"
    link_text = sources[link_path]
    for match in re.finditer(r'Map\(links, "([^"]+)", "([^"]+)", SalesLinkSource\.(\w+), PermissionCatalog\.(\w+)\)', link_text):
        endpoints.append({"operation": match[2], "verb": "GET", "route": groups[link_path, "links"] + "/" + match[1] + "/{sourceId:guid}",
                          "path": link_path, "line": line_at(link_text, match.start()), "handler": "Map: inline service.GetAsync source=" + match[3],
                          "request": [], "response": ["SalesDocumentLinkPageResponse"],
                          "permissions": sorted({permissions["SalesInvoicesView"], permissions[match[4]]}), "policies": [],
                          "conditional_permissions": [], "services": ["ISalesDocumentLinkService.GetAsync"], "registration": match[0]})

    generated_path = "src/PetraERP.ApiClient/Generated/PetraApiClient.g.cs"
    generated = (root / generated_path).read_text(encoding="utf-8-sig")
    operation_names = {e["operation"] + "Async": e["operation"] for e in endpoints}
    method_index: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for method in methods:
        if "PetraERP.Desktop/" in method["path"]:
            method_index[method["owner"], method["name"]].append(method)
    implementations: dict[str, set[str]] = defaultdict(set)
    bases: dict[str, set[str]] = defaultdict(set)
    for cls, parts in type_parts.items():
        for part in parts:
            inheritance = part["header"].split(":", 1)[-1] if ":" in part["header"] else ""
            for interface in re.findall(r"\bI[A-Z]\w+\b", inheritance):
                implementations[interface].add(cls)
            for base in re.findall(r"\b[A-Z]\w+\b", inheritance):
                if base in type_parts and base != cls:
                    bases[cls].add(base)

    def ancestors(cls: str) -> set[str]:
        found = {cls}
        pending = [cls]
        while pending:
            for base in bases.get(pending.pop(), set()) - found:
                found.add(base)
                pending.append(base)
        return found
    def targets(node: dict[str, Any]) -> list[dict[str, Any]]:
        result = []
        code = mask(node["body"])
        cls = node["owner"]
        for match in re.finditer(r"(?:(\w+)\s*\.)?\b(\w+)\s*\(", code):
            receiver, name = match[1], match[2]
            owners = ancestors(cls) if receiver in {None, "this"} else set(variables[cls].get(receiver, set()))
            for receiver_type in list(owners):
                owners.update(implementations.get(receiver_type, set()))
            for owner_type in owners:
                for target in method_index.get((owner_type, name), []):
                    result.append(target)
        # Delegate method groups, including RunAsync(LoadAsync) and command constructors.
        for name in set(re.findall(r"\b[A-Z]\w*\b", code)):
            for owner_type in ancestors(cls):
                result.extend(method_index.get((owner_type, name), []))
        return result

    def trace(node: dict[str, Any]) -> tuple[list[str], list[dict[str, Any]]]:
        pending = [node]
        visited: set[tuple[str, int, str]] = set()
        operations: set[str] = set()
        evidence = []
        while pending:
            current = pending.pop()
            key = current["path"], current["line"], current["name"]
            if key in visited:
                continue
            visited.add(key)
            evidence.append({k: current[k] for k in ("owner", "name", "path", "line")})
            # Only direct generated-client receivers establish terminal API edges.
            for call in re.findall(r"\b(session\.Api|Session\.Api|Api|authentication|client|api)\.(\w+)\s*\(", mask(current["body"])):
                receiver, name = call
                if name in operation_names and (receiver in {"session.Api", "Session.Api"} or "/Connection/" in current["path"]):
                    operations.add(operation_names[name])
            pending.extend(targets(current))
        return sorted(operations), evidence

    body_index = {(m["path"], m["line"]): m["body"] for m in methods}
    for node in commands:
        node["operations"], node["trace"] = trace(node)
        node["effect"] = ("SERVER_PATH" if node["operations"] else "LOCAL_OR_NAVIGATION_REVIEW")
        method_bodies = [body_index.get((t["path"], t["line"]), "") for t in node["trace"]]
        node["navigation_routes"] = sorted(set(re.findall(r'(?:NavigateAsync|NavigateToAsync)\("([^"]+)"', node["body"] + "\n" + "\n".join(method_bodies))))

    # View owner mappings use explicit templates and a small reviewed map for composed controls.
    views: dict[str, list[str]] = defaultdict(list)
    template = sources["src/PetraERP.Desktop/Shell/DocumentTemplates.xaml"]
    for match in re.finditer(r'<DataTemplate DataType="\{x:Type \w+:(\w+)\}">([\s\S]*?)</DataTemplate>', template):
        for view in re.findall(r"<\w+:(\w+View)\b", match[2]):
            views[view].append(match[1])
    reviewed = {
        "DesktopWindow": ["DesktopViewModel"], "LoginView": ["DesktopViewModel"],
        "HomeView": ["DesktopViewModel"], "ShellOverlays": ["DesktopViewModel"],
        "DocumentWindow": ["DesktopViewModel"], "JournalsView": ["GeneralLedgerViewModel"],
        "JournalDocumentView": ["GeneralLedgerViewModel"], "JournalEditor": ["GeneralLedgerViewModel"],
        "JournalWorkspacePanel": ["GeneralLedgerViewModel"], "UsersView": ["AdministrationViewModel"],
        "RolesView": ["AdministrationViewModel"], "AuditView": ["AdministrationViewModel"],
        "CostCenterHistoryView": ["CostCentersViewModel"], "SalesDocumentLinksView": ["SalesDocumentLinksViewModel"],
        "RelatedDocumentsView": ["RelatedDocumentsViewModel"], "DialogHost": ["DialogRequest"],
    }
    views.update(reviewed)
    nested_owners = {"Palette": "CommandPaletteViewModel", "Workspace": "WorkspaceViewModel",
                     "Dialogs": "ShellDialogService", "Notifications": "NotificationCenter"}
    actions: list[dict[str, Any]] = []
    for path, text in sources.items():
        if not path.endswith(".xaml"):
            continue
        view = Path(path).stem
        for tag in re.finditer(r"<([\w.:]+)\b[^<>]*>", text):
            attrs = dict(re.findall(r'([\w.:]+)="([^"]*)"', tag[0]))
            bindings = [(k, v) for k, v in attrs.items() if k.endswith("Command") or k in {"Click", "PreviewKeyDown", "MouseDoubleClick", "SelectionChanged", "Loaded", "Closing", "PreviewMouseDown"}]
            if attrs.get("Property", "").endswith("Command") and "Value" in attrs:
                bindings.append(("Setter.Command", attrs["Value"]))
            if not bindings and tag[1].split(":")[-1] in {"Button", "MenuItem", "Hyperlink", "ToggleButton", "RepeatButton"}:
                bindings.append(("CONTROL", "container/style/default control behavior — inspect scope"))
            for attribute, binding in bindings:
                command_match = re.search(r"([\w.]+Command)\b", binding)
                owners = list(views.get(view, []))
                command_name = command_match[1].split(".")[-1] if command_match else None
                if command_match:
                    prefix = command_match[1].replace("DataContext.", "").split(".")[0]
                    if prefix in nested_owners:
                        owners = [nested_owners[prefix]]
                effective_owners = set().union(*(ancestors(o) for o in owners)) if owners else set()
                candidates = [c for c in commands if c["name"] == command_name and c["owner"] in effective_owners]
                state = "SOURCE_COMMAND" if candidates else "REVIEW_CONTEXT_OR_CONTROL"
                events = [{"path": m["path"], "line": m["line"], "name": m["name"]} for m in methods
                          if m["path"] == path + ".cs" and m["name"] == binding]
                local_detail = "See command body/trace and reviewed local-effect register."
                if events:
                    local_detail = "Code-behind forwards UI event; inspect linked handler and destination command."
                elif "GridEntry.CopyPreviousCell" in binding:
                    local_detail = "Local routed command: GridEntry copies the previous visible editable draft cell (F4); persisted only by Save."
                elif tag[1] == "MenuItem" and attribute == "CONTROL":
                    local_detail = "Menu header/container: ItemsSource plus Menu.Route style dispatches leaf NavigateCommand; no standalone mutation."
                elif attrs.get("x:Name") == "AuditToggle":
                    local_detail = "Local ToggleButton.IsChecked controls the journal audit panel via ElementName binding."
                elif attrs.get("x:Name") == "PART_Open":
                    local_detail = "LookupPicker template button: control attaches click handler to open the lookup popup."
                actions.append({"path": path, "line": line_at(text, tag.start()), "view": view, "element": tag[1],
                                "label": attrs.get("Content", attrs.get("Header", attrs.get("x:Name", ""))),
                                "attribute": attribute, "binding": binding, "parameter": attrs.get("CommandParameter", ""),
                                "owners": owners, "command": command_name, "state": state,
                                "event_sources": events, "local_detail": local_detail,
                                "definitions": [{k: c[k] for k in ("owner", "name", "path", "line", "effect", "operations")} for c in candidates]})

    for endpoint in endpoints:
        name = endpoint["operation"]
        generated_match = re.search(r"public\s+(?:virtual\s+)?(?:async\s+)?[^\n]+\b" + name + r"Async\(", generated)
        endpoint["typed_client"] = {"path": generated_path, "line": line_at(generated, generated_match.start())} if generated_match else None
        endpoint["desktop_calls"] = [{"path": p, "line": line_at(t, m.start())} for p, t in sources.items()
                                     if "PetraERP.Desktop/" in p for m in re.finditer(r"\b" + name + r"Async\(", t)]
        endpoint["command_paths"] = [{k: c[k] for k in ("owner", "name", "path", "line")} for c in commands if name in c["operations"]]
        endpoint["actions"] = [{"path": a["path"], "line": a["line"], "view": a["view"], "command": a["command"]}
                               for a in actions if any(name in d["operations"] for d in a["definitions"])]
        area = Path(endpoint["path"]).stem
        endpoint["classification"] = ("INFRASTRUCTURE" if area == "PlatformEndpoints" else
                                      "SESSION_CONTEXT" if area in {"AuthenticationEndpoints", "ContextEndpoints"} else
                                      "ADMINISTRATION" if area in {"AdministrationEndpoints", "AuditEndpoints"} else
                                      "USER_WORKFLOW")
        endpoint["parity"] = ("SOURCE_PATH" if endpoint["actions"] else "INTEGRATED_OR_REVIEW" if endpoint["desktop_calls"] else "API_ONLY_GAP")
        endpoint["implementation_sources"] = [{"name": m["owner"] + "." + m["name"], "path": m["path"], "line": m["line"]}
            for call in endpoint["services"] for service_type, method_name in [call.split(".", 1)]
            for m in methods if m["name"] == method_name and m["owner"] in implementations.get(service_type, set())
            and ("/PetraERP.Infrastructure/" in m["path"] or "/PetraERP.Application/" in m["path"])]
        if name in {"PostJournal", "PostJournalReversal"}:
            endpoint["services"] = ["IPostingService.PostAsync (via ExecuteAsync)"]
            endpoint["implementation_sources"] = [{"name": m["owner"] + "." + m["name"], "path": m["path"], "line": m["line"]}
                for m in methods if m["owner"] == "SqlPostingService" and m["name"] == "PostAsync"]
        endpoint["reviewed_state"], endpoint["completion_path"] = reviewed_operation(endpoint)
    project_rows = []
    for path, text in sources.items():
        if path.endswith(".csproj"):
            project_rows.append({"path": path, "references": re.findall(r'<ProjectReference Include="([^"]+)"', text),
                                 "packages": re.findall(r'<PackageReference Include="([^"]+)"', text)})
    return {"status": STATUS, "source_date": "2026-10-20", "method": "lexical_static_source_no_execution",
            "projects": project_rows, "permissions": [{"name": k, "code": v, "scope": scopes.get(k, "REVIEW")} for k, v in permissions.items()],
            "endpoints": endpoints, "types": [{k: v for k, v in t.items() if k not in {"start", "end"}} for t in types],
            "methods": [{k: m[k] for k in ("owner", "name", "signature", "path", "line")} for m in methods],
            "commands": commands, "actions": actions,
            "sources": [{"path": p, "sha256": hashlib.sha256(t.encode("utf-8")).hexdigest(), "lines": t.count("\n") + 1} for p, t in sources.items()],
            "generated_client_sha256": hashlib.sha256(generated.encode("utf-8")).hexdigest()}


def reviewed_operation(endpoint: dict[str, Any]) -> tuple[str, str]:
    name = endpoint["operation"]
    if "/CurrencyEndpoints.cs" in endpoint["path"]:
        return "CURRENCY_SOURCE_UNVERIFIED", "CurrencySettingsViewModel/view via CurrenciesClient; company-scoped reads support document editors; settings/rate mutations require Ledger.Accounts.Manage. Append-only register, locked base currency; final SQL/WPF/contract checks deferred."
    if name in {"CreateUnit", "UpdateUnit", "CreateUnitConversion", "ListUnits", "ListUnitConversions"}:
        return "UNITS_SOURCE_UNVERIFIED", "UnitsClient -> UnitsViewModel -> UnitsView: list/create/classify units, create/read explicit conversions. Inventory.Items.View/Manage. Existing conversion rows are read-only; no implicit density. Final precision/scope/concurrency/WPF checks deferred."
    if name in {"AddItemPrice", "DeleteItemPrice", "QuoteItemPrice"}:
        return "PRICING_SOURCE_UNVERIFIED", "CurrenciesClient pricing seam: ItemCardViewModel/view manage scoped price rows; SalesInvoiceCardViewModel/view consumes server quotes. Explicit manual prices preserved, not an enforced price-override approval policy. Final scope/date/rounding/race checks deferred."
    if "/InventoryDocumentEndpoints.cs" in endpoint["path"]:
        return "P02_SOURCE_IMPLEMENTED_UNVERIFIED", "P02: InventoryOperationsViewModel/view and InventoryDocumentsClient; view right plus kind-specific Transfer/Adjustment Manage enforced in service; final SQL/concurrency/WPF/contract checks deferred."
    missing_editors = {"CreateItemGroup", "UpdateItemGroup", "CreateUnit", "UpdateUnit", "CreateUnitConversion", "CreateWarehouse", "UpdateWarehouse"}
    unsafe_stock = {"CreateStockTransfer", "CreateStockAdjustment", "ApproveStockAdjustment", "PostInternalIssue"}
    shared_cards = {"CreateAccount", "CreateItem", "CreateParty", "UpdateParty", "CreateDocumentPattern", "CreateSalesInvoice", "UpdateSalesInvoice"}
    if name in unsafe_stock:
        return "P02_COMPATIBILITY_FACADE_UNVERIFIED", "P02: legacy transport delegates to the shared document engine; use new /inventory/documents workflow and WPF page. Approval now requires expected version/key/reason; incoming caller UnitCost is not authoritative."
    if name in missing_editors:
        return "UI_GAP", "U02: expose authorized master-data editor using existing operation; complete adapter/VM/validation/refresh; checks in prompt 12."
    if name in {"GetCostCenter", "GetFiscalYear", "SearchAccounts"}:
        return "ALTERNATE_WORKFLOW", "Existing list/tree/year-load workflow covers selection; retain API for other clients. Dedicated detail/search use only when needed."
    if name == "GetDeletedAccountsAudit":
        return "UI_GAP", "U03: authorized deletion-history list with paging, actor/reason and surviving navigation."
    if name == "GenerateNextPatternNumber":
        return "INTERNAL_HELPER_REVIEW", "A09: numbering belongs to posting; evaluate external reservation use/permission without exposing a standalone numbering button."
    if name in shared_cards:
        return "SHARED_CARD_SOURCE", "U01: EntityCardViewModel.SaveCardCommand → concrete SaveDraftAsync → existing client; preserve and exercise inheritance/data binding in prompt 12."
    if name == "ValidateJournalLines":
        return "INTEGRATED_HELPER", "Editor validation helper; no independent screen. Retain server validation at posting."
    if name == "ListUnitConversions":
        return "UI_GAP", "U02: client adapter exists; conversion maintenance and display are not a complete user workflow."
    if "/WorkspaceEndpoints.cs" in endpoint["path"]:
        return "SHELL_INTEGRATION", "U04: shell/palette/preferences integrate these operations; trace property/async hooks and save/version paths in prompt 12."
    if endpoint["classification"] in {"SESSION_CONTEXT", "INFRASTRUCTURE"}:
        return endpoint["classification"], "Existing login/session/shell/status integration; no independent screen per endpoint. Prompt-12 security/reconnect coverage."
    if name in {"DeactivateItem", "ReactivateItem"}:
        return "SOURCE_FIX_UNVERIFIED", "U05 fixed: ItemsView.ToggleActiveCommand now calls dedicated generated API via InventoryClient and reloads. Add regression cases in prompt 12."
    return "SOURCE_PRESENT_UNVERIFIED", "Preserve linked source path; resolve candidate branches/dynamic contexts and exercise action/permission/accounting behavior in prompt 12."


def render(data: dict[str, Any]) -> str:
    endpoints = sorted(data["endpoints"], key=lambda e: (e["path"], e["line"]))
    text = ["# API / UI source parity inventory\n", f"**{STATUS}** — static snapshot 2026-10-20.\n",
            "Generated by `python tools/api_ui_parity_inventory.py`. No builds, tests, app startup, SQL or migration execution. "
            "See [reviewed findings and completion paths](API_UI_PARITY_MATRIX.md) and [sequential rules](../ai/SEQUENTIAL_EXECUTION_RULES.md).\n",
            "## Evidence limits\n",
            "Route registrations, DTO names, explicit policies and source locations are extracted from source, not a running OpenAPI server. "
            "The lexical call index follows same-class methods, delegate method groups and typed receiver candidates to direct generated-client calls. "
            "It over-approximates helper/delegate branches and does not resolve all inherited members, dynamic DataContexts, reflection or navigation. "
            "SOURCE_PATH means an inspectable candidate path exists; it is not proof of correct binding, authorization or execution. "
            "INTEGRATED_OR_REVIEW and REVIEW_CONTEXT_OR_CONTROL must be reviewed, never counted as working or missing automatically. "
            "A command with no extracted API edge is not automatically a valid local action. The reviewed matrix documents local effects. "
            "Handler-level conditional permissions are separate from unconditional route requirements.\n",
            f"Production projects: {len(data['projects'])}; named API operations: {len(endpoints)}; command definitions: {len(data['commands'])}; "
            f"XAML action/control occurrences: {len(data['actions'])}. Full machine evidence and method signatures: [JSON](API_UI_PARITY_INVENTORY.json).\n",
            "## Production projects\n",
            table(["Project", "Project references", "Packages"], [[reference(p["path"], 1), "; ".join(p["references"]), "; ".join(p["packages"])] for p in data["projects"]]),
            "## Endpoint → contract → client → VM → view/action → permission\n"]
    rows = []
    for e in endpoints:
        client = reference(e["typed_client"]["path"], e["typed_client"]["line"], e["operation"] + "Async") if e["typed_client"] else "MISSING typed client"
        roots = e["request"] + e["response"]
        contract_links = [reference(t["path"], t["line"], t["name"]) for t in data["types"] if "/PetraERP.Contracts/" in t["path"] and any(re.search(r"\b" + t["name"] + r"\b", r) for r in roots)]
        dto = "request: " + (", ".join(e["request"]) or "route/query only") + "<br>response: " + ", ".join(e["response"]) + "<br>" + "<br>".join(contract_links)
        vm = "<br>".join(sorted(set(reference(c["path"], c["line"], c["owner"] + "." + c["name"]) for c in e["command_paths"]))) or "No command edge extracted; inspect adapter/load/navigation"
        actions = "<br>".join(sorted(set(reference(a["path"], a["line"], a["view"] + ":" + str(a["command"])) for a in e["actions"]))) or "No XAML action edge extracted"
        perms = "<br>".join(e["permissions"] + e["policies"]) or "REVIEW inherited policy"
        if e["conditional_permissions"]:
            perms += "<br>conditional: " + ", ".join(e["conditional_permissions"])
        adapters = "<br>".join(reference(c["path"], c["line"]) for c in e["desktop_calls"] if "/Connection/" in c["path"]) or "see call trace / no adapter extracted"
        rows.append([reference(e["path"], e["line"], e["operation"]) + f"<br>`{e['verb']} {e['route']}`", dto,
                     ("<br>".join(e["services"]) or "Handler/context/infra; inspect source") + "<br>" + "<br>".join(reference(s["path"], s["line"], s["name"]) for s in e["implementation_sources"]), client + "<br>" + adapters, vm, actions, perms,
                     e["classification"] + "<br>" + e["parity"] + "<br>" + e["reviewed_state"] + "<br>" + e["completion_path"]])
    text.append(table(["Actual endpoint", "Contract", "Application service", "Typed client / adapter", "VM / command candidates", "WPF / user action candidates", "Authorization", "Classification / static state"], rows))
    text += ["## Reverse mapping — every XAML action and command control occurrence\n",
             "Labels and CommandParameter are retained verbatim to identify each occurrence. Container MenuItems and controls with template/style forwarding are included explicitly. "
             "Repeated bindings are not collapsed. SOURCE_COMMAND identifies a definition, not runtime success.\n"]
    rows = []
    for a in data["actions"]:
        defs = "<br>".join(reference(d["path"], d["line"], d["owner"] + "." + d["name"]) for d in a["definitions"])
        defs += "<br>".join(reference(d["path"], d["line"], d["name"]) for d in a["event_sources"])
        ops = sorted(set(op for d in a["definitions"] for op in d["operations"]))
        rows.append([reference(a["path"], a["line"], a["view"] + ":" + a["element"]), a["label"], a["attribute"] + "=" + a["binding"],
                     a["parameter"], defs or "Context/control/code-behind review", ", ".join(ops) or a["local_detail"], a["state"]])
    text.append(table(["WPF occurrence", "Label", "Binding / event", "Parameter", "Command definition", "Server terminal candidates / local", "Static state"], rows))
    text += ["## All command definitions (including commands without an extracted XAML use)\n"]
    text.append(table(["Owner.command", "Source", "Construction / target", "Server terminal candidates", "Static effect"],
                      [[c["owner"] + "." + c["name"], reference(c["path"], c["line"]), c.get("generated_from", c["body"][:260]) + ("<br>routes: " + ", ".join(c["navigation_routes"]) if c["navigation_routes"] else ""), ", ".join(c["operations"]), c["effect"]] for c in data["commands"]]))
    text += ["## Complete public contract type index\n",
             "Records/classes/enums/structs are listed with source declarations; property definitions and validation attributes remain at the linked source. "
             "API request/response roots above and nested DTO types below are distinct from Application and generated-client models.\n"]
    text.append(table(["Type", "Declaration", "Source"], [[t["name"], t["header"], reference(t["path"], t["line"])] for t in data["types"] if "/PetraERP.Contracts/" in t["path"]]))
    text += ["## Services, interfaces and domain types\n"]
    text.append(table(["Layer / type", "Declaration", "Source"], [[t["path"].split("/")[1] + ": " + t["name"], t["header"], reference(t["path"], t["line"])]
                      for t in data["types"] if "/PetraERP.Domain/" in t["path"] or (("/PetraERP.Application/" in t["path"] or "/PetraERP.Infrastructure/" in t["path"]) and ("Service" in t["name"] or t["kind"] == "interface"))]))
    text += ["## Permission catalog\n", table(["Constant", "Code", "Scope"], [[p["name"], p["code"], p["scope"]] for p in data["permissions"]]),
             "## Source coverage\n", "The JSON source list includes hand-authored non-bin/obj C#, XAML and project files in the seven production projects with normalized UTF-8 text SHA-256. The generated client is scanned separately for operation declarations and hashed, not duplicated in the type/method index. "
             "These are source identity aids, not signed receipts or execution evidence. Infrastructure health/OpenAPI routes are reviewed separately in the main matrix.\n"]
    return "\n".join(text)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    data = inventory(root)
    directory = root / "docs" / "implementation"
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "API_UI_PARITY_INVENTORY.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (directory / "API_UI_SOURCE_INVENTORY.md").write_text(render(data), encoding="utf-8")
    matrix = directory / "API_UI_PARITY_MATRIX.md"
    if matrix.exists():
        marker = "<!-- BEGIN GENERATED ENDPOINT MATRIX -->"
        content = matrix.read_text(encoding="utf-8").split(marker, 1)[0].rstrip()
        rendered = render(data)
        endpoint_section = rendered.split("## Endpoint → contract → client → VM → view/action → permission", 1)[1].split("## Reverse mapping", 1)[0]
        matrix.write_text(content + "\n\n" + marker + "\n\n## Complete endpoint matrix\n" + endpoint_section, encoding="utf-8")
    print(json.dumps({"status": STATUS, "projects": len(data["projects"]), "operations": len(data["endpoints"]),
                      "commands": len(data["commands"]), "xaml_occurrences": len(data["actions"]), "source_files": len(data["sources"])}, ensure_ascii=False))


if __name__ == "__main__":
    main()
