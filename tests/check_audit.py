import json
import sys

failed = False
for filename in sys.argv[1:]:
    with open(filename, encoding="utf-8-sig") as stream:
        report = json.load(stream)
    if report.get("logs"):
        failed = True
        print(f"{filename}: audit emitted diagnostics; review required")
    for project in report.get("projects", []):
        for framework in project.get("frameworks", []):
            for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
                if package.get("vulnerabilities"):
                    failed = True
                    print(f"Vulnerable dependency: {package['id']}")
sys.exit(1 if failed else 0)
