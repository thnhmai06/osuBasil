#!/usr/bin/env bash
# Makes the EF Core migrations under src/Infrastructure/Persistence/Migrations provider neutral.
#
# `dotnet ef migrations add` writes what the design-time provider (Npgsql) knows: store types (`type: "text"`,
# `.HasColumnType("integer")`), identity annotations and Npgsql extension calls. A migration that carries them only
# runs on that provider. This script removes them, so every provider derives the store types and the generation
# strategy from the model at run time, and it replaces the quoted entity names with `typeof(Row).FullName!`, so
# renaming a row type needs no search through strings.
#
# Usage: tools/normalize-migrations.sh [directory]   (default: src/Infrastructure/Persistence/Migrations)
set -euo pipefail

root="${1:-$(dirname "$0")/../src/Infrastructure/Persistence/Migrations}"

find "$root" -name '*.cs' -print0 | while IFS= read -r -d '' file; do
	perl -0777 -i -pe '
		# Store types of the migration operations.
		s/\btype: "[^"]*",\s*//g;
		s/,\s*type: "[^"]*"//g;
		s/\(type: "[^"]*"\)/()/g;
		s/\boldType: "[^"]*",\s*//g;
		s/,\s*oldType: "[^"]*"//g;
		# Store types of the model snapshot.
		s/\n\s*\.HasColumnType\("[^"]*"\);/;/g;
		s/\n\s*\.HasColumnType\("[^"]*"\)//g;
		# Provider annotations: the identity strategy, the identifier length of the design-time provider.
		s/\n\s*\.Annotation\("Npgsql:[^"]*",[^)]*\)//g;
		s/\n\s*\.HasAnnotation\("Relational:MaxIdentifierLength", \d+\);/;/g;
		s/\n\s*\.HasAnnotation\("Relational:MaxIdentifierLength", \d+\)//g;
		s/^[^\n]*Npgsql[^\n]*\n//mg;
		# Entity names written as types, not as strings.
		s/"(Basil\.[A-Za-z0-9_.]+Row)"/typeof($1).FullName!/g;
		s/"UsersPrincipal"/ContextModel.UsersTable/g;
		s/"ScoresPrincipal"/ContextModel.ScoresTable/g;
		if (/ContextModel\./ && !/using Basil\.Infrastructure\.Common\.Persistence;/) {
			s/(using System;\r?\n)/$1using Basil.Infrastructure.Common.Persistence;\n/;
		}
	' "$file"
done
