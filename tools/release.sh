#!/usr/bin/env bash
# Release the current commit as v<Version> on GitHub. Runs on this machine against the local game install:
# the game's assemblies never leave it, and GitHub only gets the finished Thunderstore zip.
#
# Steps: check the tree is clean and pushed, check the tag is new, run format-check, a CI=true build, the tests
# and the package, then tag, push the tag and create the release with dist/PeakXR-<version>.zip attached.
# Bump <Version> in Directory.Build.props (and push that commit) before running it.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

log() { printf '\033[1;34m[release]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[release] error:\033[0m %s\n' "$*" >&2; exit 1; }

command -v gh >/dev/null || die "missing tool: gh (GitHub CLI)"
gh auth status >/dev/null 2>&1 || die "gh is not logged in; run 'gh auth login'"

VERSION="$(grep -m1 -oP '(?<=<Version>)[^<]+' Directory.Build.props || true)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "Directory.Build.props <Version> must be Major.Minor.Patch (got '${VERSION}')"
TAG="v$VERSION"

[[ -z "$(git status --porcelain)" ]] || die "working tree is not clean; commit or stash first"
git fetch -q origin
git merge-base --is-ancestor HEAD origin/master || die "HEAD is not on origin/master; push it first"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && die "tag $TAG already exists locally; bump <Version>"
[[ -z "$(git ls-remote --tags origin "refs/tags/$TAG")" ]] || die "tag $TAG already exists on origin; bump <Version>"

log "releasing $TAG from $(git rev-parse --short HEAD)"
mise run format-check
CI=true mise run package
mise run test

# package.sh rewrites thunderstore.toml's versionNumber; a change there means it was not bumped with <Version>.
[[ -z "$(git status --porcelain)" ]] || die "packaging changed tracked files (thunderstore.toml?); commit them and rerun"

ZIP="dist/PeakXR-$VERSION.zip"
[[ -f "$ZIP" ]] || die "$ZIP was not produced"

git tag -a "$TAG" -m "PeakXR $VERSION"
git push origin "$TAG"
gh release create "$TAG" "$ZIP" --title "PeakXR $VERSION" --generate-notes --verify-tag
log "released $TAG"
