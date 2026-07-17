using System.Text.RegularExpressions;
using ButterBror.Core.Messaging.Enums;
using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging;

public class BBCodeParser : IBBCodeParser
{
    private record ExtraInfo(string Type, object? Data);
    
    private static readonly Regex TagRegex = new(
        @"\[(/?)(B|I|U|S|Q|M|H|L|E|C)(?:\s+([^=\]]+)(?:=""([^""]*)"")?)?\]", 
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
        var rawStack = new Stack<bool>(); 
        
        int lastIndex = 0;
        var matches = TagRegex.Matches(bbCodeText);

        foreach (Match match in matches)
        {
            // S0. Add text before the tag
            if (match.Index > lastIndex)
            {
                string textBefore = bbCodeText.Substring(lastIndex, match.Index - lastIndex);
                AddCurrentPart(parts, textBefore, styleStack, urlStack, extraStack, rawStack);
            }

            bool isClosing = match.Groups[1].Value == "/";
            string tagName = match.Groups[2].Value.ToUpper();
            string? attrName = match.Groups[3].Value;
            string? attrValue = match.Groups[4].Value;

            // S1. Process the tag
            ProcessTag(tagName, isClosing, attrName, attrValue, styleStack, urlStack, extraStack, rawStack);

            lastIndex = match.Index + match.Length;
        }

        // S2. Add remaining text after the last tag
        if (lastIndex < bbCodeText.Length)
        {
            string remainingText = bbCodeText.Substring(lastIndex);
            AddCurrentPart(parts, remainingText, styleStack, urlStack, extraStack, rawStack);
        }

        return MergeAdjacentParts(parts);
    }

    private void ProcessTag(string tagName, bool isClosing, string? attrName, string? attrValue,
        Stack<MessageStyles> styleStack, Stack<string?> urlStack, 
        Stack<ExtraInfo> extraStack, Stack<bool> rawStack)
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
                case "C": if (rawStack.Count > 0) rawStack.Pop(); break;
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
                case "C": rawStack.Push(true); break;
            }
        }
    }

    private void AddCurrentPart(List<MessagePart> parts, string text, 
        Stack<MessageStyles> styleStack, Stack<string?> urlStack, 
        Stack<ExtraInfo> extraStack, Stack<bool> rawStack)
    {
        if (string.IsNullOrEmpty(text)) return;

        bool isRaw = rawStack.Count > 0;
        
        var part = new MessagePart
        {
            Text = text,
            Styles = isRaw ? MessageStyles.None : CombineStyles(styleStack),
            Url = (!isRaw && urlStack.Count > 0) ? urlStack.Peek() : null,
            ExtraType = (!isRaw && extraStack.Count > 0) ? extraStack.Peek().Type : null,
            ExtraData = (!isRaw && extraStack.Count > 0) ? extraStack.Peek().Data : null,
            IsRaw = isRaw
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

    /// <summary>
    /// Optimizes the list by merging adjacent parts with identical formatting
    /// </summary>
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