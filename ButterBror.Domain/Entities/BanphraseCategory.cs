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

    public string? GetMatchedPhrase(string message)
    {
        if (CompiledRegex == null)
        {
            CompileRegex();
        }
        var match = CompiledRegex?.Match(message);
        return match?.Success == true ? match.Value : null;
    }

    public string? GetMatchedPatternPart(string message)
    {
        if (CompiledRegex == null)
        {
            CompileRegex();
        }
    
        var match = CompiledRegex?.Match(message);
        if (match?.Success != true)
        {
            return null;
        }
    
        var matchedAlternative = GetMatchedAlternative(message);
        if (!string.IsNullOrEmpty(matchedAlternative))
        {
            return matchedAlternative;
        }
    
        // Fallback: return full pattern
        return RegexPattern.Length > 50 ? RegexPattern[..50] + "..." : RegexPattern;
    }

    public string? GetMatchedAlternative(string message)
    {
        if (CompiledRegex == null)
        {
            CompileRegex();
        }
    
        var match = CompiledRegex?.Match(message);
        if (match?.Success != true || string.IsNullOrEmpty(RegexPattern))
        {
            return null;
        }
    
        // Split pattern by top-level | and test each alternative
        var alternatives = SplitRegexAlternatives(RegexPattern);
    
        foreach (var alternative in alternatives)
        {
            try
            {
                var testRegex = new Regex(
                    alternative.Trim(),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)
                );
            
                if (testRegex.IsMatch(match.Value))
                {
                    // Truncate long patterns for display
                    var display = alternative.Trim();
                    return display.Length > 50 ? display[..50] + "..." : display;
                }
            }
            catch
            {
                // Skip invalid patterns
            }
        }
    
        // Fallback: return matched text
        return match.Value.Length > 50 ? match.Value[..50] + "..." : match.Value;
    }

    /// <summary>
    /// Splits regex pattern by top-level | (not inside groups)
    /// </summary>
    private static List<string> SplitRegexAlternatives(string pattern)
    {
        var alternatives = new List<string>();
        var current = new System.Text.StringBuilder();
        int groupDepth = 0;
        bool inCharacterClass = false;
        bool escaped = false;
    
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
        
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }
        
            if (c == '\\')
            {
                current.Append(c);
                escaped = true;
                continue;
            }
        
            if (c == '[' && !inCharacterClass)
            {
                inCharacterClass = true;
                current.Append(c);
                continue;
            }
        
            if (c == ']' && inCharacterClass)
            {
                inCharacterClass = false;
                current.Append(c);
                continue;
            }
        
            if (inCharacterClass)
            {
                current.Append(c);
                continue;
            }
        
            if (c == '(')
            {
                groupDepth++;
                current.Append(c);
                continue;
            }
        
            if (c == ')')
            {
                groupDepth--;
                current.Append(c);
                continue;
            }
        
            if (c == '|' && groupDepth == 0)
            {
                alternatives.Add(current.ToString());
                current.Clear();
                continue;
            }
        
            current.Append(c);
        }
    
        if (current.Length > 0)
        {
            alternatives.Add(current.ToString());
        }
    
        return alternatives;
    }
}