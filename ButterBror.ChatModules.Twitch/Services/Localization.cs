namespace ButterBror.ChatModules.Twitch.Services;

public class Localization
{
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultTranslations => 
        new Dictionary<string, IReadOnlyDictionary<string, string>>()
    {
        { 
            "RU_RU",
            new Dictionary<string, string>()
            {
                // Add channel
                { "command.add_channel.usage", "Использование: addchannel <канал>" },
                { "command.add_channel.permission", "Доступ запрещен" },
                { "command.add_channel.already", "Канал #{0} уже есть в списке." },
                { "command.add_channel.success", "Канал #{0} добавлен в список и подключен." },
                // Auth
                { "command.auth.success", "После авторизации скопируйте код и отправьте его мне личным сообщением (/w {0} <code>): {1}" },
                // Channel settings
                { "command.channel_settings.permission", "Доступ запрещен" },
                { "command.channel_settings.usage", "Использование: twitchset <online|offline> <true|false>" },
                { "command.channel_settings.value", "Значение должно быть либо true, либо false" },
                { "command.channel_settings.unknown", "Неизвестный параметр. Доступно: online, offline" },
                { "command.channel_settings.success", "Параметр '{0}' изменен на {1}" },
                // Delete channel
                { "command.del_channel.permission", "Доступ запрещен" },
                { "command.del_channel.usage", "Использование: delchannel <канал>" },
                { "command.del_channel.not_found", "Канал #{0} не найден в списке" },
                { "command.del_channel.success", "Канал #{0} удален из списка и разделен" },
                // Join channel
                { "command.join.usage", "Использование: join <канал>" },
                { "command.join.permission", "Доступ запрещен" },
                { "command.join.success", "Канал #{0} успешно подключен" },
                // Part channel
                { "command.part.usage", "Использование: part <канал>" },
                { "command.part.permission", "Доступ запрещен" },
                { "command.part.success", "Канал #{0} успешно отключен" },
                // Set prefix
                { "command.set_prefix.usage", "Использование: setprefix <новый-префикс>" },
                { "command.set_prefix.empty", "Префикс не может быть пустым или состоять из пробелов" },
                { "command.set_prefix.32chars", "Префикс должен состоять из 1-32 символов" },
                { "command.set_prefix.success", "Префикс команды для #{0} изменен на '{1}'" },
            }
        },
        {
            "EN_US",
            new Dictionary<string, string>()
            {
                // Add channel
                { "command.add_channel.usage", "Usage: addchannel <channel>. Example: addchannel pajlada" },
                { "command.add_channel.permission", "Permission denied" },
                { "command.add_channel.already", "Channel #{0} is already in the list" },
                { "command.add_channel.success", "Channel #{0} added to list and connected" },
                // Auth
                { "command.auth.success", "After authorization, copy the code and send it to me via whisper (/w {0} <code>): {1}" },
                // Channel settings
                { "command.channel_settings.permission", "Permission denied" },
                { "command.channel_settings.usage", "Usage: twitchset <online|offline> <true|false>" },
                { "command.channel_settings.value", "The value must be true or false" },
                { "command.channel_settings.unknown", "Unknown parameter. Available: online, offline" },
                { "command.channel_settings.success", "Parameter '{0}' changed to {1}" },
                // Delete channel
                { "command.del_channel.permission", "Permission denied" },
                { "command.del_channel.usage", "Usage: delchannel <channel>. Example: delchannel hasanabi" }, // 🐕‍🦺⚡
                { "command.del_channel.not_found", "Channel #{0} not found in the list" },
                { "command.del_channel.success", "Channel #{0} deleted from list and parted" },
                // Join channel
                { "command.join.usage", "Usage: join <channel>" },
                { "command.join.permission", "Permission denied" },
                { "command.join.success", "Successfully joined channel #{0}" },
                // Part channel
                { "command.part.usage", "Usage: part <channel>" },
                { "command.part.permission", "Permission denied" },
                { "command.part.success", "Successfully parted channel #{0}" },
                // Set prefix
                { "command.set_prefix.usage", "Usage: setprefix <new-prefix>" },
                { "command.set_prefix.empty", "Prefix cannot be empty or whitespace" },
                { "command.set_prefix.32chars", "Prefix must be 1-32 characters long" },
                { "command.set_prefix.success", "Command prefix for #{0} has been changed to '{1}'" },
            }
        }
    };
}