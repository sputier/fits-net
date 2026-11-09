"""Prints the section of CHANGELOG.md for a version: the release notes of the GitHub release.

    python tools/changelog_notes.py 0.1.0

Fails when the changelog has no section "## <version>" (or when it is empty), so that a version cannot be released without notes.
"""
import os
import re
import sys

CHANGELOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "CHANGELOG.md")


def notes(text, version):
    lines = text.replace("\r\n", "\n").split("\n")
    heading = re.compile(r"^##\s+\[?" + re.escape(version) + r"\]?(\s|$)")
    start = next((i for i, line in enumerate(lines) if heading.match(line)), None)
    if start is None:
        return None
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
    return "\n".join(lines[start + 1:end]).strip()


def main(argv):
    if len(argv) != 2:
        print("usage: changelog_notes.py <version>", file=sys.stderr)
        return 2
    section = notes(open(CHANGELOG, encoding="utf-8").read(), argv[1])
    if not section:
        print(f"CHANGELOG.md has no notes for version {argv[1]}: add a section '## {argv[1]}'.", file=sys.stderr)
        return 1
    print(section)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
