# fork_porter

OmuStation fork content management tool.

## SYNOPSIS

```
python -m Tools.fork_porter [--config PATH] [--verbose] <command> [options]
```

## DESCRIPTION

fork_porter manages content ported from upstream SS14 forks into OmuStation, including upstream repo caching, prototype pulling, deduplication, map resolution, patch auditing, and status reporting.

Mutating commands auto-commit touched files if `history.auto_commit` is enabled in `config.yml`.

Configuration is read from `Tools/fork_porter/config.yml`. Use `update` to refresh upstream state; other commands use the current cache.
## COMMANDS

### update

Fetch or clone all configured upstream repos.

```
python -m Tools.fork_porter update [name ...]
```
Clones or updates each entry in the `upstreams:` config block. If a repo doesn't exist locally, it is cloned. If it exists, it is updated to the latest.


discover new content. Use `pull` or `resolve` for new imports.
### update-ported

Refresh files already ported into fork directories from upstream.

```
python -m Tools.fork_porter update-ported [fork ...] [--dry-run] [--path GLOB ...] [--strategy STRATEGY] [--interactive]
```
Scans fork-scoped files and maps each file back to the configured upstream. Only files that already exist locally are considered; use `pull` or `resolve` for new imports. Fork selectors are optional and may be fork key, `directory`, or `alt_directories` from `config.yml`.

**Options:**

| Flag | Description |
|------|-------------|
| `--dry-run` | Preview without writing |
| `--path GLOB ...` | Only update files whose relative path matches the given fnmatch globs |
| `--strategy STRATEGY` | Merge strategy: `overwrite` (default, full replace), `diff` (show diff only), `ours` (keep local), `theirs` (take upstream) |
| `--interactive` | Prompt (y/N) before each file update |

**Examples:**

```
python -m Tools.fork_porter update-ported
python -m Tools.fork_porter update-ported mono _NF
python -m Tools.fork_porter update-ported goobstation --dry-run
python -m Tools.fork_porter update-ported --path "*.yml" --strategy diff
python -m Tools.fork_porter update-ported --interactive --strategy theirs
```

### cherry-pick

Apply specific commits from a configured upstream.

```
python -m Tools.fork_porter cherry-pick --from <upstream> [--strategy STRATEGY] [--dry-run] <commit> [commit ...]
```
Fetches upstream, exports each commit, and applies it locally using `git am`. Workspace must be clean unless `--dry-run` is used.

**Options:**

| Flag | Description |
|------|-------------|
| `--from <upstream>` | Configured upstream name (required) |
| `--strategy STRATEGY` | Git apply strategy: `3way` (default), `theirs`, `ours` |
| `--dry-run` | Preview without applying |

**Examples:**

```
python -m Tools.fork_porter cherry-pick --from monolith 1a2b3c4d
python -m Tools.fork_porter cherry-pick --from goobstation --dry-run abc123 def456
python -m Tools.fork_porter cherry-pick --from goobstation --strategy theirs abc123
```

### pull

Pull prototypes and all their dependencies from upstreams.

```
python -m Tools.fork_porter pull [options] <prototype_id> [prototype_id ...]
```
Accepts any prototype ID — entity, vessel, gameMap, or other types. Resolves
the full dependency tree: parent prototypes, referenced textures, C# classes,
test files, and locale entries. Files are placed under the target fork's
`_ForkName/` directory with upstream fork prefixes stripped. C# namespaces
are updated automatically.

For vessel/gameMap prototypes, `pull` also follows `mapPath` and `shuttlePath`
references to copy the associated map file, then scans the map for `proto:`
entity references and resolves those entities' full dependency trees
(prototypes, textures, C#, tests, locale).

**Options:**

| Flag | Description |
|------|-------------|
| `--from <upstream>` | Search only this upstream (default: all in `resolve_source_order`) |
| `--to <fork>` | Target fork key for output directories (default: auto-detect from source upstream) |
| `--no-code` | Skip C# source file resolution |
| `--no-locale` | Skip FTL locale resolution |
| `--no-test` | Skip C# test file resolution |
| `--dry-run` | Preview without writing |
| `--interactive` | Prompt (y/N) before copying each file |
| `--include-non-entity` | Also pull non-entity prototypes (recipes, reactions, technologies, etc.) referenced by the entity tree |

**Examples:**

```
python -m Tools.fork_porter pull CoolGun
python -m Tools.fork_porter pull --from frontier --to nf CoolGun CoolAmmo
python -m Tools.fork_porter pull --no-test SomeEntity
python -m Tools.fork_porter pull --dry-run --no-code SomeEntity
python -m Tools.fork_porter pull --from monolith Archer
```

### dedup

Remove duplicate entity IDs across forks based on priority.

```
python -m Tools.fork_porter dedup [--dry-run] [--interactive]
```
Scans `.yml` files for duplicate entity IDs. Lower-priority fork copies are removed. Empty files are deleted if `dedup.delete_empty_files` is true. Priority is set per fork in `config.yml`. Use `--interactive` for prompts before modification.

### resolve

Discover missing prototype references in map files and pull from upstreams.

```
python -m Tools.fork_porter resolve [--dry-run] [--interactive]
```

Scans map files in directories listed under `map_scan_dirs` for `proto:`
references. Any entity ID not found locally is searched in upstreams
(ordered by `resolve_source_order`). Found prototypes are copied along
with their parent chain and referenced textures.

The command also resolves non-entity content referenced by maps:
- **Tile definitions** -- `id:` values from `type: tile` prototypes
- **Decal definitions** -- `id:` values from `type: decal` prototypes
- **Audio references** -- `.ogg` paths referenced in map files

The command prints a table showing each missing prototype ID and every
map file that references it (one row per map reference), with a status column
indicating whether each ID was resolved or unresolvable.

Use `--interactive` to be prompted before copying each file.

### audit-patches

Scan source files for inline fork-edit markers.

```
python -m Tools.fork_porter audit-patches [--include-forks] [--namespace Prefix] [--detect-orphans]
```

Searches `.cs`, `.yml`, and `.ftl` files in base-game directories
(excluding `_ForkName/` dirs by default) for comment markers matching patterns built
from `marker_names` in `config.yml`.

Detects three marker types:

- **point** -- single-line markers (e.g. `// Goob edit`)
- **start** -- block openers (e.g. `// Goob edit start`, `// begin Goob`)
- **end** -- block closers (e.g. `// Goob edit end`, `// end Goob`)

Reports per-file markers, per-fork totals, per-filetype breakdown, and
warns about unmatched block start/end pairs.

**Options:**

| Flag | Description |
|------|-------------|
| `--include-forks` | Also scan fork-specific directories |
| `--namespace` | Limit to fork scopes (repeatable, comma-separated) |
| `--detect-orphans` | Scan fork-scoped C# files for references to patched base-game classes and report potential orphans |

Use `--detect-orphans` to find fork-scoped files that depend on patches
in base-game files. This helps catch breakage when upstream patches are
removed or changed.

Use `--namespace` to restrict scanning to one or more configured fork
scopes (fork key, `directory`, or `alt_directories` from `config.yml`).
The flag is repeatable and also accepts comma-separated values:

When `--namespace` is provided, fork-scoped directories are included for the
selected scopes even without `--include-forks`.

```
python -m Tools.fork_porter audit-patches --namespace goobstation
python -m Tools.fork_porter audit-patches --namespace _Mono,_NF
python -m Tools.fork_porter audit-patches --namespace goobstation --namespace omu
```

### diff

Show unified diffs between local ported files and their upstream versions.

```
python -m Tools.fork_porter diff [fork ...] [--path GLOB ...] [--stat]
```

Compares each fork-scoped file against its upstream counterpart. Useful for
reviewing local modifications or checking for upstream drift.

**Options:**

| Flag | Description |
|------|-------------|
| `--path GLOB ...` | Only diff files matching these fnmatch path patterns |
| `--stat` | Show diffstat summary instead of full unified diffs |

**Examples:**

```
python -m Tools.fork_porter diff
python -m Tools.fork_porter diff goobstation --stat
python -m Tools.fork_porter diff --path "*.yml" "*.cs"
```

### where-from

Show provenance information for files or entity prototype IDs.

```
python -m Tools.fork_porter where-from <target> [target ...]
```

Looks up provenance metadata (recorded by `pull`, `resolve`, and
`update-ported`) to report which upstream a file or entity came from,
the commit SHA at time of import, and the timestamp.

Targets can be file paths (relative to workspace root) or entity prototype
IDs. Entity IDs are resolved by scanning local prototype files.

**Examples:**

```
python -m Tools.fork_porter where-from Resources/Prototypes/_Mono/Entities/cool_gun.yml
python -m Tools.fork_porter where-from CoolGun CoolAmmo
```

### log

Show upstream commit changelog since last sync.

```
python -m Tools.fork_porter log [fork ...] [-n LIMIT]
```

Uses provenance metadata to determine the last-synced commit SHA for each
upstream, then shows new commits since that point. This helps identify what
upstream changes are available but not yet pulled.

**Options:**

| Flag | Description |
|------|-------------|
| `-n`, `--limit` | Maximum commits to show per upstream (default: 20) |

**Examples:**

```
python -m Tools.fork_porter log
python -m Tools.fork_porter log goobstation -n 50
```

### pull-path

Pull arbitrary files or directories by path from upstreams.

```
python -m Tools.fork_porter pull-path [--from <upstream>] [--to <fork>] [--dry-run] <path> [path ...]
```

`pull-path` copies files by their upstream-relative path. This is useful for pulling non-entity
content like C# systems, locale files, textures, or configuration that
isn't part of the prototype dependency graph.

**Options:**

| Flag | Description |
|------|-------------|
| `--from <upstream>` | Upstream to pull from (default: search all in resolve order) |
| `--to <fork>` | Target fork key for destination directory |
| `--dry-run` | Preview without writing |

**Examples:**

```
python -m Tools.fork_porter pull-path Content.Server/_NF/Systems/CoolSystem.cs
python -m Tools.fork_porter pull-path --from frontier --to nf Resources/Textures/_NF/Objects/cool.rsi
python -m Tools.fork_porter pull-path --dry-run Content.Shared/_Goob/Components/FooComponent.cs
```

### clean

Remove local upstream cache clones.

```
python -m Tools.fork_porter clean
```

Deletes `.fork_porter_cache/` under the workspace root. Use this when you
want to force a fresh upstream clone on the next `update`/`pull`/`resolve`.

### status

Show a fork health dashboard.

```
python -m Tools.fork_porter status
```

Reports:

- Entity prototype count per fork
- Number of duplicate entity IDs across forks
- Number of missing map prototype references

## GLOBAL OPTIONS

| Flag | Description |
|------|-------------|
| `--config PATH` | Path to config file (default: `Tools/fork_porter/config.yml`) |
| `--verbose` | Enable debug-level log output |

## CONFIGURATION

The config file (`config.yml`) contains:

### upstreams

GitHub repo URLs for upstream forks. Each entry has `repo`, `branch`, and
`label`. Repos are cloned into `.fork_porter_cache/<name>/`.

```yaml
upstreams:
  goobstation:
    repo: https://github.com/Goob-Station/Goob-Station.git
    branch: master
    label: Goob Station
```

### forks

Registry of all forks contributing content. Controls dedup priority and
marker detection.

```yaml
forks:
  omu:
    priority: 100
    directory: _Omu
    marker_names:
      - Omu
      - Omustation
```

- `priority` -- higher wins dedup. omu=100, goobstation=85, base=0.
- `directory` -- the `_ForkName` directory under Prototypes/Textures/Locale/C#.
- `alt_directories` -- additional directory names to check (case variants).
- `marker_names` -- stems for inline comment marker detection. The tool
  auto-builds patterns matching `// <Name> edit`, `// begin <Name>`,
  `# <Name>`, etc.

### dedup

```yaml
dedup:
  delete_empty_files: true
```

### map_scan_dirs

Directories under `Resources/` scanned by `resolve` for `proto:` references.

### resolve_source_order

Ordered list of upstream keys. First match wins when pulling prototypes.

### max_resolve_depth

Maximum parent-chain recursion depth (default: 25).

### history

```yaml
history:
  auto_commit: true
```
to auto-commit touched files after successful non-dry-run execution.

## FILES

```
Tools/fork_porter/
  __main__.py       -- main CLI entry point
  config.yml        -- configuration
  porting.md        -- this file
  provenance.json   -- auto-managed provenance metadata (tracks upstream source
                       and commit SHA for each ported file)
.fork_porter_cache/ -- auto-managed upstream repo clones (gitignored)
```

## SEE ALSO

`config.yml` for the full fork registry and upstream list.
