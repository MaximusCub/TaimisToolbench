#!/usr/bin/env bash
# bootstrap.sh - Makes a fresh clone or worktree buildable.
#
# Usage:
#   ./tools/bootstrap.sh          # Restore packages if they are missing
#   ./tools/bootstrap.sh --force  # Restore even if they look present
#   ./tools/bootstrap.sh --help   # Print usage
#
# Environment overrides (optional):
#   NUGET_CACHE_DIR  Where nuget.exe is kept between worktrees
#                    (default: $LOCALAPPDATA/TaimisToolbench, else $HOME/.taimistoolbench)
#
# This repo is a classic packages.config project, so `dotnet restore` and
# `dotnet msbuild -t:restore` both print "Nothing to do" and leave the build
# failing on the missing packages\BlishHUD.1.3.0\build\BlishHUD.targets. Only
# nuget.exe restores it. CI gets nuget.exe free from the windows-latest image;
# a developer machine does not, so this script fetches it once into a shared
# cache and every worktree reuses that copy.
#
# Package HintPaths in TaimisToolbench.csproj are relative (packages\...), so
# packages/ must sit at this worktree's own root. It cannot be shared.
#
# Requires: Git Bash or WSL on Windows, internet access on first run.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SOLUTION="TaimisToolbench.sln"
NUGET_URL="https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"

# Read the version rather than hardcoding it: a pinned sentinel goes stale on
# the next BlishHUD bump and then reports a successful restore as a failure.
BLISH_VERSION="$(sed -n 's/.*id="BlishHUD" version="\([^"]*\)".*/\1/p' packages.config)"
if [ -z "$BLISH_VERSION" ]; then
  echo "bootstrap.sh: packages.config names no BlishHUD version." >&2
  exit 1
fi
SENTINEL="packages/BlishHUD.$BLISH_VERSION/build/BlishHUD.targets"

FORCE=0
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=1 ;;
    --help|-h)
      sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "bootstrap.sh: unknown argument '$arg' (try --help)" >&2
      exit 2
      ;;
  esac
done

if [ "$FORCE" -eq 0 ] && [ -f "$SENTINEL" ]; then
  echo "Packages already restored ($SENTINEL present). Nothing to do."
  exit 0
fi

default_cache="${LOCALAPPDATA:-}"
if [ -n "$default_cache" ]; then
  default_cache="$(printf '%s' "$default_cache" | tr '\\' '/')/TaimisToolbench"
else
  default_cache="$HOME/.taimistoolbench"
fi
CACHE_DIR="${NUGET_CACHE_DIR:-$default_cache}"

NUGET=""
if command -v nuget >/dev/null 2>&1; then
  NUGET="nuget"
elif [ -x "$CACHE_DIR/nuget.exe" ]; then
  NUGET="$CACHE_DIR/nuget.exe"
else
  echo "nuget.exe not found on PATH or in $CACHE_DIR - fetching it once."
  mkdir -p "$CACHE_DIR"
  # Download to a PID-unique name and rename into place. Several worktrees can
  # bootstrap at the same time, and writing nuget.exe directly would let them
  # interleave into one corrupt binary that every later run then trusts.
  staging="$CACHE_DIR/nuget.exe.$$"
  trap 'rm -f "$staging"' EXIT
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$NUGET_URL" -o "$staging"
  elif command -v powershell.exe >/dev/null 2>&1; then
    powershell.exe -NoProfile -Command \
      "Invoke-WebRequest -Uri '$NUGET_URL' -OutFile '$(printf '%s' "$staging" | tr '/' '\\')'"
  else
    echo "Cannot download nuget.exe: neither curl nor powershell.exe is available." >&2
    echo "Fetch it manually from https://www.nuget.org/downloads into $CACHE_DIR" >&2
    exit 1
  fi
  chmod +x "$staging"
  mv -f "$staging" "$CACHE_DIR/nuget.exe"
  trap - EXIT
  NUGET="$CACHE_DIR/nuget.exe"
fi

echo "Restoring $SOLUTION with $NUGET ..."
"$NUGET" restore "$SOLUTION"

if [ ! -f "$SENTINEL" ]; then
  echo "Restore finished but $SENTINEL is still missing - the build will fail." >&2
  exit 1
fi

echo "Bootstrap complete. $SENTINEL is present; the solution can build."
