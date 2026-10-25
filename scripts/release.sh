#!/usr/bin/env bash
# Tags the current `main` with a version and pushes the tag, which triggers .github/workflows/
# release.yml (on: push: tags: ["v*"]) to build the four platform binaries and publish a GitHub
# Release. Pushing `main` itself (e.g. after merging a feature branch) does not do this - only the
# tag push does.
#
# The tag is annotated with a changelog (commit subjects since the previous tag), which release.yml
# reads back out of the tag object and uses as the GitHub Release notes.
#
# Usage: scripts/release.sh 0.1.6
#        scripts/release.sh v0.1.6

set -euo pipefail

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <version>  (e.g. $0 0.1.6)" >&2
    exit 1
fi

version="$1"
version="${version#v}"

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: version must look like X.Y.Z (got '$1')" >&2
    exit 1
fi

tag="v$version"

current_branch="$(git rev-parse --abbrev-ref HEAD)"
if [ "$current_branch" != "main" ]; then
    echo "Error: you're on '$current_branch', not 'main'. Check out main first: git checkout main" >&2
    exit 1
fi

if [ -n "$(git status --porcelain)" ]; then
    echo "Error: working tree has uncommitted changes. Commit or stash them first." >&2
    exit 1
fi

echo "Fetching origin..."
git fetch origin main --tags

local_sha="$(git rev-parse main)"
remote_sha="$(git rev-parse origin/main)"
if [ "$local_sha" != "$remote_sha" ]; then
    echo "Error: local main ($local_sha) doesn't match origin/main ($remote_sha)." >&2
    echo "Push or pull main so it's in sync before tagging." >&2
    exit 1
fi

if git rev-parse "$tag" >/dev/null 2>&1; then
    echo "Error: tag '$tag' already exists locally." >&2
    exit 1
fi

if git ls-remote --tags origin "refs/tags/$tag" | grep -q "$tag"; then
    echo "Error: tag '$tag' already exists on origin." >&2
    exit 1
fi

echo "Building changelog since last tag..."
previous_tag="$(git describe --tags --abbrev=0 "$local_sha" 2>/dev/null || true)"

if [ -n "$previous_tag" ]; then
    range="$previous_tag..$local_sha"
else
    range="$local_sha"
fi

changelog="$(git log "$range" --no-merges --pretty=format:'- %s (%h)')"
if [ -z "$changelog" ]; then
    changelog="- No changes recorded since $previous_tag"
fi

tag_message="## Changelog
$changelog"

echo "Tagging $local_sha as $tag..."
git tag -a "$tag" -m "$tag_message"

echo "Pushing $tag to origin..."
git push origin "$tag"

echo "Done. See https://github.com/kevinRForshey/Paarde/actions for the release build."
