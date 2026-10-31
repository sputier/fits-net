#!/usr/bin/env python3
"""Checks the Markdown of the documentation (docs/**/*.md).

    pip install markdown-it-py
    python tools/check_docs.py

Exit code 0 when everything is fine, 1 otherwise. Each problem is printed as file:line: message.

What is checked:
  * format: no tab, no trailing space, no blank line twice in a row (outside code), file ends with one newline, UTF-8 without BOM
  * structure: exactly one level-1 heading, first in the file; heading levels never skip one; no empty heading; no duplicate
    heading id in a page
  * code: every fenced block has a language
  * raw HTML: only <a id="..."></a> anchors and comments
  * example markers: each "<!-- example: Name -->" is closed by "<!-- /example -->" and holds one fenced block
  * links: relative links point to an existing file of the documentation, and #fragments to an existing heading or <a id>
"""
from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit

from markdown_it import MarkdownIt

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"

ANCHOR_HTML = re.compile(r'^<a id="[A-Za-z0-9._-]+"></a>$')
COMMENT_HTML = re.compile(r"^<!--.*-->$", re.S)
EXAMPLE_OPEN = re.compile(r"^<!-- example: (\S+) -->$")
EXAMPLE_CLOSE = "<!-- /example -->"
INLINE_ANCHOR = re.compile(r'<a id="([^"]+)"></a>')
INLINE_ANCHOR_OPEN = re.compile(r'^<a id="([A-Za-z0-9._-]+)">$')


def slugify(text: str) -> str:
    """The heading id produced by Python-Markdown's toc extension (the one MkDocs uses)."""
    from markdown.extensions.toc import slugify as toc_slugify

    return toc_slugify(text, "-")


def heading_text(inline) -> str:
    parts = []
    for child in inline.children or []:
        if child.type in ("text", "code_inline"):
            parts.append(child.content)
    return "".join(parts)


class Page:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.rel = path.relative_to(ROOT).as_posix()
        self.raw = path.read_bytes()
        self.text = self.raw.decode("utf-8")
        self.lines = self.text.split("\n")
        self.tokens = MarkdownIt("commonmark").enable("table").parse(self.text)
        self.ids: set[str] = set()
        self.links: list[tuple[int, str]] = []


def main() -> int:
    try:
        import markdown  # noqa: F401
    except ImportError:
        print("The 'markdown' package is needed for heading ids: pip install markdown", file=sys.stderr)
        return 2

    errors: list[str] = []

    def error(page: Page, line: int, message: str) -> None:
        errors.append(f"{page.rel}:{line}: {message}")

    pages = {p: Page(p) for p in sorted(DOCS.rglob("*.md"))}
    if not pages:
        print("No Markdown file found in docs/", file=sys.stderr)
        return 2

    for page in pages.values():
        check_format(page, error)
        check_structure(page, error)

    for page in pages.values():
        check_links(page, pages, error)

    for message in errors:
        print(message)
    print(f"{len(pages)} pages checked, {len(errors)} problem(s).")
    return 1 if errors else 0


def check_format(page: Page, error) -> None:
    if page.raw.startswith(b"\xef\xbb\xbf"):
        error(page, 1, "the file starts with a byte order mark")
    if b"\r" in page.raw:
        error(page, 1, "the file contains carriage returns (use LF line endings)")
    if not page.text.endswith("\n") or page.text.endswith("\n\n"):
        error(page, len(page.lines), "the file must end with exactly one newline")

    fenced = fenced_lines(page)
    blank_run = 0
    for number, line in enumerate(page.lines, start=1):
        if "\t" in line and number not in fenced:
            error(page, number, "tab character")
        if line != line.rstrip() and number not in fenced:
            error(page, number, "trailing whitespace")
        blank_run = blank_run + 1 if line == "" and number not in fenced else 0
        if blank_run == 2:
            error(page, number, "two blank lines in a row")


def fenced_lines(page: Page) -> set[int]:
    lines: set[int] = set()
    for token in page.tokens:
        if token.type == "fence" and token.map:
            lines.update(range(token.map[0] + 1, token.map[1] + 1))
    return lines


def check_structure(page: Page, error) -> None:
    tokens = page.tokens
    previous_level = 0
    h1_count = 0
    seen_ids: dict[str, int] = {}
    first_block_checked = False

    for index, token in enumerate(tokens):
        line = (token.map[0] + 1) if token.map else 1

        if not first_block_checked and token.type != "heading_open":
            error(page, line, "the page must start with its level-1 heading")
            first_block_checked = True
        elif token.type == "heading_open":
            first_block_checked = True

        if token.type == "heading_open":
            level = int(token.tag[1])
            text = heading_text(tokens[index + 1])
            if not text.strip():
                error(page, line, "empty heading")
            if level == 1:
                h1_count += 1
                if h1_count > 1:
                    error(page, line, "more than one level-1 heading")
            elif previous_level and level > previous_level + 1:
                error(page, line, f"heading level jumps from {previous_level} to {level}")
            previous_level = level
            slug = slugify(text)
            page.ids.add(slug)
            if slug in seen_ids:
                error(page, line, f"duplicate heading id '{slug}' (first at line {seen_ids[slug]})")
            seen_ids.setdefault(slug, line)

        elif token.type == "fence":
            if not token.info.strip():
                error(page, line, "fenced code block without a language")

        elif token.type == "html_block":
            check_html(page, token.content.strip(), line, error)

        elif token.type == "inline":
            children = token.children or []
            for position, child in enumerate(children):
                if child.type == "html_inline":
                    opening = INLINE_ANCHOR_OPEN.match(child.content)
                    closes = position + 1 < len(children) and children[position + 1].content == "</a>"
                    if opening and closes:
                        page.ids.add(opening.group(1))
                    elif child.content == "</a>" and position > 0 and INLINE_ANCHOR_OPEN.match(children[position - 1].content):
                        continue
                    else:
                        error(page, line, f"raw inline HTML is not allowed: {child.content}")
                elif child.type == "link_open":
                    page.links.append((line, child.attrGet("href") or ""))
                elif child.type == "image":
                    page.links.append((line, child.attrGet("src") or ""))

    if h1_count == 0:
        error(page, 1, "no level-1 heading")

    check_examples(page, error)


def check_html(page: Page, html: str, line: int, error) -> None:
    for offset, block_line in enumerate(html.split("\n")):
        stripped = block_line.strip()
        if not stripped:
            continue
        if ANCHOR_HTML.match(stripped):
            page.ids.add(INLINE_ANCHOR.match(stripped).group(1))
        elif COMMENT_HTML.match(stripped):
            continue
        else:
            error(page, line + offset, f"raw HTML is not allowed: {stripped}")


def check_examples(page: Page, error) -> None:
    open_name = None
    open_line = 0
    fences = 0
    for number, line in enumerate(page.lines, start=1):
        match = EXAMPLE_OPEN.match(line)
        if match:
            if open_name:
                error(page, number, f"example '{open_name}' (line {open_line}) is not closed")
            open_name, open_line, fences = match.group(1), number, 0
        elif line == EXAMPLE_CLOSE:
            if not open_name:
                error(page, number, "closing example marker without an opening one")
            elif fences != 1:
                error(page, number, f"example '{open_name}' must hold exactly one code block, found {fences}")
            open_name = None
        elif open_name and line.startswith("```"):
            fences += 1 if line.strip() != "```" else 0
        elif line.startswith("<!-- example") or line.startswith("<!-- /example"):
            error(page, number, "malformed example marker")
    if open_name:
        error(page, open_line, f"example '{open_name}' is not closed")


def check_links(page: Page, pages: dict[Path, Page], error) -> None:
    for line, href in page.links:
        if not href:
            error(page, line, "empty link target")
            continue
        parts = urlsplit(href)
        if parts.scheme in ("http", "https"):
            continue
        if parts.scheme or parts.netloc:
            error(page, line, f"unsupported link '{href}'")
            continue
        if parts.path.startswith("/"):
            error(page, line, f"absolute link '{href}' (use a relative path)")
            continue
        if parts.path:
            target = (page.path.parent / unquote(parts.path)).resolve()
            if target not in pages:
                error(page, line, f"link to a page that does not exist: '{href}'")
                continue
        else:
            target = page.path.resolve()
        if parts.fragment:
            fragment = unquote(parts.fragment)
            if fragment not in pages[target].ids:
                error(page, line, f"link to an anchor that does not exist: '{href}'")


if __name__ == "__main__":
    sys.exit(main())
