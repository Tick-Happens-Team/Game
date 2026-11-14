---
name: vibe-delegate
description: Delegate mechanical, well-specified work (tests, ports, data entry) to Mistral Vibe (DeepSeek) in headless PowerShell, isolated under .tmp/vibe. Use for independent chunks while you continue other work.
---

1. Create `D:\tickhappens\.tmp\vibe\<task>\` with an isolated project if code must compile (e.g. a test csproj that `Compile Include`s only the stable source files it needs and sets TargetFramework/Nullable/TreatWarningsAsErrors itself, since Directory.Build.props is not inherited outside Game).
2. Write `prompt.md`: exact files to read, the ONLY directory it may write, forbidden paths (Game, Assets, git), acceptance command, "no TODOs/stubs".
3. `run.ps1`: `vibe --prompt-file prompt.md --workdir <dir> --add-dir D:\tickhappens\Game --trust --auto-approve --max-price 1.0 --time-limit 1800 --max-turns 120 --output-dir <dir>\out *>&1 | Out-File stdout.log`.
4. Launch with Bash `powershell -NoProfile -ExecutionPolicy Bypass -File run.ps1` and `run_in_background: true`; continue working; wait for the completion notification.
5. Review the output (read files, check scope), adapt paths to repo conventions, copy into Game, run `build-test`. Integrate only after review.
