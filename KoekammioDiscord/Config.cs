using LabApi.Loader.Features.Plugins;

namespace KoekammioDiscord
{
    public class Config
    {
        public bool isEnabled { get; set; } = true;
        public bool debug { get; set; } = false;

        public DiscordConfig discord { get; set; } = new DiscordConfig();
        public StatusMessages statuses { get; set; } = new StatusMessages();

        public class DiscordConfig
        {
            public string token { get; set; } = "YOUR_DISCORD_BOT_TOKEN";
        }

        public class StatusMessages
        {
            public Status waitingForPlayers { get; set; } = new Status { text = "Waiting for players...", status = "idle" };
            public Status noPlayers { get; set; } = new Status { text = "Players: 0", status = "idle" };
            public Status players { get; set; } = new Status { text = "Players: {count}", status = "online" };
            public Status roundEnded { get; set; } = new Status { text = "Round ended", status = "idle" };
        }

        public class Status
        {
            public string text { get; set; }
            public string status { get; set; }
        }
    }
}