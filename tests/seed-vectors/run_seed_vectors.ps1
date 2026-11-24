param(
    [string]$ProjectRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ScriptDirectory {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    $invocationPath = $MyInvocation.ScriptName
    if ([string]::IsNullOrWhiteSpace($invocationPath)) {
        $invocationPath = $MyInvocation.MyCommand.Path
    }

    if ([string]::IsNullOrWhiteSpace($invocationPath)) {
        throw 'Unable to resolve the seed-vector script directory.'
    }

    return Split-Path -Parent $invocationPath
}

$scriptDirectory = Resolve-ScriptDirectory
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path $scriptDirectory '..\..'
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

& dotnet run --project (Join-Path $ProjectRoot 'tests\seed-vectors\SeedVectorTests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Seed/RNG vector harness failed.' }
