# Online co-op for 2–4 players

Players install the game through the launcher from GitHub Releases. Hosting and joining by code happen in the game, including when launched directly. Online play requires internet access and access to EOS. Players do not need a VPN, port forwarding, registration, or a third-party launcher.

## Transport choice (November 2026)

- **EOS Connect (Device ID), Lobbies, and P2P** is the online path. Epic provides access without a service fee. Connect creates a guest identity on the device. Lobbies finds rooms. P2P tries a direct connection and uses a relay if needed. The team registers the product in Epic Developer Portal. [Licensing](https://onlineservices.epicgames.com/licensing) · [Device ID](https://dev.epicgames.com/docs/api-ref/functions/eos-connect-transfer-device-id-account) · [Relay modes](https://dev.epicgames.com/docs/en-US/api-ref/enums/eos-e-relay-control)
- **LiteNetLib** provides LAN and direct IP. Arbitrary NAT and CGNAT need another transport.
- **Steam Datagram Relay** hosted access depends on Steamworks terms. The open implementation supports custom signaling and infrastructure. [Valve documentation](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)
- **WebRTC with TURN** is a fallback to evaluate if EOS is blocked. It needs signaling and temporary TURN credentials. Self-hosting requires operations and bandwidth. Cloudflare TURN charges beyond its free allocation. [Cloudflare documentation](https://developers.cloudflare.com/realtime/turn/)

## Transport contract

### Local Windows EOS build

Use the official EOS C# SDK 1.19.2.1. Keep its `SDK` folder outside the repository. The build references the SDK source and Win64 shipping binary only when `TickHappensEosSdk` is set. Standard CI and offline builds run without EOS. Public Windows package integration is tracked in roadmap 3.5.

Create `%LOCALAPPDATA%/TickHappens/eos.local.json` or set `TICKHAPPENS_EOS_CONFIG` to a local JSON file with string fields `ProductId`, `SandboxId`, `DeploymentId`, `ClientId`, and `ClientSecret`. Use the credentials for the Windows Peer2Peer game client. Keep the file out of Git and build artifacts. EOS uses the current OS user for Device ID. Use two machines or distinct OS users for a join test.

```powershell
$env:TICKHAPPENS_EOS_CONFIG = 'D:/local/eos.local.json'
$sdk = 'D:/local/EOS/SDK'
dotnet run --project src/TickHappens.Game "-p:TickHappensEosSdk=$sdk" -- --eos-probe
dotnet run --project src/TickHappens.Game "-p:TickHappensEosSdk=$sdk"
```

The probe signs in, creates and publishes a room code, and destroys the lobby. In the character menu choose **Online**, then **Create lobby** or **Join by code**. The host can start after at least one compatible peer connects. Join before the host starts the run. The prototype uses the host's selected character for the team. Escape leaves the room. Set `TICKHAPPENS_EOS_FORCE_RELAY=1` on both machines to exercise relay-only connections. By default EOS tries a direct connection first, with relay fallback.

The current transport sends host snapshots at 10 Hz and player input at 30 Hz. Hosts advance the game and clients render snapshots. Motion reconciliation, gameplay-event cosmetics on clients, reconnect, mod installation on join, and peak bandwidth validation are outstanding. Use identical core and enabled mod data on all machines.

```csharp
interface ITransport {
    ValueTask<string> HostAsync(RoomOptions o);
    ValueTask JoinAsync(string roomCode);
    void Send(PeerId to, ReadOnlySpan<byte> data, Channel ch); // Reliable | Unreliable
    void Poll(ITransportEvents sink); // Connected, Disconnected, Data
}
```

LoopbackTransport supports single-process tests and bots. LiteNetLibTransport supports discovery and direct IP:port. EosTransport stores a random six-character Crockford base32 code as a searchable lobby attribute. Reissue the code on an ambiguous search. The host approves new members. Verify the Product User ID against lobby membership before accepting P2P. Use AllowRelays by default and offer ForceRelays to hide peer IP addresses. Share a copyable code or optionally register `tickhappens://join/CODE`. [Lobby reference](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/lobby-interface/lobby-interface-reference)

## Synchronization

| Data | Owner and transmission |
|---|---|
| Input | Client sends the latest N ticks redundantly at 30 Hz on the unreliable channel |
| Local hero position | Predicted locally and reconciled against host snapshots |
| RunClock and waves | Host includes tick and clock in every snapshot |
| Statues | Host sends reliable `StatueActivated{id, addedMinutes, tick}` events |
| Enemies | Host sends 15–20 Hz delta snapshots with IDs, spawn types, 16-bit positions, and HP buckets. Spawn and death events carry a seed reliably. |
| Damage | Host batches `Hit{target, amount, crit, sourceWeapon}` events. Clients draw VFX and numbers. |
| XP | Deterministic death drops from event seeds. Host confirms pickups. Shared versus individual XP remains open. |
| Gold | Host assigns each enemy death drop to the killer or the owner of the killing summon. Only that player can collect it automatically in range. |
| Chests and items | Host charges the nearby player's wallet, opens the shared chest once, and increases that player's next price by 1.5×. Host rolls loot and sends reliable results. |
| Particles, VFX, and audio | Clients generate them locally from events |

At 1,000 enemies and roughly five bytes per enemy at 20 Hz, estimated traffic is 100 KB/s per client. The budget is 150 KB/s.

## Compatibility and disconnects

The handshake includes protocol and game versions, the base content hash, and SHA-256 over sorted `(id, version, sha256)` entries for required mods. If game or content is incompatible, offer the compatible release via the launcher. For missing mods, download the exact versions and hashes into a session profile. Explain when a required mod is unavailable in the catalog.

A disconnected client can return within 60 seconds using the same room code. If the host leaves, end the run and show results. Host migration is outside the current plan.

## Security and release checks

Validate input and rate-limit messages and join attempts. Expire room codes. The host decides damage and loot. Use the minimal EOS Peer2Peer client policy. Any client credentials shipped with a future player build are visible to players. Keep the local config out of Git. Outside opted-in developer builds, fetch the proprietary SDK only in release CI and pin managed and native components to the same official release. Verify redistribution terms and notices. Forks register their own product. Builds without EOS support single-player and tests. LAN transport is still planned.

Smoke-test the shipped ZIP against the target EOS deployment. Verify connections across independent NAT and CGNAT networks, including ForceRelays. Verify understandable failure when outbound access is blocked and when EOS is unavailable. Check online play with the launcher closed.
