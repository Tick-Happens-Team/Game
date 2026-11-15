#Requires -Version 7.3
param(
    [Parameter(Mandatory)][string]$GameVersion,
    [Parameter(Mandatory)][string]$LauncherVersion,
    [Parameter(Mandatory)][string]$AssetsVersion,
    [Parameter(Mandatory)][long]$Sequence,
    [Parameter(Mandatory)][string]$PublicKey,
    [Parameter(Mandatory)][string]$AssetsRoot,
    [Parameter(Mandatory)][string]$DownloadBase,
    [Parameter(Mandatory)][string]$Output,
    [string]$EosSdkArchive
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($LauncherVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch launcher version.' }
if ([string]::IsNullOrWhiteSpace($env:TICKHAPPENS_RELEASE_PRIVATE_KEY)) { throw 'The protected signing key is required.' }
$gameRoot = Split-Path -Parent $PSScriptRoot
$assetSource = (Resolve-Path -LiteralPath $AssetsRoot).Path
$key = (Resolve-Path -LiteralPath $PublicKey).Path
$outputRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputRoot) { throw 'Choose a new output directory.' }
if ($EosSdkArchive) {
    $archivePath = (Resolve-Path -LiteralPath $EosSdkArchive).Path
    $expectedHash = 'A4CA3F44FA08DDFCEAFCD0009585AAAC4C7728E6E195E5035863B9613E8976CD'
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -cne $expectedHash) {
        throw 'Expected the pinned EOS C# SDK 1.19.2.1 archive.'
    }
}
$gameCommit = git -C $gameRoot rev-parse HEAD
$assetsCommit = git -C $assetSource rev-parse HEAD
if (!$gameCommit -or !$assetsCommit) { throw 'Both source directories must be Git checkouts.' }
$game = Join-Path $outputRoot game
$launcher = Join-Path $outputRoot launcher
$assets = Join-Path $outputRoot media
$release = Join-Path $outputRoot release
New-Item -ItemType Directory -Path $assets,$release | Out-Null

$gamePublishArgs = @(
    "$gameRoot/src/TickHappens.Game", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-o', $game, "-p:Version=$GameVersion"
)
if ($EosSdkArchive) {
    $sdkStage = Join-Path $outputRoot eos-sdk
    Expand-Archive -LiteralPath $archivePath -DestinationPath $sdkStage
    $gamePublishArgs += "-p:TickHappensEosSdk=$sdkStage/SDK"
}
dotnet publish @gamePublishArgs
if ($EosSdkArchive) {
    foreach ($name in 'TickHappens.EosSdk.dll', 'EOSSDK-Win64-Shipping.dll', 'EOS-ThirdPartySoftwareNotice.txt') {
        if (!(Test-Path -LiteralPath (Join-Path $game $name) -PathType Leaf)) {
            throw "EOS build did not publish $name."
        }
    }
}
dotnet publish "$gameRoot/src/TickHappens.Launcher" -c Release -r win-x64 --self-contained true -o $launcher "-p:ReleasePublicKey=$key" "-p:Version=$LauncherVersion"
Copy-Item -LiteralPath "$gameRoot/LICENSE" -Destination "$game/LICENSE-Game"
Copy-Item -LiteralPath "$gameRoot/LICENSE" -Destination "$launcher/LICENSE-Game"
Copy-Item -LiteralPath "$gameRoot/src/TickHappens.Engine/Vfx/LICENSE-SpriteFusion-VFX.txt" -Destination $game
Copy-Item -LiteralPath "$assetSource/assets" -Destination $assets -Recurse
Copy-Item -LiteralPath "$assetSource/LICENSE.md" -Destination "$assets/LICENSE-Assets.md"
if (Test-Path -LiteralPath "$assetSource/THIRD_PARTY_NOTICES.md") {
    Copy-Item -LiteralPath "$assetSource/THIRD_PARTY_NOTICES.md" -Destination $assets
}

$tool = "$gameRoot/tools/TickHappens.ReleaseTool"
dotnet build $tool -c Release
$base = $DownloadBase.TrimEnd('/')
dotnet run --project $tool -c Release --no-build -- pack game $GameVersion $gameCommit $game "$release/game-win-x64.zip" "$base/game-win-x64.zip"
dotnet run --project $tool -c Release --no-build -- pack assets $AssetsVersion $assetsCommit $assets "$release/assets.zip" "$base/assets.zip"
dotnet run --project $tool -c Release --no-build -- pack launcher $LauncherVersion $gameCommit $launcher "$release/launcher-win-x64.zip" "$base/launcher-win-x64.zip"
dotnet run --project $tool -c Release --no-build -- channel $Sequence $LauncherVersion "$release/game-win-x64.zip.json" "$release/assets.zip.json" "$release/launcher-win-x64.zip.json" $key "$release/channel.json"
