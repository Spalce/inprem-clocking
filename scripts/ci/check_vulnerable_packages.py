#!/usr/bin/env python3
"""Fail the build on high or critical NuGet advisories that are not already known.

Runs `dotnet list <solution> package --vulnerable --include-transitive --format json` and
compares every High/Critical advisory with scripts/ci/known-vulnerable-packages.txt. That
file lists the advisories the approved app (commit 1061ea8) already had. G0 may not change
the app's packages, so those are reported but tolerated; any other one fails the build.
Remove a line from the known list once its package is upgraded.

Usage: python3 scripts/ci/check_vulnerable_packages.py [path/to/solution.sln]
"""
import json
import pathlib
import subprocess
import sys

BLOCKING = {"high", "critical"}
KNOWN_FILE = pathlib.Path(__file__).with_name("known-vulnerable-packages.txt")


def load_known():
    known = set()
    for raw in KNOWN_FILE.read_text(encoding="utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if line:
            package, advisory = line.split()
            known.add((package.lower(), advisory.upper()))
    return known


def list_advisories(solution):
    result = subprocess.run(
        ["dotnet", "list", solution, "package", "--vulnerable", "--include-transitive", "--format", "json"],
        capture_output=True, text=True, check=False,
    )
    if result.returncode != 0:
        print(result.stdout, result.stderr, sep="\n")
        sys.exit("dotnet list package failed")
    report = json.loads(result.stdout)
    found = {}
    for project in report.get("projects", []):
        name = pathlib.Path(project["path"]).stem
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []):
                    for vuln in package.get("vulnerabilities", []):
                        if vuln["severity"].lower() not in BLOCKING:
                            continue
                        advisory = vuln["advisoryurl"].rstrip("/").rsplit("/", 1)[-1].upper()
                        key = (package["id"].lower(), advisory)
                        entry = found.setdefault(key, {
                            "package": package["id"], "version": package["resolvedVersion"],
                            "severity": vuln["severity"], "advisory": advisory, "projects": set(),
                        })
                        entry["projects"].add(name)
    return found


def main():
    solution = sys.argv[1] if len(sys.argv) > 1 else "InpremClockingApp.sln"
    known = load_known()
    found = list_advisories(solution)

    new = [e for k, e in sorted(found.items()) if k not in known]
    tolerated = [e for k, e in sorted(found.items()) if k in known]
    resolved = sorted(known - set(found))

    def show(entries):
        for e in entries:
            print(f"  {e['severity']:<8} {e['package']} {e['version']}  {e['advisory']}  ({', '.join(sorted(e['projects']))})")

    if tolerated:
        print(f"Known high/critical advisories in the approved app ({len(tolerated)}), tolerated until upgraded:")
        show(tolerated)
    if resolved:
        print("Known advisories no longer present (remove from known-vulnerable-packages.txt):")
        for package, advisory in resolved:
            print(f"  {package} {advisory}")
    if new:
        print(f"NEW high/critical advisories ({len(new)}):")
        show(new)
        sys.exit(1)
    print("No new high/critical advisories.")


if __name__ == "__main__":
    main()
