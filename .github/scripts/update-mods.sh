#!/bin/bash

# Refresh: Ensures the filehashes are up to date
# Migrate: Ensures the Fabric loader is up to date
# Update: Ensures the mods, resource packs and shaderpacks are up to date
~/go/bin/packwiz refresh
~/go/bin/packwiz migrate loader latest -y
~/go/bin/packwiz update --all -y

# Identify as GitHub Actions
git config --global user.email "github-actions[bot]@users.noreply.github.com"
git config --global user.name "github-actions[bot]"

# Check if there are any changes
echo "updated=false" >> $GITHUB_OUTPUT
git diff-index --quiet HEAD
if [ "$?" == "1" ]; then
  echo "updated=true" >> $GITHUB_OUTPUT
  git add . > /dev/null
  git commit -m "\`packwiz refresh\`." > /dev/null
  git push > /dev/null
fi