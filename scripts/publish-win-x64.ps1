param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$Project = Join-Path $PSScriptRoot "..\src\HsAsrDictation\HsAsrDictation.csproj"
$Output = Join-Path $PSScriptRoot "..\artifacts\publish\win-x64"

dotnet publish $Project `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $Output

Write-Host "Publish finished: $Output"
