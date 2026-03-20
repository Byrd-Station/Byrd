#!/usr/bin/env python3
"""
OmuStation Fork Porter -- unified CLI for porting & integrating fork content.

Replaces the four standalone scripts (dedup_protos.py, extract_missing_blocks.py,
resolve_missing_protos.py, fix_scrap_protos.py) with a single config-driven tool.

Usage:
    python -m Tools.fork_porter <command> [options]

Commands:
    update         Fetch/clone all upstream repos into .fork_porter_cache/
    update-ported  Refresh already-ported fork-scoped files from upstreams
    cherry-pick    Apply specific upstream commits to this repository
    pull           Pull a prototype and all dependencies from upstreams
    dedup          Remove duplicate prototypes across forks (priority-based)
    resolve        Discover & pull missing prototypes + textures from upstreams
    audit-patches  Scan source files for inline fork-edit markers and report
    status         Show fork health dashboard (dupes, missing, patch count)
    clean          Remove .fork_porter_cache/ directory

Global flags:
    --config FILE  Path to config.yml   (default: Tools/fork_porter/config.yml)
    --verbose      Print detailed log output
"""

from __future__ import annotations

import argparse
import logging
import re
import shutil
import subprocess
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

# ruamel.yaml preserves comments and formatting -- far safer than regex.
# Falls back to PyYAML (loses comments) then to a minimal regex loader.
try:
    from ruamel.yaml import YAML

    _YAML_ENGINE = "ruamel"
except ImportError:
    try:
        import yaml  # type: ignore[import-untyped]

        _YAML_ENGINE = "pyyaml"
    except ImportError:
        _YAML_ENGINE = "regex"

log = logging.getLogger("fork_porter")

# ── Utilities ────────────────────────────────────────────────────────────────


def _load_yaml_file(path: Path) -> Any:
    """Load a YAML file with the best available parser."""
    text = path.read_text(encoding="utf-8")
    if _YAML_ENGINE == "ruamel":
        y = YAML()
        y.preserve_quotes = True  # type: ignore[assignment]
        return y.load(text)
    if _YAML_ENGINE == "pyyaml":
        return yaml.safe_load(text)
    raise RuntimeError(
        "No YAML library available. Install ruamel.yaml:  pip install ruamel.yaml"
    )


def _load_config(config_path: Path) -> dict[str, Any]:
    cfg = _load_yaml_file(config_path)
    if cfg is None:
        raise ValueError(f"Empty config file: {config_path}")
    # Resolve workspace path relative to config file location.
    ws = cfg.get("workspace", "../..")
    cfg["_workspace"] = (config_path.parent / ws).resolve()
    cfg["_proto_root"] = cfg["_workspace"] / "Resources" / "Prototypes"
    cfg["_tex_root"] = cfg["_workspace"] / "Resources" / "Textures"
    cfg["_cache_dir"] = cfg["_workspace"] / ".fork_porter_cache"
    return cfg


# ── Upstream repo management ────────────────────────────────────────────────

def _ensure_upstream(name: str, info: dict, cache_dir: Path) -> Path:
    """Ensure an upstream repo is cloned and up-to-date. Returns local path."""
    # Support legacy `path` key for local overrides.
    if "path" in info:
        p = Path(info["path"]).resolve()
        if p.is_dir():
            return p
        raise FileNotFoundError(f"Upstream '{name}' local path not found: {p}")

    repo_url = info.get("repo")
    if not repo_url:
        raise ValueError(f"Upstream '{name}' has neither 'repo' nor 'path' configured.")

    branch = info.get("branch", "main")
    repo_dir = cache_dir / name

    if repo_dir.is_dir():
        # Pull latest (fast -- already a sparse/shallow clone).
        log.info("Updating upstream '%s' (%s)...", name, branch)
        subprocess.run(
            ["git", "fetch", "--depth=1", "origin", branch],
            cwd=repo_dir, check=True, capture_output=True,
        )
        # Checkout paths that exist (some upstreams may lack test dirs).
        for checkout_path in ("Resources/Prototypes", "Resources/Textures",
                              "Content.Tests", "Content.IntegrationTests"):
            subprocess.run(
                ["git", "checkout", f"origin/{branch}", "--", checkout_path],
                cwd=repo_dir, capture_output=True,  # no check -- path may not exist
            )
    else:
        # Shallow clone with sparse checkout -- only grab Resources/.
        log.info("Cloning upstream '%s' from %s (branch: %s)...", name, repo_url, branch)
        cache_dir.mkdir(parents=True, exist_ok=True)
        subprocess.run(
            ["git", "clone", "--depth=1", "--branch", branch,
             "--filter=blob:none", "--sparse", repo_url, str(repo_dir)],
            check=True, capture_output=True,
        )
        subprocess.run(
            ["git", "sparse-checkout", "set",
             "Resources/Prototypes", "Resources/Textures",
             "Content.Tests", "Content.IntegrationTests"],
            cwd=repo_dir, check=True, capture_output=True,
        )

    return repo_dir


def _resolve_upstream_path(name: str, info: dict, cache_dir: Path) -> Path:
    """Get the local path for an upstream, cloning/updating as needed."""
    try:
        return _ensure_upstream(name, info, cache_dir)
    except subprocess.CalledProcessError as e:
        stderr = e.stderr.decode(errors="replace").strip() if e.stderr else ""
        log.error("Git operation failed for upstream '%s': %s", name, stderr)
        raise
    except FileNotFoundError:
        raise


def _classify_fork(filepath: Path, forks: dict[str, dict]) -> str | None:
    """Return the fork key that owns *filepath*, or None for base game."""
    fp = filepath.as_posix()
    for key, info in forks.items():
        d = info.get("directory")
        if d and f"/{d}/" in fp:
            return key
        # Check alt_directories (case variants, non-underscore dirs, etc.)
        for alt in info.get("alt_directories") or []:
            if f"/{alt}/" in fp:
                return key
    return None


# ── Entity index ─────────────────────────────────────────────────────────────

_ENTITY_ID_RE = re.compile(r"^\s*id:\s+(\S+)", re.MULTILINE)
_TYPE_RE = re.compile(r"^[-\s]*type:\s+entity", re.MULTILINE)
_PARENT_BRACKET_RE = re.compile(r"parent:\s*\[([^\]]+)]")
_PARENT_SINGLE_RE = re.compile(r"parent:\s+(\S+)")
_SPRITE_RE = re.compile(r"(?:sprite|rsi):\s+(\S+\.rsi)")


def _iter_entity_ids(text: str) -> list[str]:
    """Yield entity IDs from YAML text, only from `type: entity` blocks.

    Parses block boundaries so that `id:` fields inside non-entity blocks
    (reagents, actions, etc.) are correctly ignored.
    """
    ids: list[str] = []
    in_entity = False
    for line in text.split("\n"):
        # Detect top-level list item start.
        if line.startswith("- "):
            in_entity = bool(re.match(r"^-\s*type:\s+entity", line))
            continue
        if not in_entity:
            continue
        m = re.match(r"^\s+id:\s+(\S+)", line)
        if m:
            ids.append(m.group(1))
    return ids


def _index_prototypes(root: Path) -> dict[str, Path]:
    """Build {entity_id: file_path} for every entity prototype under *root*."""
    index: dict[str, Path] = {}
    for yml in sorted(root.rglob("*.yml")):
        try:
            text = yml.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        # Quick check: skip files that don't define entities.
        if not _TYPE_RE.search(text):
            continue
        for eid in _iter_entity_ids(text):
            if eid not in index:
                index[eid] = yml
    return index


# Matches multi-line YAML list items under a `parent:` key:
#   parent:
#   - Foo
#   - Bar
_PARENT_MULTILINE_RE = re.compile(
    r"^\s*parent:\s*$\n((?:\s+-\s+\S+\n?)+)",
    re.MULTILINE,
)
_PARENT_LIST_ITEM_RE = re.compile(r"^\s+-\s+(\S+)", re.MULTILINE)


def _extract_parents(text: str) -> list[str]:
    """Return all parent entity IDs referenced in *text*.

    Handles three YAML formats:
      parent: [A, B]        -- bracket list
      parent: Single        -- single value
      parent:               -- multi-line YAML list
      - A
      - B
    """
    parents: list[str] = []
    # 1. Bracket lists: parent: [A, B]
    for m in _PARENT_BRACKET_RE.finditer(text):
        parents.extend(p.strip().strip("'\"") for p in m.group(1).split(","))
    # 2. Multi-line YAML lists: parent:\n  - A\n  - B
    multiline_spans: set[tuple[int, int]] = set()
    for m in _PARENT_MULTILINE_RE.finditer(text):
        multiline_spans.add((m.start(), m.end()))
        for item in _PARENT_LIST_ITEM_RE.finditer(m.group(1)):
            parents.append(item.group(1).strip().strip("'\""))
    # 3. Single values: parent: Foo
    for m in _PARENT_SINGLE_RE.finditer(text):
        # Skip if this match falls within a multi-line block we already parsed.
        if any(s <= m.start() < e for s, e in multiline_spans):
            continue
        val = m.group(1).strip().strip("'\"")
        if val.startswith("["):
            continue  # already handled by bracket regex
        if val == "-":
            continue  # stray list marker from multi-line format
        parents.append(val)
    return [p for p in parents if p]


def _extract_sprites(text: str) -> list[str]:
    return [m.group(1) for m in _SPRITE_RE.finditer(text)]


def _extract_component_stems(text: str) -> set[str]:
    """Return component type stems from prototype YAML (e.g. 'Sprite', 'PointLight')."""
    stems: set[str] = set()
    for m in re.finditer(r"^\s+-\s*type:\s+(\w+)", text, re.MULTILINE):
        val = m.group(1)
        if val.lower() != "entity":
            stems.add(val)
    return stems


# ── Upstream full checkout ───────────────────────────────────────────────────


def _ensure_upstream_full(name: str, info: dict, cache_dir: Path) -> Path:
    """Like _ensure_upstream but also checks out Locale and C# source dirs."""
    path = _ensure_upstream(name, info, cache_dir)
    branch = info.get("branch", "main")
    has_locale = (path / "Resources" / "Locale").is_dir()
    has_cs = any((path / d).is_dir() for d in ("Content.Shared", "Content.Server", "Content.Client"))
    if not has_locale or not has_cs:
        log.info("Expanding checkout for '%s' (adding Locale + C# source)...", name)
        # Start with well-known dirs.
        dirs_to_add = ["Resources/Locale", "Content.Shared", "Content.Server", "Content.Client"]
        # Discover fork-specific assemblies (Content.Goobstation.Shared, etc.).
        result = subprocess.run(
            ["git", "ls-tree", "--name-only", f"origin/{branch}"],
            cwd=path, capture_output=True, text=True,
        )
        if result.returncode == 0:
            for d in result.stdout.splitlines():
                if d.startswith("Content.") and d not in dirs_to_add:
                    dirs_to_add.append(d)
        subprocess.run(
            ["git", "sparse-checkout", "add"] + dirs_to_add,
            cwd=path, check=True, capture_output=True,
        )
    return path


_TEST_DIRS = ["Content.Tests", "Content.IntegrationTests"]


def _find_upstream_test_files(
    up_path: Path,
    cs_class_names: set[str],
) -> dict[str, Path]:
    """Find test files in upstream that reference any of the given class names."""
    found: dict[str, Path] = {}  # relative path string -> absolute source
    for tdir in _TEST_DIRS:
        root = up_path / tdir
        if not root.is_dir():
            continue
        for cs in root.rglob("*.cs"):
            try:
                text = cs.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            for cls in cs_class_names:
                if cls in text:
                    rel = cs.relative_to(up_path)
                    found[str(rel)] = cs
                    break
    return found


# ── Local class index ────────────────────────────────────────────────────────

_CLASS_DEF_RE = re.compile(r"\b(?:class|record)\s+(\w+)")


def _index_local_classes(workspace: Path) -> set[str]:
    """Return a set of C# class names defined in the local workspace."""
    classes: set[str] = set()
    # Dynamically discover Content.* assemblies instead of hardcoding.
    search_dirs: list[str] = []
    skip_suffixes = (
        ".Tests", ".IntegrationTests", ".Benchmarks", ".Packaging",
        ".Tools", ".YAMLLinter", ".MapRenderer", ".Replay",
        ".PatreonParser", ".ModuleManager",
    )
    for p in sorted(workspace.iterdir()):
        if p.is_dir() and p.name.startswith("Content.") and not any(
            p.name.endswith(s) for s in skip_suffixes
        ):
            search_dirs.append(p.name)
    # Also include RobustToolbox engine dirs.
    for engine_dir in ("RobustToolbox/Robust.Shared", "RobustToolbox/Robust.Server",
                       "RobustToolbox/Robust.Client"):
        search_dirs.append(engine_dir)
    for d in search_dirs:
        src_dir = workspace / d
        if not src_dir.is_dir():
            continue
        for cs in src_dir.rglob("*.cs"):
            try:
                text = cs.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            for m in _CLASS_DEF_RE.finditer(text):
                classes.add(m.group(1))
    return classes


def _find_upstream_cs_files(
    upstream_path: Path, class_stems: set[str],
) -> dict[str, Path]:
    """Find C# files in upstream defining Component or System classes.

    For stem 'Foo', searches for: FooComponent, FooSystem, SharedFooSystem.
    Returns {class_name: source_file_path}.
    """
    targets: set[str] = set()
    for stem in class_stems:
        targets.add(f"{stem}Component")
        targets.add(f"{stem}System")
        targets.add(f"Shared{stem}System")

    found: dict[str, Path] = {}
    for src_dir in sorted(upstream_path.iterdir()):
        if not src_dir.is_dir() or not src_dir.name.startswith("Content."):
            continue
        for cs in src_dir.rglob("*.cs"):
            try:
                text = cs.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            for m in _CLASS_DEF_RE.finditer(text):
                name = m.group(1)
                if name in targets and name not in found:
                    found[name] = cs
    return found


# ── Locale extraction ────────────────────────────────────────────────────────


def _extract_ftl_entries(text: str, entity_ids: set[str]) -> str:
    """Extract FTL message blocks matching ent-<id> for the given entity IDs."""
    keys = {f"ent-{eid}" for eid in entity_ids}
    lines = text.split("\n")
    result: list[str] = []
    capturing = False

    for line in lines:
        stripped = line.strip()
        # Detect top-level message start (not indented, contains '=').
        if stripped and not stripped.startswith("#") and "=" in line and not line[0:1].isspace():
            key = line.split("=", 1)[0].strip()
            capturing = key in keys
        elif stripped and not line[0:1].isspace() and not stripped.startswith("#"):
            # Non-indented, non-comment, non-key line -- new section.
            capturing = False

        if capturing:
            result.append(line)

    while result and result[-1].strip() == "":
        result.pop()
    return "\n".join(result) + "\n" if result else ""


def _append_unique_block(existing: str, block: str) -> str:
    """Append block text to existing text if the block is not already present."""
    if not block.strip():
        return existing
    normalized_block = block.rstrip("\n") + "\n"
    if normalized_block.strip() in existing:
        return existing
    if not existing:
        return normalized_block
    sep = "" if existing.endswith("\n") else "\n"
    return existing + sep + normalized_block


def _history_auto_commit_enabled(cfg: dict) -> bool:
    return bool(cfg.get("history", {}).get("auto_commit", False))


def _commit_touched_paths(cfg: dict, command_name: str, touched: set[Path]) -> None:
    """Commit touched workspace files for history preservation when enabled."""
    if not _history_auto_commit_enabled(cfg):
        return

    workspace = cfg.get("_workspace")
    if not isinstance(workspace, Path) or not (workspace / ".git").is_dir():
        return

    rel_paths: list[str] = []
    for path in sorted(touched):
        try:
            rel_paths.append(path.relative_to(workspace).as_posix())
        except ValueError:
            continue
    if not rel_paths:
        return

    status = subprocess.run(
        ["git", "status", "--porcelain", "--", *rel_paths],
        cwd=workspace,
        capture_output=True,
        text=True,
        check=False,
    )
    if status.returncode != 0 or not status.stdout.strip():
        return

    try:
        subprocess.run(
            ["git", "add", "-A", "--", *rel_paths],
            cwd=workspace,
            capture_output=True,
            check=True,
        )
        subprocess.run(
            ["git", "commit", "-m", f"fork_porter: {command_name}", "--", *rel_paths],
            cwd=workspace,
            capture_output=True,
            check=True,
        )
        log.info("Committed %d path(s) for history preservation.", len(rel_paths))
    except subprocess.CalledProcessError as e:
        stderr = e.stderr.decode(errors="replace").strip() if isinstance(e.stderr, bytes) else str(e.stderr or "")
        log.warning("Auto-commit skipped for '%s': %s", command_name, stderr or e)


# ── Commands ─────────────────────────────────────────────────────────────────


def cmd_dedup(cfg: dict, dry_run: bool) -> int:
    """Remove duplicate entity IDs across forks based on priority."""
    forks = cfg["forks"]
    proto_root = cfg["_proto_root"]
    delete_empty = cfg.get("dedup", {}).get("delete_empty_files", True)

    # 1. Index every entity ID → [(fork_key, file_path), ...]
    id_locations: dict[str, list[tuple[str | None, Path]]] = defaultdict(list)
    for yml in proto_root.rglob("*.yml"):
        try:
            text = yml.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        if not _TYPE_RE.search(text):
            continue
        fork_key = _classify_fork(yml, forks)
        for eid in _iter_entity_ids(text):
            id_locations[eid].append((fork_key, yml))

    # 2. For each duplicate, decide what to remove.
    removals: dict[Path, set[str]] = defaultdict(set)  # file → {ids to remove}
    dupes_found = 0

    def _prio(fork_key: str | None) -> int:
        if fork_key is None:
            return forks.get("base", {}).get("priority", 100)
        return forks.get(fork_key, {}).get("priority", 0)

    for eid, locs in id_locations.items():
        if len(locs) < 2:
            continue
        dupes_found += 1
        # Find highest priority among locations.
        best_prio = max(_prio(fk) for fk, _ in locs)

        for fk, fpath in locs:
            if _prio(fk) < best_prio:
                removals[fpath].add(eid)
                log.info(
                    "DEDUP %s: remove from %s (fork=%s, prio=%d < %d)",
                    eid, fpath.relative_to(proto_root), fk, _prio(fk), best_prio,
                )

    # 3. Apply removals.
    files_modified = 0
    files_deleted = 0
    touched: set[Path] = set()
    for fpath, ids_to_remove in removals.items():
        try:
            text = fpath.read_text(encoding="utf-8")
        except OSError:
            continue

        new_text = _remove_entity_blocks(text, ids_to_remove)
        if new_text.strip() == "":
            if dry_run:
                log.info("  Would delete empty file %s", fpath.relative_to(proto_root))
            elif delete_empty:
                fpath.unlink()
                files_deleted += 1
                touched.add(fpath)
            else:
                fpath.write_text("", encoding="utf-8")
                files_modified += 1
                touched.add(fpath)
            continue

        if new_text != text:
            if not dry_run:
                fpath.write_text(new_text, encoding="utf-8")
            files_modified += 1
            touched.add(fpath)

    action = "Would modify" if dry_run else "Modified"
    log.info(
        "Dedup complete: %d duplicate IDs found, %s %d files, deleted %d empty files.",
        dupes_found, action, files_modified, files_deleted,
    )
    if not dry_run:
        _commit_touched_paths(cfg, "dedup", touched)
    return 0


def _remove_entity_blocks(text: str, ids: set[str]) -> str:
    """Remove YAML entity blocks whose id is in *ids*.

    Correctly handles full block boundaries: each block starts at a `^- `
    line and extends until the next `^- ` line or EOF.
    """
    lines = text.split("\n")
    # Parse into blocks: each block is (start_line_idx, [lines]).
    blocks: list[tuple[int, list[str]]] = []
    current: list[str] = []
    start = 0
    for i, line in enumerate(lines):
        if line.startswith("- ") and current:
            blocks.append((start, current))
            current = []
            start = i
        current.append(line)
    if current:
        blocks.append((start, current))

    # Filter out blocks that are entity blocks with target IDs.
    keep: list[list[str]] = []
    for _, block_lines in blocks:
        # Check if this is an entity block with a target ID.
        is_entity = any(re.match(r"^-\s*type:\s+entity", l) for l in block_lines)
        if is_entity:
            block_id = None
            for l in block_lines:
                m = re.match(r"^\s+id:\s+(\S+)", l)
                if m:
                    block_id = m.group(1)
                    break
            if block_id and block_id in ids:
                continue  # Skip this block entirely.
        keep.append(block_lines)

    if not keep:
        return ""
    result_lines: list[str] = []
    for block_lines in keep:
        result_lines.extend(block_lines)
    # Clean trailing blank lines.
    while result_lines and result_lines[-1].strip() == "":
        result_lines.pop()
    return "\n".join(result_lines) + "\n" if result_lines else ""


def _extract_entity_blocks(text: str, ids: set[str]) -> str:
    """Extract only entity blocks whose id is in *ids* from YAML text.

    Inverse of _remove_entity_blocks: keeps only matching entity blocks.
    """
    lines = text.split("\n")
    blocks: list[tuple[int, list[str]]] = []
    current: list[str] = []
    start = 0
    for i, line in enumerate(lines):
        if line.startswith("- ") and current:
            blocks.append((start, current))
            current = []
            start = i
        current.append(line)
    if current:
        blocks.append((start, current))

    keep: list[list[str]] = []
    for _, block_lines in blocks:
        is_entity = any(re.match(r"^-\s*type:\s+entity", l) for l in block_lines)
        if not is_entity:
            continue
        block_id = None
        for l in block_lines:
            m = re.match(r"^\s+id:\s+(\S+)", l)
            if m:
                block_id = m.group(1)
                break
        if block_id and block_id in ids:
            keep.append(block_lines)

    if not keep:
        return ""
    result_lines: list[str] = []
    for block_lines in keep:
        result_lines.extend(block_lines)
    while result_lines and result_lines[-1].strip() == "":
        result_lines.pop()
    return "\n".join(result_lines) + "\n" if result_lines else ""


def cmd_update(cfg: dict, names: list[str] | None = None) -> int:
    """Fetch or clone configured upstream repos into the cache."""
    upstreams = cfg.get("upstreams", {})
    cache_dir = cfg["_cache_dir"]

    if not upstreams:
        log.info("No upstreams configured -- nothing to update.")
        return 0

    if names:
        unknown = [n for n in names if n not in upstreams]
        if unknown:
            log.error("Unknown upstream(s): %s", ", ".join(unknown))
            log.info("Available: %s", ", ".join(upstreams))
            return 1
        targets = {n: upstreams[n] for n in names}
    else:
        targets = upstreams

    log.info("=== Updating %d upstream repos ===", len(targets))
    succeeded = 0
    failed = 0

    for name, info in targets.items():
        if "path" in info:
            log.info("  %-20s (local path -- skipped)", name)
            continue
        try:
            path = _resolve_upstream_path(name, info, cache_dir)
            proto_count = sum(1 for _ in (path / "Resources" / "Prototypes").rglob("*.yml")) if (path / "Resources" / "Prototypes").is_dir() else 0
            log.info("  %-20s OK  (%d proto files)", name, proto_count)
            succeeded += 1
        except (subprocess.CalledProcessError, FileNotFoundError, ValueError) as e:
            log.error("  %-20s FAILED: %s", name, e)
            failed += 1

    log.info("")
    log.info("Update complete: %d succeeded, %d failed.", succeeded, failed)
    return 1 if failed else 0


def _resolve_fork_names(forks: dict[str, dict], raw_names: list[str] | None) -> tuple[list[str], list[str]]:
    """Resolve CLI fork selectors to fork keys.

    Selectors may be fork keys, `directory`, or entries in `alt_directories`.
    Matching is case-insensitive and ignores separators/leading underscores.
    """
    if not raw_names:
        return sorted(forks.keys()), []

    selected: list[str] = []
    unknown: list[str] = []
    for raw in raw_names:
        matched = False
        needle = _normalize_fork_token(raw)
        for fk, info in forks.items():
            names = [fk]
            if info.get("directory"):
                names.append(str(info["directory"]))
            names.extend(str(x) for x in info.get("alt_directories", []) or [])
            if any(_normalize_fork_token(name) == needle for name in names if name):
                selected.append(fk)
                matched = True
        if not matched:
            unknown.append(raw)

    return list(dict.fromkeys(selected)), unknown


def _first_existing_path(paths: list[Path]) -> Path | None:
    for p in paths:
        if p.is_file():
            return p
    return None


def _guess_upstream_for_fork(fork_key: str, fork_info: dict, upstreams: dict[str, dict]) -> str | None:
    """Best-effort upstream inference for forks without explicit mapping."""
    if fork_key in upstreams:
        return fork_key

    fork_tokens = {_normalize_fork_token(fork_key)}
    if fork_info.get("directory"):
        fork_tokens.add(_normalize_fork_token(str(fork_info["directory"])))
    for alt in fork_info.get("alt_directories", []) or []:
        fork_tokens.add(_normalize_fork_token(str(alt)))
    for marker in fork_info.get("marker_names", []) or []:
        fork_tokens.add(_normalize_fork_token(str(marker)))

    best: tuple[int, str] | None = None
    for up_name in upstreams:
        up_token = _normalize_fork_token(up_name)
        score = 0
        if up_token in fork_tokens:
            score = 100
        elif any(tok and (tok in up_token or up_token in tok) for tok in fork_tokens):
            score = 10
        if score > 0 and (best is None or score > best[0]):
            best = (score, up_name)

    return best[1] if best else None


def _build_update_ported_candidates(
    up_path: Path,
    base_prefix: Path,
    tail: Path,
    aliases: list[str],
) -> list[Path]:
    candidates = [up_path / base_prefix / tail]
    for alias in aliases:
        candidates.append(up_path / base_prefix / alias / tail)
    return candidates


def _rewrite_namespace_for_destination(src: Path, up_root: Path, dest: Path, workspace: Path, text: str) -> str:
    """Rewrite C# namespace to match destination path, mirroring pull behavior."""
    if dest.suffix != ".cs":
        return text
    try:
        old_rel = src.relative_to(up_root)
        new_rel = dest.relative_to(workspace)
    except ValueError:
        return text
    old_ns = ".".join(old_rel.parent.parts)
    new_ns = ".".join(new_rel.parent.parts)
    if old_ns != new_ns:
        text = text.replace(f"namespace {old_ns}", f"namespace {new_ns}", 1)
    return text


def cmd_update_ported(cfg: dict, forks_filter: list[str] | None, dry_run: bool = False) -> int:
    """Refresh already-ported fork-scoped files from their configured upstreams."""
    workspace = cfg["_workspace"]
    forks = cfg.get("forks", {})
    upstreams = cfg.get("upstreams", {})
    cache_dir = cfg["_cache_dir"]

    selected_forks, unknown = _resolve_fork_names(forks, forks_filter)
    if unknown:
        log.warning("Unknown fork selector(s): %s", ", ".join(unknown))

    if not selected_forks:
        log.info("No matching forks selected -- nothing to update.")
        return 1

    updated = 0
    unchanged = 0
    missing_src = 0
    scanned = 0
    touched: set[Path] = set()

    log.info("=== Updating already-ported content (%d forks) ===", len(selected_forks))

    for fk in selected_forks:
        info = forks.get(fk, {})
        local_aliases: list[str] = []
        if info.get("directory"):
            local_aliases.append(str(info["directory"]))
        local_aliases.extend(str(x) for x in info.get("alt_directories", []) or [])
        local_aliases = list(dict.fromkeys(local_aliases))
        if not local_aliases:
            log.debug("Skipping fork '%s' (no directory or alt_directories configured).", fk)
            continue

        up_name = info.get("upstream") or _guess_upstream_for_fork(fk, info, upstreams)
        if not up_name or up_name not in upstreams:
            log.warning("Skipping fork '%s' -- no configured upstream mapping.", fk)
            continue

        try:
            up_root = _ensure_upstream_full(up_name, upstreams[up_name], cache_dir)
        except Exception as e:
            log.warning("Skipping fork '%s' -- upstream '%s' unavailable: %s", fk, up_name, e)
            continue

        aliases = local_aliases

        log.info("Fork '%s' -> upstream '%s'", fk, up_name)

        roots: list[tuple[Path, Path]] = []
        for alias in local_aliases:
            roots.append((workspace / "Resources" / "Prototypes" / alias, Path("Resources") / "Prototypes"))
            roots.append((workspace / "Resources" / "Textures" / alias, Path("Resources") / "Textures"))

        locale_root = workspace / "Resources" / "Locale"
        if locale_root.is_dir():
            for lang_dir in locale_root.iterdir():
                if not lang_dir.is_dir():
                    continue
                for alias in local_aliases:
                    roots.append((lang_dir / alias, Path("Resources") / "Locale" / lang_dir.name))

        for p in sorted(workspace.iterdir()):
            if not p.is_dir() or not p.name.startswith("Content."):
                continue
            for alias in local_aliases:
                roots.append((p / alias, Path(p.name)))

        seen_local: set[Path] = set()
        for local_root, up_base_prefix in roots:
            if not local_root.is_dir():
                continue
            for local_file in local_root.rglob("*"):
                if not local_file.is_file():
                    continue
                if local_file in seen_local:
                    continue
                seen_local.add(local_file)
                scanned += 1
                rel_tail = local_file.relative_to(local_root)
                candidates = _build_update_ported_candidates(up_root, up_base_prefix, rel_tail, aliases)
                src = _first_existing_path(candidates)
                if src is None:
                    missing_src += 1
                    continue

                try:
                    src_bytes = src.read_bytes()
                    dst_bytes = local_file.read_bytes()
                except OSError:
                    continue

                if src_bytes == dst_bytes:
                    unchanged += 1
                    continue

                if dry_run:
                    log.info("  Would update %s <- %s", local_file.relative_to(workspace), src.relative_to(up_root))
                    updated += 1
                    continue

                if local_file.suffix == ".cs":
                    try:
                        text = src.read_text(encoding="utf-8", errors="replace")
                        text = _rewrite_namespace_for_destination(src, up_root, local_file, workspace, text)
                        local_file.write_text(text, encoding="utf-8")
                    except OSError:
                        continue
                else:
                    shutil.copy2(src, local_file)
                updated += 1
                touched.add(local_file)
                log.info("  Updated %s <- %s", local_file.relative_to(workspace), src.relative_to(up_root))

    verb = "Would update" if dry_run else "Updated"
    log.info("%s %d file(s), %d unchanged, %d missing upstream match (scanned %d).",
             verb, updated, unchanged, missing_src, scanned)
    if not dry_run:
        _commit_touched_paths(cfg, "update-ported", touched)
    return 0


def _ensure_upstream_full_history(name: str, info: dict, cache_dir: Path) -> Path:
    """Ensure upstream repo is available with full commit history for cherry-pick."""
    path = _resolve_upstream_path(name, info, cache_dir)
    if "path" in info:
        return path

    shallow = subprocess.run(
        ["git", "rev-parse", "--is-shallow-repository"],
        cwd=path,
        capture_output=True,
        text=True,
        check=False,
    )
    if shallow.returncode == 0 and shallow.stdout.strip() == "true":
        subprocess.run(
            ["git", "fetch", "--unshallow", "origin"],
            cwd=path,
            capture_output=True,
            check=True,
        )

    subprocess.run(
        ["git", "fetch", "--tags", "--prune", "origin"],
        cwd=path,
        capture_output=True,
        check=True,
    )
    return path


def cmd_cherry_pick(cfg: dict, from_upstream: str, commits: list[str], dry_run: bool = False) -> int:
    """Apply specific commit(s) from a configured upstream repository."""
    workspace = cfg["_workspace"]
    upstreams = cfg.get("upstreams", {})
    cache_dir = cfg["_cache_dir"]

    if from_upstream not in upstreams:
        log.error("Unknown upstream: '%s'", from_upstream)
        log.info("Available: %s", ", ".join(sorted(upstreams.keys())))
        return 1

    if not commits:
        log.info("No commits provided -- nothing to do.")
        return 0

    if not (workspace / ".git").is_dir():
        log.error("Workspace is not a git repository: %s", workspace)
        return 1

    status = subprocess.run(
        ["git", "status", "--porcelain"],
        cwd=workspace,
        capture_output=True,
        text=True,
        check=False,
    )
    if status.returncode != 0:
        log.error("Failed to check workspace git status.")
        return 1
    if status.stdout.strip() and not dry_run:
        log.error("Workspace has uncommitted changes; commit/stash them before cherry-picking.")
        return 1

    try:
        up_path = _ensure_upstream_full_history(from_upstream, upstreams[from_upstream], cache_dir)
    except (subprocess.CalledProcessError, FileNotFoundError, ValueError) as e:
        log.error("Failed to prepare upstream '%s': %s", from_upstream, e)
        return 1

    applied = 0
    failed = 0
    for commit in commits:
        verify = subprocess.run(
            ["git", "rev-parse", "--verify", f"{commit}^{{commit}}"],
            cwd=up_path,
            capture_output=True,
            text=True,
            check=False,
        )
        if verify.returncode != 0:
            log.warning("Commit not found in upstream '%s': %s", from_upstream, commit)
            failed += 1
            continue

        patch = subprocess.run(
            ["git", "format-patch", "-1", "--stdout", commit],
            cwd=up_path,
            capture_output=True,
            check=False,
        )
        if patch.returncode != 0 or not patch.stdout:
            log.warning("Failed to export patch for commit: %s", commit)
            failed += 1
            continue

        if dry_run:
            check_apply = subprocess.run(
                ["git", "apply", "--check", "--3way", "-"],
                cwd=workspace,
                input=patch.stdout,
                capture_output=True,
                check=False,
            )
            if check_apply.returncode != 0:
                log.warning("Would fail to apply %s", commit)
                failed += 1
            else:
                log.info("Would apply commit %s from '%s'", commit, from_upstream)
                applied += 1
            continue

        am = subprocess.run(
            ["git", "am", "-3", "--keep-non-patch", "-"],
            cwd=workspace,
            input=patch.stdout,
            capture_output=True,
            check=False,
        )
        if am.returncode != 0:
            log.warning("Failed to apply commit %s; aborting current am session.", commit)
            subprocess.run(["git", "am", "--abort"], cwd=workspace, capture_output=True, check=False)
            failed += 1
            continue

        applied += 1
        log.info("Applied commit %s from '%s'", commit, from_upstream)

    verb = "Would apply" if dry_run else "Applied"
    log.info("%s %d/%d commit(s).", verb, applied, len(commits))
    return 1 if failed else 0


def cmd_pull(cfg: dict, entity_ids: list[str], from_upstream: str | None,
             to_fork: str | None, dry_run: bool,
             skip_code: bool, skip_locale: bool,
             skip_test: bool = False) -> int:
    """Pull entity prototypes and all their dependencies from upstreams.

    Resolves the full dependency tree: parent prototypes, textures/sprites,
    C# component/system source files, and FTL locale entries.
    """
    workspace = cfg["_workspace"]
    proto_root = cfg["_proto_root"]
    tex_root = cfg["_tex_root"]
    locale_root = workspace / "Resources" / "Locale"
    cache_dir = cfg["_cache_dir"]
    upstreams = cfg.get("upstreams", {})
    source_order = cfg.get("resolve_source_order", list(upstreams.keys()))
    forks = cfg["forks"]
    max_depth = cfg.get("max_resolve_depth", 25)

    # Determine which upstreams to search.
    search_order = [from_upstream] if from_upstream else source_order

    # Prepare upstreams (full checkout if we need C#/locale).
    log.info("Preparing upstreams...")
    upstream_data: dict[str, tuple[Path, dict[str, Path]]] = {}
    need_full = not skip_code or not skip_locale
    for name in search_order:
        info = upstreams.get(name)
        if not info:
            log.warning("Unknown upstream: '%s'", name)
            continue
        try:
            if need_full:
                path = _ensure_upstream_full(name, info, cache_dir)
            else:
                path = _resolve_upstream_path(name, info, cache_dir)
            up_proto = path / "Resources" / "Prototypes"
            if up_proto.is_dir():
                log.info("  Indexing '%s'...", name)
                upstream_data[name] = (path, _index_prototypes(up_proto))
        except Exception as e:
            log.warning("  Skipping upstream '%s': %s", name, e)

    if not upstream_data:
        log.error("No upstreams available. Run 'update' first.")
        return 1

    # Index local prototypes.
    log.info("Indexing local prototypes...")
    local_index = _index_prototypes(proto_root)

    # ── Phase 1: Find target entities ─────────────────────────────────────
    log.info("=== Pulling %d prototype(s) ===", len(entity_ids))
    source_upstream: str | None = None
    entity_sources: dict[str, tuple[str, Path]] = {}

    for eid in entity_ids:
        if eid in local_index:
            log.info("  '%s' already exists locally at %s", eid,
                     local_index[eid].relative_to(proto_root))
            continue
        for name, (path, index) in upstream_data.items():
            if eid in index:
                entity_sources[eid] = (name, index[eid])
                if source_upstream is None:
                    source_upstream = name
                log.info("  Found '%s' in upstream '%s'", eid, name)
                break
        else:
            log.warning("  '%s' not found in any upstream.", eid)

    if not entity_sources:
        log.info("Nothing to pull -- all entities exist locally or weren't found.")
        return 0

    # Determine target fork directory.
    # Build upstream-name → fork-key mapping from forks with 'upstream' key.
    upstream_to_fork: dict[str, str] = {}
    for fk, finfo in forks.items():
        up = finfo.get("upstream")
        if up:
            upstream_to_fork[up] = fk
        elif fk in upstreams:
            upstream_to_fork[fk] = fk

    if to_fork:
        fork_info = forks.get(to_fork, {})
        fork_dir = fork_info.get("directory") if "directory" in fork_info else f"_{to_fork}"
    elif source_upstream:
        matched_fork = upstream_to_fork.get(source_upstream)
        if matched_fork:
            fork_info = forks[matched_fork]
            fork_dir = fork_info.get("directory") if "directory" in fork_info else f"_{matched_fork}"
            to_fork = matched_fork
        else:
            fork_dir = f"_{source_upstream}"
            to_fork = source_upstream
    else:
        fork_dir = "_imported"
        to_fork = "imported"

    log.info("Target fork directory: %s (fork: %s)", fork_dir or "(root)", to_fork)

    # ── Phase 2: Recursively resolve prototype dependencies ───────────────
    proto_files: dict[Path, tuple[str, Path]] = {}   # dest → (upstream, src)
    tex_dirs: dict[Path, tuple[str, Path]] = {}
    component_stems: set[str] = set()
    all_entity_ids: set[str] = set()

    queue = set(entity_sources.keys())
    visited: set[str] = set()

    for _iteration in range(max_depth):
        if not queue:
            break
        next_queue: set[str] = set()
        for eid in queue:
            if eid in visited or eid in local_index:
                continue
            visited.add(eid)
            all_entity_ids.add(eid)

            # Find entity source.
            src_info = entity_sources.get(eid)
            if not src_info:
                for name, (path, index) in upstream_data.items():
                    if eid in index:
                        src_info = (name, index[eid])
                        entity_sources[eid] = src_info
                        break
            if not src_info:
                log.warning("  Cannot resolve: %s", eid)
                continue

            up_name, src_file = src_info
            up_path = upstream_data[up_name][0]

            # Queue prototype file for copying.
            rel = src_file.relative_to(up_path / "Resources" / "Prototypes")
            # Strip leading fork directories from the upstream's path.
            parts = rel.parts
            if parts and parts[0].startswith("_"):
                rel = Path(*parts[1:]) if len(parts) > 1 else rel
            dest = (proto_root / fork_dir / rel) if fork_dir else (proto_root / rel)
            if not dest.exists() and dest not in proto_files:
                proto_files[dest] = (up_name, src_file)

            # Parse only the target entity's block for dependencies, not the whole file.
            try:
                full_text = src_file.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            text = _extract_entity_blocks(full_text, {eid})
            if not text:
                text = full_text  # Fallback if extraction fails.

            for pid in _extract_parents(text):
                if pid not in local_index and pid not in visited:
                    next_queue.add(pid)

            for rsi in _extract_sprites(text):
                src_tex = up_path / "Resources" / "Textures" / rsi
                if src_tex.is_dir():
                    dest_tex = tex_root / rsi
                    if not dest_tex.exists():
                        tex_dirs[dest_tex] = (up_name, src_tex)

            component_stems |= _extract_component_stems(text)

        queue = next_queue
        if next_queue:
            log.info("  Dependency pass %d: %d new IDs.", _iteration + 1, len(next_queue))

    # ── Phase 3: Resolve C# dependencies ──────────────────────────────────
    cs_files: dict[Path, tuple[str, Path]] = {}
    if not skip_code and component_stems:
        log.info("Checking %d component types for C# dependencies...", len(component_stems))
        local_classes = _index_local_classes(workspace)

        missing_stems: set[str] = set()
        for stem in component_stems:
            if f"{stem}Component" not in local_classes:
                missing_stems.add(stem)

        if missing_stems:
            log.info("  %d component types missing locally, searching upstreams...",
                     len(missing_stems))
            for up_name, (up_path, _) in upstream_data.items():
                found = _find_upstream_cs_files(up_path, missing_stems)
                for cls_name, src in found.items():
                    try:
                        rel = src.relative_to(up_path)
                    except ValueError:
                        continue
                    assembly = rel.parts[0]   # e.g. "Content.Shared"
                    inner = Path(*rel.parts[1:])
                    # Strip fork dirs from inner path.
                    if inner.parts and inner.parts[0].startswith("_"):
                        inner = Path(*inner.parts[1:]) if len(inner.parts) > 1 else inner
                    dest = (workspace / assembly / fork_dir / inner) if fork_dir else (workspace / assembly / inner)
                    if not dest.exists() and dest not in cs_files:
                        cs_files[dest] = (up_name, src)
                        log.info("    %s ← %s/%s", cls_name, up_name, rel)
                # Remove found stems so we stop searching further upstreams.
                for cls_name in found:
                    for suffix in ("Component", "System"):
                        if cls_name.endswith(suffix):
                            missing_stems.discard(cls_name[:-len(suffix)])
                    if cls_name.startswith("Shared") and cls_name.endswith("System"):
                        missing_stems.discard(cls_name[len("Shared"):-len("System")])
                if not missing_stems:
                    break
        else:
            log.info("  All component types already available locally.")
    elif skip_code:
        log.info("Skipping C# dependency resolution (--no-code).")

    # ── Phase 3b: Resolve test files ──────────────────────────────────────
    test_files: dict[Path, tuple[str, Path]] = {}
    if not skip_code and not skip_test and cs_files:
        # Build set of class names from pulled C# files for test matching.
        pulled_classes: set[str] = set()
        for dest in cs_files:
            for m in _CLASS_DEF_RE.finditer(dest.stem):
                pulled_classes.add(m.group(1))
            # Also match on the filename stem itself (e.g. "FooComponent" from "FooComponent.cs").
            pulled_classes.add(dest.stem)
        # Add the component stems themselves (with Component/System suffixes).
        for stem in component_stems:
            pulled_classes.add(f"{stem}Component")
            pulled_classes.add(f"{stem}System")

        if pulled_classes:
            log.info("Searching for test files referencing %d pulled classes...", len(pulled_classes))
            for up_name, (up_path, _) in upstream_data.items():
                found = _find_upstream_test_files(up_path, pulled_classes)
                for rel_str, src in found.items():
                    rel = Path(rel_str)
                    # e.g. Content.Tests/Tests/_Goob/FooTest.cs
                    test_project = rel.parts[0]  # Content.Tests or Content.IntegrationTests
                    inner = Path(*rel.parts[1:]) if len(rel.parts) > 1 else rel
                    # Strip upstream fork dirs.
                    if inner.parts and inner.parts[0].startswith("_"):
                        inner = Path(*inner.parts[1:]) if len(inner.parts) > 1 else inner
                    dest = (workspace / test_project / fork_dir / inner) if fork_dir else (workspace / test_project / inner)
                    if not dest.exists() and dest not in test_files:
                        test_files[dest] = (up_name, src)
                        log.info("    %s ← %s/%s", dest.relative_to(workspace), up_name, rel_str)

    # ── Phase 4: Resolve locale entries ───────────────────────────────────
    locale_entries: dict[Path, str] = {}   # dest_file → extracted FTL content
    if not skip_locale and all_entity_ids:
        log.info("Searching for locale entries for %d entities...", len(all_entity_ids))
        for up_name, (up_path, _) in upstream_data.items():
            up_locale = up_path / "Resources" / "Locale"
            if not up_locale.is_dir():
                continue
            for ftl in up_locale.rglob("*.ftl"):
                try:
                    text = ftl.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue
                extracted = _extract_ftl_entries(text, all_entity_ids)
                if not extracted.strip():
                    continue
                rel = ftl.relative_to(up_path / "Resources" / "Locale")
                parts = rel.parts
                # Preserve the language dir (en-US), strip fork dirs inside it.
                if len(parts) >= 2 and parts[1].startswith("_"):
                    rel = Path(parts[0], *parts[2:]) if len(parts) > 2 else rel
                if fork_dir:
                    dest = locale_root / rel.parts[0] / fork_dir / Path(*rel.parts[1:])
                else:
                    dest = locale_root / rel
                if dest in locale_entries:
                    locale_entries[dest] += "\n" + extracted
                else:
                    locale_entries[dest] = extracted
    elif skip_locale:
        log.info("Skipping locale resolution (--no-locale).")

    # ── Phase 5: Summary and copy ─────────────────────────────────────────
    total = len(proto_files) + len(tex_dirs) + len(cs_files) + len(test_files) + len(locale_entries)
    log.info("")
    log.info("=== Pull Summary ===")
    log.info("  Prototype files:  %d", len(proto_files))
    log.info("  Texture dirs:     %d", len(tex_dirs))
    log.info("  C# source files:  %d", len(cs_files))
    log.info("  Test files:       %d", len(test_files))
    log.info("  Locale files:     %d", len(locale_entries))
    log.info("  Total:            %d", total)

    if total == 0:
        log.info("Nothing new to copy.")
        return 0

    action = "Would copy" if dry_run else "Copying"
    touched: set[Path] = set()
    log.info("")

    for dest, (up_name, src) in sorted(proto_files.items()):
        log.info("  %s proto  %s ← %s", action, dest.relative_to(proto_root), up_name)
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            text = src.read_text(encoding="utf-8", errors="replace")
            # Extract only the entities we actually need from this file.
            src_ids = set(_iter_entity_ids(text))
            needed_ids = src_ids & all_entity_ids
            if needed_ids and needed_ids != src_ids:
                text = _extract_entity_blocks(text, needed_ids)
                skipped = src_ids - needed_ids
                if skipped:
                    log.debug("    Extracted %d/%d entities (skipped: %s)",
                              len(needed_ids), len(src_ids), ", ".join(sorted(skipped)))
            dest.write_text(text, encoding="utf-8")
            touched.add(dest)

    for dest, (up_name, src) in sorted(tex_dirs.items()):
        log.info("  %s texture %s ← %s", action, dest.relative_to(tex_root), up_name)
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copytree(src, dest)
            touched.add(dest)

    for dest, (up_name, src) in sorted(cs_files.items()):
        log.info("  %s C#     %s ← %s", action, dest.relative_to(workspace), up_name)
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            text = src.read_text(encoding="utf-8", errors="replace")
            # Update namespace to match new directory path.
            try:
                old_rel = src.relative_to(upstream_data[up_name][0])
                new_rel = dest.relative_to(workspace)
                old_ns = ".".join(old_rel.parent.parts)
                new_ns = ".".join(new_rel.parent.parts)
                if old_ns != new_ns:
                    text = text.replace(f"namespace {old_ns}", f"namespace {new_ns}", 1)
            except ValueError:
                pass
            dest.write_text(text, encoding="utf-8")
            touched.add(dest)

    for dest, (up_name, src) in sorted(test_files.items()):
        log.info("  %s test   %s ← %s", action, dest.relative_to(workspace), up_name)
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            text = src.read_text(encoding="utf-8", errors="replace")
            try:
                old_rel = src.relative_to(upstream_data[up_name][0])
                new_rel = dest.relative_to(workspace)
                old_ns = ".".join(old_rel.parent.parts)
                new_ns = ".".join(new_rel.parent.parts)
                if old_ns != new_ns:
                    text = text.replace(f"namespace {old_ns}", f"namespace {new_ns}", 1)
            except ValueError:
                pass
            dest.write_text(text, encoding="utf-8")
            touched.add(dest)

    for dest, content in sorted(locale_entries.items()):
        mode = "append locale" if dest.exists() else "locale"
        log.info("  %s %s %s", action, mode, dest.relative_to(locale_root))
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            if dest.exists():
                existing = dest.read_text(encoding="utf-8", errors="replace")
                merged = _append_unique_block(existing, content)
                if merged != existing:
                    dest.write_text(merged, encoding="utf-8")
                    touched.add(dest)
            else:
                dest.write_text(content, encoding="utf-8")
                touched.add(dest)

    if dry_run:
        log.info("\nDry run -- no files written. Remove --dry-run to apply.")
    else:
        log.info("\nPull complete. %d files written.", total)
        if cs_files:
            log.info("NOTE: C# namespaces were updated automatically -- review for correctness.")
        _commit_touched_paths(cfg, "pull", touched)
    return 0


def cmd_resolve(cfg: dict, dry_run: bool) -> int:
    """Discover missing prototypes in map files and pull from upstreams."""
    proto_root = cfg["_proto_root"]
    tex_root = cfg["_tex_root"]
    workspace = cfg["_workspace"]
    upstreams = cfg.get("upstreams", {})
    scan_dirs = cfg.get("map_scan_dirs", ["Maps/_Mono"])
    max_depth = cfg.get("max_resolve_depth", 25)
    source_order = cfg.get("resolve_source_order", list(upstreams.keys()))

    # 1. Build local entity index.
    log.info("Indexing local prototypes...")
    local_index = _index_prototypes(proto_root)
    log.info("  %d local entities indexed.", len(local_index))

    # 2. Build upstream indexes (full checkout for C#/locale).
    upstream_paths: dict[str, Path] = {}
    upstream_indexes: dict[str, dict[str, Path]] = {}
    cache_dir = cfg["_cache_dir"]
    for name in source_order:
        info = upstreams.get(name)
        if not info:
            continue
        try:
            up_path = _ensure_upstream_full(name, info, cache_dir)
        except (FileNotFoundError, subprocess.CalledProcessError, ValueError) as e:
            log.warning("Skipping upstream '%s' -- not available: %s", name, e)
            continue
        up_proto = up_path / "Resources" / "Prototypes"
        if up_proto.is_dir():
            log.info("Indexing upstream '%s' at %s ...", name, up_proto)
            upstream_paths[name] = up_path
            upstream_indexes[name] = _index_prototypes(up_proto)
            log.info("  %d entities.", len(upstream_indexes[name]))

    # 3. Collect entity refs from map files.
    missing: set[str] = set()
    proto_ref_re = re.compile(r"proto:\s+(\S+)")
    for scan_dir in scan_dirs:
        maps_dir = workspace / "Resources" / scan_dir
        if not maps_dir.is_dir():
            continue
        for mapfile in maps_dir.rglob("*.yml"):
            try:
                text = mapfile.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            for m in proto_ref_re.finditer(text):
                eid = m.group(1).strip("'\"")
                if eid not in local_index:
                    missing.add(eid)

    log.info("Found %d missing prototype references in maps.", len(missing))

    # 4. Recursive resolution.
    files_to_copy: dict[Path, tuple[str, Path]]  = {}  # dest → (upstream_name, src)
    textures_to_copy: dict[Path, tuple[str, Path]] = {}
    component_stems: set[str] = set()
    resolved: set[str] = set()
    unresolvable: set[str] = set()
    queue = set(missing)

    for iteration in range(max_depth):
        if not queue:
            break
        new_queue: set[str] = set()
        for eid in queue:
            if eid in resolved or eid in local_index:
                continue
            # Find in upstreams.
            found = False
            for up_name, up_idx in upstream_indexes.items():
                if eid not in up_idx:
                    continue
                src_file = up_idx[eid]
                up_root = upstream_paths[up_name]
                rel = src_file.relative_to(up_root / "Resources" / "Prototypes")
                dest = proto_root / rel

                if dest not in files_to_copy:
                    files_to_copy[dest] = (up_name, src_file)
                    # Parse only the target entity's block for dependencies.
                    try:
                        full_text = src_file.read_text(encoding="utf-8", errors="replace")
                    except OSError:
                        continue
                    text = _extract_entity_blocks(full_text, {eid})
                    if not text:
                        text = full_text  # Fallback if extraction fails.
                    for parent_id in _extract_parents(text):
                        if parent_id not in local_index and parent_id not in resolved:
                            new_queue.add(parent_id)
                    for rsi in _extract_sprites(text):
                        src_tex = up_root / "Resources" / "Textures" / rsi
                        if src_tex.is_dir():
                            dest_tex = tex_root / rsi
                            if not dest_tex.exists():
                                textures_to_copy[dest_tex] = (up_name, src_tex)
                    component_stems |= _extract_component_stems(text)

                resolved.add(eid)
                found = True
                break

            if not found:
                unresolvable.add(eid)

        queue = new_queue
        if new_queue:
            log.info("  Resolve iteration %d: %d new dependencies.", iteration + 1, len(new_queue))

    # 5. Resolve C# dependencies.
    cs_files_to_copy: dict[Path, tuple[str, Path]] = {}
    if component_stems:
        log.info("Checking %d component types for C# dependencies...", len(component_stems))
        local_classes = _index_local_classes(workspace)
        missing_stems = {s for s in component_stems if f"{s}Component" not in local_classes}
        if missing_stems:
            log.info("  %d component types missing locally, searching upstreams...", len(missing_stems))
            for up_name in upstream_paths:
                up_path = upstream_paths[up_name]
                found = _find_upstream_cs_files(up_path, missing_stems)
                for cls_name, src in found.items():
                    try:
                        rel = src.relative_to(up_path)
                    except ValueError:
                        continue
                    dest = workspace / rel
                    if not dest.exists() and dest not in cs_files_to_copy:
                        cs_files_to_copy[dest] = (up_name, src)
                        log.info("    %s ← %s/%s", cls_name, up_name, rel)
                    for suffix in ("Component", "System"):
                        if cls_name.endswith(suffix):
                            missing_stems.discard(cls_name[:-len(suffix)])
                    if cls_name.startswith("Shared") and cls_name.endswith("System"):
                        missing_stems.discard(cls_name[len("Shared"):-len("System")])
                if not missing_stems:
                    break

    # 6. Resolve locale entries.
    locale_root = workspace / "Resources" / "Locale"
    locale_entries: dict[Path, str] = {}
    if resolved:
        log.info("Searching for locale entries for %d resolved entities...", len(resolved))
        for up_name, up_path in upstream_paths.items():
            up_locale = up_path / "Resources" / "Locale"
            if not up_locale.is_dir():
                continue
            for ftl in up_locale.rglob("*.ftl"):
                try:
                    text = ftl.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue
                extracted = _extract_ftl_entries(text, resolved)
                if not extracted.strip():
                    continue
                rel = ftl.relative_to(up_path / "Resources" / "Locale")
                dest = locale_root / rel
                if not dest.exists():
                    if dest in locale_entries:
                        locale_entries[dest] += "\n" + extracted
                    else:
                        locale_entries[dest] = extracted

    # 7. Copy all files.
    copied_files = 0
    copied_tex = 0
    copied_cs = 0
    copied_locale = 0
    touched: set[Path] = set()
    for dest, (up_name, src) in files_to_copy.items():
        if dest.exists():
            continue
        if dry_run:
            log.info("  Would copy proto %s ← %s", dest.relative_to(proto_root), up_name)
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            touched.add(dest)
        copied_files += 1

    for dest, (up_name, src) in textures_to_copy.items():
        if dest.exists():
            continue
        if dry_run:
            log.info("  Would copy texture %s ← %s", dest.relative_to(tex_root), up_name)
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copytree(src, dest)
            touched.add(dest)
        copied_tex += 1

    for dest, (up_name, src) in cs_files_to_copy.items():
        if dest.exists():
            continue
        if dry_run:
            log.info("  Would copy C# %s ← %s", dest.relative_to(workspace), up_name)
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            touched.add(dest)
        copied_cs += 1

    for dest, content in locale_entries.items():
        if dest.exists():
            continue
        if dry_run:
            log.info("  Would copy locale %s", dest.relative_to(locale_root))
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(content, encoding="utf-8")
            touched.add(dest)
        copied_locale += 1

    action = "Would copy" if dry_run else "Copied"
    log.info(
        "Resolve complete: %s %d proto files, %d textures, %d C# files, %d locale files. "
        "%d unresolvable IDs.",
        action, copied_files, copied_tex, copied_cs, copied_locale, len(unresolvable),
    )
    if unresolvable:
        for eid in sorted(unresolvable):
            log.warning("  Unresolvable: %s", eid)
    if not dry_run:
        _commit_touched_paths(cfg, "resolve", touched)
    return 0


def _build_marker_engine(forks: dict[str, dict]) -> tuple[re.Pattern, dict[str, str]]:
    """Build a compiled regex and lookup table from fork marker_names.

    Returns (compiled_pattern, {lowered_name: fork_key}).
    The pattern matches fork-name-stems at the start of a comment body, handling:
      // <Name> …        # <Name> …
      // begin <Name> …  // end <Name> …
      // <Name>-<word>   (hyphenated, e.g. Corvax-Next-Footprints)
    """
    name_to_fork: dict[str, str] = {}  # lowered stem → fork key
    all_stems: list[str] = []

    for key, info in forks.items():
        for name in info.get("marker_names") or []:
            low = name.lower()
            name_to_fork[low] = key
            all_stems.append(re.escape(name))

    if not all_stems:
        return re.compile(r"(?!x)x"), {}  # never-match pattern

    # Sort longest first so alternation is greedy.
    all_stems.sort(key=lambda s: -len(s))
    names_alt = "|".join(all_stems)

    # Match comment-start then optional begin/end prefix then fork name.
    # The fork name must be followed by a word boundary OR hyphen (for Corvax-Next-*).
    # Works for both C# (//) and YAML/FTL (#) comments.
    pattern = re.compile(
        rf"(?://|#)\s*(?:begin\s+|end\s+)?({names_alt})(?:\b|(?=-))",
        re.IGNORECASE,
    )
    return pattern, name_to_fork


def _detect_marker_type(line: str, stem_lower: str) -> str:
    """Classify a matched marker line as 'start', 'end', or 'point'.

    Block markers are comments using explicit start/end/begin keywords near
    the fork name.  Patterns supported:
      // begin Goobstation      // end Goobstation
      // Goob edit start        // Goob edit end
      // Goob edit start - desc // Goob change end
      // Shitmed Change Start   // Shitmed Change end
    """
    low = line.lower()
    # Extract just the comment body.
    comment_body = ""
    for tok in ("//", "#"):
        idx = low.find(tok)
        if idx >= 0:
            comment_body = low[idx + len(tok):].strip()
            break

    if not comment_body:
        return "point"

    # "begin <Name>" or "end <Name>" prefix style.
    if comment_body.startswith("begin "):
        return "start"
    if comment_body.startswith("end "):
        return "end"

    # "<Name> ... start/begin/end" suffix style.
    # Look for the keyword in the first few tokens after the fork name stem.
    after_stem = comment_body.split(stem_lower, 1)[-1].strip() if stem_lower in comment_body else ""
    tokens = after_stem.split()
    # Only check the first 3 tokens after the stem for the keyword -- beyond
    # that it's more likely a description than a block boundary.
    head = tokens[:3]
    for t in head:
        if t in ("start", "begin"):
            return "start"
        if t == "end":
            return "end"
        # Stop scanning if we hit a description separator.
        if t in ("-", ":", "--"):
            break
    return "point"


def _normalize_namespace_filters(values: list[str] | None) -> list[str]:
    """Normalize `--namespace` CLI values (repeatable and comma-separated)."""
    if not values:
        return []
    out: list[str] = []
    for raw in values:
        for part in raw.split(","):
            ns = part.strip()
            if ns:
                out.append(ns)
    # De-duplicate while preserving original order.
    return list(dict.fromkeys(out))


def _normalize_fork_token(value: str) -> str:
    """Normalize names for case-insensitive fork/path matching."""
    return re.sub(r"[^a-z0-9]+", "", value.lower().lstrip("_"))


def _build_fork_scope_tokens(forks: dict[str, dict], scopes: list[str]) -> tuple[set[str], list[str], list[str]]:
    """Build normalized path tokens for selected fork keys/directories."""
    tokens: set[str] = set()
    resolved: list[str] = []
    unresolved: list[str] = []

    for raw_scope in scopes:
        scope = raw_scope.strip()
        if not scope:
            continue
        normalized_scope = _normalize_fork_token(scope)

        matched_keys: list[str] = []
        for fork_key, info in forks.items():
            names = [fork_key]
            if info.get("directory"):
                names.append(str(info["directory"]))
            names.extend(str(x) for x in info.get("alt_directories", []) or [])
            if any(_normalize_fork_token(name) == normalized_scope for name in names if name):
                matched_keys.append(fork_key)

        if not matched_keys:
            unresolved.append(scope)
            tokens.add(normalized_scope)
            continue

        for fork_key in matched_keys:
            resolved.append(fork_key)
            info = forks.get(fork_key, {})
            for name in [fork_key, info.get("directory"), *(info.get("alt_directories", []) or [])]:
                if not name:
                    continue
                tokens.add(_normalize_fork_token(str(name)))

    return tokens, list(dict.fromkeys(resolved)), unresolved


def _path_matches_fork_scope(rel_path: str, tokens: set[str]) -> bool:
    """Return True if a relative path appears to belong to selected fork scopes."""
    if not tokens:
        return True
    for seg in rel_path.split("/"):
        normalized_seg = _normalize_fork_token(seg)
        if not normalized_seg:
            continue
        if any(tok and tok in normalized_seg for tok in tokens):
            return True
    return False


def cmd_audit_patches(
    cfg: dict,
    include_forks: bool = False,
    namespaces: list[str] | None = None,
) -> int:
    """Scan source files for inline fork-edit markers and produce a report."""
    workspace = cfg["_workspace"]
    forks = cfg["forks"]
    namespace_filters = _normalize_namespace_filters(namespaces)
    scope_tokens: set[str] = set()
    resolved_scopes: list[str] = []
    unresolved_scopes: list[str] = []
    if namespace_filters:
        scope_tokens, resolved_scopes, unresolved_scopes = _build_fork_scope_tokens(forks, namespace_filters)

    marker_re, name_to_fork = _build_marker_engine(forks)
    if not name_to_fork:
        log.info("No marker_names configured -- nothing to audit.")
        return 0

    # ── Define scan targets ──────────────────────────────────────────────
    scan_targets: list[tuple[Path, str, list[str]]] = []
    # C# files in base-game assemblies.
    for d in ("Content.Client", "Content.Server", "Content.Shared"):
        p = workspace / d
        if p.is_dir():
            scan_targets.append((p, "cs", ["*.cs"]))
    # Also scan fork-specific C# assemblies (Content.Goobstation.Shared, etc.)
    for p in sorted(workspace.iterdir()):
        if (p.is_dir() and p.name.startswith("Content.")
                and p.name not in ("Content.Client", "Content.Server", "Content.Shared")
                and not p.name.endswith((".Tests", ".IntegrationTests", ".Benchmarks",
                                        ".Packaging", ".Tools", ".YAMLLinter",
                                        ".MapRenderer", ".Replay", ".PatreonParser",
                                        ".ModuleManager"))):
            scan_targets.append((p, "cs", ["*.cs"]))
    # YAML prototypes.
    proto_root = cfg["_proto_root"]
    if proto_root.is_dir():
        scan_targets.append((proto_root, "yml", ["*.yml"]))
    # FTL locale files.
    locale_root = workspace / "Resources" / "Locale"
    if locale_root.is_dir():
        scan_targets.append((locale_root, "ftl", ["*.ftl"]))

    report: dict[str, list[dict]] = defaultdict(list)
    total = 0
    block_pairs = 0
    unmatched_starts: list[dict] = []
    unmatched_ends: list[dict] = []

    for scan_dir, file_type, globs in scan_targets:
        for glob_pat in globs:
            for src_file in scan_dir.rglob(glob_pat):
                rel = src_file.relative_to(workspace).as_posix()

                # Skip fork-specific directories unless --include-forks.
                if not include_forks and not namespace_filters and "/_" in rel:
                    continue

                if namespace_filters and not _path_matches_fork_scope(rel, scope_tokens):
                    continue

                try:
                    text = src_file.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue

                lines = text.split("\n")

                # Track open blocks per fork for this file.
                open_blocks: dict[str, list[int]] = defaultdict(list)

                for i, line in enumerate(lines, 1):
                    m = marker_re.search(line)
                    if not m:
                        continue

                    matched_stem = m.group(1).lower()
                    fork_key = name_to_fork.get(matched_stem, "unknown")
                    mtype = _detect_marker_type(line, matched_stem)

                    report[rel].append({
                        "line": i,
                        "fork": fork_key,
                        "type": mtype,
                        "text": line.strip(),
                    })
                    total += 1

                    # Block tracking.
                    if mtype == "start":
                        open_blocks[fork_key].append(i)
                    elif mtype == "end":
                        if open_blocks.get(fork_key):
                            block_pairs += 1
                            open_blocks[fork_key].pop()
                        else:
                            unmatched_ends.append({"file": rel, "line": i, "fork": fork_key})

                # Any still-open blocks at EOF are unmatched starts.
                for fk, start_stack in open_blocks.items():
                    for start_line in start_stack:
                        unmatched_starts.append({"file": rel, "line": start_line, "fork": fk})

    # ── Print report ─────────────────────────────────────────────────────
    log.info("=== Inline Fork-Patch Audit ===")
    if namespace_filters:
        if resolved_scopes:
            log.info("Fork scope filter: %s", ", ".join(resolved_scopes))
        else:
            log.info("Fork scope filter: (raw) %s", ", ".join(namespace_filters))
        if unresolved_scopes:
            log.warning("Unknown fork scopes: %s", ", ".join(unresolved_scopes))
    log.info("Found %d inline patch markers across %d files.", total, len(report))
    log.info("  Block pairs (start+end):  %d", block_pairs)
    log.info("  Point markers (single):   %d", total - block_pairs * 2)
    log.info("")

    for filepath, patches in sorted(report.items()):
        log.info("  %s (%d markers)", filepath, len(patches))
        for p in patches:
            tag = f"[{p['fork']}]"
            if p["type"] != "point":
                tag = f"[{p['fork']} {p['type']}]"
            log.info("    L%d %s: %s", p["line"], tag, p["text"])

    # ── Summary by fork ──────────────────────────────────────────────────
    log.info("\n--- Summary by fork ---")
    by_fork: dict[str, dict[str, int]] = defaultdict(lambda: {"point": 0, "start": 0, "end": 0})
    by_format: dict[str, int] = defaultdict(int)
    for filepath, patches in report.items():
        ext = filepath.rsplit(".", 1)[-1] if "." in filepath else "?"
        for p in patches:
            by_fork[p["fork"]][p["type"]] += 1
            by_format[ext] += 1

    for fk, counts in sorted(by_fork.items(), key=lambda x: -sum(x[1].values())):
        t = sum(counts.values())
        blocks = min(counts["start"], counts["end"])
        pts = counts["point"]
        log.info("  %-20s %4d total  (%d blocks, %d point)", fk, t, blocks, pts)

    log.info("\n--- Summary by file type ---")
    for ext, count in sorted(by_format.items(), key=lambda x: -x[1]):
        log.info("  .%-6s %4d markers", ext, count)

    # ── Warn about unmatched blocks ──────────────────────────────────────
    if unmatched_starts or unmatched_ends:
        log.info("\n--- Unmatched block markers (possible bugs) ---")
        for u in unmatched_starts:
            log.warning("  UNMATCHED START: %s L%d [%s]", u["file"], u["line"], u["fork"])
        for u in unmatched_ends:
            log.warning("  UNMATCHED END:   %s L%d [%s]", u["file"], u["line"], u["fork"])
    return 0


def cmd_clean(cfg: dict) -> int:
    """Remove the .fork_porter_cache/ directory."""
    cache_dir = cfg["_cache_dir"]
    if cache_dir.is_dir():
        shutil.rmtree(cache_dir)
        log.info("Removed cache directory: %s", cache_dir)
    else:
        log.info("Cache directory does not exist: %s", cache_dir)
    return 0


def cmd_status(cfg: dict) -> int:
    """Quick health dashboard: dupes, missing map refs, inline patches."""
    proto_root = cfg["_proto_root"]
    workspace = cfg["_workspace"]
    forks = cfg["forks"]

    # Count entities per fork.
    fork_counts: dict[str, int] = defaultdict(int)
    all_ids: dict[str, list[str]] = defaultdict(list)
    for yml in proto_root.rglob("*.yml"):
        try:
            text = yml.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        if not _TYPE_RE.search(text):
            continue
        fk = _classify_fork(yml, forks) or "base"
        for eid in _iter_entity_ids(text):
            fork_counts[fk] += 1
            all_ids[eid].append(fk)

    # Count duplicates.
    dupes = sum(1 for locs in all_ids.values() if len(locs) > 1)

    # Count missing map refs.
    local_ids = set(all_ids.keys())
    missing_map_refs = 0
    scan_dirs = cfg.get("map_scan_dirs", ["Maps/_Mono"])
    proto_ref_re = re.compile(r"proto:\s+(\S+)")
    for d in scan_dirs:
        maps_dir = workspace / "Resources" / d
        if not maps_dir.is_dir():
            continue
        for mapfile in maps_dir.rglob("*.yml"):
            try:
                text = mapfile.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            for m in proto_ref_re.finditer(text):
                if m.group(1).strip("'\"") not in local_ids:
                    missing_map_refs += 1

    # Count inline patch markers (quick scan of base-game C# files).
    marker_re, name_to_fork = _build_marker_engine(forks)
    patch_counts: dict[str, int] = defaultdict(int)
    if name_to_fork:
        for d in ("Content.Client", "Content.Server", "Content.Shared"):
            src_dir = workspace / d
            if not src_dir.is_dir():
                continue
            for cs in src_dir.rglob("*.cs"):
                rel = cs.relative_to(workspace).as_posix()
                if "/_" in rel:
                    continue
                try:
                    text = cs.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue
                for m in marker_re.finditer(text):
                    fk = name_to_fork.get(m.group(1).lower(), "unknown")
                    patch_counts[fk] += 1

    log.info("=== OmuStation Fork Status ===")
    log.info("")
    log.info("Entity prototypes by fork:")
    for fk, count in sorted(fork_counts.items(), key=lambda x: -x[1]):
        log.info("  %-20s %5d entities", fk, count)
    log.info("  %-20s %5d total", "TOTAL", sum(fork_counts.values()))
    log.info("")
    log.info("Duplicate IDs across forks:  %d", dupes)
    log.info("Missing map prototype refs:  %d", missing_map_refs)
    if patch_counts:
        total_patches = sum(patch_counts.values())
        log.info("Inline patch markers:        %d", total_patches)
        for fk, count in sorted(patch_counts.items(), key=lambda x: -x[1]):
            log.info("  %-20s %5d markers", fk, count)
    else:
        log.info("Inline patch markers:        (no marker_names configured)")
    return 0


# ── CLI ──────────────────────────────────────────────────────────────────────


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="fork_porter",
        description="OmuStation Fork Porter -- unified CLI for fork content management.",
    )
    parser.add_argument(
        "--config",
        default="Tools/fork_porter/config.yml",
        help="Path to config.yml (default: Tools/fork_porter/config.yml)",
    )
    parser.add_argument("--verbose", action="store_true", help="Enable debug logging.")

    sub = parser.add_subparsers(dest="command")

    _dry = {"args": ["--dry-run"], "kwargs": {"action": "store_true", "help": "Preview without writing."}}

    dedup_p = sub.add_parser("dedup", help="Remove duplicate prototypes across forks.")
    dedup_p.add_argument(*_dry["args"], **_dry["kwargs"])

    resolve_p = sub.add_parser("resolve", help="Pull missing prototypes from upstreams.")
    resolve_p.add_argument(*_dry["args"], **_dry["kwargs"])

    update_p = sub.add_parser("update", help="Fetch/clone upstream repos.")
    update_p.add_argument("names", nargs="*",
                          help="Upstream name(s) to update (default: all).")

    update_ported_p = sub.add_parser(
        "update-ported",
        help="Refresh already-ported fork-scoped files from upstreams.",
    )
    update_ported_p.add_argument(
        "forks",
        nargs="*",
        help="Optional fork selector(s): fork key, directory, or alt directory.",
    )
    update_ported_p.add_argument(*_dry["args"], **_dry["kwargs"])

    cherry_p = sub.add_parser("cherry-pick", help="Apply specific upstream commits to this repository.")
    cherry_p.add_argument("commits", nargs="+", help="Upstream commit SHA(s) to apply.")
    cherry_p.add_argument("--from", dest="from_upstream", required=True,
                          help="Configured upstream name to cherry-pick from.")
    cherry_p.add_argument(*_dry["args"], **_dry["kwargs"])

    pull_p = sub.add_parser("pull", help="Pull a prototype and its dependencies from upstreams.")
    pull_p.add_argument("entities", nargs="+", help="Entity prototype ID(s) to pull.")
    pull_p.add_argument("--from", dest="from_upstream",
                        help="Upstream to pull from (default: search all).")
    pull_p.add_argument("--to", dest="to_fork",
                        help="Target fork key for destination directory (default: auto-detect).")
    pull_p.add_argument("--no-code", action="store_true",
                        help="Skip C# source file resolution.")
    pull_p.add_argument("--no-locale", action="store_true",
                        help="Skip FTL locale resolution.")
    pull_p.add_argument("--no-test", action="store_true",
                        help="Skip test file resolution.")
    pull_p.add_argument(*_dry["args"], **_dry["kwargs"])

    audit_p = sub.add_parser("audit-patches", help="Report inline fork-edit markers in source files.")
    audit_p.add_argument("--include-forks", action="store_true",
                         help="Also scan fork-specific directories (/_Foo/).")
    audit_p.add_argument(
        "--namespace",
        dest="namespaces",
        action="append",
        help=(
            "Limit scan to fork scopes from config.yml (fork key, directory, or alt directory). "
            "Repeatable and accepts comma-separated values."
        ),
    )
    sub.add_parser("status", help="Fork health dashboard.")
    sub.add_parser("clean", help="Remove .fork_porter_cache/ directory.")

    args = parser.parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(levelname)-7s %(message)s",
    )

    if not args.command:
        parser.print_help()
        return 1

    config_path = Path(args.config)
    if not config_path.is_absolute():
        # Resolve relative to CWD.
        config_path = Path.cwd() / config_path

    if not config_path.exists():
        log.error("Config not found: %s", config_path)
        return 1

    cfg = _load_config(config_path)

    status = 0
    if args.command == "update":
        status = cmd_update(cfg, args.names)
    elif args.command == "update-ported":
        status = cmd_update_ported(cfg, args.forks, args.dry_run)
    elif args.command == "cherry-pick":
        status = cmd_cherry_pick(cfg, args.from_upstream, args.commits, args.dry_run)
    elif args.command == "pull":
        status = cmd_pull(cfg, args.entities, args.from_upstream, args.to_fork,
                          args.dry_run, args.no_code, args.no_locale, args.no_test)
    elif args.command == "dedup":
        status = cmd_dedup(cfg, args.dry_run)
    elif args.command == "resolve":
        status = cmd_resolve(cfg, args.dry_run)
    elif args.command == "audit-patches":
        status = cmd_audit_patches(cfg, args.include_forks, args.namespaces)
    elif args.command == "status":
        status = cmd_status(cfg)
    elif args.command == "clean":
        status = cmd_clean(cfg)

    return status if isinstance(status, int) else 0


if __name__ == "__main__":
    sys.exit(main())
