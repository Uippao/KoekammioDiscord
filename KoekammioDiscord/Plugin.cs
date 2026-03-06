using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Loader.Features.Plugins;
using Discord;
using Discord.WebSocket;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;

namespace KoekammioDiscord
{
    public class KoekammioDiscord : Plugin<Config>
    {
        public override string Name => "KoekammioDiscord";
        public override string Description => "A simple but customizable server status indicator Discord bot.";
        public override string Author => "Uippao";
        public override Version Version => new Version(1, 2, 0, 0);
        public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);

        private DiscordSocketClient _client;
        private CancellationTokenSource _cts;
        private Task _statusLoopTask;

        private int _lastPlayerCount = -1;
        private string _lastText = null;
        private UserStatus _lastStatus = UserStatus.Idle;

        private volatile bool _forceUpdate = false;
        private volatile bool _needInitialRoundDelay = false;

        private int _pollIntervalMs;
        private int _waitingForPlayersDelayMs;

        public override void Enable()
        {
            LoadConfigs();

            if (!Config.isEnabled)
            {
                Logger.Info("KoekammioDiscord is disabled in config.");
                return;
            }
            _pollIntervalMs = Config.timing.pollIntervalSeconds * 1000;
            _waitingForPlayersDelayMs = Config.timing.waitingForPlayersDelaySeconds * 1000;

            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
            ServerEvents.RoundEnded += OnRoundEnded;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
            });

            _client.Ready += OnDiscordReady;

            Task.Run(async () =>
            {
                await _client.LoginAsync(TokenType.Bot, Config.discord.token);
                await _client.StartAsync();
            });

            Logger.Info("KoekammioDiscord enabled.");
        }

        public override void Disable()
        {
            if (!Config.isEnabled)
                return;

            ServerEvents.RoundStarted -= OnRoundStarted;
            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
            ServerEvents.RoundEnded -= OnRoundEnded;

            try
            {
                _cts?.Cancel();
                _statusLoopTask?.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error while stopping status loop: {ex}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _statusLoopTask = null;
            }

            if (_client != null)
            {
                try
                {
                    _client.SetActivityAsync((IActivity)null).GetAwaiter().GetResult();
                    _client.SetStatusAsync(UserStatus.Online).GetAwaiter().GetResult();

                    _client.StopAsync().GetAwaiter().GetResult();
                    _client.LogoutAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to properly clear Discord status: {ex}");
                }
            }

            Logger.Info("KoekammioDiscord disabled.");
        }

        private async Task OnDiscordReady()
        {
            try
            {
                Logger.Info($"Discord bot connected as {_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}");

                _lastPlayerCount = -1;
                _lastText = null;

                _cts = new CancellationTokenSource();
                _statusLoopTask = Task.Run(() => StatusLoopAsync(_cts.Token), _cts.Token);

                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Logger.Error($"OnDiscordReady error: {ex}");
            }
        }

        private void OnRoundStarted()
        {
            Debug("Round started (event). Will apply initial stabilization delay before measuring.");
            _needInitialRoundDelay = true;
            _forceUpdate = true;
        }

        private void OnWaitingForPlayers()
        {
            Debug("Entered waiting for players (event). Forcing an update on next poll.");
            _forceUpdate = true;
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            Debug("Round ended (event). Forcing an update on next poll.");
            _forceUpdate = true;
        }

        private async Task StatusLoopAsync(CancellationToken token)
        {
            await Task.Delay(500, token).ConfigureAwait(false);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_needInitialRoundDelay)
                    {
                        Debug($"WaitingForPlayers stabilization delay: {_waitingForPlayersDelayMs}ms.");

                        try
                        {
                            await Task.Delay(_waitingForPlayersDelayMs, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        _needInitialRoundDelay = false;
                    }

                    await CheckAndUpdateStatusAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Status loop error: {ex}");
                }

                try
                {
                    await Task.Delay(_pollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Debug("Status loop ended.");
        }

        private async Task CheckAndUpdateStatusAsync(CancellationToken token)
        {
            if (_client == null || !_client.LoginState.HasFlag(LoginState.LoggedIn))
                return;

            int playerCount = Player.ReadyList.Count(p => !p.IsDummy);

            string text;
            UserStatus discordStatus;

            if (playerCount == 0 && !Round.IsRoundStarted)
            {
                text = Config.statuses.waitingForPlayers.text;
                discordStatus = MapStatus(Config.statuses.waitingForPlayers.status);
            }
            else if (!Round.IsRoundStarted)
            {
                text = Config.statuses.waitingForPlayers.text;
                discordStatus = MapStatus(Config.statuses.waitingForPlayers.status);
            }
            else if (Round.IsRoundInProgress)
            {
                if (playerCount == 0)
                {
                    text = Config.statuses.noPlayers.text;
                    discordStatus = MapStatus(Config.statuses.noPlayers.status);
                }
                else
                {
                    text = Config.statuses.players.text;
                    discordStatus = MapStatus(Config.statuses.players.status);
                }
            }
            else if (Round.IsRoundEnded)
            {
                text = Config.statuses.roundEnded.text;
                discordStatus = MapStatus(Config.statuses.roundEnded.status);
            }
            else
            {
                text = Config.statuses.waitingForPlayers.text;
                discordStatus = MapStatus(Config.statuses.waitingForPlayers.status);
            }

            text = text.Replace("{count}", playerCount.ToString());

            bool nothingChanged =
                playerCount == _lastPlayerCount &&
                string.Equals(text, _lastText, StringComparison.Ordinal) &&
                discordStatus == _lastStatus;

            if (nothingChanged && !_forceUpdate)
            {
                Debug("No state change detected; skipping Discord update.");
                return;
            }

            _forceUpdate = false;

            Debug($"Updating Discord status: \"{text}\" ({discordStatus})");

            _lastPlayerCount = playerCount;
            _lastText = text;
            _lastStatus = discordStatus;

            try
            {
                await _client.SetActivityAsync(new Discord.CustomStatusGame(text)).ConfigureAwait(false);
                await _client.SetStatusAsync(discordStatus).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to set Discord status: {ex}");
            }
        }

        private UserStatus MapStatus(string status)
        {
            switch (status?.ToLowerInvariant())
            {
                case "online":
                    return UserStatus.Online;
                case "idle":
                    return UserStatus.Idle;
                case "dnd":
                    return UserStatus.DoNotDisturb;
                case "offline":
                    return UserStatus.Invisible;
                default:
                    return UserStatus.Idle;
            }
        }

        private void Debug(string message)
        {
            if (Config.debug)
                Logger.Info($"[DEBUG] {message}");
        }
    }
}