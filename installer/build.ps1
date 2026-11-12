$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
$appRoot = $packageRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','Microsoft.CSharp.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ ('/win32icon:' + (Join-Path $appRoot 'ClaudeMeter.ico')) ('/win32manifest:' + (Join-Path $appRoot 'source/app.manifest')) ('/resource:' + (Join-Path $PSScriptRoot 'payload.zip') + ',payload.zip') ('/out:' + (Join-Path $packageRoot 'ClaudeMeter-1.0-Lite-Setup.exe')) $references (Join-Path $PSScriptRoot 'Setup.cs') (Join-Path $appRoot 'source/ClaudeAccount.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
