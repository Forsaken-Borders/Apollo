#!/bin/bash

# Check if any new mods, resourcepacks or shaderpacks were added. If so, we're bumping the semver minor version up by one
new_semver=""
current_semver="$(grep -Po 'version = "\K[^"]+' pack.toml)"
new_or_removed_files=($(git diff --name-only --diff-filter=AD HEAD~1 | grep -E 'mods|resourcepacks|shaderpacks'))
modified_files=($(git diff --name-only --diff-filter=M HEAD~1 | grep -E 'mods|resourcepacks|shaderpacks'))
if [ ! -z "${new_or_removed_files[@]}" ]; then
  # Bump the semver minor version up by one and reset the patch version to zero
  new_semver="$(echo $current_semver | awk -F. -v OFS=. '{$(NF-1)++; $NF=0; print}')"
# Else if any mods, resourcepacks or shaderpacks were modified, bump the patch version up by one
elif [ ! -z "${modified_files[@]}" ]; then
  # Bump the semver patch version up by one
  new_semver="$(echo $current_semver | awk -F. -v OFS=. '{$NF++; print}')"
fi

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

# Modify the pack.toml file
if [ ! -z "$new_semver" ]; then
  sed -i "s/version = \"$current_semver\"/version = \"$new_semver\"/g" pack.toml
  git diff-index --quiet HEAD
  if [ "$?" == "1" ]; then
    git config --global user.email "lunar@forsaken-borders.net"
    git config --global user.name "OoLunar"
    git add pack.toml > /dev/null
    git commit -m "Bump version to $new_semver." > /dev/null

    # Create a new tag
    git tag -a "$new_semver" -m "Release $new_semver." > /dev/null
    git push --repo "https://$1@github.com/Forsaken-Borders/Apollo.git" > /dev/null
    git push --tags --repo "https://$1@github.com/Forsaken-Borders/Apollo.git" > /dev/null
  fi
fi