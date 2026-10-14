param([string]$OutputName = 'RE8CharacterManager.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001',
    ('/out:' + (Join-Path $taskRoot $OutputName)),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    (Join-Path $taskRoot 'src\Core.cs'), (Join-Path $taskRoot 'src\App.cs'), (Join-Path $taskRoot 'src\Tests.cs'), (Join-Path $taskRoot 'src\AssemblyInfo.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output (Join-Path $taskRoot $OutputName)
