#!/usr/bin/env python3
"""Fail-closed product-language QA: fixed AI-agent claims are not product features."""
from __future__ import annotations
import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
PRODUCT_ROOTS = ("marketing/public", "EduOS.App/Views", "EduOS.App/wwwroot/js", "EduOS.App/wwwroot/locales", "EduOS.App/Controllers", "EduOS.App/Areas", "EduOS.App/Resources", "EduOS.App/Services", "EduOS.App/Pages")
EXTENSIONS = {".html", ".cshtml", ".js", ".json", ".resx", ".cs", ".ts", ".tsx", ".razor"}
# Distinguish counts of scheduled tasks and non-AI numbers from fixed AI-agent claims.
COUNT = r"(?:[2-9]|[1-9][0-9]+|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)"
AGENT = r"(?:AI[\s-]+)?agents?\b"
# Match worded and numeric agent claims, with optional short qualifiers.
FIXED_AGENT_CLAIM = re.compile(
    rf"(?i)(?<![\w]){COUNT}(?:[\s-]+(?:dedicated|specialized|intelligent|autonomous|powered|virtual|digital|AI|agent))*[\s-]+{AGENT}"
    rf"|\b{AGENT}\s*(?:[:=]|are|of|totaling)\s*{COUNT}\b"
    rf"|\b(?:exactly|only|total|all)\s+{COUNT}(?:\s+(?:dedicated|specialized|AI))?\s+{AGENT}"
)
# Human-readable headlines and product descriptions must not promote computed counts.
DYNAMIC_HEADLINE = re.compile(
    r"(?i)(?:agentCount|agentsCount|totalAgents|agentTotal|capabilities\.length)"
    r"\s*\}?[\s\S]{0,90}\b(?:AI\s+)?agents?\b"
)

def product_files(root: pathlib.Path):
    for folder in PRODUCT_ROOTS:
        base = root / folder
        if not base.is_dir():
            continue
        for path in base.rglob("*"):
            if path.is_file() and path.suffix.lower() in EXTENSIONS and not any(
                p in {"node_modules", "dist", "obj", "bin", "archive", "archives"} for p in path.parts
            ):
                yield path

def violations(root: pathlib.Path):
    results = []
    for path in product_files(root):
        content = path.read_text(encoding="utf-8-sig", errors="replace")
        for number, line in enumerate(content.splitlines(), 1):
            if FIXED_AGENT_CLAIM.search(line) or DYNAMIC_HEADLINE.search(line):
                results.append((path.relative_to(root).as_posix(), number))
    return results

def self_test():
    forbidden = [
        "6 AI Agents", "5 agents", "Exactly 6 AI agents", "Agents: 5",
        "Six AI agents", "5 dedicated AI agents", "six-agent AI platform", "AI agents: five",
        "Total 8 AI agents", "12-agent education platform",
        "agentCount} AI agents", "totalAgents AI agents"
    ]
    allowed = [
        "Five scheduled development tasks", "5 hourly automations",
        "EduOS Intelligent AI Assistant", "AI-powered education ERP",
        "500 students", "6 campuses", "capability registry",
        "agentCount", "AI agents have no fixed count"
    ]
    for s in forbidden:
        assert FIXED_AGENT_CLAIM.search(s) or DYNAMIC_HEADLINE.search(s), s
    for s in allowed:
        assert not FIXED_AGENT_CLAIM.search(s) and not DYNAMIC_HEADLINE.search(s), s
    print(f"PASS: {len(forbidden)} forbidden and {len(allowed)} allowed language fixtures")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    failures = violations(ROOT)
    if failures:
        for path, line in failures:
            print(f"FAIL: fixed AI-agent product claim at {path}:{line}")
        return 1
    print("PASS: no fixed/count-based AI-agent marketing claims in current product UI roots")
    print("NOTE: source-language contract only; does not certify capability registry, runtime or browser")
    return 0

if __name__ == "__main__":
    sys.exit(main())
