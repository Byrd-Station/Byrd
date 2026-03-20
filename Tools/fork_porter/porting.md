# fork_porter

OmuStation fork content management tool.

## SYNOPSIS

```
python -m Tools.fork_porter [--config PATH] [--verbose] <command> [options]
```

## DESCRIPTION

fork_porter manages content ported from upstream SS14 forks into OmuStation.
It handles upstream repo caching, prototype pulling with full dependency
resolution, deduplication, map prototype resolution, inline patch auditing,
and status reporting.

When `history.auto_commit` is enabled in `config.yml`, mutating commands
(`pull`, `resolve`, `update-ported`, `dedup`) auto-commit touched files in
the local git repository to preserve local change history where possible.

Configuration is read from `Tools/fork_porter/config.yml`, which defines
upstream repos, fork priorities, marker names, and scan directories.

Upstream repos are shallow-cloned into `.fork_porter_cache/` on first use.
The `cherry-pick` command automatically expands upstream history to full depth.

## COMMANDS

### update

Fetch or clone all configured upstream repos.

```
python -m Tools.fork_porter update [name ...]
```

Iterates every entry in the `upstreams:` config block (or only the named
entries if provided). Repos that don't exist locally are shallow-cloned with
sparse checkout (Prototypes, Textures, Tests). Existing repos are fetched
to latest.

### update-ported

Refresh files that are already ported into fork directories from upstream.

```
python -m Tools.fork_porter update-ported [fork ...] [--dry-run]
```

Scans existing fork-scoped files (for example `Resources/Prototypes/_Mono/*`,
`Resources/Locale/*/_Mono/*`, `Content.* /_Mono/*`) and attempts to map each
file back to the configured upstream for that fork.

Only files that already exist locally are considered; this command does not
discover new content. Use `pull` or `resolve` for new imports.

Fork selectors are optional and may be fork key, `directory`, or
`alt_directories` value from `config.yml`.

**Examples:**

```
python -m Tools.fork_porter update-ported
python -m Tools.fork_porter update-ported mono _NF
python -m Tools.fork_porter update-ported goobstation --dry-run
```

### cherry-pick

Apply specific commits from a configured upstream to this repository.

```
python -m Tools.fork_porter cherry-pick --from <upstream> [--dry-run] <commit> [commit ...]
```

Ensures a full upstream fetch, exports each commit from the selected upstream,
and applies it locally using `git am -3`, preserving commit metadata.
The workspace must be clean unless
`--dry-run` is used.

**Examples:**

```
python -m Tools.fork_porter cherry-pick --from monolith 1a2b3c4d
python -m Tools.fork_porter cherry-pick --from goobstation --dry-run abc123 def456
```

### pull

Pull entity prototypes and all their dependencies from upstreams.

```
python -m Tools.fork_porter pull [options] <entity_id> [entity_id ...]
```

Resolves the full dependency tree for each entity:

- Parent prototypes (recursive, up to `max_resolve_depth`)
- RSI texture directories referenced by `sprite:` or `rsi:` fields
- C# component and system classes (searches for `<Type>Component`,
  `<Type>System`, `Shared<Type>System` when missing locally)
- Test files from `Content.Tests` / `Content.IntegrationTests` that
  reference pulled C# classes
- FTL locale entries matching `ent-<id>`

Files are placed under the target fork's `_ForkName/` directory with
upstream fork prefixes stripped from paths. C# namespaces are updated
automatically.

**Options:**

| Flag | Description |
|------|-------------|
| `--from <upstream>` | Search only this upstream (default: all in `resolve_source_order`) |
| `--to <fork>` | Target fork key for output directories (default: auto-detect from source upstream) |
| `--no-code` | Skip C# source file resolution |
| `--no-locale` | Skip FTL locale resolution |
| `--no-test` | Skip C# test file resolution |
| `--dry-run` | Preview without writing |

**Examples:**

```
python -m Tools.fork_porter pull CoolGun
python -m Tools.fork_porter pull --from frontier --to nf CoolGun CoolAmmo
python -m Tools.fork_porter pull --no-test SomeEntity
python -m Tools.fork_porter pull --dry-run --no-code SomeEntity
```

### dedup

Remove duplicate entity IDs across forks based on priority.

```
python -m Tools.fork_porter dedup [--dry-run]
```

Scans all `.yml` files under `Resources/Prototypes/` for `type: entity`
blocks. When the same `id:` appears in multiple forks, the copy in the
lower-priority fork is removed. If removal leaves an empty file, the file
is deleted when `dedup.delete_empty_files` is true; otherwise the file is
retained as an empty file.

Priority is set per-fork in `config.yml` under `forks.<name>.priority`.
Higher number wins.

### resolve

Discover missing prototype references in map files and pull from upstreams.

```
python -m Tools.fork_porter resolve [--dry-run]
```

Scans map files in directories listed under `map_scan_dirs` for `proto:`
references. Any entity ID not found locally is searched in upstreams
(ordered by `resolve_source_order`). Found prototypes are copied along
with their parent chain and referenced textures.

### audit-patches

Scan source files for inline fork-edit markers.

```
python -m Tools.fork_porter audit-patches [--include-forks] [--namespace Prefix]
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

When enabled, `pull`, `resolve`, `update-ported`, and `dedup` will attempt
to auto-commit touched files after successful non-dry-run execution.

## FILES

```
Tools/fork_porter/
  __main__.py       -- main CLI entry point
  config.yml        -- configuration
  porting.md        -- this file
.fork_porter_cache/ -- auto-managed upstream repo clones (gitignored)
```

## SEE ALSO

`config.yml` for the full fork registry and upstream list.
