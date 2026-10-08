#!/usr/bin/env python3
"""Check EduOS.Core for redundant public DTO declarations and empty C# source files."""
from collections import defaultdict
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1] / "EduOS.Core"
NAMESPACE = re.compile(r"\bnamespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*[;{]")
DECLARATION = re.compile(r"(?m)^\s*public\s+(?:(?:abstract|sealed|partial|static|readonly|new)\s+)*(?:class|record(?:\s+class)?|struct|interface|enum)\s+([A-Za-z_]\w*)\b")


def main() -> int:
    if not ROOT.is_dir():
        print(f"ERROR: Core source not found at {ROOT}", file=sys.stderr)
        return 1

    dto_names = defaultdict(list)
    qualified_names = defaultdict(list)
    empty_files = []
    source_files = [p for p in ROOT.rglob("*.cs") if "obj" not in p.parts and "bin" not in p.parts]

    for path in sorted(source_files):
        content = path.read_text(encoding="utf-8-sig")
        relative = path.relative_to(ROOT).as_posix()
        if not content.strip():
            empty_files.append(relative)
            continue

        namespace_match = NAMESPACE.search(content)
        if not namespace_match:
            continue

        namespace = namespace_match.group(1)
        for match in DECLARATION.finditer(content):
            name = match.group(1)
            qualified_names[f"{namespace}.{name}"].append(relative)
            if relative.startswith("DTOs/") and name.endswith("Dto"):
                dto_names[name].append((namespace, relative))

    duplicate_types = {name: paths for name, paths in qualified_names.items() if len(paths) > 1}
    duplicate_dtos = {name: owners for name, owners in dto_names.items() if len(owners) > 1}
    errors = []

    for path in empty_files:
        errors.append(f"Empty Core C# file: {path}")
    for name, paths in sorted(duplicate_types.items()):
        errors.append(f"Duplicate public type {name}: {', '.join(paths)}")
    for name, owners in sorted(duplicate_dtos.items()):
        files = ", ".join(f"{ns} ({path})" for ns, path in owners)
        errors.append(f"Ambiguous public DTO name {name}: {files}")

    for error in errors:
        print("ERROR:", error, file=sys.stderr)

    if errors:
        print(f"EduOS.Core canonical contract audit failed: {len(errors)} issue(s).", file=sys.stderr)
        return 1

    print(f"EduOS.Core canonical contracts OK: {len(source_files)} C# files, "
          f"{len(dto_names)} distinct public DTO names, no empty files or duplicate declarations.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
