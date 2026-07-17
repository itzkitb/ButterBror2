using System.Text.RegularExpressions;
using ButterBror.Core.Messaging.Enums;
using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging;

public class BBCodeParser : IBBCodeParser
{
    private record ExtraInfo(string Type, object? Data);
    
    private static readonly Regex TagRegex = new(
        @"^\[(/?)(B|I|U|S|Q|M|H|L|E|C)(?:\s+([^=\]]+)(?:=""([^""]*)"")?)?\]$", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<MessagePart> Parse(string bbCodeText)
    {
        if (string.IsNullOrEmpty(bbCodeText))
        {
            return new List<MessagePart> { new() { Text = string.Empty } };
        }

        var parts = new List<MessagePart>();
        var styleStack = new Stack<MessageStyles>();
        var urlStack = new Stack<string?>();
        var extraStack = new Stack<ExtraInfo>();
        
        int currentIndex = 0;
        int length = bbCodeText.Length;

        while (currentIndex < length)
        {
            // S0. Looking for the next opening square bracket
            int openBracketIndex = bbCodeText.IndexOf('[', currentIndex);
            
            if (openBracketIndex == -1)
            {
                AddCurrentPart(parts, bbCodeText.Substring(currentIndex), styleStack, urlStack, extraStack);
                break;
            }
            
            if (openBracketIndex > currentIndex)
            {
                AddCurrentPart(parts, bbCodeText.Substring(currentIndex, openBracketIndex - currentIndex), styleStack, urlStack, extraStack);
            }
            
            int closeBracketIndex = bbCodeText.IndexOf(']', openBracketIndex);
            if (closeBracketIndex == -1)
            {
                AddCurrentPart(parts, bbCodeText.Substring(openBracketIndex), styleStack, urlStack, extraStack);
                break;
            }
            
            string potentialTag = bbCodeText.Substring(openBracketIndex, closeBracketIndex - openBracketIndex + 1);
            var match = TagRegex.Match(potentialTag);
            
            if (match.Success)
            {
                bool isClosing = match.Groups[1].Value == "/";
                string tagName = match.Groups[2].Value.ToUpperInvariant();
                string? attrName = match.Groups[3].Value;
                string? attrValue = match.Groups[4].Value;
                
                if (tagName == "C" && !isClosing)
                {
                    int endRawIndex = bbCodeText.IndexOf("[/C]", closeBracketIndex + 1, StringComparison.OrdinalIgnoreCase);
                    
                    if (endRawIndex != -1)
                    {
                        string rawText = bbCodeText.Substring(closeBracketIndex + 1, endRawIndex - (closeBracketIndex + 1));
                        
                        if (!string.IsNullOrEmpty(rawText))
                        {
                            parts.Add(new MessagePart
                            {
                                Text = rawText,
                                Styles = MessageStyles.None,
                                IsRaw = true
                            });
                        }
                        
                        currentIndex = endRawIndex + 4;
                        continue;
                    }
                    else
                    {
                        string rawText = bbCodeText.Substring(closeBracketIndex + 1);
                        if (!string.IsNullOrEmpty(rawText))
                        {
                            parts.Add(new MessagePart
                            {
                                Text = rawText,
                                Styles = MessageStyles.None,
                                IsRaw = true
                            });
                        }
                        break;
                    }
                }
                
                ProcessTag(tagName, isClosing, attrName, attrValue, styleStack, urlStack, extraStack);
                currentIndex = closeBracketIndex + 1;
            }
            else
            {
                AddCurrentPart(parts, "[", styleStack, urlStack, extraStack);
                currentIndex = openBracketIndex + 1;
            }
        }

        return MergeAdjacentParts(parts);
    }

    private void ProcessTag(string tagName, bool isClosing, string? attrName, string? attrValue,
        Stack<MessageStyles> styleStack, Stack<string?> urlStack, Stack<ExtraInfo> extraStack)
    {
        if (isClosing)
        {
            switch (tagName)
            {
                case "B": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Bold) styleStack.Pop(); break;
                case "I": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Italic) styleStack.Pop(); break;
                case "U": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Underline) styleStack.Pop(); break;
                case "S": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Strikethrough) styleStack.Pop(); break;
                case "Q": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Quote) styleStack.Pop(); break;
                case "M": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Monospace) styleStack.Pop(); break;
                case "H": if (styleStack.Count > 0 && styleStack.Peek() == MessageStyles.Spoiler) styleStack.Pop(); break;
                case "L": if (urlStack.Count > 0) urlStack.Pop(); break;
                case "E": if (extraStack.Count > 0) extraStack.Pop(); break;
            }
        }
        else
        {
            switch (tagName)
            {
                case "B": styleStack.Push(MessageStyles.Bold); break;
                case "I": styleStack.Push(MessageStyles.Italic); break;
                case "U": styleStack.Push(MessageStyles.Underline); break;
                case "S": styleStack.Push(MessageStyles.Strikethrough); break;
                case "Q": styleStack.Push(MessageStyles.Quote); break;
                case "M": styleStack.Push(MessageStyles.Monospace); break;
                case "H": styleStack.Push(MessageStyles.Spoiler); break;
                case "L": urlStack.Push(attrValue ?? string.Empty); break;
                case "E": extraStack.Push(new ExtraInfo(attrValue ?? "default", null)); break;
            }
        }
    }

    private void AddCurrentPart(List<MessagePart> parts, string text, 
        Stack<MessageStyles> styleStack, Stack<string?> urlStack, Stack<ExtraInfo> extraStack)
    {
        if (string.IsNullOrEmpty(text)) return;

        var part = new MessagePart
        {
            Text = text,
            Styles = CombineStyles(styleStack),
            Url = urlStack.Count > 0 ? urlStack.Peek() : null,
            ExtraType = extraStack.Count > 0 ? extraStack.Peek().Type : null,
            ExtraData = extraStack.Count > 0 ? extraStack.Peek().Data : null,
            IsRaw = false
        };

        parts.Add(part);
    }

    private MessageStyles CombineStyles(Stack<MessageStyles> styleStack)
    {
        MessageStyles combined = MessageStyles.None;
        foreach (var style in styleStack)
        {
            combined |= style;
        }
        return combined;
    }

    private List<MessagePart> MergeAdjacentParts(List<MessagePart> parts)
    {
        if (parts.Count <= 1) return parts;

        var merged = new List<MessagePart> { parts[0] };
        for (int i = 1; i < parts.Count; i++)
        {
            var current = parts[i];
            var previous = merged[^1];

            if (previous.Styles == current.Styles && 
                previous.Url == current.Url && 
                previous.ExtraType == current.ExtraType && 
                previous.IsRaw == current.IsRaw)
            {
                merged[^1] = previous with { Text = previous.Text + current.Text };
            }
            else
            {
                merged.Add(current);
            }
        }
        return merged;
    }
}