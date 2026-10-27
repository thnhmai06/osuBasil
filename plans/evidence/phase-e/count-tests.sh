#!/usr/bin/env bash
# Runs every test project of the build and prints the passed/skipped totals and their sum.
# Needs the local PostgreSQL cluster (see plans/module-events-plan-20261027.md, Progress).
set -u
export BASIL_TEST_POSTGRES="${BASIL_TEST_POSTGRES:-Host=127.0.0.1;Port=55432;Username=postgres;Database=postgres}"
cd "$(git rev-parse --show-toplevel)"
total=0; skipped=0
for project in $(find tests -name '*.Tests.csproj' -o -name 'Basil.ArchitectureTests.csproj' | grep -v 'Basil.Host\|LoadTests\|IntegrationTests' | sort); do
	line=$(dotnet test "$project" --nologo -v q 2>&1 | grep -E '^(Passed|Failed)!' | head -1)
	echo "$project: ${line:-NO RESULT}"
	passed=$(echo "$line" | sed -n 's/.*Passed: *\([0-9]*\).*/\1/p'); skip=$(echo "$line" | sed -n 's/.*Skipped: *\([0-9]*\).*/\1/p')
	total=$((total + ${passed:-0})); skipped=$((skipped + ${skip:-0}))
done
echo "TOTAL passed=$total skipped=$skipped"
