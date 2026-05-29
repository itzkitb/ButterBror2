<img src="https://tupid.lol/i/bb2/twitch_module.png" width="100%">

![GitHub last commit](https://img.shields.io/github/last-commit/itzkitb/butterbror2?path=ButterBror.ChatModules.Twitch)
![TwitchLib](https://img.shields.io/badge/TwitchLib-3.10.2-6441A5)
![License](https://img.shields.io/badge/license-Apache%202.0-blue)

Twitch integration module for ButterBror2. This module enables your bot to connect to Twitch channels, process chat messages, execute commands, and react to platform-specific events like subscriptions, bits, and raids.

<img src="https://tupid.lol/i/bb2/features.png" width="100%">

- **Twitch IRC integration** via TwitchLib
- **Command execution** with prefix parsing, caching, and per-channel customization
- **Flexible reply modes**: `Mention` (default) or threaded `Reply`
- **Twitch Event handling**

<img src="https://tupid.lol/i/bb2/configuration.png" width="100%">

The module loads settings from `Twitch.json` in the ButterBror2 AppData directory:

| OS | Path |
|----|------|
| Windows | `%APPDATA%\SillyApps\ButterBror2\Twitch.json` |
| Linux / macOS | `~/.local/share/SillyApps/ButterBror2/Twitch.json` |

### `Twitch.json` Structure

```json
{
  "BotUsername": "your_bot_name",
  "OauthToken": "oauth:your_token_here",
  "Channel": "initial_target_channel",
  "ClientId": "your_twitch_client_id",
  "IsEnabled": true,
  "CommandPrefix": "#",
  "ReplyMode": "Mention"
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BotUsername` | `string` | `"butterbror_bot"` | Twitch username of the bot account |
| `OauthToken` | `string` | `""` | OAuth token (format: `oauth:xxxxxxxx`) — required |
| `Channel` | `string` | `""` | Initial channel to join on startup |
| `ClientId` | `string` | `""` | Twitch API Client ID — required for API/EventSub |
| `IsEnabled` | `bool` | `true` | Set `false` to disable the module without uninstalling |
| `CommandPrefix` | `string` | `"!"` | Default command prefix (can be overridden per-channel) |
| `ReplyMode` | `enum` | `"Mention"` | `"Mention"` or `"Reply"` — how the bot responds to commands |

<img src="https://tupid.lol/i/bb2/commands.png" width="100%">

All commands are scoped to the `sillyapps:twitch` platform and require explicit permissions.

| Command | Aliases | Permission | Usage | Description |
|---------|---------|------------|-------|-------------|
| `join` | `jc` | `su:twitch:join` | `!join <channel>` | Make the bot join a Twitch channel |
| `part` | `pc`, `leave` | `su:twitch:part` | `!part <channel>` | Make the bot leave a Twitch channel |
| `setprefix` | `prefix`, `sp` | `su:twitch:prefix` | `!setprefix <new_prefix>` | Change command prefix for current channel |

<img src="https://tupid.lol/i/bb2/project_structure.png" width="100%">

```
ButterBror.ChatModules.Twitch/
├── TwitchModule.cs                 # IChatModule implementation
├── Services/
│   ├── TwitchLibClient.cs          # TwitchLib wrapper with resilience
│   └── TwitchConfigurationService.cs # Config loader
├── Commands/
│   ├── JoinChannelCommand.cs
│   ├── PartChannelCommand.cs
│   ├── SetPrefixCommand.cs
│   └── Meta.cs                     # ICommandMetadata implementations
├── Models/
│   ├── ChatMessage.cs, TwitchUser.cs, TwitchChannel.cs
│   ├── TwitchConfiguration.cs, TwitchReplyMode.cs
│   └── ITwitchClient.cs            # Abstraction for testing
├── Events/                         # Strongly-typed event args
└── ButterBror.ChatModules.Twitch.csproj
```

### Key Dependencies

```xml
<PackageReference Include="TwitchLib.Api" Version="3.10.2" />
<PackageReference Include="TwitchLib.Client" Version="4.0.1" />
<PackageReference Include="TwitchLib.EventSub.Websockets" Version="0.8.0" />
<PackageReference Include="Microsoft.Extensions.Resilience" Version="10.6.0" />
<PackageReference Include="Polly.Core" Version="8.6.6" />
```

<img src="https://tupid.lol/i/bb2/installation.png" width="100%">

This module is built as part of the ButterBror2 solution. To use it:

1. **Clone the repository**
   ```bash
   git clone https://github.com/itzkitb/butterbror2.git
   cd butterbror2
   ```

2. **Build and install module**
   ```bash
   python3 scripts/module-compilator.py -t chat -p ButterBror.ChatModules.Twitch/ButterBror.ChatModules.Twitch.csproj
   ```

3. **Configure Twitch credentials**  
   Create `Twitch.json` in the AppData directory (see Configuration above)

3. **Run the host**
   ```bash
   dotnet run --project ButterBror.Host/ButterBror.Host.csproj
   ```

<img src="https://tupid.lol/i/bb2/license.png" width="100%">

This project is licensed under the **Apache-2.0 license**. See [LICENSE.txt](LICENSE.txt) for the full text

Copyright © 2026 DumbDev (aka. ItzKITb)

<img src="https://tupid.lol/i/bb2/support.png" width="100%">

If you encounter any issues or have questions:

- Create an issue in the [GitHub repository](https://github.com/itzkitb/zapret-cli/issues)
- Twitch: [https://twitch.tv/itzkitb](https://twitch.tv/itzkitb)
- Email: [itzkitb@gmail.com](mailto:itzkitb@gmail.com)
- Donate: [My Boosty](https://boosty.to/itzkitb)

---

*DumbDev :P*
