#!/bin/bash

# Run Packwiz Refresh
~/go/bin/packwiz refresh

# Check if there are any changes
git diff-index --quiet HEAD
if [ "$?" == "1" ]; then
  git config --global user.email "github-actions[bot]@users.noreply.github.com"
  git config --global user.name "github-actions[bot]"
  git add . > /dev/null
  git commit -m "\`packwiz refresh\`." > /dev/null
  git push > /dev/null
fi

# Check if any new mods, resourcepacks or shaderpacks were added. If so, we're bumping the semver minor version up by one
new_semver=""
current_semver="$(grep -Po 'version = "\K[^"]+' pack.toml)"
new_files=($(git diff --name-only --diff-filter=A HEAD~1 | grep -E 'mods|resourcepacks|shaderpacks'))
modified_files=($(git diff --name-only --diff-filter=M HEAD~1 | grep -E 'mods|resourcepacks|shaderpacks'))
if [ -z "${new_files[@]}" ]; then
  # Bump the semver minor version up by one and reset the patch version to zero
  new_semver="$(echo $current_semver | awk -F. -v OFS=. '{$(NF-1)++; $NF=0; print}')"
# Else if any mods, resourcepacks or shaderpacks were modified, bump the patch version up by one
elif [ -z "${modified_files[@]}" ]; then
  # Bump the semver patch version up by one
  new_semver="$(echo $current_semver | awk -F. -v OFS=. '{$NF++; print}')"
fi

# Create a new release
git tag -a "$current_semver" -m "Release $new_semver." > /dev/null
git push --tags > /dev/null