using System.Text.RegularExpressions;

namespace KuaiyunClient.Services;

public static partial class NodeDisplayNameFormatter
{
    public static string Format(string? originalName, NodeLocation location)
    {
        string original = (originalName ?? string.Empty).Trim();
        if (original.Length == 0)
        {
            return location.CountryName;
        }

        string value = StripFlagRegex().Replace(original, string.Empty).Trim();
        value = LeadingCountryCodesRegex().Replace(value, string.Empty).Trim();
        value = value.TrimStart(' ', '-', '_', '|', '·', '—', ':', '/');

        if (value.Length == 0)
        {
            value = original;
        }

        return value;
    }

    [GeneratedRegex("[\\uD83C][\\uDDE6-\\uDDFF][\\uD83C][\\uDDE6-\\uDDFF]", RegexOptions.Compiled)]
    private static partial Regex StripFlagRegex();

    [GeneratedRegex("^(?:(?:[A-Za-z]{2,3})[\\s._|:/-]+)+", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingCountryCodesRegex();
}
