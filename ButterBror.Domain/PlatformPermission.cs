namespace ButterBror.Domain;

[Flags]
public enum PlatformPermission
{
    CanDeleteOwnMessages,
    CanEditOwnMessages,
    CanDeleteOtherMessages,
    CanEditOtherMessages,
    Moderator,
    Owner,
    Vip,
    CanBanUser,
    CanUnbanUser,
    CanEditChatData,
    CanAddModerators,
    CanRemoveModerators,
    CanUseBotCommands,
    Bot
}