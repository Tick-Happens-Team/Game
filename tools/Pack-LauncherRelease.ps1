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
    [string]$EosSdkArchive,
    [string]$EosConfig,
    # Directory of <language>.json patch notes signed into channel.json.
    [string]$NotesDirectory,
    # The previous release's channel.json, with channel.json.sig beside it; its version history is carried forward.
    [string]$PreviousChannel
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
    # The official archive, or the build subset (Source/Core, Generated, Overrides, the Win64 shipping DLL and the
    # notices) that release CI fetches from the private eos-sdk repository.
    $expectedHashes = @(
        'A4CA3F44FA08DDFCEAFCD0009585AAAC4C7728E6E195E5035863B9613E8976CD',
        '7A31A4F3E55ADE779D267B01A4B55FACF1123775854F84A3871B94D32F9655D5'
    )
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -notin $expectedHashes) {
        throw 'Expected the pinned EOS C# SDK 1.19.2.1 archive.'
    }
}
if ($EosConfig) {
    if (!$EosSdkArchive) { throw 'EosConfig requires EosSdkArchive.' }
    $eosConfigPath = (Resolve-Path -LiteralPath $EosConfig).Path
    $eosFields = Get-Content -LiteralPath $eosConfigPath -Raw | ConvertFrom-Json
    foreach ($field in 'ProductId', 'SandboxId', 'DeploymentId', 'ClientId', 'ClientSecret') {
        if ([string]::IsNullOrWhiteSpace($eosFields.$field)) { throw "EOS configuration field $field is required." }
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
    if ($EosConfig) { Copy-Item -LiteralPath $eosConfigPath -Destination (Join-Path $game eos.json) }
}
dotnet publish "$gameRoot/src/TickHappens.Launcher" -c Release -r win-x64 --self-contained true -o $launcher "-p:ReleasePublicKey=$key" "-p:Version=$LauncherVersion"
Copy-Item -LiteralPath "$gameRoot/LICENSE" -Destination "$game/LICENSE-Game"
Copy-Item -LiteralPath "$gameRoot/LICENSE" -Destination "$launcher/LICENSE-Game"
Copy-Item -LiteralPath "$gameRoot/src/TickHappens.Engine/Vfx/LICENSE-SpriteFusion-VFX.txt" -Destination $game
Copy-Item -LiteralPath "$assetSource/assets" -Destination $assets -Recurse
Copy-Item -LiteralPath "$assetSource/LICENSE.md" -Destination "$assets/LICENSE-Assets.md"
# The launcher embeds the hero atlas for its cover page.
Copy-Item -LiteralPath "$assetSource/LICENSE.md" -Destination "$launcher/LICENSE-Assets.md"
if (Test-Path -LiteralPath "$assetSource/THIRD_PARTY_NOTICES.md") {
    Copy-Item -LiteralPath "$assetSource/THIRD_PARTY_NOTICES.md" -Destination $assets
}

$tool = "$gameRoot/tools/TickHappens.ReleaseTool"
dotnet build $tool -c Release
$base = $DownloadBase.TrimEnd('/')
dotnet run --project $tool -c Release --no-build -- pack game $GameVersion $gameCommit $game "$release/game-win-x64.zip" "$base/game-win-x64.zip"
dotnet run --project $tool -c Release --no-build -- pack assets $AssetsVersion $assetsCommit $assets "$release/assets.zip" "$base/assets.zip"
dotnet run --project $tool -c Release --no-build -- pack launcher $LauncherVersion $gameCommit $launcher "$release/launcher-win-x64.zip" "$base/launcher-win-x64.zip"
$channelArgs = @(
    'channel', $Sequence, $LauncherVersion, "$release/game-win-x64.zip.json",
    "$release/assets.zip.json", "$release/launcher-win-x64.zip.json", $key, "$release/channel.json"
)
if ($NotesDirectory) { $channelArgs += (Resolve-Path -LiteralPath $NotesDirectory).Path }
if ($PreviousChannel) { $channelArgs += '--previous', (Resolve-Path -LiteralPath $PreviousChannel).Path }
dotnet run --project $tool -c Release --no-build -- @channelArgs
