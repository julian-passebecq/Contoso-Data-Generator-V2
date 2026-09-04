#!/usr/bin/env pwsh
$scriptPath = Join-Path $PSScriptRoot 'scripts\lab.py'
python $scriptPath @args
exit $LASTEXITCODE
