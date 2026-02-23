# KoekammioDiscord
KoekammioDiscord is a simple but customizable event-based SCP:SL server status indicator bot. It is built on LabAPI and Discord.NET, aiming to provide an easy way to indicate the status of your SCP:SL server in your Discord server.
The approach is to be very simple by only doing one thing. If you want a more extensive solution, this might not be for you. Despite this, it consists of only one component to further simplify the experience.
KoekammioDiscord provides a simple configuration which allows you to customize/translate it to suit your needs.

## Setup
1. Download the latest version of `KoekammioDiscord.dll` as well as `dependencies.zip` from the releases page.
2. Place `KoekammioDiscord.dll` into `YOUR_SERVER_DIR/LabAPI/plugins/global/`, or alternatively into a specific port's directory instead of global.
3. Extract `dependencies.zip` into `YOUR_SERVER_DIR/LabAPI/dependencies/global/`, or alternatively into a specific port's directory instead of global.
4. Restart the server to generate the configs into `YOUR_SERVER_DIR/LabAPI/configs/PORT/KoekammioDiscord/config.yml`
5. Create a discord bot. I'm too lazy to explain the process here, but there are loads of guides on how to do it.
6. Get your bot token, and replace the placeholder in the config with it. Configure further however you please.
7. Add the bot to any servers you want it to be in, maybe give it a special role and place on the server list.
8. You're done!

## Configuration
The default configuration of the plugin for reference:
```yaml
is_enabled: true
debug: false
discord:
  token: YOUR_DISCORD_BOT_TOKEN
statuses:
  waiting_for_players:
    text: Waiting for players...
    status: idle
  no_players:
    text: 'Players: 0'
    status: idle
  players:
    text: 'Players: {count}'
    status: online
  round_ended:
    text: 'Players: {count}'
    status: online
```

What the options do:
- `is_enabled`: Enables/disables the plugin.
- `debug`: Enables/disables debug messages in the server console.
- `token`: Your Discord bot's token. Has to be set for the bot to work.
- `waiting_for_players`: Sets the bot's status when the server is waiting for players (the round hasn't started yet).
- `no_players`: Sets the bot's status when the round has already started but there are zero people playing.
- `players`: Sets the bot's status when the round is ongoing and there are players.
- `round_ended`: Sets the bot's status when the round has already ended. By default it is "disabled", being set to the same as when there are players. This makes the two indistinguishable from the bot's status, but this can be changed.

Keep in mind:
- You can use "{count}" in the text fields of any status. It gets replaced by the current player count.
- Player count excludes dummies and NPCs to avoid issues.
- You can disable text in a certain state by setting the text field empty (`""`).
- You can disable distinguishing one state from another by setting the text and status to be the same.
- The plugin supports the status values `online`, `idle`, `dnd` and `invisible`.
