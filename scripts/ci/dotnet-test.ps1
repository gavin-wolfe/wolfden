# Runs the .NET tests for CI (#248), after `dotnet build -c Release`.
#
#   pwsh scripts/ci/dotnet-test.ps1 [results-dir]
#
# PowerShell, not bash, on both OSes: under Git Bash on Windows, the MSYS
# runtime turns on backup privilege, every test process inherits it, and
# folder permission tests then skip (#256 review).
#
# Everything goes to the results folder (default TestResults): the console
# log, and, if a test hangs, blame's sequence file and a dump of the test
# host and its xUnit process. CI uploads that folder when the job fails.
#
# Blame: a test that runs past 5 minutes is taken to have hung. Its test host
# is dumped and stopped, and the log names the test. That's per test, and the
# longest any test waits on purpose is 2 minutes.
#
# One known failure gets a second try. xUnit's VSTest adapter starts each test
# executable once with -assemblyInfo, to ask which xUnit it uses, and gives up
# if it hasn't exited after 60 seconds, a limit built into xUnit. On a busy
# Windows runner that can be missed. The adapter then reports
# "Catastrophic failure ... Test process did not respond within 60 seconds"
# for that assembly, and runs none of its tests. If that's the only thing
# wrong (every other test project ran and passed), those assemblies are run
# again once, on their own. Anything else fails the run as it is.
# Exits with dotnet test's status.

param([string]$Results = 'TestResults')

# Not 'Stop': dotnet's standard error, merged into the log, mustn't end the script.
$ErrorActionPreference = 'Continue'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $root
New-Item -ItemType Directory -Force $Results | Out-Null
$log = Join-Path $Results 'dotnet-test.log'
$testArgs = @('--no-build', '-c', 'Release', '--results-directory', $Results,
    '--blame-hang', '--blame-hang-timeout', '5m', '--blame-hang-dump-type', 'mini')

dotnet test @testArgs 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log
$status = $LASTEXITCODE
if ($status -eq 0) { exit 0 }

$text = Get-Content $log
$probe = 'Catastrophic failure: System.InvalidOperationException: Test process did not respond within 60 seconds'
$missed = @($text | Select-String -Pattern "([A-Za-z0-9_.]+): $([regex]::Escape($probe))" |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
if ($missed.Count -eq 0) { exit $status }

# Only when every test project ran and every one that ran passed: a failed
# test, a crash, a project that never started, or any other catastrophic
# failure fails the run.
$expected = @(Get-ChildItem -Path tests/*/*.Tests.csproj).Count
$started = @($text | Select-String -Pattern '^Test run for ').Count
$passed = @($text | Select-String -Pattern '^Passed! +- Failed: +0,').Count
$catastrophic = @($text | Select-String -Pattern 'Catastrophic failure' -SimpleMatch).Count
if ($started -ne $expected -or ($passed + $missed.Count) -ne $expected -or $catastrophic -ne $missed.Count) {
    exit $status
}

foreach ($name in $missed) {
    $project = "tests/$name/$name.csproj"
    if (-not (Test-Path $project)) {
        Write-Output "::error::$name missed xUnit's 60-second start-up check, and $project isn't there to run again."
        exit $status
    }

    Write-Output "::warning::$name didn't answer xUnit's 60-second start-up check, so none of its tests ran. Running it again on its own (#248)."
    dotnet test $project @testArgs 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
