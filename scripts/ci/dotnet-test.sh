#!/usr/bin/env bash
# Runs the .NET tests for CI (#248), after `dotnet build -c Release`.
#
#   scripts/ci/dotnet-test.sh [results-dir]
#
# Everything goes to the results folder (default TestResults): the console
# log, and, if a test hangs, blame's sequence file and a dump of the test
# host. CI uploads that folder when the step fails.
#
# Blame: a test that runs past 5 minutes is taken to have hung. Its test host
# is dumped and stopped, and the log names the test. That's per test, and the
# longest any test waits on purpose is 2 minutes.
#
# One known failure gets a second try. xUnit's VSTest adapter starts each test
# executable once with -assemblyInfo, to ask which xUnit it uses, and gives up
# if it hasn't exited after 60 seconds, a limit built into xUnit. On a busy
# Windows runner a start can take longer than that. The adapter then reports
# "Catastrophic failure ... Test process did not respond within 60 seconds"
# for that assembly, and runs none of its tests. If that's the only thing
# wrong (every other assembly passed), those assemblies are run again once,
# on their own. Anything else fails the run as it is.
# Exits with dotnet test's status.

set -uo pipefail

results=${1:-TestResults}
mkdir -p "$results"
log="$results/dotnet-test.log"
args=(--no-build -c Release --results-directory "$results"
  --blame-hang --blame-hang-timeout 5m --blame-hang-dump-type mini)

dotnet test "${args[@]}" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
[ "$status" -eq 0 ] && exit 0

probe='Catastrophic failure: System.InvalidOperationException: Test process did not respond within 60 seconds'
mapfile -t missed < <(grep -oE "[A-Za-z0-9_.]+: $probe" "$log" | sed 's/: .*//' | sort -u)
[ "${#missed[@]}" -gt 0 ] || exit "$status"

# Only when every assembly that ran passed: a failed test, a crash, or any
# other catastrophic failure fails the run.
assemblies=$(grep -c '^Test run for ' "$log")
passed=$(grep -cE '^Passed! +- Failed: +0,' "$log")
catastrophic=$(grep -c 'Catastrophic failure' "$log")
if [ $((passed + ${#missed[@]})) -ne "$assemblies" ] || [ "$catastrophic" -ne "${#missed[@]}" ]; then
  exit "$status"
fi

for name in "${missed[@]}"; do
  project="tests/$name/$name.csproj"
  [ -f "$project" ] || { echo "::error::$name missed xUnit's 60-second start-up check, and $project isn't there to run again."; exit "$status"; }
  echo "::warning::$name didn't answer xUnit's 60-second start-up check, so none of its tests ran. Running it again on its own (#248)."
  dotnet test "$project" "${args[@]}" 2>&1 | tee -a "$log"
  rerun=${PIPESTATUS[0]}
  [ "$rerun" -eq 0 ] || exit "$rerun"
done
