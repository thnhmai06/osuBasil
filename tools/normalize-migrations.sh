#!/usr/bin/env bash
# Rewrites the EF Core migrations under Basil.Infrastructure.Persistence/Migrations after `dotnet ef migrations add`.
#
# The migrations are PostgreSQL-native: they keep the store types, the Npgsql annotations and the Npgsql usings that
# the design-time provider writes. This script only replaces the quoted entity names with `typeof(Row).FullName!` (and
# the shared reference tables with `ContextModel.*`), so renaming a row type needs no search through strings.
#
# Usage: tools/normalize-migrations.sh [directory]   (default: Basil.Infrastructure.Persistence/Migrations)
set -euo pipefail

root="${1:-$(dirname "$0")/../Basil.Infrastructure.Persistence/Migrations}"

find "$root" -name '*.cs' -print0 | while IFS= read -r -d '' file; do
	perl -0777 -i -pe '
		# Entity names written as types, not as strings.
		s/"(Basil\.[A-Za-z0-9_.]+Row)"/typeof($1).FullName!/g;
		s/"UsersPrincipal"/ContextModel.UsersTable/g;
		s/"ScoresPrincipal"/ContextModel.ScoresTable/g;
		if (/ContextModel\./ && !/using Basil\.Infrastructure\.Shared\.Persistence;/) {
			s/(using System;\r?\n)/$1using Basil.Infrastructure.Shared.Persistence;\n/;
		}
	' "$file"
done
