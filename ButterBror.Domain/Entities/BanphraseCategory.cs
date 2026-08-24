using System.Text.RegularExpressions;

namespace ButterBror.Domain.Entities;

/// <summary>
/// Banphrase category with compiled regex patterns
/// </summary>
public class BanphraseCategory
{
    public string CategoryName { get; init; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public Guid ChatId { get; init; }
    public string RegexPattern { get; init; } = string.Empty;
    private Regex? CompiledRegex { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    
    public void CompileRegex()
    {
        if (!string.IsNullOrWhiteSpace(RegexPattern))
        {
            CompiledRegex = new Regex(
                RegexPattern, 
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(500)
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
    
        // fallback: return full pattern
        return RegexPattern.Length > 50 ? RegexPattern[..50] + "..." : RegexPattern;
    }

    private string? GetMatchedAlternative(string message)
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
    
        // split pattern by top-level and test each alternative
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

                if (!testRegex.IsMatch(match.Value))
                    continue;
                
                // truncate long patterns for display
                var display = alternative.Trim();
                return display.Length > 50 ? display[..50] + "..." : display;
            }
            catch
            {
                // skip invalid patterns
            }
        }
    
        // fallback: return matched text
        return match.Value.Length > 50 ? match.Value[..50] + "..." : match.Value;
    }
    
    private static List<string> SplitRegexAlternatives(string pattern)
    {
        var alternatives = new List<string>();
        var current = new System.Text.StringBuilder();
        var groupDepth = 0;
        var inCharacterClass = false;
        var escaped = false;
    
        foreach (var c in pattern)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }
        
            switch (c)
            {
                case '\\':
                    current.Append(c);
                    escaped = true;
                    continue;
                case '[' when !inCharacterClass:
                    inCharacterClass = true;
                    current.Append(c);
                    continue;
                case ']' when inCharacterClass:
                    inCharacterClass = false;
                    current.Append(c);
                    continue;
            }

            if (inCharacterClass)
            {
                current.Append(c);
                continue;
            }
        
            switch (c)
            {
                case '(':
                    groupDepth++;
                    current.Append(c);
                    continue;
                case ')':
                    groupDepth--;
                    current.Append(c);
                    continue;
                case '|' when groupDepth == 0:
                    alternatives.Add(current.ToString());
                    current.Clear();
                    continue;
                default:
                    current.Append(c);
                    break;
            }
        }
    
        if (current.Length > 0)
        {
            alternatives.Add(current.ToString());
        }
    
        return alternatives;
    }
}