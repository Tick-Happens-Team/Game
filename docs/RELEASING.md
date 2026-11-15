# Repositories, releases, and launcher

TickHappens is distributed free of charge through GitHub Releases. The public Tick-Happens-Team organization contains Game, Assets, and Mods. Game code and Mods catalog code use MIT. Original Assets media uses CC BY 4.0. Third-party content retains its separate license terms.

- Game contains the game, C# engine, launcher, AssetTool, shaders, schemas, core gameplay data, and GitHub Actions.
- Assets contains palettes, recipes, indexed sprites, animations, music, sound, licenses, and provenance. GitHub Releases will hold runtime art and audio ZIPs.
- Mods accepts community PRs and publishes ZIPs to Releases and a signed index to Pages. The game provides the [Workshop client](MODDING.md).

## Branches and version pairing

Open pull requests from topic branches into `dev`, the default branch for Game and Assets. Mods PRs target `main`. Review and run CI before merging, including for documentation changes. For changes spanning Game and Assets, link the PRs and merge Assets first when Game depends on the new atlas. Promote tested changes to `stable` through a separate PR. Promote the matching Assets changes first when the Game promotion depends on them.

Until the launcher is available, run the prototype directly from the Game checkout using the [README](../README.md). Keep a compatible Assets checkout beside Game or select it with `TICKHAPPENS_ASSETS_ROOT`. Asset generation runs locally on files. As part of the release rollout, Game will pin the full Assets commit and expected package tag in a lock file. Stable release builds will require an available package from the Assets/stable history.

Game and Assets automatically squash-merge ready same-repository PRs into `dev` when an organization owner authors the PR and an owner triggers a successful CI run for its current head. Keep cross-repository PRs in draft until their dependencies are merged. Update the owner account IDs in both `admin-auto-merge.yml` workflows when organization ownership changes.

The current workflows build and test both branches. Assets publishes an atlas ZIP and SHA-256 checksum from stable. Game uploads a self-contained Windows visual benchmark as an Actions artifact from stable. The signed launcher and game release described below requires the playable game and launcher.

## Publication

1. Assets/stable CI runs AssetTool from the full Game commit pinned in Assets. The first release starts from an existing working AssetTool. CI validates inputs, records pack format and exact commit, and publishes a ZIP with licenses under an immutable tag.
2. Game/stable CI resolves the pinned package and verifies metadata and format. It builds self-contained win-x64 game and launcher ZIPs, runs tests, and smoke-tests the exact pair. Shaders ship in the game package.
3. Game CI stages the game ZIP, launcher ZIP, notices, and signed manifest in a draft. It publishes the immutable release with latest disabled, verifies public availability of both packages, and promotes it to latest. Serialize publication and reject stale promotions. Retries resume drafts or verify published artifacts before promoting. Corrections use a new tag.

Build on `ChannelManifest` and `PackageManifest` in Distribution. The signed manifest names its schema, sequence, minimum launcher version, game version, RID, asset format, and exact package tags and source commits. Each ZIP has a URL, byte length, SHA-256, and embedded file manifest SHA-256. A fixed filename under `Game/releases/latest/download/` identifies the signed manifest. The launcher uses one manifest throughout an update, retrying the same packages if temporarily unavailable. [GitHub release management](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository) · [Latest-release URLs](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases)

CI signs with a key from the protected Game environment. The launcher verifies the signature before using any fields. Ship trust for a replacement key before rotating. PR and test workflows use read-only access. Only protected release jobs receive publish permissions.

### Prepare a signed Windows release

Use clean, exact Game and Assets checkouts and PowerShell 7.3 or later. Run `dotnet run --project tools/TickHappens.ReleaseTool -- keygen PRIVATE.pem PUBLIC.pem` once for a new release key and store the private PEM in the protected signing environment. Set `TICKHAPPENS_RELEASE_PRIVATE_KEY` from that secret for the packaging step. Keep the same public key across releases so existing launchers can verify new manifests.

```powershell
./tools/Pack-LauncherRelease.ps1 -GameVersion 0.1.0 -LauncherVersion 0.1.0 `
    -AssetsVersion 0.1.0 -Sequence 1 -PublicKey C:/signing/public.pem `
    -AssetsRoot ../Assets -DownloadBase https://github.com/Tick-Happens-Team/Game/releases/download/v0.1.0 `
    -Output .tmp/release-v0.1.0
```

To include EOS in a signed Windows build, add `-EosSdkArchive` and `-EosConfig`. The archive is either the official C# archive `EOS-SDK-CSharp-58105819-Release-v1.19.2.1.zip` or its build subset `eos-sdk-csharp-1.19.2.1-win64.zip` (C# sources from `Source/Core`, `Generated`, and `Overrides`, the Win64 shipping DLL, and the notices). The packer checks both pinned SHA-256 values before extraction and verifies that the ZIP staging contains the managed wrapper, Win64 shipping DLL, and SDK notices. `-EosConfig` names the Peer2Peer client JSON. The packer validates its five fields and ships it as `eos.json` in the game package. Upload only the intended artifacts from the `release` output directory. See [local EOS configuration](NETWORKING.md#local-windows-eos-build).

### Release workflow

`.github/workflows/release.yml` runs only through manual dispatch from `stable` in the protected `release` environment. Its deployment branch policy allows only `stable`. Inputs are the game, launcher, and assets versions, a full Assets commit from `Assets/stable`, and a sequence larger than the current latest release. The job builds and tests, generates the atlas, packages and signs with `tools/release-public.pem`, and stages a draft `v<game version>`. It publishes the release with latest disabled, downloads every file and compares it with the signed build, and then promotes the release to latest. A run resumes an interrupted draft. It fails on an already published tag and refuses a promotion when a newer sequence is already latest.

The `release` environment holds these secrets:

- `TICKHAPPENS_RELEASE_PRIVATE_KEY` signs `channel.json`. Its public half is `tools/release-public.pem` and is embedded in every launcher. Keep an offline backup of the private key. Losing it requires shipping trust for a new key through a launcher signed with the old one.
- `EOS_CONFIG_JSON` holds the Peer2Peer client configuration that becomes `eos.json`.
- `EOS_SDK_DEPLOY_KEY` is a read-only SSH deploy key for the private `Tick-Happens-Team/eos-sdk` repository. That repository holds the build subset archive at its root. The proprietary SDK stays out of Game and its public history.


Publish `launcher-win-x64.zip`, `game-win-x64.zip`, `assets.zip`, `channel.json`, and `channel.json.sig` from the output's `release` directory. The adjacent ZIP reference JSON files are packaging intermediates. Test away from the source checkouts before promoting a release to latest. For a local smoke test, point both the package URLs and `TICKHAPPENS_CHANNEL_URL` at the same loopback HTTP file server and use a disposable test key.

## Install, repair, and run

Players initially download the launcher ZIP from Game Releases. The launcher validates the manifest, schema, RID, minimum launcher version, and monotonic release sequence. Keep the highest successfully installed sequence across local rollbacks. If the launcher is too old, show the download link for a new one. Cache packages by digest to reuse unchanged content.

Verify archive lengths and SHA-256 before extracting into same-volume staging. Validate paths, links, collisions, and extracted size. Verify installed files against the embedded hashes and use them for repair. Prevent concurrent installs and wait for the game to exit before switching. Activate the complete verified pair by atomically replacing a local pointer.

A failed or interrupted update leaves the current pair usable. Keep the previous working pair for local rollback and support offline launch. Keep settings, saves, and mods outside versioned installations. If a save format is incompatible, allow the player to select a backup for rollback. A central rollback publishes a larger sequence pointing to a previously tested pair.

Players may launch the game directly after installation. The game provides the room UI and checks Workshop mods against the game version and content schema.

The launcher window has Play, Mods, and Settings pages. The Play page is a box cover: a light ground with faceted shards, the title over a rule, and the playable heroes in a row with their idle animation. It also shows the installed game version and release sequence. Settings show the assets version as well. A bottom bar shows status, download progress, and the play button. Settings choose the language, check for updates, and open the launcher folder. For review captures, run the launcher with `--offline`, `--page play|mods|settings`, `--frames N`, and `--screenshot path.png`. The capture waits for any running update to finish. `TICKHAPPENS_LAUNCHER_ROOT` replaces `%LOCALAPPDATA%/TickHappens` for disposable test installs.

The Windows launcher checks on startup and every 15 minutes while idle. It can also retry from its window. It stores packages, versioned installs, and the local mod profile under `%LOCALAPPDATA%/TickHappens`. Keep the original launcher shortcut. It hands off to a verified newer executable in that directory. A failed update leaves the last installed game available, including offline. Previous installations remain on disk. Selecting a previous version in the UI and signed Workshop catalog browsing are roadmap work.

Local item mods go under `mods/<mod-id>/items.json`. The file is an array using the core item schema, with IDs such as `my-mod:my-item`. Check the mod in the launcher to include it at the next launch. The profile also applies when launching the installed game directly. Only item definitions are supported in this local workflow.
