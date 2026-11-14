# Repositories, releases, and launcher

TickHappens is distributed free of charge through GitHub Releases. The public Tick-Happens-Team organization contains Game, Assets, and Mods. Game code and Mods catalog code use MIT. Original Assets media uses CC BY 4.0. Third-party content retains its separate license terms.

- Game contains the game, C# engine, launcher, AssetTool, shaders, schemas, core gameplay data, and GitHub Actions.
- Assets contains palettes, recipes, indexed sprites, animations, music, sound, licenses, and provenance. GitHub Releases will hold runtime art and audio ZIPs.
- Mods accepts community PRs and publishes ZIPs to Releases and a signed index to Pages. The game provides the [Workshop client](MODDING.md).

## Branches and version pairing

Open pull requests from topic branches into `dev`, the default branch for Game and Assets. Mods PRs target `main`. Review and run CI before merging, including for documentation changes. For changes spanning Game and Assets, link the PRs and merge Assets first when Game depends on the new atlas. Promote tested changes to `stable` through a separate PR. Promote the matching Assets changes first when the Game promotion depends on them.

Until the launcher is available, run the prototype directly from the Game checkout using the [README](../README.md). Keep a compatible Assets checkout beside Game or select it with `TICKHAPPENS_ASSETS_ROOT`. Asset generation runs locally on files. As part of the release rollout, Game will pin the full Assets commit and expected package tag in a lock file. Stable release builds will require an available package from the Assets/stable history.

The current workflows build and test both branches. Assets publishes an atlas ZIP and SHA-256 checksum from stable. Game uploads a self-contained Windows visual benchmark as an Actions artifact from stable. The signed launcher and game release described below requires the playable game and launcher.

## Publication

1. Assets/stable CI runs AssetTool from the full Game commit pinned in Assets. The first release starts from an existing working AssetTool. CI validates inputs, records pack format and exact commit, and publishes a ZIP with licenses under an immutable tag.
2. Game/stable CI resolves the pinned package and verifies metadata and format. It builds self-contained win-x64 game and launcher ZIPs, runs tests, and smoke-tests the exact pair. Shaders ship in the game package.
3. Game CI stages the game ZIP, launcher ZIP, notices, and signed manifest in a draft. It publishes the immutable release with latest disabled, verifies public availability of both packages, and promotes it to latest. Serialize publication and reject stale promotions. Retries resume drafts or verify published artifacts before promoting. Corrections use a new tag.

Build on `ChannelManifest` and `PackageManifest` in Distribution. The signed manifest names its schema, sequence, minimum launcher version, game version, RID, asset format, and exact package tags and source commits. Each ZIP has a URL, byte length, SHA-256, and embedded file manifest SHA-256. A fixed filename under `Game/releases/latest/download/` identifies the signed manifest. The launcher uses one manifest throughout an update, retrying the same packages if temporarily unavailable. [GitHub release management](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository) · [Latest-release URLs](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases)

CI signs with a key from the protected Game environment. The launcher verifies the signature before using any fields. Ship trust for a replacement key before rotating. PR and test workflows use read-only access. Only protected release jobs receive publish permissions.

## Install, repair, and run

Players initially download the launcher ZIP from Game Releases. The launcher validates the manifest, schema, RID, minimum launcher version, and monotonic release sequence. Keep the highest successfully installed sequence across local rollbacks. If the launcher is too old, show the download link for a new one. Cache packages by digest to reuse unchanged content.

Verify archive lengths and SHA-256 before extracting into same-volume staging. Validate paths, links, collisions, and extracted size. Verify installed files against the embedded hashes and use them for repair. Prevent concurrent installs and wait for the game to exit before switching. Activate the complete verified pair by atomically replacing a local pointer.

A failed or interrupted update leaves the current pair usable. Keep the previous working pair for local rollback and support offline launch. Keep settings, saves, and mods outside versioned installations. If a save format is incompatible, allow the player to select a backup for rollback. A central rollback publishes a larger sequence pointing to a previously tested pair.

Players may launch the game directly after installation. The game provides the room UI and checks Workshop mods against the game version and content schema.
