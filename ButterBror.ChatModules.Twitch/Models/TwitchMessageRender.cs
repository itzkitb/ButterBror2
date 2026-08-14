using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Messaging.Enums;

namespace ButterBror.ChatModules.Twitch.Models;

internal class TwitchMessageRender(IPasteBinService pasteBinService, ILocalizationService localization)
{
    private const int MaxTwitchMessageLength = 500;
    
    private const string StandardChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const string BoldStr = "𝗮𝗯𝗰𝗱𝗲𝗳𝗴𝗵𝗶𝗷𝗸𝗹𝗺𝗻𝗼𝗽𝗾𝗿𝘀𝘁𝘂𝘃𝘄𝘅𝘆𝘇𝗔𝗕𝗖𝗗𝗘𝗙𝗚𝗛𝗜𝗝𝗞𝗟𝗠𝗡𝗢𝗣𝗤𝗥𝗦𝗧𝗨𝗩𝗪𝗫𝗬𝗭𝟎𝟏𝟐𝟑𝟒𝟓𝟔𝟕𝟖𝟗";
    private const string ItalicStr = "𝘢𝘣𝘤𝘥𝘦𝘧𝘨𝘩𝘪𝘫𝘬𝘭𝘮𝘯𝘰𝘱𝘲𝘳𝘴𝘵𝘶𝘷𝘸𝘹𝘺𝘻𝘈𝘉𝘊𝘋𝘌𝘍𝘎𝘏𝘐𝘑𝘒𝘓𝘔𝘕𝘖𝘗𝘘𝘙𝘚𝘛𝘜𝘝𝘞𝘟𝘠𝘡0123456789";
    private const string BoldItalicStr = "𝙖𝙗𝙘𝙙𝙚𝙛𝙜𝙝𝙞𝙟𝙠𝙡𝙢𝙣𝙤𝙥𝙦𝙧𝙨𝙩𝙪𝙫𝙬𝙭𝙮𝙯𝘼𝘽𝘾𝘿𝙀𝙁𝙂𝙃𝙄𝙅𝙆𝙇𝙈𝙉𝙊𝙋𝙌𝙍𝙎𝙏𝙐𝙑𝙒𝙓𝙔𝙕0123456789";
    private const string MonospaceStr = "𝚊𝚋𝚌𝚍𝚎𝚏𝚐𝚑𝚒𝚓𝚔𝚕𝚖𝚗𝚘𝚙𝚚𝚛𝚜𝚝𝚞𝚟𝚠𝚡𝚢𝚣𝙰𝙱𝙲𝙳𝙴𝙵𝙶𝙷𝙸𝙹𝙺𝙻𝙼𝙽𝙾𝙿𝚀𝚁𝚂𝚃𝚄𝚅𝚆𝚇𝚈𝚉𝟶𝟷𝟸𝟹𝟺𝟻𝟼𝟽𝟾𝟿";
    
    private static readonly string[] BoldChars = SplitToGraphemes(BoldStr);
    private static readonly string[] ItalicChars = SplitToGraphemes(ItalicStr);
    private static readonly string[] BoldItalicChars = SplitToGraphemes(BoldItalicStr);
    private static readonly string[] MonospaceChars = SplitToGraphemes(MonospaceStr);

    public async Task<string> RenderTwitchMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        string twitchText = RenderTwitchMessageInternal(message);
        
        if (twitchText.Length <= MaxTwitchMessageLength)
        {
            return twitchText;
        }
        
        string markdownText = RenderMarkdownMessage(message);
        string url = await pasteBinService.UploadTextAsync(markdownText, cancellationToken);
        
        return await localization.GetStringAsync("core.bot.twitch.long_text", "EN_US", url);
    }
    
    // ><> Twitch render
    public string RenderTwitchMessageInternal(Message message)
    {
        var sb = new StringBuilder();
        foreach (var part in message.Parts)
        {
            sb.Append(FormatForTwitch(part.Text, part.Styles));
        }
        return sb.ToString();
    }
    
    private string FormatForTwitch(string text, MessageStyles styles)
    {
        if (string.IsNullOrEmpty(text) || styles == MessageStyles.None) 
            return text;
        
        var sb = new StringBuilder(text.Length * 2);
        foreach (char c in text)
        {
            sb.Append(FormatCharForTwitch(c, styles));
        }

        string result = sb.ToString();
        
        if (styles.HasFlag(MessageStyles.Quote))
        {
            var lines = result.Split('\n');
            result = string.Join("\n", lines.Select(l => $"> {l}"));
        }

        if (styles.HasFlag(MessageStyles.Spoiler))
        {
            result = $"||{result}||";
        }
        
        return result;
    }

    private string FormatCharForTwitch(char c, MessageStyles styles)
    {
        if (styles == MessageStyles.None)
            return c.ToString();
        
        int index = StandardChars.IndexOf(c);
        
        if (index == -1)
            return c.ToString();
        
        string baseCharStr;

        if (styles.HasFlag(MessageStyles.Bold) && styles.HasFlag(MessageStyles.Italic))
            baseCharStr = BoldItalicChars[index];
        else if (styles.HasFlag(MessageStyles.Bold))
            baseCharStr = BoldChars[index];
        else if (styles.HasFlag(MessageStyles.Italic))
            baseCharStr = ItalicChars[index];
        else if (styles.HasFlag(MessageStyles.Monospace))
            baseCharStr = MonospaceChars[index];
        else
            baseCharStr = c.ToString();
        
        if (styles.HasFlag(MessageStyles.Underline))
        {
            baseCharStr = $"{baseCharStr}\u0332";
        }
    
        if (styles.HasFlag(MessageStyles.Strikethrough))
        {
            baseCharStr = $"{baseCharStr}\u0336";
        }

        return baseCharStr;
    }
    
    // ><> Markdown render
    private string RenderMarkdownMessage(Message message)
    {
        var sb = new StringBuilder();
        foreach (var part in message.Parts)
        {
            sb.Append(FormatForMarkdown(part.Text, part.Styles));
        }
        return sb.ToString();
    }

    private string FormatForMarkdown(string text, MessageStyles styles)
    {
        if (string.IsNullOrEmpty(text) || styles == MessageStyles.None)
            return text;

        string result = text;

        if (styles.HasFlag(MessageStyles.Monospace))
        {
            result = $"`{result}`";
        }
        else
        {
            if (styles.HasFlag(MessageStyles.Bold) && styles.HasFlag(MessageStyles.Italic))
                result = $"***{result}***";
            else if (styles.HasFlag(MessageStyles.Bold))
                result = $"**{result}**";
            else if (styles.HasFlag(MessageStyles.Italic))
                result = $"*{result}*";
        }

        if (styles.HasFlag(MessageStyles.Strikethrough))
        {
            result = $"~~{result}~~";
        }

        if (styles.HasFlag(MessageStyles.Quote))
        {
            var lines = result.Split('\n');
            result = string.Join("\n", lines.Select(l => $"> {l}"));
        }

        if (styles.HasFlag(MessageStyles.Spoiler))
        {
            result = $"||{result}||";
        }

        return result;
    }
    
    // ><> Extra
    private static string[] SplitToGraphemes(string text)
    {
        var result = new List<string>();
        foreach (var rune in text.EnumerateRunes())
        {
            result.Add(rune.ToString());
        }
        return result.ToArray();
    }
}