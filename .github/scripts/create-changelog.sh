#!/bin/bash

current_semver="$(git tag | tail -n 1)"
previous_semver="$(git tag | tail -n 2 | head -n 1)"
new_files=($(git diff --name-only --diff-filter=A "$previous_semver..$current_semver" | sort | grep -E 'mods|resourcepacks|shaderpacks'))
modified_files=($(git diff --name-only --diff-filter=M "$previous_semver..$current_semver" | sort | grep -E 'mods|resourcepacks|shaderpacks'))

# Initialize changelogs for each category
mod_changelog=""
resourcepack_changelog=""
shaderpack_changelog=""

for file in "${new_files[@]}"; do
    # Parse the name from the file
    name="$(grep -Po 'name = "\K[^"]+' $file | head -n 1)"

    # Check if the file is a mod, resourcepack, or shaderpack
    if echo "$file" | grep -q 'mods'; then
        mod_changelog="$mod_changelog- Add **$name**\n"
    elif echo "$file" | grep -q 'resourcepacks'; then
        resourcepack_changelog="$resourcepack_changelog- Add **$name**\n"
    elif echo "$file" | grep -q 'shaderpacks'; then
        shaderpack_changelog="$shaderpack_changelog- Add **$name**\n"
    fi
done

mod_version_regex="[-v](?!1\.(19|20|20\.1))(\d+\.?){1,3}"
semver_regex="(\d+\.\d+\.\d+)"
for file in "${modified_files[@]}"; do
    # Parse the name from the file
    name="$(grep -Po 'name = "\K[^"]+' $file | head -n 1)"

    # Check if the file is a mod, resourcepack, or shaderpack
    if echo "$file" | grep -q 'mods'; then
        old_version="$(git show "$previous_semver:$file" | grep -Po "$mod_version_regex" | grep -Po "$semver_regex" | head -n 1)"
        new_version="$(git show "$current_semver:$file" | grep -Po "$mod_version_regex" | grep -Po "$semver_regex" | head -n 1)"

        # Check if the versions are equal or if one of them is empty
        if [ "$old_version" == "$new_version" ] || [ -z "$old_version" ] || [ -z "$new_version" ]; then
            mod_changelog="$mod_changelog- Update **$name**\n"
        else
            mod_changelog="$mod_changelog- Update **$name** from \`$old_version\` to \`$new_version\`\n"
        fi
    elif echo "$file" | grep -q 'resourcepacks'; then
        resourcepack_changelog="$resourcepack_changelog- Update **$name**\n"
    elif echo "$file" | grep -q 'shaderpacks'; then
        shaderpack_changelog="$shaderpack_changelog- Update **$name**\n"
    fi
done

# If any of the changelogs are not empty, append them to the changelog variable
changelog=""

if [ ! -z "$mod_changelog" ]; then
    changelog="$changelog## Mods\n"
    changelog="$changelog$mod_changelog\n"
fi

if [ ! -z "$resourcepack_changelog" ]; then
    changelog="$changelog## Resourcepacks\n"
    changelog="$changelog$resourcepack_changelog\n"
fi

if [ ! -z "$shaderpack_changelog" ]; then
    changelog="$changelog## Shaderpacks\n"
    changelog="$changelog$shaderpack_changelog\n"
fi

# If the changelog variable is not empty, print it
if [ ! -z "$changelog" ]; then
    printf "$changelog" >> $GITHUB_OUTPUT
fi