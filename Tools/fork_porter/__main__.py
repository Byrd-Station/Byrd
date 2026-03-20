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
    pull-path      Pull arbitrary files by path from an upstream
    dedup          Remove duplicate prototypes across forks (priority-based)
    resolve        Discover & pull missing prototypes + textures from upstreams
    diff           Show diffs between local ported files and upstream
    where-from     Show provenance of local files / entity IDs
    log            Show upstream commits touching ported files since last sync
    audit-patches  Scan source files for inline fork-edit markers and report
    status         Show fork health dashboard (dupes, missing, patch count)
    clean          Remove .fork_porter_cache/ directory

Global flags:
    --config FILE  Path to config.yml   (default: Tools/fork_porter/config.yml)
    --verbose      Print detailed log output
"""

from __future__ import annotations

import argparse
import difflib
import fnmatch
import json
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
    cfg["_provenance_path"] = cfg["_cache_dir"] / "provenance.json"
    return cfg


# ── Provenance metadata ─────────────────────────────────────────────────────

def _load_provenance(cfg: dict) -> dict[str, Any]:
    """Load provenance metadata from the cache directory."""
    path = cfg.get("_provenance_path")
    if path and path.is_file():
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return {}
    return {}


def _save_provenance(cfg: dict, prov: dict[str, Any]) -> None:
    """Persist provenance metadata to the cache directory."""
    path = cfg.get("_provenance_path")
    if not path:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(prov, indent=2, sort_keys=True), encoding="utf-8")


def _record_provenance(cfg: dict, command_name: str, touched: set[Path],
                       upstream_name: str | None = None) -> None:
    """Record provenance for touched files: upstream, timestamp, commit SHA."""
    if not touched:
        return
    workspace = cfg["_workspace"]
    cache_dir = cfg["_cache_dir"]
    prov = _load_provenance(cfg)

    # Get upstream HEAD sha if available.
    up_sha = None
    if upstream_name:
        up_path = cache_dir / upstream_name
        if up_path.is_dir():
            result = subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=up_path, capture_output=True, text=True, check=False,
            )
            if result.returncode == 0:
                up_sha = result.stdout.strip()

    import datetime
    ts = datetime.datetime.now(datetime.timezone.utc).isoformat()

    files_section = prov.setdefault("files", {})
    for p in sorted(touched):
        try:
            rel = p.relative_to(workspace).as_posix()
        except ValueError:
            continue
        files_section[rel] = {
            "upstream": upstream_name or "unknown",
            "upstream_sha": up_sha,
            "synced_at": ts,
            "command": command_name,
        }

    _save_provenance(cfg, prov)


# ── Interactive prompt helper ────────────────────────────────────────────────

def _interactive_confirm(prompt: str) -> bool:
    """Ask a yes/no question on stdin. Returns True for yes."""
    try:
        answer = input(f"{prompt} [y/N] ").strip().lower()
        return answer in ("y", "yes")
    except (EOFError, KeyboardInterrupt):
        return False


# ── Upstream repo management ────────────────────────────────────────────────

def _ensure_upstream(
    name: str,
    info: dict,
    cache_dir: Path,
    *,
    refresh: bool = False,
) -> Path:
    """Ensure an upstream repo is present locally.

    When *refresh* is true, fetch latest upstream state into the cache.
    """
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
        if refresh:
            # Pull latest (full clone).
            log.info("Updating upstream '%s' (%s)...", name, branch)
            subprocess.run(
                ["git", "fetch", "origin", branch],
                cwd=repo_dir, check=True, capture_output=True,
            )
            # Refresh whatever is currently included in sparse-checkout.
            # This preserves any prior expansion done by other commands.
            subprocess.run(
                ["git", "checkout", f"origin/{branch}", "--", "."],
                cwd=repo_dir, check=True, capture_output=True,
            )
    else:
        # Full clone repository contents.
        log.info("Cloning upstream '%s' from %s (branch: %s)...", name, repo_url, branch)
        cache_dir.mkdir(parents=True, exist_ok=True)
        subprocess.run(
            ["git", "clone", "--branch", branch, repo_url, str(repo_dir)],
            check=True,
            capture_output=True,
        )

    return repo_dir


def _resolve_upstream_path(
    name: str,
    info: dict,
    cache_dir: Path,
    *,
    refresh: bool = False,
) -> Path:
    """Get local upstream path, optionally refreshing cache from remote."""
    try:
        return _ensure_upstream(name, info, cache_dir, refresh=refresh)
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


def _log_table(title: str, headers: list[str], rows: list[list[Any]]) -> None:
    """Log a column-aligned table for CLI output."""
    if not headers:
        return
    ncols = len(headers)
    # Normalize rows.
    str_rows: list[list[str]] = []
    for row in rows:
        cells = [str(c).replace("\n", " ") for c in row]
        while len(cells) < ncols:
            cells.append("")
        str_rows.append(cells[:ncols])
    # Column widths.
    widths = [len(h) for h in headers]
    for cells in str_rows:
        for i, c in enumerate(cells):
            widths[i] = max(widths[i], len(c))
    # Detect numeric columns for right-alignment.
    numeric = [False] * ncols
    for i in range(ncols):
        if str_rows and all(c[i].lstrip("-").replace(".", "", 1).isdigit() or c[i] == "" for c in str_rows):
            numeric[i] = True
    def _fmt(cells: list[str], bold: bool = False) -> str:
        parts: list[str] = []
        for i, c in enumerate(cells):
            # Last column: skip left-padding but still right-align numbers.
            if i == ncols - 1 and not numeric[i]:
                parts.append(c)
            elif numeric[i]:
                parts.append(c.rjust(widths[i]))
            else:
                parts.append(c.ljust(widths[i]))
        return "  ".join(parts)
    log.info("%s", title)
    log.info("  %s", _fmt(headers))
    # Last column separator: full width if numeric, header width if text.
    seps = ["─" * widths[i] for i in range(ncols - 1)]
    seps.append("─" * (widths[-1] if numeric[-1] else len(headers[-1])))
    log.info("  %s", "  ".join(seps))
    for cells in str_rows:
        log.info("  %s", _fmt(cells))


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
    """Ensure full upstream repo is available locally."""
    return _ensure_upstream(name, info, cache_dir)


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


# ── Non-entity prototype indexing ────────────────────────────────────────────

# Prototype types that are *not* entity but define cross-referenced content.
_NON_ENTITY_TYPE_RE = re.compile(
    r"^\s*-?\s*type:\s+(latheRecipe|reaction|technology|constructionGraph|"
    r"loadout|startingGear|job|gameMap|gamePreset|"
    r"reagent|constructionPrototype|decal|tile)\b",
    re.MULTILINE,
)
_NON_ENTITY_ID_RE = re.compile(r"^\s+id:\s+(\S+)", re.MULTILINE)

# Map file references beyond proto:
_TILE_REF_RE = re.compile(r"\btiles:\s*\n((?:\s+-.*\n)*)", re.MULTILINE)
_TILE_ID_RE = re.compile(r"\b(\w+):\s*\d+")  # "FloorSteel: 42"
_DECAL_REF_RE = re.compile(r"id:\s+(\S+)", re.MULTILINE)  # in decal chunks


def _index_non_entity_prototypes(root: Path) -> dict[str, tuple[str, Path]]:
    """Build {proto_id: (proto_type, file_path)} for non-entity prototypes."""
    index: dict[str, tuple[str, Path]] = {}
    for yml in sorted(root.rglob("*.yml")):
        try:
            text = yml.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        blocks = text.split("\n- ")
        for i, block in enumerate(blocks):
            chunk = block if i == 0 else "- " + block
            type_m = re.search(r"type:\s+(\w+)", chunk)
            id_m = re.search(r"id:\s+(\S+)", chunk)
            if type_m and id_m:
                ptype = type_m.group(1)
                pid = id_m.group(1)
                if ptype != "entity" and pid not in index:
                    index[pid] = (ptype, yml)
    return index


def _extract_non_entity_block(text: str, target_id: str) -> str:
    """Extract a single YAML block with the given id from text."""
    lines = text.split("\n")
    blocks: list[list[str]] = []
    current: list[str] = []
    for line in lines:
        if line.startswith("- ") and current:
            blocks.append(current)
            current = []
        current.append(line)
    if current:
        blocks.append(current)

    for block in blocks:
        joined = "\n".join(block)
        m = re.search(r"id:\s+(\S+)", joined)
        if m and m.group(1) == target_id:
            while block and block[-1].strip() == "":
                block.pop()
            return "\n".join(block) + "\n"
    return ""


def _extract_tile_refs_from_map(text: str) -> set[str]:
    """Extract tile definition names from map file tile grids."""
    tiles: set[str] = set()
    for m in _TILE_ID_RE.finditer(text):
        name = m.group(1)
        if name and not name.isdigit() and name[0].isupper():
            tiles.add(name)
    return tiles


def _extract_decal_refs_from_map(text: str) -> set[str]:
    """Extract decal prototype IDs from map files."""
    decals: set[str] = set()
    for m in re.finditer(r"(?:decalId|id):\s+(\S+)", text):
        val = m.group(1).strip("'\"")
        if val and not val.isdigit():
            decals.add(val)
    return decals


def _extract_audio_refs_from_map(text: str) -> set[str]:
    """Extract audio file paths referenced in map/prototype files."""
    audio: set[str] = set()
    for m in re.finditer(r"(?:path|sound):\s+(/Audio/\S+)", text):
        audio.add(m.group(1).lstrip("/"))
    return audio


# ── Commands ─────────────────────────────────────────────────────────────────


def cmd_dedup(cfg: dict, dry_run: bool, interactive: bool = False) -> int:
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
    skipped_interactive = 0
    touched: set[Path] = set()
    for fpath, ids_to_remove in removals.items():
        # Interactive mode: ask before each file.
        if interactive and not dry_run:
            if not _interactive_confirm(f"  Dedup {len(ids_to_remove)} ID(s) from {fpath.relative_to(proto_root)}?"):
                skipped_interactive += 1
                continue

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
    summary_rows = [
        ["Duplicate IDs", dupes_found],
        ["Action", action],
        ["Files modified", files_modified],
        ["Empty files deleted", files_deleted],
    ]
    if interactive:
        summary_rows.append(["Skipped (interactive)", skipped_interactive])
    _log_table("Dedup summary:", ["Metric", "Value"], summary_rows)
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
    rows: list[list[Any]] = []

    for name, info in targets.items():
        if "path" in info:
            rows.append([name, "skipped", "local path", "-"])
            continue
        try:
            path = _resolve_upstream_path(name, info, cache_dir, refresh=True)
            proto_count = sum(1 for _ in (path / "Resources" / "Prototypes").rglob("*.yml")) if (path / "Resources" / "Prototypes").is_dir() else 0
            rows.append([name, "ok", "-", proto_count])
            succeeded += 1
        except (subprocess.CalledProcessError, FileNotFoundError, ValueError) as e:
            rows.append([name, "failed", str(e), "-"])
            failed += 1

    _log_table("Update results:", ["Upstream", "Status", "Details", "Proto files"], rows)
    _log_table(
        "Update summary:",
        ["Metric", "Value"],
        [["Succeeded", succeeded], ["Failed", failed]],
    )
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


def cmd_update_ported(cfg: dict, forks_filter: list[str] | None, dry_run: bool = False,
                      path_filter: list[str] | None = None, strategy: str = "overwrite",
                      interactive: bool = False) -> int:
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
    skipped = 0
    scanned = 0
    touched: set[Path] = set()
    source_upstream: str | None = None

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

        source_upstream = up_name
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
            for local_file in sorted(local_root.rglob("*")):
                if not local_file.is_file():
                    continue
                if local_file in seen_local:
                    continue
                seen_local.add(local_file)

                # Apply path filter (feature 2).
                if path_filter:
                    rel_str = local_file.relative_to(workspace).as_posix()
                    rel_tail_str = local_file.relative_to(local_root).as_posix()
                    if not any(fnmatch.fnmatch(rel_str, pat) or fnmatch.fnmatch(rel_tail_str, pat)
                               for pat in path_filter):
                        continue

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

                rel_display = local_file.relative_to(workspace)

                # Interactive mode: ask before each file.
                if interactive and not dry_run:
                    if not _interactive_confirm(f"  Update {rel_display}?"):
                        skipped += 1
                        continue

                if dry_run:
                    log.info("  Would update %s <- %s", rel_display, src.relative_to(up_root))
                    updated += 1
                    continue

                # Strategy handling (feature 7).
                if strategy == "diff":
                    # Show diff only, don't write.
                    try:
                        local_text = local_file.read_text(encoding="utf-8", errors="replace")
                        src_text = src.read_text(encoding="utf-8", errors="replace")
                        diff_lines = difflib.unified_diff(
                            local_text.splitlines(keepends=True),
                            src_text.splitlines(keepends=True),
                            fromfile=str(rel_display),
                            tofile=f"upstream/{src.relative_to(up_root)}",
                        )
                        sys.stdout.writelines(diff_lines)
                    except OSError:
                        pass
                    updated += 1
                    continue

                if strategy == "ours":
                    # Keep local version, skip.
                    skipped += 1
                    continue

                # Default "overwrite" or "theirs" strategy: replace with upstream.
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
                log.info("  Updated %s <- %s", rel_display, src.relative_to(up_root))

    verb = "Would update" if dry_run else "Updated"
    _log_table(
        "Update-ported summary:",
        ["Metric", "Value"],
        [
            ["Action", verb],
            ["Strategy", strategy],
            ["Updated files", updated],
            ["Unchanged files", unchanged],
            ["Skipped (interactive/ours)", skipped],
            ["Missing upstream match", missing_src],
            ["Scanned files", scanned],
        ],
    )
    if not dry_run:
        _record_provenance(cfg, "update-ported", touched, source_upstream)
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


def cmd_cherry_pick(cfg: dict, from_upstream: str, commits: list[str],
                    dry_run: bool = False, strategy: str = "3way") -> int:
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
    rows: list[list[Any]] = []
    for commit in commits:
        verify = subprocess.run(
            ["git", "rev-parse", "--verify", f"{commit}^{{commit}}"],
            cwd=up_path,
            capture_output=True,
            text=True,
            check=False,
        )
        if verify.returncode != 0:
            rows.append([commit, "not-found", from_upstream, "commit missing in upstream"])
            failed += 1
            continue

        patch = subprocess.run(
            ["git", "format-patch", "-1", "--stdout", commit],
            cwd=up_path,
            capture_output=True,
            check=False,
        )
        if patch.returncode != 0 or not patch.stdout:
            rows.append([commit, "failed", from_upstream, "failed to export patch"])
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
                rows.append([commit, "would-fail", from_upstream, "git apply --check failed"])
                failed += 1
            else:
                rows.append([commit, "would-apply", from_upstream, "ok"])
                applied += 1
            continue

        # Build git am flags based on strategy (feature 7).
        am_flags = ["git", "am", "--keep-non-patch"]
        if strategy == "theirs":
            am_flags.extend(["--3way", "--strategy-option=theirs"])
        elif strategy == "ours":
            am_flags.extend(["--3way", "--strategy-option=ours"])
        else:
            am_flags.append("-3")
        am_flags.append("-")

        am = subprocess.run(
            am_flags,
            cwd=workspace,
            input=patch.stdout,
            capture_output=True,
            check=False,
        )
        if am.returncode != 0:
            rows.append([commit, "failed", from_upstream, "git am failed"])
            subprocess.run(["git", "am", "--abort"], cwd=workspace, capture_output=True, check=False)
            failed += 1
            continue

        applied += 1
        rows.append([commit, "applied", from_upstream, "ok"])

    _log_table("Cherry-pick results:", ["Commit", "Status", "Upstream", "Details"], rows)
    verb = "Would apply" if dry_run else "Applied"
    _log_table(
        "Cherry-pick summary:",
        ["Metric", "Value"],
        [[verb, f"{applied}/{len(commits)}"], ["Failed", failed]],
    )
    return 1 if failed else 0


def cmd_pull(cfg: dict, entity_ids: list[str], from_upstream: str | None,
             to_fork: str | None, dry_run: bool,
             skip_code: bool, skip_locale: bool,
             skip_test: bool = False,
             interactive: bool = False,
             include_non_entity: bool = False) -> int:
    """Pull entity prototypes and all their dependencies from upstreams.

    Resolves the full dependency tree: parent prototypes, textures/sprites,
    C# component/system source files, and FTL locale entries.
    When include_non_entity is True, also resolves non-entity prototype
    dependencies like lathe recipes, reactions, and technologies (feature 9).
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

    # ── Phase 4b: Non-entity prototype resolution (feature 9) ────────────
    non_entity_files: dict[Path, tuple[str, Path]] = {}
    if include_non_entity and all_entity_ids:
        log.info("Searching for non-entity prototypes referencing pulled entities...")
        # Build index of non-entity prototypes in upstreams.
        for up_name, (up_path, _) in upstream_data.items():
            up_proto = up_path / "Resources" / "Prototypes"
            if not up_proto.is_dir():
                continue
            for yml in up_proto.rglob("*.yml"):
                try:
                    text = yml.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue
                # Check if any pulled entity ID is referenced.
                refs_any = False
                for eid in all_entity_ids:
                    if eid in text:
                        refs_any = True
                        break
                if not refs_any:
                    continue
                # Check if this file has non-entity prototypes.
                if not _NON_ENTITY_TYPE_RE.search(text):
                    continue
                rel = yml.relative_to(up_path / "Resources" / "Prototypes")
                parts = rel.parts
                if parts and parts[0].startswith("_"):
                    rel = Path(*parts[1:]) if len(parts) > 1 else rel
                dest = (proto_root / fork_dir / rel) if fork_dir else (proto_root / rel)
                if not dest.exists() and dest not in non_entity_files and dest not in proto_files:
                    non_entity_files[dest] = (up_name, yml)
                    log.info("    Non-entity proto: %s ← %s", rel, up_name)

    # ── Phase 5: Summary and copy ─────────────────────────────────────────
    total = len(proto_files) + len(tex_dirs) + len(cs_files) + len(test_files) + len(locale_entries) + len(non_entity_files)
    _log_table(
        "Pull summary:",
        ["Category", "Count"],
        [
            ["Prototype files", len(proto_files)],
            ["Non-entity protos", len(non_entity_files)],
            ["Texture dirs", len(tex_dirs)],
            ["C# source files", len(cs_files)],
            ["Test files", len(test_files)],
            ["Locale files", len(locale_entries)],
            ["Total", total],
        ],
    )

    if total == 0:
        log.info("Nothing new to copy.")
        return 0

    action = "Would copy" if dry_run else "Copying"
    touched: set[Path] = set()

    op_rows: list[list[Any]] = []
    for dest, (up_name, _) in sorted(proto_files.items()):
        op_rows.append(["proto", dest.relative_to(proto_root), up_name])
    for dest, (up_name, _) in sorted(non_entity_files.items()):
        op_rows.append(["non-entity", dest.relative_to(proto_root), up_name])
    for dest, (up_name, _) in sorted(tex_dirs.items()):
        op_rows.append(["texture", dest.relative_to(tex_root), up_name])
    for dest, (up_name, _) in sorted(cs_files.items()):
        op_rows.append(["csharp", dest.relative_to(workspace), up_name])
    for dest, (up_name, _) in sorted(test_files.items()):
        op_rows.append(["test", dest.relative_to(workspace), up_name])
    for dest in sorted(locale_entries):
        op_rows.append(["locale", dest.relative_to(locale_root), "mixed"])

    _log_table(f"{action} operations:", ["Type", "Path", "Upstream"], op_rows)

    # Interactive mode: let user pick which operations to apply (feature 10).
    if interactive and not dry_run:
        filtered_proto: dict[Path, tuple[str, Path]] = {}
        for dest, val in sorted(proto_files.items()):
            if _interactive_confirm(f"  Copy proto {dest.relative_to(proto_root)}?"):
                filtered_proto[dest] = val
        proto_files = filtered_proto

        filtered_ne: dict[Path, tuple[str, Path]] = {}
        for dest, val in sorted(non_entity_files.items()):
            if _interactive_confirm(f"  Copy non-entity {dest.relative_to(proto_root)}?"):
                filtered_ne[dest] = val
        non_entity_files = filtered_ne

        filtered_tex: dict[Path, tuple[str, Path]] = {}
        for dest, val in sorted(tex_dirs.items()):
            if _interactive_confirm(f"  Copy texture {dest.relative_to(tex_root)}?"):
                filtered_tex[dest] = val
        tex_dirs = filtered_tex

        filtered_cs: dict[Path, tuple[str, Path]] = {}
        for dest, val in sorted(cs_files.items()):
            if _interactive_confirm(f"  Copy C# {dest.relative_to(workspace)}?"):
                filtered_cs[dest] = val
        cs_files = filtered_cs

        filtered_test: dict[Path, tuple[str, Path]] = {}
        for dest, val in sorted(test_files.items()):
            if _interactive_confirm(f"  Copy test {dest.relative_to(workspace)}?"):
                filtered_test[dest] = val
        test_files = filtered_test

        filtered_locale: dict[Path, str] = {}
        for dest, content in sorted(locale_entries.items()):
            if _interactive_confirm(f"  Copy locale {dest.relative_to(locale_root)}?"):
                filtered_locale[dest] = content
        locale_entries = filtered_locale

        total = len(proto_files) + len(non_entity_files) + len(tex_dirs) + len(cs_files) + len(test_files) + len(locale_entries)

    for dest, (up_name, src) in sorted(proto_files.items()):
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
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copytree(src, dest)
            touched.add(dest)

    for dest, (up_name, src) in sorted(cs_files.items()):
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

    # Copy non-entity prototype files (feature 9).
    for dest, (up_name, src) in sorted(non_entity_files.items()):
        if not dry_run:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            touched.add(dest)

    if dry_run:
        log.info("\nDry run -- no files written. Remove --dry-run to apply.")
    else:
        log.info("\nPull complete. %d files written.", total)
        if cs_files:
            log.info("NOTE: C# namespaces were updated automatically -- review for correctness.")
        _record_provenance(cfg, "pull", touched, source_upstream)
        _commit_touched_paths(cfg, "pull", touched)
    return 0


def cmd_resolve(cfg: dict, dry_run: bool, interactive: bool = False) -> int:
    """Discover missing prototypes in map files and pull from upstreams.

    Also resolves tile definitions, decal prototypes, and audio paths
    referenced by map files (feature 6).
    """
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

    # Also build local non-entity index for tile/decal resolution (feature 6).
    local_non_entity = _index_non_entity_prototypes(proto_root)
    log.info("  %d local non-entity prototypes indexed.", len(local_non_entity))

    # 2. Build upstream indexes (full checkout for C#/locale).
    upstream_paths: dict[str, Path] = {}
    upstream_indexes: dict[str, dict[str, Path]] = {}
    upstream_ne_indexes: dict[str, dict[str, tuple[str, Path]]] = {}
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
            upstream_ne_indexes[name] = _index_non_entity_prototypes(up_proto)
            log.info("  %d entities, %d non-entity.", len(upstream_indexes[name]), len(upstream_ne_indexes[name]))

    # 3. Collect entity refs from map files.
    missing: set[str] = set()
    missing_refs: dict[str, set[str]] = defaultdict(set)
    # Also collect tile/decal/audio refs (feature 6).
    missing_tiles: set[str] = set()
    missing_decals: set[str] = set()
    missing_audio: set[str] = set()
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
                if eid and eid not in local_index:
                    missing.add(eid)
                    missing_refs[eid].add(mapfile.relative_to(workspace).as_posix())

            # Feature 6: Extract tile/decal/audio references.
            for tile_name in _extract_tile_refs_from_map(text):
                if tile_name not in local_non_entity and tile_name not in local_index:
                    missing_tiles.add(tile_name)
            for decal_id in _extract_decal_refs_from_map(text):
                if decal_id not in local_non_entity and decal_id not in local_index:
                    missing_decals.add(decal_id)
            for audio_path in _extract_audio_refs_from_map(text):
                full = workspace / "Resources" / audio_path
                if not full.exists():
                    missing_audio.add(audio_path)

    log.info("Found %d missing prototype references in maps.", len(missing))
    if missing_tiles:
        log.info("Found %d missing tile definitions.", len(missing_tiles))
    if missing_decals:
        log.info("Found %d missing decal prototypes.", len(missing_decals))
    if missing_audio:
        log.info("Found %d missing audio files.", len(missing_audio))

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

    # 6b. Resolve missing tiles and decals from upstreams (feature 6).
    ne_files_to_copy: dict[Path, tuple[str, Path]] = {}
    audio_to_copy: dict[Path, tuple[str, Path]] = {}
    resolved_tiles: set[str] = set()
    resolved_decals: set[str] = set()
    resolved_audio: set[str] = set()

    all_missing_ne = missing_tiles | missing_decals
    if all_missing_ne:
        for ne_id in all_missing_ne:
            for up_name, ne_idx in upstream_ne_indexes.items():
                if ne_id in ne_idx:
                    ptype, src_file = ne_idx[ne_id]
                    up_root = upstream_paths[up_name]
                    rel = src_file.relative_to(up_root / "Resources" / "Prototypes")
                    dest = proto_root / rel
                    if not dest.exists() and dest not in ne_files_to_copy:
                        ne_files_to_copy[dest] = (up_name, src_file)
                    if ne_id in missing_tiles:
                        resolved_tiles.add(ne_id)
                    if ne_id in missing_decals:
                        resolved_decals.add(ne_id)
                    break

    if missing_audio:
        for audio_path in missing_audio:
            for up_name, up_path in upstream_paths.items():
                src = up_path / "Resources" / audio_path
                if src.is_file():
                    dest = workspace / "Resources" / audio_path
                    if not dest.exists() and dest not in audio_to_copy:
                        audio_to_copy[dest] = (up_name, src)
                        resolved_audio.add(audio_path)
                    break

    # 7. Copy all files.
    copied_files = 0
    copied_tex = 0
    copied_cs = 0
    copied_locale = 0
    copied_ne = 0
    copied_audio = 0
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
        if interactive and not _interactive_confirm(f"  Copy locale {dest.relative_to(locale_root)}?"):
            continue
        if dry_run:
            log.info("  Would copy locale %s", dest.relative_to(locale_root))
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(content, encoding="utf-8")
            touched.add(dest)
        copied_locale += 1

    # Copy non-entity (tile/decal) prototype files (feature 6).
    for dest, (up_name, src) in ne_files_to_copy.items():
        if dest.exists():
            continue
        if interactive and not _interactive_confirm(f"  Copy tile/decal {dest.relative_to(proto_root)}?"):
            continue
        if dry_run:
            log.info("  Would copy tile/decal %s ← %s", dest.relative_to(proto_root), up_name)
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            touched.add(dest)
        copied_ne += 1

    # Copy audio files (feature 6).
    for dest, (up_name, src) in audio_to_copy.items():
        if dest.exists():
            continue
        if interactive and not _interactive_confirm(f"  Copy audio {dest.relative_to(workspace)}?"):
            continue
        if dry_run:
            log.info("  Would copy audio %s ← %s", dest.relative_to(workspace), up_name)
        else:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
            touched.add(dest)
        copied_audio += 1

    action = "Would copy" if dry_run else "Copied"
    log.info(
        "Resolve complete: %s %d proto files, %d textures, %d C# files, %d locale files, "
        "%d tile/decal files, %d audio files. %d unresolvable IDs.",
        action, copied_files, copied_tex, copied_cs, copied_locale,
        copied_ne, copied_audio, len(unresolvable),
    )
    # Summary for feature 6 non-entity resolution.
    if missing_tiles or missing_decals or missing_audio:
        ne_rows: list[list[Any]] = []
        ne_rows.append(["Missing tiles", len(missing_tiles)])
        ne_rows.append(["Resolved tiles", len(resolved_tiles)])
        ne_rows.append(["Missing decals", len(missing_decals)])
        ne_rows.append(["Resolved decals", len(resolved_decals)])
        ne_rows.append(["Missing audio", len(missing_audio)])
        ne_rows.append(["Resolved audio", len(resolved_audio)])
        _log_table("Non-entity resolution:", ["Metric", "Count"], ne_rows)
    if missing_refs:
        resolve_rows: list[list[Any]] = []
        for eid in sorted(missing_refs):
            refs = sorted(missing_refs[eid])
            status = "unresolvable" if eid in unresolvable else "resolved"
            if not refs:
                resolve_rows.append([eid, status, "(none)"])
                continue
            for idx, map_path in enumerate(refs):
                resolve_rows.append([eid if idx == 0 else "", status if idx == 0 else "", map_path])
        _log_table("Missing prototype references:", ["Prototype ID", "Status", "Map"], resolve_rows)
    if not dry_run:
        _record_provenance(cfg, "resolve", touched)
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
    detect_orphans: bool = False,
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
    _log_table(
        "Audit summary:",
        ["Metric", "Value"],
        [
            ["Total markers", total],
            ["Files with markers", len(report)],
            ["Block pairs", block_pairs],
            ["Point markers", total - block_pairs * 2],
        ],
    )

    detail_rows: list[list[Any]] = []
    for filepath, patches in sorted(report.items()):
        for p in patches:
            detail_rows.append([filepath, p["line"], p["fork"], p["type"], p["text"]])
    if detail_rows:
        _log_table(
            "Audit marker details:",
            ["File", "Line", "Fork", "Type", "Marker"],
            detail_rows,
        )

    # ── Summary by fork ──────────────────────────────────────────────────
    log.info("\n--- Summary by fork ---")
    by_fork: dict[str, dict[str, int]] = defaultdict(lambda: {"point": 0, "start": 0, "end": 0})
    by_format: dict[str, int] = defaultdict(int)
    for filepath, patches in report.items():
        ext = filepath.rsplit(".", 1)[-1] if "." in filepath else "?"
        for p in patches:
            by_fork[p["fork"]][p["type"]] += 1
            by_format[ext] += 1

    by_fork_rows: list[list[Any]] = []
    for fk, counts in sorted(by_fork.items(), key=lambda x: -sum(x[1].values())):
        t = sum(counts.values())
        blocks = min(counts["start"], counts["end"])
        pts = counts["point"]
        by_fork_rows.append([fk, t, blocks, pts])
    if by_fork_rows:
        _log_table("Summary by fork:", ["Fork", "Total", "Blocks", "Point"], by_fork_rows)

    by_type_rows = [[f".{ext}", count] for ext, count in sorted(by_format.items(), key=lambda x: -x[1])]
    if by_type_rows:
        _log_table("Summary by file type:", ["Type", "Markers"], by_type_rows)

    # ── Warn about unmatched blocks ──────────────────────────────────────
    if unmatched_starts or unmatched_ends:
        unmatched_rows: list[list[Any]] = []
        for u in unmatched_starts:
            unmatched_rows.append(["start", u["file"], u["line"], u["fork"]])
        for u in unmatched_ends:
            unmatched_rows.append(["end", u["file"], u["line"], u["fork"]])
        _log_table(
            "Unmatched block markers:",
            ["Kind", "File", "Line", "Fork"],
            unmatched_rows,
        )

    # ── Feature 8: Orphan detection ──────────────────────────────────────
    if detect_orphans:
        log.info("\n--- Orphan Detection ---")
        # Collect all fork marker stems found in base-game files.
        base_markers_by_fork: dict[str, set[str]] = defaultdict(set)
        for filepath, patches in report.items():
            # Only consider markers in base-game files (no /_ForkName/).
            if "/_" in filepath:
                continue
            for p in patches:
                base_markers_by_fork[p["fork"]].add(filepath)

        # Now scan fork-scoped files for references to base files that have markers.
        orphan_rows: list[list[Any]] = []
        for fork_key, base_files_with_markers in base_markers_by_fork.items():
            fork_info = forks.get(fork_key, {})
            fork_dirs: list[str] = []
            if fork_info.get("directory"):
                fork_dirs.append(str(fork_info["directory"]))
            fork_dirs.extend(str(x) for x in fork_info.get("alt_directories", []) or [])
            if not fork_dirs:
                continue

            # Collect class/type names from base-game marker files.
            base_symbols: set[str] = set()
            for base_file_rel in base_files_with_markers:
                base_path = workspace / base_file_rel
                if not base_path.is_file():
                    continue
                try:
                    text = base_path.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue
                for m in _CLASS_DEF_RE.finditer(text):
                    base_symbols.add(m.group(1))

            if not base_symbols:
                continue

            # Scan fork-scoped files for references to these symbols.
            for fork_dir in fork_dirs:
                for scan_base in ("Content.Client", "Content.Server", "Content.Shared"):
                    fork_path = workspace / scan_base / fork_dir
                    if not fork_path.is_dir():
                        continue
                    for cs_file in fork_path.rglob("*.cs"):
                        try:
                            text = cs_file.read_text(encoding="utf-8", errors="replace")
                        except OSError:
                            continue
                        for sym in base_symbols:
                            if sym in text:
                                orphan_rows.append([
                                    cs_file.relative_to(workspace).as_posix(),
                                    fork_key,
                                    sym,
                                    "references base-game patched class",
                                ])
                                break

        if orphan_rows:
            _log_table(
                "Potential orphan dependencies (fork files → base patches):",
                ["Fork File", "Fork", "Referenced Symbol", "Note"],
                orphan_rows,
            )
        else:
            log.info("No orphan dependencies detected.")

    return 0


# ── Feature 1: diff command ─────────────────────────────────────────────────


def cmd_diff(cfg: dict, forks_filter: list[str] | None, path_filter: list[str] | None,
             stat_only: bool = False) -> int:
    """Show unified diffs between local ported files and their upstream versions."""
    workspace = cfg["_workspace"]
    forks = cfg.get("forks", {})
    upstreams = cfg.get("upstreams", {})
    cache_dir = cfg["_cache_dir"]

    selected_forks, unknown = _resolve_fork_names(forks, forks_filter)
    if unknown:
        log.warning("Unknown fork selector(s): %s", ", ".join(unknown))
    if not selected_forks:
        log.info("No matching forks selected.")
        return 1

    changed = 0
    identical = 0
    missing_src = 0
    diff_rows: list[list[Any]] = []

    for fk in selected_forks:
        info = forks.get(fk, {})
        local_aliases: list[str] = []
        if info.get("directory"):
            local_aliases.append(str(info["directory"]))
        local_aliases.extend(str(x) for x in info.get("alt_directories", []) or [])
        local_aliases = list(dict.fromkeys(local_aliases))
        if not local_aliases:
            continue

        up_name = info.get("upstream") or _guess_upstream_for_fork(fk, info, upstreams)
        if not up_name or up_name not in upstreams:
            continue

        try:
            up_root = _ensure_upstream_full(up_name, upstreams[up_name], cache_dir)
        except Exception:
            continue

        # Scan all file types.
        roots: list[tuple[Path, Path]] = []
        for alias in local_aliases:
            roots.append((workspace / "Resources" / "Prototypes" / alias, Path("Resources") / "Prototypes"))
            roots.append((workspace / "Resources" / "Textures" / alias, Path("Resources") / "Textures"))
        locale_root = workspace / "Resources" / "Locale"
        if locale_root.is_dir():
            for lang_dir in locale_root.iterdir():
                if lang_dir.is_dir():
                    for alias in local_aliases:
                        roots.append((lang_dir / alias, Path("Resources") / "Locale" / lang_dir.name))
        for p in sorted(workspace.iterdir()):
            if p.is_dir() and p.name.startswith("Content."):
                for alias in local_aliases:
                    roots.append((p / alias, Path(p.name)))

        for local_root, up_base in roots:
            if not local_root.is_dir():
                continue
            for local_file in sorted(local_root.rglob("*")):
                if not local_file.is_file():
                    continue
                rel_tail = local_file.relative_to(local_root)
                # Apply path filter.
                if path_filter:
                    rel_str = local_file.relative_to(workspace).as_posix()
                    if not any(fnmatch.fnmatch(rel_str, pat) or fnmatch.fnmatch(rel_tail.as_posix(), pat)
                               for pat in path_filter):
                        continue

                candidates = _build_update_ported_candidates(up_root, up_base, rel_tail, local_aliases)
                src = _first_existing_path(candidates)
                if src is None:
                    missing_src += 1
                    continue

                try:
                    local_text = local_file.read_text(encoding="utf-8", errors="replace")
                    upstream_text = src.read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue

                if local_text == upstream_text:
                    identical += 1
                    continue

                changed += 1
                rel_display = local_file.relative_to(workspace).as_posix()
                diff_rows.append([rel_display, fk, up_name, "changed"])

                if not stat_only:
                    # Print unified diff.
                    diff_lines = difflib.unified_diff(
                        upstream_text.splitlines(keepends=True),
                        local_text.splitlines(keepends=True),
                        fromfile=f"upstream/{up_name}/{rel_tail}",
                        tofile=rel_display,
                    )
                    sys.stdout.writelines(diff_lines)
                    sys.stdout.write("\n")

    _log_table(
        "Diff summary:",
        ["Metric", "Value"],
        [
            ["Changed files", changed],
            ["Identical files", identical],
            ["No upstream match", missing_src],
        ],
    )
    if stat_only and diff_rows:
        _log_table("Changed files:", ["File", "Fork", "Upstream", "Status"], diff_rows)
    return 0


# ── Feature 4: where-from command ───────────────────────────────────────────


def cmd_where_from(cfg: dict, targets: list[str]) -> int:
    """Show provenance of local files or entity IDs."""
    workspace = cfg["_workspace"]
    proto_root = cfg["_proto_root"]
    forks = cfg.get("forks", {})
    upstreams = cfg.get("upstreams", {})
    cache_dir = cfg["_cache_dir"]
    prov = _load_provenance(cfg)
    files_prov = prov.get("files", {})

    rows: list[list[Any]] = []
    for target in targets:
        # Check if it's a file path.
        target_path = workspace / target
        if target_path.is_file():
            rel = target_path.relative_to(workspace).as_posix()
            fk = _classify_fork(target_path, forks) or "base"
            entry = files_prov.get(rel, {})
            rows.append([
                rel,
                fk,
                entry.get("upstream", "unknown"),
                entry.get("synced_at", "unknown"),
                entry.get("upstream_sha", "unknown")[:12] if entry.get("upstream_sha") else "unknown",
            ])
            continue

        # Check if it's an entity ID.
        local_index = _index_prototypes(proto_root)
        if target in local_index:
            fpath = local_index[target]
            rel = fpath.relative_to(workspace).as_posix()
            fk = _classify_fork(fpath, forks) or "base"
            entry = files_prov.get(rel, {})

            # Also search upstreams.
            found_in: list[str] = []
            for up_name, up_info in upstreams.items():
                try:
                    up_path = _resolve_upstream_path(up_name, up_info, cache_dir)
                    up_proto = up_path / "Resources" / "Prototypes"
                    if up_proto.is_dir():
                        up_idx = _index_prototypes(up_proto)
                        if target in up_idx:
                            found_in.append(up_name)
                except Exception:
                    continue

            rows.append([
                f"{target} → {rel}",
                fk,
                entry.get("upstream", ", ".join(found_in) if found_in else "unknown"),
                entry.get("synced_at", "unknown"),
                entry.get("upstream_sha", "unknown")[:12] if entry.get("upstream_sha") else "unknown",
            ])
            continue

        rows.append([target, "?", "not found", "-", "-"])

    _log_table(
        "Provenance:",
        ["Target", "Fork", "Upstream", "Last Synced", "Upstream SHA"],
        rows,
    )
    return 0


# ── Feature 5: log command (stale/upstream changelog) ───────────────────────


def cmd_log(cfg: dict, forks_filter: list[str] | None, limit: int = 20) -> int:
    """Show upstream commits touching ported files since last sync."""
    workspace = cfg["_workspace"]
    forks = cfg.get("forks", {})
    upstreams = cfg.get("upstreams", {})
    cache_dir = cfg["_cache_dir"]
    prov = _load_provenance(cfg)
    files_prov = prov.get("files", {})

    selected_forks, unknown = _resolve_fork_names(forks, forks_filter)
    if unknown:
        log.warning("Unknown fork selector(s): %s", ", ".join(unknown))

    # Group files by upstream with their last-synced sha.
    upstream_files: dict[str, dict[str, str | None]] = defaultdict(dict)
    for rel, entry in files_prov.items():
        up = entry.get("upstream")
        sha = entry.get("upstream_sha")
        if up:
            upstream_files[up][rel] = sha

    if not upstream_files:
        log.info("No provenance data available. Run 'pull' or 'update-ported' to populate.")
        return 0

    total_commits = 0
    for up_name, file_shas in upstream_files.items():
        if up_name not in upstreams:
            continue

        # Find oldest sha among synced files.
        shas = [s for s in file_shas.values() if s]
        if not shas:
            log.info("Upstream '%s': no SHA recorded, cannot determine changes.", up_name)
            continue

        try:
            up_path = _ensure_upstream_full_history(up_name, upstreams[up_name], cache_dir)
        except Exception as e:
            log.warning("Cannot access upstream '%s': %s", up_name, e)
            continue

        # Use the oldest SHA as the since-point.
        since_sha = shas[0]

        result = subprocess.run(
            ["git", "log", "--oneline", f"--max-count={limit}", f"{since_sha}..HEAD"],
            cwd=up_path, capture_output=True, text=True, check=False,
        )
        if result.returncode != 0:
            log.warning("Could not get log for upstream '%s' since %s", up_name, since_sha[:12])
            continue

        commits = result.stdout.strip().splitlines()
        if not commits:
            log.info("Upstream '%s': up-to-date (no new commits since %s).", up_name, since_sha[:12])
            continue

        total_commits += len(commits)
        log.info("Upstream '%s': %d new commit(s) since %s:", up_name, len(commits), since_sha[:12])
        for line in commits:
            log.info("  %s", line)

    if total_commits == 0:
        log.info("All tracked upstreams appear up-to-date.")
    else:
        log.info("Total: %d new upstream commit(s).", total_commits)
    return 0


# ── Feature 3: pull-path command ────────────────────────────────────────────


def cmd_pull_path(cfg: dict, paths: list[str], from_upstream: str | None,
                  to_fork: str | None, dry_run: bool) -> int:
    """Pull arbitrary files/directories by path from an upstream."""
    workspace = cfg["_workspace"]
    cache_dir = cfg["_cache_dir"]
    upstreams = cfg.get("upstreams", {})
    forks = cfg.get("forks", {})
    source_order = cfg.get("resolve_source_order", list(upstreams.keys()))
    search_order = [from_upstream] if from_upstream else source_order

    # Determine target fork dir.
    fork_dir: str | None = None
    if to_fork:
        fork_info = forks.get(to_fork, {})
        fork_dir = fork_info.get("directory") if "directory" in fork_info else f"_{to_fork}"

    copied = 0
    touched: set[Path] = set()
    source_upstream_name: str | None = None

    for rel_path_str in paths:
        rel_path = Path(rel_path_str)
        found = False

        for up_name in search_order:
            info = upstreams.get(up_name)
            if not info:
                continue
            try:
                up_path = _resolve_upstream_path(up_name, info, cache_dir)
            except Exception:
                continue

            src = up_path / rel_path
            if not src.exists():
                continue

            source_upstream_name = up_name
            # Auto-detect fork dir from upstream if not specified.
            if not fork_dir and not to_fork:
                parts = rel_path.parts
                for i, part in enumerate(parts):
                    if part.startswith("_"):
                        fork_dir = part
                        break

            if src.is_file():
                # Determine destination.
                if fork_dir:
                    parts = list(rel_path.parts)
                    # Strip upstream fork dir, insert ours.
                    stripped = [p for p in parts if not p.startswith("_")]
                    # Insert fork_dir after the first directory (e.g., Content.Shared/_Omu/...)
                    if stripped:
                        dest = workspace / Path(stripped[0]) / fork_dir / Path(*stripped[1:])
                    else:
                        dest = workspace / fork_dir / rel_path
                else:
                    dest = workspace / rel_path

                if dest.exists():
                    log.info("  Skipping (exists): %s", dest.relative_to(workspace))
                else:
                    if dry_run:
                        log.info("  Would copy: %s ← %s/%s", dest.relative_to(workspace), up_name, rel_path)
                    else:
                        dest.parent.mkdir(parents=True, exist_ok=True)
                        text = src.read_text(encoding="utf-8", errors="replace")
                        if src.suffix == ".cs":
                            text = _rewrite_namespace_for_destination(src, up_path, dest, workspace, text)
                        dest.write_text(text, encoding="utf-8")
                        touched.add(dest)
                    copied += 1
            elif src.is_dir():
                for src_file in sorted(src.rglob("*")):
                    if not src_file.is_file():
                        continue
                    file_rel = src_file.relative_to(up_path)
                    if fork_dir:
                        parts = list(file_rel.parts)
                        stripped = [p for p in parts if not p.startswith("_")]
                        if stripped:
                            dest = workspace / Path(stripped[0]) / fork_dir / Path(*stripped[1:])
                        else:
                            dest = workspace / fork_dir / file_rel
                    else:
                        dest = workspace / file_rel

                    if dest.exists():
                        continue
                    if dry_run:
                        log.info("  Would copy: %s ← %s/%s", dest.relative_to(workspace), up_name, file_rel)
                    else:
                        dest.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(src_file, dest)
                        touched.add(dest)
                    copied += 1

            found = True
            log.info("Found '%s' in upstream '%s'.", rel_path_str, up_name)
            break

        if not found:
            log.warning("'%s' not found in any upstream.", rel_path_str)

    action = "Would copy" if dry_run else "Copied"
    log.info("%s %d file(s).", action, copied)

    if not dry_run and touched:
        _record_provenance(cfg, "pull-path", touched, source_upstream_name)
        _commit_touched_paths(cfg, "pull-path", touched)
    return 0


def cmd_clean(cfg: dict) -> int:
    """Remove the .fork_porter_cache/ directory."""
    cache_dir = cfg["_cache_dir"]
    if cache_dir.is_dir():
        shutil.rmtree(cache_dir)
        log.info("Removed cache: %s", cache_dir)
    else:
        log.info("No cache directory to remove.")
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
                eid = m.group(1).strip("'\"")
                if eid and eid not in local_ids:
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
    fork_rows = [[fk, count] for fk, count in sorted(fork_counts.items(), key=lambda x: -x[1])]
    fork_rows.append(["TOTAL", sum(fork_counts.values())])
    _log_table("Entity prototypes by fork:", ["Fork", "Entities"], fork_rows)
    _log_table(
        "Health summary:",
        ["Metric", "Value"],
        [["Duplicate IDs across forks", dupes], ["Missing map prototype refs", missing_map_refs]],
    )
    if patch_counts:
        total_patches = sum(patch_counts.values())
        patch_rows = [["TOTAL", total_patches]]
        patch_rows.extend([[fk, count] for fk, count in sorted(patch_counts.items(), key=lambda x: -x[1])])
        _log_table("Inline patch markers:", ["Fork", "Markers"], patch_rows)
    else:
        _log_table("Inline patch markers:", ["Fork", "Markers"], [["-", "no marker_names configured"]])
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
    dedup_p.add_argument("--interactive", action="store_true",
                         help="Prompt before each file modification.")

    resolve_p = sub.add_parser("resolve", help="Pull missing prototypes from upstreams.")
    resolve_p.add_argument(*_dry["args"], **_dry["kwargs"])
    resolve_p.add_argument("--interactive", action="store_true",
                           help="Prompt before copying each file.")

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
    update_ported_p.add_argument(
        "--path", dest="path_filter", nargs="+",
        help="Only update files matching these path patterns (fnmatch globs).",
    )
    update_ported_p.add_argument(
        "--strategy", choices=["overwrite", "diff", "ours", "theirs"],
        default="overwrite",
        help="Merge strategy: overwrite (default), diff (show only), ours (keep local), theirs (take upstream).",
    )
    update_ported_p.add_argument("--interactive", action="store_true",
                                  help="Prompt before each file update.")

    cherry_p = sub.add_parser("cherry-pick", help="Apply specific upstream commits to this repository.")
    cherry_p.add_argument("commits", nargs="+", help="Upstream commit SHA(s) to apply.")
    cherry_p.add_argument("--from", dest="from_upstream", required=True,
                          help="Configured upstream name to cherry-pick from.")
    cherry_p.add_argument("--strategy", choices=["3way", "theirs", "ours"],
                          default="3way",
                          help="Git apply strategy: 3way (default), theirs, ours.")
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
    pull_p.add_argument("--interactive", action="store_true",
                        help="Prompt before copying each file.")
    pull_p.add_argument("--include-non-entity", action="store_true",
                        help="Also pull non-entity prototypes (recipes, reactions, etc.).")
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
    audit_p.add_argument("--detect-orphans", action="store_true",
                         help="Also detect fork-scoped files that reference patched base-game classes.")

    # -- New commands --
    diff_p = sub.add_parser("diff", help="Show unified diffs between local ported files and upstream.")
    diff_p.add_argument("forks", nargs="*",
                        help="Optional fork selector(s) to limit scope.")
    diff_p.add_argument("--path", dest="path_filter", nargs="+",
                        help="Only diff files matching these path patterns (fnmatch globs).")
    diff_p.add_argument("--stat", action="store_true",
                        help="Show diffstat summary instead of full diffs.")

    where_from_p = sub.add_parser("where-from", help="Show provenance for files or entity IDs.")
    where_from_p.add_argument("targets", nargs="+",
                              help="File path(s) or entity prototype ID(s) to look up.")

    log_p = sub.add_parser("log", help="Show upstream commit changelog since last sync.")
    log_p.add_argument("forks", nargs="*",
                       help="Optional fork selector(s) to limit scope.")
    log_p.add_argument("-n", "--limit", type=int, default=20,
                       help="Max commits to show per upstream (default: 20).")

    pull_path_p = sub.add_parser("pull-path", help="Pull arbitrary files/directories by path from upstreams.")
    pull_path_p.add_argument("paths", nargs="+",
                             help="Upstream-relative file or directory path(s) to pull.")
    pull_path_p.add_argument("--from", dest="from_upstream",
                             help="Upstream to pull from (default: search all).")
    pull_path_p.add_argument("--to", dest="to_fork",
                             help="Target fork key for destination directory.")
    pull_path_p.add_argument(*_dry["args"], **_dry["kwargs"])
    sub.add_parser("status", help="Fork health dashboard.")
    sub.add_parser("clean", help="Remove .fork_porter_cache/ directory.")

    args = parser.parse_args(argv)

    class _Formatter(logging.Formatter):
        def format(self, record: logging.LogRecord) -> str:
            if record.levelno <= logging.INFO:
                return record.getMessage()
            return f"{record.levelname:<7s} {record.getMessage()}"

    handler = logging.StreamHandler()
    handler.setFormatter(_Formatter())
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        handlers=[handler],
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
        status = cmd_update_ported(cfg, args.forks, args.dry_run,
                                   path_filter=args.path_filter,
                                   strategy=args.strategy,
                                   interactive=args.interactive)
    elif args.command == "cherry-pick":
        status = cmd_cherry_pick(cfg, args.from_upstream, args.commits,
                                 args.dry_run, strategy=args.strategy)
    elif args.command == "pull":
        status = cmd_pull(cfg, args.entities, args.from_upstream, args.to_fork,
                          args.dry_run, args.no_code, args.no_locale, args.no_test,
                          interactive=args.interactive,
                          include_non_entity=args.include_non_entity)
    elif args.command == "dedup":
        status = cmd_dedup(cfg, args.dry_run, interactive=args.interactive)
    elif args.command == "resolve":
        status = cmd_resolve(cfg, args.dry_run, interactive=args.interactive)
    elif args.command == "audit-patches":
        status = cmd_audit_patches(cfg, args.include_forks, args.namespaces,
                                   detect_orphans=args.detect_orphans)
    elif args.command == "diff":
        status = cmd_diff(cfg, args.forks or None, args.path_filter,
                          stat_only=args.stat)
    elif args.command == "where-from":
        status = cmd_where_from(cfg, args.targets)
    elif args.command == "log":
        status = cmd_log(cfg, args.forks or None, limit=args.limit)
    elif args.command == "pull-path":
        status = cmd_pull_path(cfg, args.paths, args.from_upstream,
                               args.to_fork, args.dry_run)
    elif args.command == "status":
        status = cmd_status(cfg)
    elif args.command == "clean":
        status = cmd_clean(cfg)

    return status if isinstance(status, int) else 0


if __name__ == "__main__":
    sys.exit(main())
