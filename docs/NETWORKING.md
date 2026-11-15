# Online co-op for 2–4 players

Players install the game through the launcher from GitHub Releases. Hosting and joining happen in the game, including when launched directly. LAN play uses LiteNetLib. Internet play uses EOS with a guest device identity.

## Transport choice (November 2026)

- **EOS Connect (Device ID), Lobbies, and P2P** is the online path. Epic provides access without a service fee. Connect creates a guest identity on the device. Lobbies finds rooms. P2P tries a direct connection and uses a relay if needed. The team registers the product in Epic Developer Portal. [Licensing](https://onlineservices.epicgames.com/licensing) · [Device ID](https://dev.epicgames.com/docs/api-ref/functions/eos-connect-transfer-device-id-account) · [Relay modes](https://dev.epicgames.com/docs/en-US/api-ref/enums/eos-e-relay-control)
- **LiteNetLib** provides LAN and direct IP. Arbitrary NAT and CGNAT need another transport.
- **Steam Datagram Relay** hosted access depends on Steamworks terms. The open implementation supports custom signaling and infrastructure. [Valve documentation](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)
- **WebRTC with TURN** is a fallback to evaluate if EOS is blocked. It needs signaling and temporary TURN credentials. Self-hosting requires operations and bandwidth. Cloudflare TURN charges beyond its free allocation. [Cloudflare documentation](https://developers.cloudflare.com/realtime/turn/)

## Transport contract

### LAN and direct IPv4

Open **Co-op** with **F1** in the character menu. On the host, choose **Create lobby** with **F2**. Clients on the same subnet can choose **Find LAN room** with **F3**. If discovery finds multiple rooms or broadcasts are blocked, use **Direct IP** with **F5** and enter the host's IPv4 address. An optional `:port` overrides the default UDP port 39230. The period key enters a dot and the semicolon key enters a colon. The host can start with **Enter** once two or more players are in the room.

For a two-computer smoke test, allow inbound UDP 39230 on the host's private network. Try discovery and direct IPv4 in separate runs, move and dash on both peers, and verify that closing the host shows the guest's results. Both users need matching game builds and enabled gameplay content.

### Local Windows EOS build

Use the official EOS C# SDK 1.19.2.1. Keep its `SDK` folder outside the repository. The build references the SDK source and Win64 shipping binary only when `TickHappensEosSdk` is set. Standard CI and offline builds run without EOS. Public Windows package integration is tracked in roadmap 3.5.

Create `%LOCALAPPDATA%/TickHappens/eos.local.json` or set `TICKHAPPENS_EOS_CONFIG` to a local JSON file with string fields `ProductId`, `SandboxId`, `DeploymentId`, `ClientId`, and `ClientSecret`. Use the credentials for the Windows Peer2Peer game client. Keep the file out of Git and build artifacts. EOS uses the current OS user for Device ID. Use two machines or distinct OS users for a join test.

```powershell
$env:TICKHAPPENS_EOS_CONFIG = 'D:/local/eos.local.json'
$sdk = 'D:/local/EOS/SDK'
dotnet run --project src/TickHappens.Game "-p:TickHappensEosSdk=$sdk" -- --eos-probe
dotnet run --project src/TickHappens.Game "-p:TickHappensEosSdk=$sdk"
```

The probe signs in, creates and publishes a room code, and destroys the lobby. In the character menu open **Co-op** with **F1**, switch to EOS with **F6**, then choose **Create lobby** or **Join by code**. The host can start after at least one compatible peer connects. Join before the host starts the run. The prototype uses the host's selected character for the team. Escape leaves the room. Use **F5** in the EOS menu to enable relay-only mode before joining or hosting. The `TICKHAPPENS_EOS_FORCE_RELAY=1` environment variable sets its initial value. By default EOS tries a direct connection first, with relay fallback.

The current transport sends host snapshots at 10 Hz and player input at 30 Hz. Hosts advance the game. Clients predict their own movement and dash, reconcile to the authoritative state, and replay inputs newer than the last one the host applied. Clients interpolate remote players and enemies. Reliable keyframes arrive about once per second. Smaller unreliable snapshots refer to the current keyframe, with a full-state fallback when the delta would be larger. The host sends reliable gameplay-event batches, including chain paths, visual seeds, and the RNG state for enemy gold drops. Clients replay those events and read loot and progression from authoritative snapshots. Exact mod installation on join and measurements across independent networks are outstanding. Use identical core and enabled mod data on all machines.

```csharp
interface ICoopTransport
{
    event Action<string, ReadOnlyMemory<byte>>? PacketReceived;
    event Action<string>? PeerDisconnected;
    void Send(string peerId, ReadOnlySpan<byte> payload, bool reliable);
}
```

Loopback endpoints support single-process tests. LanTransport supports discovery and direct IPv4 with an optional port. EosTransport stores a random six-character Crockford base32 code as a searchable lobby attribute. Reissue the code on an ambiguous search. The EOS transport checks the Product User ID against lobby membership before accepting P2P. The default is AllowRelays. The EOS menu can select ForceRelays before a connection. [Lobby reference](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/lobby-interface/lobby-interface-reference)

## Synchronization

| Data | Owner and transmission |
|---|---|
| Input | Client sends the latest three samples redundantly at 30 Hz on the unreliable channel |
| Local hero position | Predicted locally and reconciled against host snapshots |
| RunClock and waves | Host includes tick and clock in every snapshot |
| Statues | Host sends reliable activation events with the statue ID and added minutes |
| Enemies | Host sends 15–20 Hz delta snapshots with IDs, spawn types, 16-bit positions, and HP buckets. Enemy death events carry the drop seed on the reliable channel. |
| Damage | Host batches hit events with the hit position, damage, and damage kind. Clients draw VFX and numbers. |
| XP | The host computes enemy drops and pickups and includes their state in snapshots. The kill event carries the RNG state used for the gold roll. |
| Gold | Host assigns each enemy death drop to the killer or the owner of the killing summon. Only that player can collect it automatically in range. |
| Chests and items | Host charges the nearby player's wallet, opens the shared chest once, and increases that player's next price by 1.5×. Host rolls loot and sends reliable results. |
| Particles and VFX | Clients generate them locally from events |

Run the loopback UDP benchmark with a game host, a client, and 1,000 active enemies.

```powershell
dotnet run --project src/TickHappens.Game -c Release -- --net-bench .tmp/bench/network.csv
```

The report records host bytes sent and elapsed time from client input submission to authoritative acknowledgement. It excludes IP and UDP headers and does not measure latency between computers. The local sample measured 70.0 kB/s host outbound, with acknowledgement latency of 73.5 ms average and 78.6 ms p99. The per-client traffic budget is 150 kB/s.

## Compatibility and disconnects

The handshake checks the protocol version and a SHA-256 digest of the loaded gameplay data, including enabled local item mods. Exact required-mod versions, catalog retrieval, and a session profile depend on the mod loader and catalog in P4.

When a connection drops during a run, the client shows the latest results. Press **F7** in that window to retry the previous LAN address or EOS room code. Keep the guest game process open to preserve the reconnect key. If the host is still running, it restores the same player slot and progress for up to 60 seconds from when it detected the disconnect. EOS reconnect still needs verification between separate devices. Host migration is outside the current plan.

## Security and release checks

Validate input and rate-limit messages and join attempts. Expire room codes. The host decides damage and loot. Use the minimal EOS Peer2Peer client policy. Any client credentials shipped with a future player build are visible to players. Keep the local config out of Git. Outside opted-in developer builds, fetch the proprietary SDK only in release CI and pin managed and native components to the same official release. Verify redistribution terms and notices. Forks register their own product. Builds without EOS support single-player and LAN co-op.

Smoke-test the shipped ZIP against the target EOS deployment. Verify connections across independent NAT and CGNAT networks, including ForceRelays. Verify understandable failure when outbound access is blocked and when EOS is unavailable. Check online play with the launcher closed.
