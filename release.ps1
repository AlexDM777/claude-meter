param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$declared=[regex]::Match((Get-Content (Join-Path $root 'source/AppUpdate.cs') -Raw),'CurrentVersion="([^"]+)"').Groups[1].Value
if([version]$Version -ne [version]$declared){throw 'Release version must match AppUpdate.CurrentVersion'}
& (Join-Path $root 'source/build.ps1')
$dist=Join-Path $root 'dist';New-Item $dist -ItemType Directory -Force|Out-Null
$stage=Join-Path $root ('build/release-'+[guid]::NewGuid().ToString('N'));New-Item $stage -ItemType Directory -Force|Out-Null
$definition=[regex]::Match((Get-Content (Join-Path $root 'source/UpdatePackage.cs') -Raw),'RuntimeFiles=\{([^}]+)\}').Groups[1].Value
$runtime=[regex]::Matches($definition,'"([^"]+)"')|ForEach-Object{$_.Groups[1].Value}
foreach($file in $runtime){Copy-Item (Join-Path $root $file) $stage}
Copy-Item (Join-Path $root 'skins') $stage -Recurse
$payload=Join-Path $root 'installer/payload.zip'
Compress-Archive -Path (Get-ChildItem $stage|ForEach-Object{$_.FullName}) -DestinationPath $payload -Force
& (Join-Path $root 'installer/build.ps1')
$label=if(([version]$Version).Build -eq 0){([version]$Version).ToString(2)}else{$Version}
Copy-Item (Join-Path $root 'ClaudeMeter-1.0-Lite-Setup.exe') (Join-Path $dist "ClaudeMeter-$label-Lite-Setup.exe") -Force
Copy-Item $payload (Join-Path $dist "ClaudeMeter-$label-Lite-Portable.zip") -Force
$hashes=[ordered]@{}
Get-ChildItem $stage -Recurse -File|Sort-Object FullName|ForEach-Object{$relative=$_.FullName.Substring($stage.Length+1).Replace('\','/');$hashes[$relative]=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
@{version=$Version;files=$hashes}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $stage 'update-manifest.json') -Encoding UTF8
Compress-Archive -Path (Get-ChildItem $stage|ForEach-Object{$_.FullName}) -DestinationPath (Join-Path $dist 'ClaudeMeter-update-win-x64.zip') -Force
$lines=Get-ChildItem $dist -File|Where-Object{ $_.Name -in @(('ClaudeMeter-'+$label+'-Lite-Portable.zip'),('ClaudeMeter-'+$label+'-Lite-Setup.exe'),'ClaudeMeter-update-win-x64.zip') }|Sort-Object Name|ForEach-Object{(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$lines,[Text.UTF8Encoding]::new($false))
Write-Output "Release $Version built in $dist"
