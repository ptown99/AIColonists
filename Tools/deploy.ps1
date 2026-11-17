# AIColonists deploy pipeline - deterministic spine for the aic-deploy skill.
# NOTE: keep this file PURE ASCII. Windows PowerShell 5.1 reads .ps1 as ANSI,
# and multi-byte chars (em-dash etc.) corrupt string parsing.
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File Tools\deploy.ps1
# Runs the full sequence: game-closed check -> Release build (0w/0e) -> harness
# -> PromptProbe -> robocopy deploy -> SHA256 staged-vs-live verify.
#
# Exit codes:  0 = deployed + verified
#              1 = RimWorld is running (blocked, nothing done)
#              2 = build failed (or warnings > 0)
#              3 = harness failed
#              4 = PromptProbe failed
#              5 = robocopy failed (exit >= 4)
#              6 = SHA256 mismatch after copy

$ErrorActionPreference = 'Stop'

$Repo     = 'F:\Antigravity\Project\AIColonists'
$Live     = 'F:\SteamLibrary\steamapps\common\RimWorld\Mods\AIColonists'
$Staged   = "$Repo\Assemblies\AIColonists.dll"
$Deployed = "$Live\Assemblies\AIColonists.dll"

function Say($msg) { Write-Host "[deploy] $msg" }
function Tail([string[]]$lines, [int]$n = 15) {
    $lines | Where-Object { $_ -ne '' } | Select-Object -Last $n | ForEach-Object { Say "      $_" }
}

# --- 1. Game must be closed -------------------------------------------------
$game = Get-Process RimWorldWin64 -ErrorAction SilentlyContinue
if ($game) {
    Say "BLOCKED: RimWorld is running (PID $($game.Id -join ', ')). Close it and re-run."
    exit 1
}
Say "RimWorld not running"

Push-Location $Repo
try {
    # --- 2. Build -----------------------------------------------------------
    Say "building (Release, DebugType=none)..."
    $out = & dotnet build Source\AIColonists.csproj -c Release -p:DebugType=none 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    if ($code -ne 0) { Say "BUILD FAILED (dotnet exit $code)"; Tail $out; exit 2 }
    $warns = [int]([regex]::Match(($out -join "`n"), '(\d+) Warning\(s\)').Groups[1].Value)
    if ($warns -gt 0) { Say "BUILD FAILED: $warns warning(s) (baseline is zero)"; Tail $out; exit 2 }
    Say "build OK - 0 warnings, 0 errors"

    # --- 3. Harness ----------------------------------------------------------
    Say "running harness..."
    $out = & dotnet run --project Tests 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    if ($code -ne 0) { Say "HARNESS FAILED (exit $code)"; Tail $out; exit 3 }
    $summary = ($out | Where-Object { $_ -match '^\d+ passed, \d+ failed$' } | Select-Object -Last 1)
    if (-not $summary) { Say "HARNESS FAILED: no summary line found"; Tail $out; exit 3 }
    Say "harness OK: $summary"

    # --- 4. PromptProbe ------------------------------------------------------
    Say "running PromptProbe..."
    $out = & dotnet run --project Tools\PromptProbe -c Release 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    if ($code -ne 0) { Say "PROBE FAILED (exit $code)"; Tail $out; exit 4 }
    # The success line starts with "PASS" at column 0 (the probe prints it with an
    # em-dash, so match on the ASCII prefix only).
    if (-not ($out | Where-Object { $_ -match '^PASS' })) { Say "PROBE FAILED: no PASS line"; Tail $out; exit 4 }
    Say "probe OK"
}
finally { Pop-Location }

# --- 5. Deploy ---------------------------------------------------------------
Say "robocopy -> $Live"
& robocopy $Repo $Live /MIR /XF .gitignore /XD Source Tests Tools .git /R:1 /W:1 | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 4) { Say "ROBOCOPY FAILED (exit ${rc})"; exit 5 }
$cat = @('no changes - already in sync', 'files copied', 'copied + extras at dest', 'copied + extras')[[Math]::Min($rc, 3)]
Say "robocopy OK (exit ${rc}: ${cat})"

# --- 6. Verify ----------------------------------------------------------------
$s = (Get-FileHash $Staged   -Algorithm SHA256).Hash
$l = (Get-FileHash $Deployed -Algorithm SHA256).Hash
if ($s -ne $l) { Say "HASH MISMATCH - staged=${s} live=${l}"; exit 6 }

Say "RESULT: DEPLOYED + VERIFIED  SHA256=${s}"
exit 0
