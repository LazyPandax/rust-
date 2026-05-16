using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private readonly RustPlusToolsDataService _chatToolData = new();
    private static readonly HttpClient s_chatHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly DateTime _chatCommandStartedUtc = DateTime.UtcNow;
    private DateTime? _chatCommandServerConnectedUtc;
    private DateTime _lastChatCommandTime = DateTime.MinValue;
    private const int ChatCommandCooldownSeconds = 2;
    private const int ChatCommandMaxLength = 235;
    private bool _chatCommandsMuted;

    private readonly List<ChatCommandEvent> _chatEvents = new();
    private readonly Dictionary<ulong, DateTime> _teamOnlineSinceUtc = new();
    private readonly Dictionary<ulong, DateTime> _teamAliveSinceUtc = new();
    private readonly Dictionary<ulong, TeamMovementSnapshot> _teamMovement = new();
    private readonly object _teamCommandStateLock = new();
    private readonly Dictionary<int, ChatTimerInfo> _chatTimers = new();
    private readonly List<ChatNote> _chatNotes = new();
    private readonly List<ChatMapMarker> _chatMapMarkers = new();
    private int _nextChatTimerId = 1;
    private int _nextChatNoteId = 1;
    private int _nextChatMarkerId = 1;

    private sealed record ChatCommandEvent(DateTime Utc, string Kind, string Message);
    private sealed record ChatTimerInfo(int Id, DateTime DueUtc, string Text, CancellationTokenSource Cts);
    private sealed record ChatNote(int Id, DateTime CreatedUtc, string Text);
    private sealed record ChatMapMarker(int Id, DateTime CreatedUtc, string Name, ulong CreatedBy, double X, double Y);
    private sealed record TeamMovementSnapshot(double X, double Y, DateTime LastMovedUtc);
    private sealed record TeamCommandMember(ulong SteamId, string Name, bool IsLeader, bool IsOnline, bool IsDead, double? X, double? Y);

    private void BtnOpenChatCommands_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _vm.Selected?.SyncChatCommands();
        ChatCommandsOverlay.Visibility = System.Windows.Visibility.Visible;
    }

    private void BtnCloseChatCommands_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ChatCommandsOverlay.Visibility = System.Windows.Visibility.Collapsed;
        _vm.Save();
    }

    private async Task ProcessChatCommands(TeamChatMessage m)
    {
        var profile = _vm.Selected;
        if (profile == null || !profile.ChatCommandsEnabled) return;

        var raw = (m.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("!", StringComparison.Ordinal)) return;

        var (verb, args) = SplitCommand(raw[1..].Trim());
        var lowerVerb = verb.ToLowerInvariant();
        if (lowerVerb.Length == 0) return;
        var commandKey = ResolveChatCommandKey(profile, lowerVerb);

        if (commandKey != "unmute" && _chatCommandsMuted)
        {
            AppendLog($"[ChatCommand] Ignoring '{raw}' from {m.Author} (muted)");
            return;
        }

        if (commandKey is not ("mute" or "unmute") &&
            (DateTime.UtcNow - _lastChatCommandTime).TotalSeconds < ChatCommandCooldownSeconds)
        {
            AppendLog($"[ChatCommand] Ignoring '{raw}' from {m.Author} (cooldown active)");
            return;
        }

        _lastChatCommandTime = DateTime.UtcNow;

        if (_rust is not RustPlusClientReal real) return;

        async Task ReplyAsync(string message)
        {
            await SendTeamChatSafeAsync(CompactChat(message));
        }

        try
        {
            if (commandKey == "mute")
            {
                await SendTeamChatReliableAsync("Rust+ Desk chat bot muted. Use !unmute to enable replies again.");
                _chatCommandsMuted = true;
                AppendLog($"[ChatCommand] Muted by {m.Author}");
                return;
            }

            if (commandKey == "unmute")
            {
                _chatCommandsMuted = false;
                await ReplyAsync("Rust+ Desk chat bot unmuted.");
                AppendLog($"[ChatCommand] Unmuted by {m.Author}");
                return;
            }

            if (commandKey == "pop")
            {
                await ReplyAsync(BuildPopulationMessage());
                AppendLog($"[ChatCommand] Pop executed by {m.Author}");
                return;
            }

            if (commandKey == "time")
            {
                await ReplyAsync(BuildTimeMessage());
                AppendLog($"[ChatCommand] Time executed by {m.Author}");
                return;
            }

            if (commandKey == "leader")
            {
                await HandleLeaderCommandAsync(real, m, args, ReplyAsync);
                return;
            }

            if (commandKey == "deepsea")
            {
                await ReplyAsync(BuildDeepSeaStatus());
                AppendLog($"[ChatCommand] DeepSea executed by {m.Author}");
                return;
            }

            if (commandKey == "cargo")
            {
                await ReplyAsync(BuildCargoStatus(profile));
                AppendLog($"[ChatCommand] Cargo executed by {m.Author}");
                return;
            }

            if (commandKey == "oilrig")
            {
                await ReplyAsync($"{BuildOilRigStatus("Small Oil Rig")} | {BuildOilRigStatus("Large Oil Rig")}");
                AppendLog($"[ChatCommand] OilRig executed by {m.Author}");
                return;
            }

            switch (commandKey)
            {
                case "afk":
                    await ReplyAsync(BuildAfkStatus());
                    return;
                case "alive":
                    await ReplyAsync(BuildAliveStatus(args));
                    return;
                case "connection":
                case "connections":
                    await ReplyAsync(BuildRecentEvents("connection", args, "connection events"));
                    return;
                case "death":
                case "deaths":
                    await ReplyAsync(BuildRecentEvents("death", args, "death events"));
                    return;
                case "events":
                    await ReplyAsync(BuildEventsSummary(args));
                    return;
                case "heli":
                    await ReplyAsync(BuildDynamicEventStatus(8, "heli", "Patrol Helicopter"));
                    return;
                case "chinook":
                    await ReplyAsync(BuildDynamicEventStatus(4, "chinook", "Chinook 47"));
                    return;
                case "vendor":
                    await ReplyAsync(BuildDynamicEventStatus(6, "vendor", "Travelling Vendor"));
                    return;
                case "large":
                    await ReplyAsync(BuildOilRigStatus("Large Oil Rig"));
                    return;
                case "small":
                    await ReplyAsync(BuildOilRigStatus("Small Oil Rig"));
                    return;
                case "craft":
                case "recycle":
                case "research":
                case "decay":
                    await ReplyAsync(BuildRustLabsReply(commandKey, args));
                    return;
                case "market":
                    await HandleMarketCommandAsync(real, args, ReplyAsync);
                    return;
                case "marker":
                case "markers":
                    await ReplyAsync(HandleMarkerCommand(lowerVerb, args, m));
                    return;
                case "note":
                case "notes":
                    await ReplyAsync(HandleNoteCommand(lowerVerb, args));
                    return;
                case "offline":
                    await ReplyAsync(BuildTeamStatus(online: false));
                    return;
                case "online":
                    await ReplyAsync(BuildTeamStatus(online: true));
                    return;
                case "player":
                case "players":
                    await ReplyAsync(await BuildPlayersStatusAsync(args));
                    return;
                case "prox":
                    await ReplyAsync(BuildProximityStatus(m, args));
                    return;
                case "steamid":
                    await ReplyAsync(BuildSteamIdStatus(m, args));
                    return;
                case "team":
                    await ReplyAsync(BuildTeamList());
                    return;
                case "timer":
                case "timers":
                    await ReplyAsync(HandleTimerCommand(lowerVerb, args));
                    return;
                case "upkeep":
                    await ReplyAsync(BuildAllUpkeepStatus(profile));
                    return;
                case "uptime":
                    await ReplyAsync(BuildUptimeStatus());
                    return;
                case "wipe":
                    await ReplyAsync(BuildWipeStatus());
                    return;
                case "tts":
                    await ReplyAsync("Discord TTS needs a Discord bot bridge. Rust+ Desk does not have that bridge configured yet.");
                    return;
                case "send":
                    await ReplyAsync("Discord DM sending needs a Discord bot bridge. Rust+ Desk does not have that bridge configured yet.");
                    return;
                case "tr":
                    await ReplyAsync(await HandleTranslateToCommandAsync(args));
                    return;
                case "trf":
                    await ReplyAsync(await HandleTranslateFromToCommandAsync(args));
                    return;
            }

            foreach (var mapping in profile.SwitchCommandMappings)
            {
                if (MatchesCommand(lowerVerb, mapping.Command) && mapping.EntityId != 0)
                {
                    await ToggleCommandSwitch(real, mapping.EntityId, m.Author);
                    return;
                }
            }

            foreach (var mapping in profile.UpkeepCommandMappings)
            {
                if (MatchesCommand(lowerVerb, mapping.Command) && mapping.EntityId != 0)
                {
                    await ProcessUpkeepCommand(real, mapping.EntityId, m.Author);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ChatCommand] {raw}: {ex.Message}");
            await ReplyAsync("Command failed. Check the desktop log for details.");
        }
    }

    private static (string Verb, string Args) SplitCommand(string input)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0) return ("", "");

        var parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? (parts[0].Trim(), "")
            : (parts[0].Trim(), parts[1].Trim());
    }

    private static bool MatchesCommand(string verb, string? configured)
    {
        configured = (configured ?? "").Trim().TrimStart('!');
        return configured.Length > 0 && string.Equals(verb, configured, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveChatCommandKey(ServerProfile profile, string verb)
    {
        List<ChatCommandMapping> builtInMappings;
        if (Dispatcher.CheckAccess())
        {
            profile.EnsureBuiltInChatCommands();
            builtInMappings = profile.BuiltInCommandMappings.ToList();
        }
        else
        {
            builtInMappings = Dispatcher.Invoke(() =>
            {
                profile.EnsureBuiltInChatCommands();
                return profile.BuiltInCommandMappings.ToList();
            });
        }

        var custom = builtInMappings.FirstOrDefault(m => MatchesCommand(verb, m.Command));
        if (custom != null && !string.IsNullOrWhiteSpace(custom.Key))
            return custom.Key.ToLowerInvariant();

        if (MatchesCommand(verb, profile.CmdPop)) return "pop";
        if (MatchesCommand(verb, profile.CmdTime)) return "time";
        if (MatchesCommand(verb, profile.CmdPromote)) return "leader";
        if (MatchesCommand(verb, profile.CmdDeepSea)) return "deepsea";
        if (MatchesCommand(verb, profile.CmdCargo)) return "cargo";
        if (MatchesCommand(verb, profile.CmdOilRig)) return "oilrig";

        return verb switch
        {
            "promote" => "leader",
            "connection" or "connections" => "connections",
            "death" or "deaths" => "deaths",
            "marker" or "markers" => "marker",
            "note" or "notes" => "notes",
            "player" or "players" => "players",
            "timer" or "timers" => "timer",
            "oilrig" => "oilrig",
            _ => ServerProfile.BuiltInChatCommandDefinitions.Any(d =>
                string.Equals(d.DefaultCommand, verb, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Key, verb, StringComparison.OrdinalIgnoreCase))
                    ? ServerProfile.BuiltInChatCommandDefinitions.First(d =>
                        string.Equals(d.DefaultCommand, verb, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d.Key, verb, StringComparison.OrdinalIgnoreCase)).Key
                    : verb
        };
    }

    private static string CompactChat(string text)
    {
        var clean = Regex.Replace((text ?? "").Replace("\r", " ").Replace("\n", " | "), @"\s+", " ").Trim();
        return clean.Length <= ChatCommandMaxLength ? clean : clean[..(ChatCommandMaxLength - 3)] + "...";
    }

    private string BuildPopulationMessage()
    {
        var players = string.IsNullOrWhiteSpace(_vm.ServerPlayers) ? "-/-" : _vm.ServerPlayers;
        var queue = string.IsNullOrWhiteSpace(_vm.ServerQueue) || _vm.ServerQueue == "-" ? "0" : _vm.ServerQueue;
        return queue != "0"
            ? $"Players: {players}. Queue: {queue}."
            : $"Players: {players}.";
    }

    private string BuildTimeMessage()
    {
        var msg = $"Current in-game time: {_vm.ServerTime}.";
        if (!string.IsNullOrWhiteSpace(_vm.TimeUntilNextPhase))
            msg += $" ({_vm.TimeUntilNextPhase})";
        return msg;
    }

    private string BuildDeepSeaStatus()
    {
        if (_deepSeaActive)
        {
            if (_deepSeaSpawnTime.HasValue)
            {
                var elapsed = DateTime.UtcNow - _deepSeaSpawnTime.Value;
                return $"Deep Sea is active. Running for {FormatDurationShort(elapsed)}.";
            }

            return "Deep Sea is active, but spawn time is unknown because the app connected mid-event.";
        }

        if (_deepSeaDespawnTime.HasValue)
        {
            var ago = DateTime.UtcNow - _deepSeaDespawnTime.Value;
            return $"Deep Sea is not active. Ended {FormatDurationShort(ago)} ago this session.";
        }

        return "Deep Sea event status unknown. Enable shop polling to track it.";
    }

    private string BuildCargoStatus(ServerProfile profile)
    {
        var activeCargo = _cargoDockStates.Values.OrderByDescending(x => x.LastSeen).FirstOrDefault();
        if (activeCargo != null)
        {
            var grid = GetGridLabel(activeCargo.LastX, activeCargo.LastY);
            if (activeCargo.IsDocked && activeCargo.DockTime.HasValue)
            {
                int dockDuration = TrackingService.GetLearnedDockingDuration(profile.Host);
                if (dockDuration > 0 && !activeCargo.WasAlreadyDocked)
                {
                    var dockRemain = TimeSpan.FromMinutes(dockDuration) - (DateTime.UtcNow - activeCargo.DockTime.Value);
                    return dockRemain.TotalSeconds > 0
                        ? $"Cargo Ship docked at {activeCargo.HarborName ?? "harbor"} ({grid}). Departs in about {FormatDurationShort(dockRemain)}."
                        : $"Cargo Ship docked at {activeCargo.HarborName ?? "harbor"} ({grid}) and preparing to depart.";
                }

                return $"Cargo Ship docked at {activeCargo.HarborName ?? "harbor"} ({grid}); departure time unknown.";
            }

            if (activeCargo.SeenAtEdge)
            {
                int fullLife = TrackingService.GetLearnedCargoFullLife(profile.Host);
                if (fullLife > 0 && activeCargo.FirstSeen.HasValue)
                {
                    var remain = TimeSpan.FromMinutes(fullLife) - (DateTime.UtcNow - activeCargo.FirstSeen.Value);
                    return remain.TotalSeconds > 0
                        ? $"Cargo Ship active at {grid}. Leaves in about {FormatDurationShort(remain)}."
                        : $"Cargo Ship active at {grid} and preparing to leave soon.";
                }

                return $"Cargo Ship active at {grid}; route duration is not learned for this server yet.";
            }

            return $"Cargo Ship active at {grid}; connected mid-route so remaining time is unknown.";
        }

        var cargo = GetPersistentEvent(_lastMarkers ?? Array.Empty<RustPlusClientReal.DynMarker>(), 5);
        if (cargo.Id != 0)
            return $"Cargo Ship active at {GetGridLabel(cargo.X, cargo.Y)}.";

        if (_cargoLastDespawnUtc.HasValue)
        {
            var ago = DateTime.UtcNow - _cargoLastDespawnUtc.Value;
            return $"Cargo Ship is not active. Last seen {FormatDurationShort(ago)} ago this session.";
        }

        return "Cargo Ship is not active or has not been seen this session.";
    }

    private string BuildOilRigStatus(string rigName)
    {
        var timeLeft = _monumentWatcher.GetActiveEventTimeLeft(rigName);
        if (timeLeft.HasValue)
            return $"{rigName}: crate unlocks in {FormatDurationShort(timeLeft.Value)}.";

        var lastTriggered = _monumentWatcher.GetLastTriggered(rigName);
        if (lastTriggered.HasValue)
        {
            var ago = DateTime.UtcNow - lastTriggered.Value;
            return $"{rigName}: last triggered {FormatDurationShort(ago)} ago.";
        }

        return $"{rigName}: not triggered this session.";
    }

    private string BuildDynamicEventStatus(int type, string eventKind, string eventName)
    {
        var marker = GetPersistentEvent(_lastMarkers ?? Array.Empty<RustPlusClientReal.DynMarker>(), type);
        if (marker.Id != 0)
            return $"{eventName} active at {GetGridLabel(marker.X, marker.Y)}.";

        if (eventKind == "heli" && _heliLastEventUtc.HasValue)
        {
            var ago = DateTime.UtcNow - _heliLastEventUtc.Value;
            var suffix = _heliLastEventWasCrash ? "shot down" : "left the map";
            return $"{eventName} is not active. Last {suffix} {FormatDurationShort(ago)} ago this session.";
        }

        var last = SnapshotEvents()
            .Where(e => string.Equals(e.Kind, eventKind, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Utc)
            .FirstOrDefault();

        return last is not null
            ? $"{eventName} is not active. Last event {FormatDurationShort(DateTime.UtcNow - last.Utc)} ago: {last.Message}"
            : $"{eventName} is not active or has not been seen this session.";
    }

    private async Task HandleLeaderCommandAsync(
        RustPlusClientReal real,
        TeamChatMessage message,
        string args,
        Func<string, Task> replyAsync)
    {
        var snapshot = GetTeamCommandSnapshot();
        var target = FindTeamMember(snapshot, args, message.SteamId);
        if (target == null)
        {
            await replyAsync(string.IsNullOrWhiteSpace(args)
                ? "Could not match you to a team member for leadership."
                : $"No team member matched '{args}'.");
            return;
        }

        var ok = await real.PromoteToLeaderAsync(target.SteamId);
        await replyAsync(ok
            ? $"{target.Name} was promoted to team leader."
            : $"Could not promote {target.Name}. The current leader must be the paired Rust+ account.");
        AppendLog($"[ChatCommand] Leader executed by {message.Author} for {target.Name}");
    }

    private string BuildAfkStatus()
    {
        var now = DateTime.UtcNow;
        var snapshot = GetTeamCommandSnapshot();
        Dictionary<ulong, TeamMovementSnapshot> movement;
        lock (_teamCommandStateLock)
            movement = new Dictionary<ulong, TeamMovementSnapshot>(_teamMovement);

        var afk = snapshot
            .Where(m => m.IsOnline && !m.IsDead && movement.TryGetValue(m.SteamId, out var move) && now - move.LastMovedUtc >= TimeSpan.FromMinutes(5))
            .Select(m => $"{m.Name} ({FormatDurationShort(now - movement[m.SteamId].LastMovedUtc)})")
            .ToList();

        return afk.Count == 0
            ? "No AFK teammates detected. AFK means no XY movement for 5+ minutes."
            : "AFK teammates: " + string.Join(", ", afk);
    }

    private string BuildAliveStatus(string args)
    {
        var now = DateTime.UtcNow;
        var snapshot = GetTeamCommandSnapshot();
        Dictionary<ulong, DateTime> aliveSince;
        lock (_teamCommandStateLock)
            aliveSince = new Dictionary<ulong, DateTime>(_teamAliveSinceUtc);

        if (!string.IsNullOrWhiteSpace(args))
        {
            var target = FindTeamMember(snapshot, args, 0);
            if (target == null) return $"No team member matched '{args}'.";
            if (!target.IsOnline) return $"{target.Name} is offline.";
            if (target.IsDead) return $"{target.Name} is dead.";

            var since = aliveSince.TryGetValue(target.SteamId, out var started) ? started : now;
            return $"{target.Name} has been alive for {FormatDurationShort(now - since)}.";
        }

        var best = snapshot
            .Where(m => m.IsOnline && !m.IsDead && aliveSince.ContainsKey(m.SteamId))
            .OrderBy(m => aliveSince[m.SteamId])
            .FirstOrDefault();

        if (best == null) return "No alive teammate timer is available yet.";

        return $"{best.Name} has the longest alive time: {FormatDurationShort(now - aliveSince[best.SteamId])}.";
    }

    private string BuildRecentEvents(string kind, string args, string label)
    {
        var (filter, count) = ParseEventArgs(args, defaultKind: "");
        var events = SnapshotEvents()
            .Where(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Where(e => string.IsNullOrWhiteSpace(filter) || e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Utc)
            .Take(count)
            .ToList();

        return FormatEventList(events, $"No recent {label} recorded this session.");
    }

    private string BuildEventsSummary(string args)
    {
        var (kind, count) = ParseEventArgs(args, defaultKind: "");
        var events = SnapshotEvents()
            .Where(e => string.IsNullOrWhiteSpace(kind) || string.Equals(e.Kind, NormalizeEventKind(kind), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Utc)
            .Take(count)
            .ToList();

        return FormatEventList(events, "No recent events recorded this session.");
    }

    private static (string FilterOrKind, int Count) ParseEventArgs(string args, string defaultKind)
    {
        var count = 5;
        var filter = defaultKind;
        var parts = Regex.Split((args ?? "").Trim(), @"\s+").Where(x => x.Length > 0).ToList();
        if (parts.Count == 0) return (filter, count);

        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstCount))
        {
            count = Math.Clamp(firstCount, 1, 10);
            return (filter, count);
        }

        filter = parts[0];
        if (filter.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            filter.Equals("alle", StringComparison.OrdinalIgnoreCase))
            filter = "";

        if (parts.Count > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var secondCount))
            count = Math.Clamp(secondCount, 1, 10);

        return (filter, count);
    }

    private string FormatEventList(List<ChatCommandEvent> events, string emptyMessage)
    {
        if (events.Count == 0) return emptyMessage;

        var now = DateTime.UtcNow;
        return string.Join(" | ", events.Select(e =>
            $"{FormatDurationShort(now - e.Utc)} ago: {e.Message}"));
    }

    private string BuildRustLabsReply(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return command switch
            {
                "craft" => "Usage: !craft <item> [quantity]",
                "recycle" => "Usage: !recycle <item> [quantity]",
                "research" => "Usage: !research <item>",
                "decay" => "Usage: !decay <item/building/entity>",
                _ => "Usage: provide an item name."
            };

        var (query, quantity) = ExtractTrailingQuantity(args);
        if (string.IsNullOrWhiteSpace(query)) query = args.Trim();

        try
        {
            var entry = _chatToolData.Search(query, 1).FirstOrDefault();
            if (entry == null) return $"No RustLabs item matched '{query}'.";

            var formatted = command switch
            {
                "craft" => _chatToolData.FormatCraft(entry, quantity),
                "recycle" => _chatToolData.FormatRecycle(entry, quantity, "recycler"),
                "research" => _chatToolData.FormatResearch(entry),
                "decay" => _chatToolData.FormatDecay(entry),
                _ => ""
            };

            return ToolFormatToChat(formatted);
        }
        catch (Exception ex)
        {
            return $"RustLabs data is unavailable: {ex.Message}";
        }
    }

    private static (string Query, int Quantity) ExtractTrailingQuantity(string args)
    {
        var parts = Regex.Split((args ?? "").Trim(), @"\s+").Where(x => x.Length > 0).ToList();
        if (parts.Count > 1 &&
            int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) &&
            qty > 0)
        {
            return (string.Join(" ", parts.Take(parts.Count - 1)), Math.Min(qty, 10000));
        }

        return ((args ?? "").Trim(), 1);
    }

    private static string ToolFormatToChat(string formatted)
    {
        var lines = (formatted ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('-', ' '))
            .Where(x => x.Length > 0);
        return string.Join("; ", lines);
    }

    private async Task HandleMarketCommandAsync(RustPlusClientReal real, string args, Func<string, Task> replyAsync)
    {
        var (sub, rest) = SplitCommand(args);
        var action = sub.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(action))
        {
            await replyAsync("Usage: !market search|sub|unsub <sell|buy|all> <item>, or !market list");
            return;
        }

        if (action == "list")
        {
            if (_alertRules.Count == 0)
            {
                await replyAsync("No market subscriptions.");
                return;
            }

            var list = _alertRules
                .Take(8)
                .Select((r, i) => $"{i + 1}. {r.QueryText} [{MarketModeLabel(r.MatchSellSide, r.MatchBuySide)}]")
                .ToList();
            await replyAsync("Market subscriptions: " + string.Join("; ", list));
            return;
        }

        if (action is not ("search" or "sub" or "unsub"))
        {
            rest = args;
            action = "search";
        }

        var (matchSell, matchBuy, modeLabel, query) = ParseMarketSide(rest);
        if (string.IsNullOrWhiteSpace(query))
        {
            await replyAsync("Usage: !market search sell Thompson, !market sub all Scrap, !market unsub sell m249");
            return;
        }

        if (action == "search")
        {
            var shops = await EnsureMarketSnapshotAsync(real);
            if (shops.Count == 0)
            {
                await replyAsync("No vending machine data loaded yet. Enable shop polling or try again after the map loads.");
                return;
            }

            var matches = FindMarketMatches(shops, query, matchSell, matchBuy).Take(5).ToList();
            if (matches.Count == 0)
            {
                await replyAsync($"No vending offers matched '{query}' on {modeLabel} side.");
                return;
            }

            await replyAsync($"Market {modeLabel} '{query}': " + string.Join(" | ", matches));
            return;
        }

        if (action == "sub")
        {
            var exists = _alertRules.Any(r =>
                r.QueryText.Equals(query, StringComparison.OrdinalIgnoreCase) &&
                r.MatchSellSide == matchSell &&
                r.MatchBuySide == matchBuy);

            if (exists)
            {
                await replyAsync($"Already subscribed to {query} [{modeLabel}].");
                return;
            }

            var rule = new ShopAlertRule
            {
                QueryText = query,
                MatchSellSide = matchSell,
                MatchBuySide = matchBuy,
                NotifyChat = true,
                NotifySound = false,
                IsSaved = true
            };

            foreach (var shop in _lastShops)
            {
                if (shop.Orders == null) continue;
                foreach (var order in shop.Orders)
                {
                    if (!((matchSell && MatchOrderLeft(order, query)) || (matchBuy && MatchOrderRight(order, query)))) continue;
                    rule.Baseline.Add(new AlertSeenOrder
                    {
                        ShopId = shop.Id,
                        ItemShort = order.ItemShortName,
                        CurrencyShort = order.CurrencyShortName,
                        Stock = order.Stock,
                        Quantity = order.Quantity,
                        CurrencyAmount = order.CurrencyAmount
                    });
                }
            }

            _alertRules.Add(rule);
            await RefreshMarketAlertUiAsync();
            await replyAsync($"Subscribed to market alerts for {query} [{modeLabel}].");
            return;
        }

        var removed = _alertRules.RemoveAll(r =>
            r.QueryText.Equals(query, StringComparison.OrdinalIgnoreCase) &&
            r.MatchSellSide == matchSell &&
            r.MatchBuySide == matchBuy);

        await RefreshMarketAlertUiAsync();
        await replyAsync(removed > 0
            ? $"Removed {removed} market subscription(s) for {query} [{modeLabel}]."
            : $"No market subscription matched {query} [{modeLabel}].");
    }

    private async Task<List<RustPlusClientReal.ShopMarker>> EnsureMarketSnapshotAsync(RustPlusClientReal real)
    {
        if (_lastShops.Count > 0) return _lastShops;

        try
        {
            var shops = await real.GetVendingShopsAsync();
            if (shops is { Count: > 0 }) _lastShops = shops;
        }
        catch (Exception ex)
        {
            AppendLog("[ChatCommand] market fetch failed: " + ex.Message);
        }

        return _lastShops;
    }

    private IEnumerable<string> FindMarketMatches(IEnumerable<RustPlusClientReal.ShopMarker> shops, string query, bool matchSell, bool matchBuy)
    {
        foreach (var shop in shops)
        {
            if (shop.Orders == null) continue;
            foreach (var order in shop.Orders)
            {
                if (!((matchSell && MatchOrderLeft(order, query)) || (matchBuy && MatchOrderRight(order, query)))) continue;

                var itemName = ResolveItemName(order.ItemId, order.ItemShortName);
                var currencyName = ResolveItemName(order.CurrencyItemId, order.CurrencyShortName);
                var stock = order.Stock > 0 ? $"stock {order.Stock}" : "out of stock";
                yield return $"{itemName} x{order.Quantity} for {order.CurrencyAmount} {currencyName} [{GetGridLabel(shop)}], {stock}";
            }
        }
    }

    private static (bool Sell, bool Buy, string Label, string Query) ParseMarketSide(string input)
    {
        var (first, rest) = SplitCommand(input);
        var side = first.ToLowerInvariant();
        return side switch
        {
            "sell" => (true, false, "sell", rest),
            "buy" => (false, true, "buy", rest),
            "all" => (true, true, "all", rest),
            _ => (true, true, "all", input.Trim())
        };
    }

    private static string MarketModeLabel(bool sell, bool buy)
        => sell && buy ? "all" : sell ? "sell" : buy ? "buy" : "none";

    private Task RefreshMarketAlertUiAsync()
    {
        void Refresh()
        {
            SavePersistentAlerts();
            RefreshAlertListUI();
            UpdateMasterToggleState();
            SyncAlertMenuItems();
        }

        if (Dispatcher.CheckAccess())
        {
            Refresh();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(Refresh).Task;
    }

    private string HandleMarkerCommand(string verb, string args, TeamChatMessage message)
    {
        var (sub, rest) = SplitCommand(args);
        var action = sub.ToLowerInvariant();

        if (verb == "markers" || string.IsNullOrWhiteSpace(action) || action == "list")
            return FormatMarkerList();

        if (action == "add")
        {
            var name = string.IsNullOrWhiteSpace(rest) ? $"Marker {_nextChatMarkerId}" : rest.Trim();
            if (!TryGetMemberPosition(message.SteamId, GetTeamCommandSnapshot(), out var x, out var y))
                return "Could not add marker: your current team position is unknown.";

            var marker = new ChatMapMarker(_nextChatMarkerId++, DateTime.UtcNow, name, message.SteamId, x, y);
            _chatMapMarkers.Add(marker);
            RecordChatCommandEvent("marker", $"{message.Author} added marker {marker.Id}: {marker.Name} @ {GetGridLabel(x, y)}");
            return $"Marker #{marker.Id} added: {marker.Name} @ {GetGridLabel(x, y)}.";
        }

        if (action is "remove" or "delete" or "del")
        {
            if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return "Usage: !marker remove <id>";

            var marker = _chatMapMarkers.FirstOrDefault(x => x.Id == id);
            if (marker == null) return $"Marker #{id} was not found.";
            _chatMapMarkers.Remove(marker);
            return $"Marker #{id} removed.";
        }

        var query = args.Trim();
        var found = int.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var markerId)
            ? _chatMapMarkers.FirstOrDefault(x => x.Id == markerId)
            : _chatMapMarkers.FirstOrDefault(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        return found == null
            ? $"No marker matched '{query}'."
            : $"Marker #{found.Id}: {found.Name} @ {GetGridLabel(found.X, found.Y)}.";
    }

    private string FormatMarkerList()
    {
        if (_chatMapMarkers.Count == 0)
            return "No custom markers. Use !marker add <name> while standing at the spot.";

        return "Markers: " + string.Join("; ", _chatMapMarkers.Take(8).Select(m =>
            $"#{m.Id} {m.Name} [{GetGridLabel(m.X, m.Y)}]"));
    }

    private string HandleNoteCommand(string verb, string args)
    {
        var (sub, rest) = SplitCommand(args);
        var action = sub.ToLowerInvariant();

        if (verb == "notes" || string.IsNullOrWhiteSpace(action) || action == "list")
            return FormatNoteList();

        if (action == "add")
        {
            if (string.IsNullOrWhiteSpace(rest)) return "Usage: !note add <text>";
            var note = new ChatNote(_nextChatNoteId++, DateTime.UtcNow, rest.Trim());
            _chatNotes.Add(note);
            return $"Note #{note.Id} added.";
        }

        if (action is "remove" or "delete" or "del")
        {
            if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return "Usage: !note remove <id>";

            var note = _chatNotes.FirstOrDefault(x => x.Id == id);
            if (note == null) return $"Note #{id} was not found.";
            _chatNotes.Remove(note);
            return $"Note #{id} removed.";
        }

        return "Usage: !note add <text>, !note remove <id>, or !notes";
    }

    private string FormatNoteList()
    {
        if (_chatNotes.Count == 0) return "No notes. Use !note add <text>.";
        return "Notes: " + string.Join("; ", _chatNotes.Take(8).Select(n => $"#{n.Id} {n.Text}"));
    }

    private string BuildTeamStatus(bool online)
    {
        var members = GetTeamCommandSnapshot()
            .Where(m => m.IsOnline == online)
            .OrderBy(m => m.Name)
            .ToList();

        return members.Count == 0
            ? (online ? "No teammates are online." : "No teammates are offline.")
            : $"{(online ? "Online" : "Offline")} teammates ({members.Count}): {FormatMemberNames(members)}";
    }

    private string BuildTeamList()
    {
        var members = GetTeamCommandSnapshot().OrderByDescending(m => m.IsLeader).ThenBy(m => m.Name).ToList();
        return members.Count == 0
            ? "No team members loaded yet."
            : $"Team ({members.Count}): {FormatMemberNames(members)}";
    }

    private static string FormatMemberNames(IEnumerable<TeamCommandMember> members)
        => string.Join(", ", members.Select(m => m.IsLeader ? $"{m.Name} (leader)" : m.Name));

    private string BuildSteamIdStatus(TeamChatMessage message, string args)
    {
        var snapshot = GetTeamCommandSnapshot();
        var target = FindTeamMember(snapshot, args, message.SteamId);
        if (target == null)
            return string.IsNullOrWhiteSpace(args) ? "Usage: !steamid <teammate>" : $"No team member matched '{args}'.";

        return $"{target.Name}: {target.SteamId}";
    }

    private string BuildProximityStatus(TeamChatMessage message, string args)
    {
        var snapshot = GetTeamCommandSnapshot();
        var caller = FindTeamMember(snapshot, "", message.SteamId);
        if (caller == null || !TryGetMemberPosition(caller.SteamId, snapshot, out var x, out var y))
            return "Could not calculate proximity: your position is unknown.";

        if (!string.IsNullOrWhiteSpace(args))
        {
            var target = FindTeamMember(snapshot, args, 0);
            if (target == null) return $"No team member matched '{args}'.";
            if (!TryGetMemberPosition(target.SteamId, snapshot, out var tx, out var ty))
                return $"{target.Name}'s position is unknown.";

            return $"{target.Name} is about {Math.Round(Distance(x, y, tx, ty))}m away.";
        }

        var closest = snapshot
            .Where(m => m.SteamId != caller.SteamId && m.IsOnline && !m.IsDead)
            .Select(m => TryGetMemberPosition(m.SteamId, snapshot, out var tx, out var ty)
                ? new { Member = m, Distance = Distance(x, y, tx, ty) }
                : null)
            .Where(x => x != null)
            .OrderBy(x => x!.Distance)
            .Take(3)
            .Select(x => $"{x!.Member.Name} {Math.Round(x.Distance)}m")
            .ToList();

        return closest.Count == 0
            ? "No nearby online teammates with known positions."
            : "Closest teammates: " + string.Join(", ", closest);
    }

    private async Task<string> BuildPlayersStatusAsync(string args)
    {
        try
        {
            if (!TrackingService.LastPullTime.HasValue ||
                DateTime.Now - TrackingService.LastPullTime.Value > TimeSpan.FromMinutes(2))
            {
                await TrackingService.FetchOnlinePlayersNowAsync();
            }
        }
        catch (Exception ex)
        {
            AppendLog("[ChatCommand] BattleMetrics fetch failed: " + ex.Message);
        }

        var players = TrackingService.LastOnlinePlayers;
        if (!string.IsNullOrWhiteSpace(args))
            players = players.Where(p => p.Name.Contains(args, StringComparison.OrdinalIgnoreCase)).ToList();

        if (players.Count == 0)
            return string.IsNullOrWhiteSpace(args)
                ? "No BattleMetrics online player data available."
                : $"No online BattleMetrics player matched '{args}'.";

        var shown = players
            .OrderByDescending(p => p.Duration)
            .Take(8)
            .Select(p => $"{p.Name} {p.PlayTimeStr}")
            .ToList();

        var suffix = players.Count > shown.Count ? $" (+{players.Count - shown.Count} more)" : "";
        return $"Online players ({players.Count}): {string.Join(", ", shown)}{suffix}";
    }

    private static readonly Dictionary<string, string> s_languageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arabic"] = "ar",
        ["chinese"] = "zh-CN",
        ["dutch"] = "nl",
        ["english"] = "en",
        ["french"] = "fr",
        ["german"] = "de",
        ["hebrew"] = "he",
        ["italian"] = "it",
        ["japanese"] = "ja",
        ["korean"] = "ko",
        ["polish"] = "pl",
        ["portuguese"] = "pt",
        ["russian"] = "ru",
        ["spanish"] = "es",
        ["swedish"] = "sv",
        ["turkish"] = "tr",
        ["ukrainian"] = "uk"
    };

    private async Task<string> HandleTranslateToCommandAsync(string args)
    {
        var (language, text) = SplitCommand(args);
        if (string.Equals(language, "language", StringComparison.OrdinalIgnoreCase))
        {
            var code = ResolveLanguageCode(text);
            return string.IsNullOrWhiteSpace(code)
                ? "Usage: !tr language <language>. Example: !tr language german"
                : $"{text}: {code}";
        }

        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(text))
            return "Usage: !tr <language-code> <text>. Example: !tr en hello";

        return await TranslateTextAsync("auto", language, text);
    }

    private async Task<string> HandleTranslateFromToCommandAsync(string args)
    {
        var (from, rest) = SplitCommand(args);
        var (to, text) = SplitCommand(rest);

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(text))
            return "Usage: !trf <from-language> <to-language> <text>. Example: !trf ru en privet";

        return await TranslateTextAsync(from, to, text);
    }

    private static string ResolveLanguageCode(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) return "";
        return s_languageCodes.TryGetValue(value, out var code) ? code : value;
    }

    private static async Task<string> TranslateTextAsync(string sourceLanguage, string targetLanguage, string text)
    {
        sourceLanguage = string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : ResolveLanguageCode(sourceLanguage);
        targetLanguage = ResolveLanguageCode(targetLanguage);

        if (string.IsNullOrWhiteSpace(targetLanguage) || string.IsNullOrWhiteSpace(text))
            return "Missing language or text.";

        try
        {
            var url =
                "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t" +
                $"&sl={Uri.EscapeDataString(sourceLanguage)}" +
                $"&tl={Uri.EscapeDataString(targetLanguage)}" +
                $"&q={Uri.EscapeDataString(text)}";

            using var response = await s_chatHttp.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return "Translation failed: empty response.";

            var translated = new StringBuilder();
            foreach (var segment in doc.RootElement[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0)
                    translated.Append(segment[0].GetString());
            }

            return translated.Length == 0 ? "Translation failed: no text returned." : translated.ToString();
        }
        catch (Exception ex)
        {
            return $"Translation failed: {ex.Message}";
        }
    }

    private string HandleTimerCommand(string verb, string args)
    {
        var (sub, rest) = SplitCommand(args);
        var action = sub.ToLowerInvariant();

        if (verb == "timers" || string.IsNullOrWhiteSpace(action) || action == "list")
            return FormatTimerList();

        if (action is "remove" or "delete" or "del")
        {
            if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var removeId))
                return "Usage: !timer remove <id>";

            lock (_chatTimers)
            {
                if (!_chatTimers.TryGetValue(removeId, out var timer))
                    return $"Timer #{removeId} was not found.";

                timer.Cts.Cancel();
                timer.Cts.Dispose();
                _chatTimers.Remove(removeId);
            }

            return $"Timer #{removeId} removed.";
        }

        var timerInput = action == "add" ? rest : args;
        var (durationToken, timerText) = SplitCommand(timerInput);
        if (!TryParseTimerDuration(durationToken, out var duration))
            return "Usage: !timer add 10m <text>. Supported units: d, h, m, s.";

        timerText = string.IsNullOrWhiteSpace(timerText) ? "Timer expired" : timerText.Trim();
        var id = _nextChatTimerId++;
        var due = DateTime.UtcNow + duration;
        var cts = new CancellationTokenSource();

        lock (_chatTimers)
        {
            _chatTimers[id] = new ChatTimerInfo(id, due, timerText, cts);
        }

        _ = RunChatTimerAsync(id, duration, timerText, cts);
        return $"Timer #{id} set for {FormatDurationShort(duration)}: {timerText}";
    }

    private async Task RunChatTimerAsync(int id, TimeSpan duration, string text, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(duration, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        lock (_chatTimers)
        {
            _chatTimers.Remove(id);
        }

        RecordChatCommandEvent("timer", $"Timer #{id} expired: {text}");
        if (!_chatCommandsMuted)
            await SendTeamChatSafeAsync(CompactChat($"Timer #{id} done: {text}"));

        cts.Dispose();
    }

    private string FormatTimerList()
    {
        List<ChatTimerInfo> timers;
        lock (_chatTimers)
            timers = _chatTimers.Values.OrderBy(t => t.DueUtc).ToList();

        if (timers.Count == 0) return "No active timers. Use !timer add 10m <text>.";

        var now = DateTime.UtcNow;
        return "Timers: " + string.Join("; ", timers.Take(8).Select(t =>
            $"#{t.Id} {FormatDurationShort(t.DueUtc - now)} {t.Text}"));
    }

    private static bool TryParseTimerDuration(string token, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        token = (token ?? "").Trim().ToLowerInvariant();
        if (token.Length == 0) return false;

        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutesOnly) && minutesOnly > 0)
        {
            duration = TimeSpan.FromMinutes(minutesOnly);
            return true;
        }

        var matches = Regex.Matches(token, @"(\d+)([dhms])", RegexOptions.IgnoreCase);
        if (matches.Count == 0 || string.Concat(matches.Cast<Match>().Select(m => m.Value)) != token) return false;

        double seconds = 0;
        foreach (Match match in matches)
        {
            var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            seconds += match.Groups[2].Value.ToLowerInvariant() switch
            {
                "d" => value * 86400,
                "h" => value * 3600,
                "m" => value * 60,
                "s" => value,
                _ => 0
            };
        }

        if (seconds <= 0) return false;
        duration = TimeSpan.FromSeconds(Math.Min(seconds, 31_536_000));
        return true;
    }

    private string BuildAllUpkeepStatus(ServerProfile profile)
    {
        var tcs = profile.AllDevices
            .Where(d => (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor") && d.Storage?.IsToolCupboard == true)
            .ToList();

        if (tcs.Count == 0) return "No connected Tool Cupboard storage monitors found.";

        return "TC upkeep: " + string.Join("; ", tcs.Take(6).Select(d =>
        {
            var secs = d.UpkeepSeconds ?? 0;
            var time = secs <= 0 ? "empty or expired" : FormatDurationShort(TimeSpan.FromSeconds(secs));
            return $"{d.PureName}: {time}";
        }));
    }

    private string BuildUptimeStatus()
    {
        var app = FormatDurationShort(DateTime.UtcNow - _chatCommandStartedUtc);
        var server = _chatCommandServerConnectedUtc.HasValue
            ? FormatDurationShort(DateTime.UtcNow - _chatCommandServerConnectedUtc.Value)
            : (_vm.Selected?.IsConnected == true ? "connected, start time unknown" : "not connected");

        return $"Rust+ Desk uptime: {app}. Server session: {server}.";
    }

    private string BuildWipeStatus()
    {
        var wipe = (_vm.ServerWipe ?? "").Trim();
        return string.IsNullOrWhiteSpace(wipe) || wipe == "-" || wipe == "\u2013"
            ? "Wipe time is not available from the current server data."
            : $"Server wipe: {wipe}.";
    }

    private List<TeamCommandMember> GetTeamCommandSnapshot()
    {
        List<TeamCommandMember> Snapshot() => TeamMembers
            .Select(m => new TeamCommandMember(m.SteamId, m.Name, m.IsLeader, m.IsOnline, m.IsDead, m.X, m.Y))
            .ToList();

        return Dispatcher.CheckAccess() ? Snapshot() : Dispatcher.Invoke(Snapshot);
    }

    private TeamCommandMember? FindTeamMember(List<TeamCommandMember> members, string query, ulong preferredSteamId)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0 && preferredSteamId != 0)
            return members.FirstOrDefault(m => m.SteamId == preferredSteamId);

        if (ulong.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid))
            return members.FirstOrDefault(m => m.SteamId == sid);

        return members.FirstOrDefault(m => m.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? members.FirstOrDefault(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetMemberPosition(ulong steamId, List<TeamCommandMember> members, out double x, out double y)
    {
        var member = members.FirstOrDefault(m => m.SteamId == steamId);
        if (member is { X: { } mx, Y: { } my })
        {
            x = mx;
            y = my;
            return true;
        }

        if (TryResolvePosFromDynMarkers(steamId, out x, out y))
            return true;

        x = y = 0;
        return false;
    }

    private static double Distance(double ax, double ay, double bx, double by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void TrackTeamCommandSnapshot(TeamMemberVM vm, bool hadPrev, (bool online, bool dead) prev)
    {
        var now = DateTime.UtcNow;

        lock (_teamCommandStateLock)
        {
            if (vm.IsOnline)
            {
                if (!hadPrev || !prev.online || !_teamOnlineSinceUtc.ContainsKey(vm.SteamId))
                    _teamOnlineSinceUtc[vm.SteamId] = now;
            }
            else
            {
                _teamOnlineSinceUtc.Remove(vm.SteamId);
            }

            if (vm.IsOnline && !vm.IsDead)
            {
                if (!hadPrev || !prev.online || prev.dead || !_teamAliveSinceUtc.ContainsKey(vm.SteamId))
                    _teamAliveSinceUtc[vm.SteamId] = now;

                if (vm.X.HasValue && vm.Y.HasValue)
                {
                    if (_teamMovement.TryGetValue(vm.SteamId, out var old))
                    {
                        if (Distance(old.X, old.Y, vm.X.Value, vm.Y.Value) > 1.0)
                            _teamMovement[vm.SteamId] = new TeamMovementSnapshot(vm.X.Value, vm.Y.Value, now);
                    }
                    else
                    {
                        _teamMovement[vm.SteamId] = new TeamMovementSnapshot(vm.X.Value, vm.Y.Value, now);
                    }
                }
            }
            else
            {
                _teamAliveSinceUtc.Remove(vm.SteamId);
                if (!vm.IsOnline)
                    _teamMovement.Remove(vm.SteamId);
            }
        }
    }

    private void RecordChatCommandEvent(string kind, string message)
    {
        kind = NormalizeEventKind(kind);
        message = CompactChat(message);
        if (kind.Length == 0 || message.Length == 0) return;

        lock (_chatEvents)
        {
            _chatEvents.Add(new ChatCommandEvent(DateTime.UtcNow, kind, message));
            if (_chatEvents.Count > 100)
                _chatEvents.RemoveRange(0, _chatEvents.Count - 100);
        }
    }

    private List<ChatCommandEvent> SnapshotEvents()
    {
        lock (_chatEvents)
            return _chatEvents.ToList();
    }

    private static string NormalizeEventKind(string kind)
    {
        kind = (kind ?? "").Trim().ToLowerInvariant();
        return kind switch
        {
            "patrol" or "patrolheli" or "patrol_helicopter" or "patrol helicopter" or "helicopter" => "heli",
            "ch47" or "chinook47" => "chinook",
            "cargo ship" or "cargoship" => "cargo",
            "connections" => "connection",
            "deaths" => "death",
            "largeoil" or "largeoilrig" => "large",
            "smalloil" or "smalloilrig" => "small",
            "travellingvendor" or "travelingvendor" or "travelling vendor" or "traveling vendor" => "vendor",
            _ => kind
        };
    }

    private void MarkChatCommandServerConnected()
    {
        _chatCommandServerConnectedUtc = DateTime.UtcNow;
    }

    private static string FormatDurationShort(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{Math.Max(0, ts.Seconds)}s";
    }

    private async Task ProcessUpkeepCommand(RustPlusClientReal real, uint entityId, string author)
    {
        var profile = _vm.Selected;
        if (profile == null) return;

        var dev = profile.AllDevices.FirstOrDefault(d =>
            d.EntityId == entityId &&
            (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));

        if (dev != null && dev.Storage?.IsToolCupboard == true)
        {
            var secs = dev.UpkeepSeconds ?? 0;
            var msg = secs <= 0
                ? $"Upkeep in {dev.PureName} TC: empty or expired."
                : $"Upkeep in {dev.PureName} TC: {FormatDurationShort(TimeSpan.FromSeconds(secs))}.";
            await SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Upkeep for {dev.Name} executed by {author}");
        }
        else
        {
            await SendTeamChatSafeAsync("Bound Tool Cupboard monitor not found or not paired.");
        }
    }

    private async Task ToggleCommandSwitch(RustPlusClientReal real, uint entityId, string author)
    {
        var profile = _vm.Selected;
        if (profile == null) return;

        var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == entityId && d.Kind == "SmartSwitch");
        if (dev != null)
        {
            bool newState = !(dev.IsOn ?? false);
            try
            {
                await real.ToggleSmartSwitchAsync(entityId, newState);
                await SendTeamChatSafeAsync($"{dev.Name} turned {(newState ? "ON" : "OFF")}.");
                AppendLog($"[ChatCommand] {dev.Name} toggled to {newState} by {author}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ChatCommand] Failed to toggle {dev.Name}: {ex.Message}");
            }
        }
        else
        {
            await SendTeamChatSafeAsync("Bound Smart Switch not found or not paired.");
        }
    }
}
