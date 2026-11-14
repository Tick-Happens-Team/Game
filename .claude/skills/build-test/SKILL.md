---
name: build-test
description: Build the TickHappens solution and run all tests (warnings are errors). Use before declaring any Game change done, after editing C#, shaders, content JSON or tests.
---

Run from `D:\tickhappens\Game` (Assets must be the sibling `..\Assets`, or set `TICKHAPPENS_ASSETS_ROOT`).

1. `dotnet build TickHappens.sln -v q 2>&1 | grep -E "error|Ошибок|Предупр"` — expect 0 errors and 0 warnings.
   To list errors compactly: `... | grep " error " | awk -F'tickhappens' '{print $NF}' | sort -u`.
2. `dotnet test TickHappens.sln 2>&1 | grep -E "Пройден|Не пройден|\[FAIL\]"` — all must pass.
3. If art sources changed in Assets: `dotnet run --project tools/TickHappens.AssetTool -- build` (lint errors block).
4. Then run the affected entry point (see the `play-capture` skill). A green build is not proof that the game works.

Test scratch files go to `.tmp/tests` via `AssetPipelineTests.TmpDir()`, never the system temp dir.
