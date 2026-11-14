#if TICKHAPPENS_EOS
using System.Security.Cryptography;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace TickHappens.Game.Online;

public sealed partial class EosTransport
{
    public Task<string> HostAsync(int maxPlayers = 4)
    {
        RequireNoLobby();
        if (maxPlayers is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
        Begin();
        var finished = new TaskCompletionSource<string>();
        string code = string.Create(6, RandomNumberGenerator.GetBytes(6), static (output, random) =>
        {
            for (int i = 0; i < output.Length; i++) output[i] = Alphabet[random[i] & 31];
        });
        var options = new CreateLobbyOptions
        {
            LocalUserId = _local, MaxLobbyMembers = (uint)maxPlayers,
            PermissionLevel = LobbyPermissionLevel.Publicadvertised,
            BucketId = Bucket, DisableHostMigration = true,
        };
        _lobby.CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            string? id = info.LobbyId?.ToString();
            _completed.Enqueue(() =>
            {
                try
                {
                    Check(result, "CreateLobby");
                    _lobbyId = id ?? throw new InvalidOperationException("EOS returned no lobby ID.");
                    HostPeer = LocalPeer;
                    PublishCode(code, finished);
                }
                catch (Exception error)
                {
                    DestroyFailedHost();
                    Fail(finished, error);
                }
            });
        });
        return finished.Task;
    }

    private void PublishCode(string code, TaskCompletionSource<string> finished)
    {
        var options = new UpdateLobbyModificationOptions { LocalUserId = _local, LobbyId = _lobbyId };
        Check(_lobby.UpdateLobbyModification(ref options, out var modification), "UpdateLobbyModification");
        var attribute = new LobbyModificationAddAttributeOptions
        {
            Attribute = new AttributeData { Key = CodeAttribute, Value = code },
            Visibility = LobbyAttributeVisibility.Public,
        };
        try
        {
            Check(modification.AddAttribute(ref attribute), "AddAttribute");
            var update = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            _lobby.UpdateLobby(ref update, null, (ref UpdateLobbyCallbackInfo info) =>
            {
                Result result = info.ResultCode;
                _completed.Enqueue(() =>
                {
                    modification.Release();
                    if (result != Result.Success)
                    {
                        DestroyFailedHost();
                        Fail(finished, Error(result, "Publish room code"));
                        return;
                    }
                    RefreshMembers();
                    _busy = false;
                    finished.SetResult(code);
                });
            });
        }
        catch
        {
            modification.Release();
            throw;
        }
    }

    public Task JoinAsync(string roomCode)
    {
        RequireNoLobby();
        string code = roomCode.Trim().ToUpperInvariant().Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (code.Length != 6 || code.Any(character => !Alphabet.Contains(character)))
            throw new ArgumentException("Enter the six-character room code.", nameof(roomCode));
        Begin();
        var finished = new TaskCompletionSource();
        LobbySearch? search = null;
        try
        {
            var create = new CreateLobbySearchOptions { MaxResults = 2 };
            Check(_lobby.CreateLobbySearch(ref create, out search), "CreateLobbySearch");
            SearchParameter(search, CodeAttribute, code);
            SearchParameter(search, LobbyInterface.SEARCH_BUCKET_ID, Bucket);
            var find = new LobbySearchFindOptions { LocalUserId = _local };
            search.Find(ref find, null, (ref LobbySearchFindCallbackInfo info) =>
            {
                Result result = info.ResultCode;
                _completed.Enqueue(() => JoinFoundRoom(search, result, finished));
            });
        }
        catch (Exception error)
        {
            search?.Release();
            Fail(finished, error);
        }
        return finished.Task;
    }

    private static void SearchParameter(LobbySearch search, Utf8String key, string value)
    {
        var parameter = new LobbySearchSetParameterOptions
        {
            Parameter = new AttributeData { Key = key, Value = value },
            ComparisonOp = ComparisonOp.Equal,
        };
        Check(search.SetParameter(ref parameter), "Search parameter");
    }

    private void JoinFoundRoom(LobbySearch search, Result result, TaskCompletionSource finished)
    {
        LobbyDetails? details = null;
        try
        {
            Check(result, "Find room");
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = search.GetSearchResultCount(ref countOptions);
            if (count == 0) throw new InvalidOperationException("No available lobby matches that code.");
            if (count != 1) throw new InvalidOperationException("The code matches multiple rooms. Ask the host to create a new room.");
            var copy = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            Check(search.CopySearchResultByIndex(ref copy, out details), "CopySearchResultByIndex");
            var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
            var owner = details.GetLobbyOwner(ref ownerOptions);
            if (owner is null || !owner.IsValid()) throw new InvalidOperationException("The room has no host.");
            if (owner.ToString() == LocalPeer)
                throw new InvalidOperationException("The room uses your Device ID. Join from another Windows user or PC.");
            var options = new JoinLobbyOptions { LobbyDetailsHandle = details, LocalUserId = _local };
            _lobby.JoinLobby(ref options, null, (ref JoinLobbyCallbackInfo info) =>
            {
                Result status = info.ResultCode;
                string? joined = info.LobbyId?.ToString();
                _completed.Enqueue(() =>
                {
                    details.Release();
                    try
                    {
                        Check(status, "JoinLobby");
                        _lobbyId = joined ?? throw new InvalidOperationException("EOS returned no joined lobby ID.");
                        HostPeer = owner.ToString();
                        RefreshMembers();
                        _busy = false;
                        finished.SetResult();
                    }
                    catch (Exception error) { Fail(finished, error); }
                });
            });
        }
        catch (Exception error)
        {
            details?.Release();
            Fail(finished, error);
        }
        finally { search.Release(); }
    }

    public Task LeaveAsync()
    {
        if (_lobbyId is null) return Task.CompletedTask;
        Begin();
        var finished = new TaskCompletionSource();
        string id = _lobbyId;
        void Finish(Result result)
        {
            _completed.Enqueue(() =>
            {
                ClearRoom();
                if (result is Result.Success or Result.NotFound)
                {
                    _busy = false;
                    finished.SetResult();
                }
                else Fail(finished, Error(result, "Leave lobby"));
            });
        }
        if (IsHost)
        {
            var options = new DestroyLobbyOptions { LobbyId = id, LocalUserId = _local };
            _lobby.DestroyLobby(ref options, null, (ref DestroyLobbyCallbackInfo info) => Finish(info.ResultCode));
        }
        else
        {
            var options = new LeaveLobbyOptions { LobbyId = id, LocalUserId = _local };
            _lobby.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo info) => Finish(info.ResultCode));
        }
        return finished.Task;
    }

    private void RequireNoLobby()
    {
        if (_local is null) throw new InvalidOperationException("Sign in to EOS before entering a lobby.");
        if (_lobbyId is not null) throw new InvalidOperationException("Leave the current room first.");
    }

    private void DestroyFailedHost()
    {
        if (_lobbyId is null) return;
        var destroy = new DestroyLobbyOptions { LobbyId = _lobbyId, LocalUserId = _local };
        _lobby.DestroyLobby(ref destroy, null, static (ref DestroyLobbyCallbackInfo _) => { });
        ClearRoom();
    }

    private void RefreshMembers()
    {
        if (_lobbyId is null || _local is null) return;
        var copy = new CopyLobbyDetailsHandleOptions { LocalUserId = _local, LobbyId = _lobbyId };
        if (_lobby.CopyLobbyDetailsHandle(ref copy, out var details) != Result.Success) return;
        _refreshed.Clear();
        try
        {
            var count = new LobbyDetailsGetMemberCountOptions();
            uint total = details.GetMemberCount(ref count);
            for (uint i = 0; i < total; i++)
            {
                var options = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                var user = details.GetMemberByIndex(ref options);
                if (user is null || !user.IsValid()) continue;
                string id = user.ToString();
                if (id != LocalPeer) _refreshed[id] = user;
            }
        }
        finally { details.Release(); }

        _removed.Clear();
        foreach (var (id, user) in _members)
        {
            if (_refreshed.ContainsKey(id)) continue;
            _removed.Add(id);
            Close(user);
        }
        foreach (string id in _removed) _members.Remove(id);
        foreach (var (id, user) in _refreshed)
            if (_members.TryAdd(id, user) && IsAllowed(id)) Accept(user);
        foreach (string id in _removed) PeerDisconnected?.Invoke(id);
    }

    private void ClearRoom()
    {
        _lobbyId = null;
        HostPeer = null;
        foreach (var user in _members.Values) Close(user);
        _members.Clear();
        _refreshed.Clear();
    }

    private void HandleRoomClosed()
    {
        _removed.Clear();
        _removed.AddRange(_members.Keys);
        ClearRoom();
        foreach (string id in _removed) PeerDisconnected?.Invoke(id);
    }
}
#endif
