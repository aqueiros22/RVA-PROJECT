param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor')
$ErrorActionPreference = 'Stop'
$arenaRoot = Split-Path $PSScriptRoot -Parent
$arenaTemp = Join-Path ([IO.Path]::GetTempPath()) ('ArenaDuelTests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaTemp | Out-Null
$arenaRuntime = Join-Path $UnityEditor 'Data/NetCoreRuntime'
$arenaCompiler = Join-Path $UnityEditor 'Data/DotNetSdkRoslyn'
$arenaFramework = Get-ChildItem (Join-Path $arenaRuntime 'shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$arenaArgs = @('-nologo', '-target:exe', '-nostdlib+', ('-out:"' + (Join-Path $arenaTemp 'DuelMatchTests.dll') + '"'))
$arenaArgs += @('System.Private.CoreLib.dll', 'System.Runtime.dll', 'System.Console.dll', 'System.Collections.dll') | ForEach-Object { '-r:"' + (Join-Path $arenaFramework.FullName $_) + '"' }
$arenaArgs += '"' + (Join-Path $arenaRoot 'Assets/Scripts/DuelMatch.cs') + '"'
$arenaArgs += '"' + (Join-Path $arenaRoot 'Assets/Scripts/PenStroke.cs') + '"'
$arenaArgs += '"' + (Join-Path $PSScriptRoot 'DuelMatchTests.cs') + '"'
$arenaResponse = Join-Path $arenaTemp 'tests.rsp'
Set-Content -LiteralPath $arenaResponse -Value $arenaArgs
& (Join-Path $arenaRuntime 'dotnet.exe') (Join-Path $arenaCompiler 'csc.dll') "@$arenaResponse"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
$arenaConfig = @{ runtimeOptions = @{ framework = @{ name = 'Microsoft.NETCore.App'; version = $arenaFramework.Name } } } | ConvertTo-Json -Depth 4
Set-Content -LiteralPath (Join-Path $arenaTemp 'DuelMatchTests.runtimeconfig.json') -Value $arenaConfig
& (Join-Path $arenaRuntime 'dotnet.exe') (Join-Path $arenaTemp 'DuelMatchTests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Duel rule tests failed' }
