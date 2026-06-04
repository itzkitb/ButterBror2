using System;
using System.Collections.Generic;
using System.Text;

namespace ButterBror.ChatModules.Twitch.Models;

public interface ITwitchWhisperClient : ITwitchClient
{
    /// <summary>
    /// Whisper a message into the ear of one of the chatters
    /// </summary>
    Task SendWhisperAsync(string recipientUserId, string message);
}
