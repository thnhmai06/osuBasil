#!/usr/bin/env bash
# Adds a provider neutral migration to the context of one module.
#
# Usage: tools/add-migration.sh <Module> <Name>     (for example: tools/add-migration.sh Users AddRestrictionReason)
set -euo pipefail

module="${1:?module, such as Users}"
name="${2:?migration name}"
cd "$(dirname "$0")/.."

project="src/Infrastructure/Persistence"
dotnet ef migrations add "$name" --project "$project" --startup-project "$project" \
	--context "${module}DbContext" --output-dir "Migrations/${module}" \
	--namespace "Basil.Infrastructure.Persistence.Migrations.${module}"

# The snapshot lands in a folder named after the root namespace; keep it next to the migration.
nested="$project/Basil"
if [ -d "$nested" ]; then
	find "$nested" -name '*ModelSnapshot.cs' -exec mv {} "$project/Migrations/${module}/" \;
	rm -r "$nested"
fi

tools/normalize-migrations.sh "$project/Migrations/${module}"
