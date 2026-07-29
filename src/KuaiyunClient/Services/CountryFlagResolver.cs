using System.Text;
using System.Text.RegularExpressions;

namespace KuaiyunClient.Services;

public static class CountryFlagResolver
{
    private static readonly Regex FlagRegex = new(
        "[\\uD83C][\\uDDE6-\\uDDFF][\\uD83C][\\uDDE6-\\uDDFF]",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, IsoCountryEntry> RegionsByCode =
        IsoCountryCatalog.All.ToDictionary(
            item => item.Alpha2,
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<CountryAlias> Aliases = BuildAliases();

    public static int SupportedRegionCount => IsoCountryCatalog.All.Count;

    public static NodeLocation Resolve(string? nodeName)
    {
        string source = (nodeName ?? string.Empty).Trim();
        Match existingFlag = FlagRegex.Match(source);

        if (existingFlag.Success)
        {
            string isoCode = FlagToIsoCode(existingFlag.Value);
            return new NodeLocation(
                existingFlag.Value,
                isoCode,
                GetCountryName(isoCode),
                StripLeadingFlag(source));
        }

        foreach (CountryAlias alias in Aliases)
        {
            if (!ContainsAlias(source, alias.Alias))
            {
                continue;
            }

            return new NodeLocation(
                ToFlag(alias.IsoCode),
                alias.IsoCode,
                alias.CountryName,
                source);
        }

        if (ContainsAny(source, "自动", "AUTO", "GLOBAL", "全球", "智能", "故障转移", "负载均衡"))
        {
            return new NodeLocation("🌐", string.Empty, "自动选择", source);
        }

        if (ContainsAny(source, "中转", "入口", "出口", "RELAY", "TRANSIT"))
        {
            return new NodeLocation("🔁", string.Empty, "中转线路", source);
        }

        return new NodeLocation("🌐", string.Empty, "其他地区", source);
    }

    private static IReadOnlyList<CountryAlias> BuildAliases()
    {
        Dictionary<string, CountryAlias> aliases = new(StringComparer.OrdinalIgnoreCase);

        foreach (IsoCountryEntry region in IsoCountryCatalog.All)
        {
            AddAlias(aliases, region.ChineseName, region);
            AddAlias(aliases, region.EnglishName, region);
            AddAlias(aliases, region.Alpha2, region);
            AddAlias(aliases, region.Alpha3, region);

            foreach (string alternateName in region.AlternateNames)
            {
                AddAlias(aliases, alternateName, region);
            }
        }

        foreach (CityAliasDefinition definition in CityAliases())
        {
            if (!RegionsByCode.TryGetValue(definition.IsoCode, out IsoCountryEntry? region))
            {
                continue;
            }

            foreach (string alias in definition.Aliases)
            {
                AddAlias(aliases, alias, region);
            }
        }

        return aliases.Values
            .OrderByDescending(item => item.Alias.Length)
            .ThenBy(item => item.Alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<CityAliasDefinition> CityAliases()
    {
        return
        [
            new("CN", "中国大陆", "大陆", "大陸", "Mainland", "北京", "上海", "广州", "深圳", "杭州", "成都", "重庆", "青岛", "南京", "武汉", "PEK", "PVG", "CAN", "SZX"),
            new("HK", "HONGKONG", "HKG", "九龙", "九龍"),
            new("TW", "台灣", "Taipei", "台北", "高雄", "TPE", "KHH"),
            new("MO", "澳門", "Macau", "MFM"),
            new("JP", "Tokyo", "东京", "東京", "Osaka", "大阪", "Nagoya", "名古屋", "Fukuoka", "福冈", "福岡", "Sapporo", "札幌", "NRT", "HND", "KIX"),
            new("KR", "韩国", "韓國", "南韩", "南韓", "Korea", "South Korea", "Seoul", "首尔", "首爾", "Busan", "釜山", "ICN", "GMP"),
            new("SG", "狮城", "獅城", "SIN"),
            new("MY", "Kuala Lumpur", "吉隆坡", "KUL"),
            new("TH", "Bangkok", "曼谷", "BKK"),
            new("VN", "Hanoi", "河内", "河內", "Ho Chi Minh", "胡志明"),
            new("PH", "Manila", "马尼拉", "馬尼拉", "MNL"),
            new("ID", "印尼", "Jakarta", "雅加达", "雅加達"),
            new("IN", "Mumbai", "孟买", "孟買", "Delhi", "德里", "Chennai", "Bangalore", "班加罗尔"),
            new("AE", "阿联酋", "阿聯酋", "UAE", "Dubai", "迪拜", "Abu Dhabi", "阿布扎比", "DXB"),
            new("TR", "Türkiye", "Turkey", "Istanbul", "伊斯坦布尔", "伊斯坦堡"),
            new("US", "USA", "America", "Los Angeles", "洛杉矶", "洛杉磯", "San Jose", "圣何塞", "聖荷西", "Seattle", "西雅图", "西雅圖", "New York", "纽约", "紐約", "Chicago", "芝加哥", "Dallas", "达拉斯", "達拉斯", "Miami", "迈阿密", "邁阿密", "Washington", "华盛顿", "華盛頓", "Silicon Valley", "硅谷", "矽谷", "LAX", "SJC", "SFO", "SEA", "JFK", "IAD", "ORD", "DFW"),
            new("CA", "Toronto", "多伦多", "多倫多", "Vancouver", "温哥华", "溫哥華", "Montreal", "蒙特利尔", "蒙特利爾", "YYZ", "YVR"),
            new("MX", "Mexico City", "墨西哥城"),
            new("BR", "São Paulo", "Sao Paulo", "圣保罗", "聖保羅", "Rio de Janeiro", "里约", "里約"),
            new("AR", "Buenos Aires", "布宜诺斯艾利斯", "布宜諾斯艾利斯"),
            new("CL", "Santiago", "圣地亚哥", "聖地牙哥"),
            new("CO", "Bogota", "Bogotá", "波哥大"),
            new("GB", "UK", "Great Britain", "Britain", "England", "London", "伦敦", "倫敦", "Manchester", "曼彻斯特", "曼徹斯特", "LHR"),
            new("IE", "Dublin", "都柏林"),
            new("FR", "Paris", "巴黎", "Marseille", "马赛", "馬賽", "CDG"),
            new("DE", "Frankfurt", "法兰克福", "法蘭克福", "Berlin", "柏林", "Munich", "慕尼黑", "FRA"),
            new("NL", "Holland", "Amsterdam", "阿姆斯特丹", "AMS"),
            new("BE", "Brussels", "布鲁塞尔", "布魯塞爾"),
            new("CH", "Zurich", "苏黎世", "蘇黎世", "Geneva", "日内瓦", "日內瓦"),
            new("AT", "Vienna", "维也纳", "維也納"),
            new("IT", "Milan", "米兰", "米蘭", "Rome", "罗马", "羅馬"),
            new("ES", "Madrid", "马德里", "馬德里", "Barcelona", "巴塞罗那", "巴塞隆納"),
            new("PT", "Lisbon", "里斯本"),
            new("CZ", "Prague", "布拉格"),
            new("PL", "Warsaw", "华沙", "華沙"),
            new("FI", "Helsinki", "赫尔辛基", "赫爾辛基"),
            new("SE", "Stockholm", "斯德哥尔摩", "斯德哥爾摩"),
            new("NO", "Oslo", "奥斯陆", "奧斯陸"),
            new("DK", "Copenhagen", "哥本哈根"),
            new("RU", "Moscow", "莫斯科", "Saint Petersburg", "圣彼得堡", "聖彼得堡"),
            new("UA", "Kyiv", "Kiev", "基辅", "基輔"),
            new("AU", "澳洲", "Sydney", "悉尼", "雪梨", "Melbourne", "墨尔本", "墨爾本", "Brisbane", "布里斯班"),
            new("NZ", "Auckland", "奥克兰", "奧克蘭"),
            new("ZA", "Johannesburg", "约翰内斯堡", "約翰尼斯堡", "Cape Town", "开普敦", "開普敦"),
            new("EG", "Cairo", "开罗", "開羅"),
            new("MA", "Casablanca", "卡萨布兰卡", "卡薩布蘭卡"),
            new("NG", "Lagos", "拉各斯"),
            new("KE", "Nairobi", "内罗毕", "奈洛比")
        ];
    }

    private static string GetCountryName(string isoCode)
    {
        return RegionsByCode.TryGetValue(isoCode, out IsoCountryEntry? region)
            ? region.ChineseName
            : "其他地区";
    }

    private static void AddAlias(
        IDictionary<string, CountryAlias> aliases,
        string? alias,
        IsoCountryEntry region)
    {
        string value = (alias ?? string.Empty).Trim();
        if (value.Length < 2)
        {
            return;
        }

        aliases.TryAdd(
            value,
            new CountryAlias(value, region.Alpha2.ToUpperInvariant(), region.ChineseName));
    }

    private static bool ContainsAlias(string source, string alias)
    {
        if (IsShortAsciiAlias(alias))
        {
            return Regex.IsMatch(
                source,
                $"(?<![A-Za-z]){Regex.Escape(alias)}(?![A-Za-z])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return source.Contains(alias, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShortAsciiAlias(string alias)
    {
        return alias.Length <= 3 && alias.All(character =>
            (character >= 'A' && character <= 'Z')
            || (character >= 'a' && character <= 'z'));
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string StripLeadingFlag(string source)
    {
        string result = FlagRegex.Replace(source, string.Empty, 1).Trim();
        return result.TrimStart(' ', '-', '_', '|', '·', '—', ':');
    }

    private static string ToFlag(string isoCode)
    {
        string code = isoCode.Trim().ToUpperInvariant();
        if (code.Length != 2 || !code.All(character => character is >= 'A' and <= 'Z'))
        {
            return "🌐";
        }

        return string.Concat(
            char.ConvertFromUtf32(0x1F1E6 + code[0] - 'A'),
            char.ConvertFromUtf32(0x1F1E6 + code[1] - 'A'));
    }

    private static string FlagToIsoCode(string flag)
    {
        Rune[] runes = flag.EnumerateRunes().ToArray();
        if (runes.Length != 2)
        {
            return string.Empty;
        }

        int first = runes[0].Value - 0x1F1E6;
        int second = runes[1].Value - 0x1F1E6;
        if (first is < 0 or > 25 || second is < 0 or > 25)
        {
            return string.Empty;
        }

        return string.Concat((char)('A' + first), (char)('A' + second));
    }

    private sealed record CountryAlias(string Alias, string IsoCode, string CountryName);

    private sealed record CityAliasDefinition(
        string IsoCode,
        params string[] Aliases);
}

public sealed record NodeLocation(
    string Flag,
    string CountryCode,
    string CountryName,
    string DisplayName);
