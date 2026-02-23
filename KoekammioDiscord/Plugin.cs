using System;
using System.Linq;
using System.Threading.Tasks;
using LabApi.Events.Handlers;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
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
        public override Version Version => new Version(1, 1, 0, 0);
        public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);

        private DiscordSocketClient _client;
        private int _playerCount;
        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private bool _updatePending = false;

        public override void Enable()
        {
            LoadConfigs();

            if (!Config.isEnabled)
            {
                Logger.Info("KoekammioDiscord is disabled in config.");
                return;
            }

            PlayerEvents.Joined += OnPlayerJoined;
            PlayerEvents.Left += OnPlayerLeft;
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

            PlayerEvents.Joined -= OnPlayerJoined;
            PlayerEvents.Left -= OnPlayerLeft;
            ServerEvents.RoundStarted -= OnRoundStarted;
            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
            ServerEvents.RoundEnded -= OnRoundEnded;

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
            Logger.Info($"Discord bot connected as {_client.CurrentUser.Username}#{_client.CurrentUser.Discriminator}");
            await Task.Delay(3000);
            _playerCount = Player.ReadyList.Count(p => !p.IsDummy);
            await UpdateStatusAsync();
        }

        private void OnPlayerJoined(PlayerJoinedEventArgs ev)
        {
            _playerCount = Player.ReadyList.Count(p => !p.IsDummy);
            Debug($"Player joined. New count: {_playerCount}");
            _ = SafeUpdateStatusAsync();
        }

        private void OnPlayerLeft(PlayerLeftEventArgs ev)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                _playerCount = Player.ReadyList.Count(p => !p.IsDummy);
                Debug($"Player left. New count: {_playerCount}");
                await SafeUpdateStatusAsync();
            });
        }

        private void OnRoundStarted()
        {
            Debug("Round started.");
            _playerCount = Player.ReadyList.Count(p => !p.IsDummy);
            _ = SafeUpdateStatusAsync();
        }

        private void OnWaitingForPlayers()
        {
            Debug("Entered waiting for players state.");
            _playerCount = Player.ReadyList.Count(p => !p.IsDummy);

            string waitingText = Config.statuses.waitingForPlayers.text.Replace("{count}", _playerCount.ToString());
            UserStatus waitingStatus = MapStatus(Config.statuses.waitingForPlayers.status);
            Debug($"Forcing WaitingForPlayers state: \"{waitingText}\" ({waitingStatus})");

            _ = Task.Run(async () =>
            {
                await _client.SetActivityAsync(new Discord.CustomStatusGame(waitingText));
                await _client.SetStatusAsync(waitingStatus);
            });
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            Debug("Round ended.");
            _playerCount = Player.ReadyList.Count(p => !p.IsDummy);
            _ = SafeUpdateStatusAsync();
        }

        private async Task SafeUpdateStatusAsync()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastStatusUpdate).TotalSeconds;

            if (elapsed < 3)
            {
                if (!_updatePending)
                {
                    _updatePending = true;
                    double delay = 3 - elapsed;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay));
                        _updatePending = false;
                        await UpdateStatusAsync();
                    });
                    Debug("Queued status update due to rate limit.");
                }
                else
                {
                    Debug("Skipped status update (already queued).");
                }
                return;
            }

            _lastStatusUpdate = now;
            await UpdateStatusAsync();
        }

        private async Task UpdateStatusAsync()
        {
            if (_client == null || !_client.LoginState.HasFlag(LoginState.LoggedIn))
                return;

            _playerCount = Player.ReadyList.Count(p => !p.IsDummy);

            string text;
            UserStatus discordStatus = UserStatus.Idle;

            if (!Round.IsRoundStarted)
            {
                text = Config.statuses.waitingForPlayers.text;
                discordStatus = MapStatus(Config.statuses.waitingForPlayers.status);
            }
            else if (Round.IsRoundInProgress)
            {
                if (_playerCount == 0)
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

            text = text.Replace("{count}", _playerCount.ToString());
            Debug($"Updating Discord status: \"{text}\" ({discordStatus})");

            await _client.SetActivityAsync(new Discord.CustomStatusGame(text));
            await _client.SetStatusAsync(discordStatus);
        }

        private UserStatus MapStatus(string status)
        {
            switch (status.ToLower())
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
