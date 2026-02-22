using System.Text.RegularExpressions;

namespace ButterBror.Domain.Entities;

/// <summary>
/// Banphrase category with compiled regex patterns
/// </summary>
public class BanphraseCategory
{
    public string CategoryName { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string RegexPattern { get; set; } = string.Empty;
    public Regex? CompiledRegex { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public int MatchCount { get; set; }
    
    public void CompileRegex()
    {
        if (!string.IsNullOrWhiteSpace(RegexPattern))
        {
            CompiledRegex = new Regex(
                RegexPattern, 
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(500) // Timeout for safety
            );
        }
    }
    
    public bool IsMatch(string message)
    {
        if (CompiledRegex == null)
        {
            CompileRegex();
        }
        
        return CompiledRegex?.IsMatch(message) ?? false;
    }
}