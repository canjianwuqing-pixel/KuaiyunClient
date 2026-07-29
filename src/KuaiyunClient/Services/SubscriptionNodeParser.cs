using KuaiyunClient.Models;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class SubscriptionNodeParser
{
    public IReadOnlyList<ProxyNode> Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new SubscriptionParseException("订阅内容为空。");
        }

        List<Dictionary<string, string>> mappings = ReadProxyMappings(yaml);
        List<ProxyNode> nodes = [];
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (Dictionary<string, string> mapping in mappings)
        {
            if (!mapping.TryGetValue("name", out string? name)
                || string.IsNullOrWhiteSpace(name)
                || !mapping.TryGetValue("type", out string? type)
                || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            string originalName = name.Trim();
            if (!names.Add(originalName))
            {
                continue;
            }

            NodeLocation location = CountryFlagResolver.Resolve(originalName);
            string displayName = string.IsNullOrWhiteSpace(location.DisplayName)
                ? originalName
                : location.DisplayName;

            if (!displayName.StartsWith(location.Flag, StringComparison.Ordinal))
            {
                displayName = $"{location.Flag} {displayName}";
            }

            mapping.TryGetValue("server", out string? server);

            nodes.Add(new ProxyNode
            {
                Name = originalName,
                DisplayName = displayName,
                GroupName = location.CountryName,
                Type = type.Trim().ToUpperInvariant(),
                Server = string.IsNullOrWhiteSpace(server) ? null : server.Trim(),
                CountryCode = location.CountryCode,
                CountryName = location.CountryName,
                CountryFlag = location.Flag
            });
        }

        if (nodes.Count == 0)
        {
            throw new SubscriptionParseException(
                "订阅中没有找到有效的 proxies 节点。请确认后台返回的是 Mihomo/Clash Meta YAML。");
        }

        return nodes;
    }

    private static List<Dictionary<string, string>> ReadProxyMappings(string yaml)
    {
        string[] lines = yaml
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimStart('\uFEFF')
            .Split('\n');

        int sectionLine = -1;
        int sectionIndent = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].Trim();
            if (trimmed.Equals("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                sectionLine = index;
                sectionIndent = CountIndent(lines[index]);
                break;
            }

            if (trimmed.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase)
                && trimmed["proxies:".Length..].TrimStart().StartsWith("[]", StringComparison.Ordinal))
            {
                return [];
            }
        }

        if (sectionLine < 0)
        {
            throw new SubscriptionParseException(
                "订阅不是 Mihomo YAML：缺少顶层 proxies 配置。");
        }

        List<Dictionary<string, string>> output = [];
        Dictionary<string, string>? current = null;

        for (int index = sectionLine + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int indent = CountIndent(line);
            if (indent <= sectionIndent && !trimmed.StartsWith('-'))
            {
                break;
            }

            if (trimmed.StartsWith('-'))
            {
                AddMapping(output, current);
                current = null;

                string item = trimmed[1..].Trim();
                if (item.StartsWith('{') && item.EndsWith('}'))
                {
                    Dictionary<string, string> flowMapping = ParseFlowMapping(item[1..^1]);
                    AddMapping(output, flowMapping);
                    continue;
                }

                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (item.Length > 0)
                {
                    ParseKeyValue(item, current);
                }

                continue;
            }

            if (current is not null)
            {
                ParseKeyValue(trimmed, current);
            }
        }

        AddMapping(output, current);
        return output;
    }

    private static Dictionary<string, string> ParseFlowMapping(string content)
    {
        Dictionary<string, string> mapping = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in SplitTopLevel(content, ','))
        {
            ParseKeyValue(part.Trim(), mapping);
        }

        return mapping;
    }

    private static void ParseKeyValue(
        string text,
        IDictionary<string, string> mapping)
    {
        int separator = FindTopLevelSeparator(text, ':');
        if (separator <= 0)
        {
            return;
        }

        string key = Unquote(text[..separator].Trim());
        if (key.Length == 0)
        {
            return;
        }

        string rawValue = RemoveInlineComment(text[(separator + 1)..].Trim());
        mapping[key] = Unquote(rawValue.Trim());
    }

    private static IEnumerable<string> SplitTopLevel(string content, char separator)
    {
        int start = 0;
        int depth = 0;
        char quote = '\0';
        bool escaped = false;

        for (int index = 0; index < content.Length; index++)
        {
            char character = content[index];

            if (quote != '\0')
            {
                if (quote == '"' && character == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (character == quote && !escaped)
                {
                    quote = '\0';
                }

                escaped = false;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character is '{' or '[' or '(')
            {
                depth++;
                continue;
            }

            if (character is '}' or ']' or ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (character == separator && depth == 0)
            {
                yield return content[start..index];
                start = index + 1;
            }
        }

        yield return content[start..];
    }

    private static int FindTopLevelSeparator(string text, char separator)
    {
        int depth = 0;
        char quote = '\0';
        bool escaped = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (quote != '\0')
            {
                if (quote == '"' && character == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (character == quote && !escaped)
                {
                    quote = '\0';
                }

                escaped = false;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character is '{' or '[' or '(')
            {
                depth++;
                continue;
            }

            if (character is '}' or ']' or ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (character == separator && depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string RemoveInlineComment(string value)
    {
        char quote = '\0';
        bool escaped = false;

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];

            if (quote != '\0')
            {
                if (quote == '"' && character == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (character == quote && !escaped)
                {
                    quote = '\0';
                }

                escaped = false;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '#' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index].TrimEnd();
            }
        }

        return value;
    }

    private static string Unquote(string value)
    {
        string text = value.Trim();
        if (text.Length < 2)
        {
            return text;
        }

        if (text[0] == '\'' && text[^1] == '\'')
        {
            return text[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (text[0] == '"' && text[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(text) ?? string.Empty;
            }
            catch (JsonException)
            {
                return text[1..^1];
            }
        }

        return text;
    }

    private static int CountIndent(string line)
    {
        int indent = 0;
        foreach (char character in line)
        {
            if (character == ' ')
            {
                indent++;
            }
            else if (character == '\t')
            {
                indent += 4;
            }
            else
            {
                break;
            }
        }

        return indent;
    }

    private static void AddMapping(
        ICollection<Dictionary<string, string>> output,
        Dictionary<string, string>? mapping)
    {
        if (mapping is not null && mapping.Count > 0)
        {
            output.Add(mapping);
        }
    }
}

public sealed class SubscriptionParseException(string message) : Exception(message);
