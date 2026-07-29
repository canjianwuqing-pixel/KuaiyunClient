using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KuaiyunClient.Services;

public static class CountryFlagResolver
{
    private static readonly Regex FlagRegex = new(
        "[\\uD83C][\\uDDE6-\\uDDFF][\\uD83C][\\uDDE6-\\uDDFF]",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> RegionNames = BuildRegionNames();
    private static readonly IReadOnlyList<CountryAlias> Aliases = BuildAliases();

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

        foreach (AliasDefinition definition in CustomDefinitions())
        {
            string countryName = string.IsNullOrWhiteSpace(definition.CountryName)
                ? GetCountryName(definition.IsoCode)
                : definition.CountryName;

            foreach (string alias in definition.Aliases)
            {
                AddAlias(aliases, alias, definition.IsoCode, countryName);
            }
        }

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                RegionInfo region = new(culture.Name);
                string isoCode = region.TwoLetterISORegionName.ToUpperInvariant();
                string countryName = GetCountryName(isoCode);

                AddAlias(aliases, region.EnglishName, isoCode, countryName);
                AddAlias(aliases, region.NativeName, isoCode, countryName);
                AddAlias(aliases, region.TwoLetterISORegionName, isoCode, countryName);
                AddAlias(aliases, region.ThreeLetterISORegionName, isoCode, countryName);
            }
            catch (ArgumentException)
            {
                // 部分特殊文化没有可用的 RegionInfo，忽略即可。
            }
        }

        return aliases.Values
            .OrderByDescending(item => item.Alias.Length)
            .ThenBy(item => item.Alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<AliasDefinition> CustomDefinitions()
    {
        return new[]
        {
            new AliasDefinition("HK", "香港", "香港", "Hong Kong", "HONGKONG", "HKG", "HK"),
            new AliasDefinition("TW", "台湾", "台湾", "台灣", "Taiwan", "Taipei", "台北", "TPE", "TW"),
            new AliasDefinition("MO", "澳门", "澳门", "澳門", "Macao", "Macau", "MFM", "MO"),
            new AliasDefinition("CN", "中国大陆", "中国", "中國", "大陆", "大陸", "China", "Mainland", "北京", "上海", "广州", "深圳", "杭州", "成都", "重庆", "青岛", "南京", "武汉", "CN"),
            new AliasDefinition("JP", "日本", "日本", "Japan", "Tokyo", "东京", "東京", "Osaka", "大阪", "Nagoya", "名古屋", "Fukuoka", "福冈", "福岡", "Sapporo", "札幌", "NRT", "HND", "KIX", "JP"),
            new AliasDefinition("KR", "韩国", "韩国", "韓國", "南韩", "南韓", "Korea", "South Korea", "Seoul", "首尔", "首爾", "Busan", "釜山", "ICN", "GMP", "KR"),
            new AliasDefinition("KP", "朝鲜", "朝鲜", "朝鮮", "North Korea", "DPRK", "KP"),
            new AliasDefinition("SG", "新加坡", "新加坡", "Singapore", "狮城", "獅城", "SIN", "SG"),
            new AliasDefinition("MY", "马来西亚", "马来西亚", "馬來西亞", "Malaysia", "Kuala Lumpur", "吉隆坡", "KUL", "MY"),
            new AliasDefinition("TH", "泰国", "泰国", "泰國", "Thailand", "Bangkok", "曼谷", "BKK", "TH"),
            new AliasDefinition("VN", "越南", "越南", "Vietnam", "Hanoi", "河内", "河內", "Ho Chi Minh", "胡志明", "VN"),
            new AliasDefinition("PH", "菲律宾", "菲律宾", "菲律賓", "Philippines", "Manila", "马尼拉", "馬尼拉", "MNL", "PH"),
            new AliasDefinition("ID", "印度尼西亚", "印度尼西亚", "印度尼西亞", "印尼", "Indonesia", "Jakarta", "雅加达", "雅加達", "ID"),
            new AliasDefinition("IN", "印度", "印度", "India", "Mumbai", "孟买", "孟買", "Delhi", "德里", "Chennai", "Bangalore", "班加罗尔", "IN"),
            new AliasDefinition("PK", "巴基斯坦", "巴基斯坦", "Pakistan", "Karachi", "卡拉奇", "PK"),
            new AliasDefinition("BD", "孟加拉国", "孟加拉", "孟加拉国", "孟加拉國", "Bangladesh", "Dhaka", "达卡", "達卡", "BD"),
            new AliasDefinition("LK", "斯里兰卡", "斯里兰卡", "斯里蘭卡", "Sri Lanka", "Colombo", "科伦坡", "科倫坡", "LK"),
            new AliasDefinition("NP", "尼泊尔", "尼泊尔", "尼泊爾", "Nepal", "Kathmandu", "加德满都", "加德滿都", "NP"),
            new AliasDefinition("MM", "缅甸", "缅甸", "緬甸", "Myanmar", "Burma", "Yangon", "仰光", "MM"),
            new AliasDefinition("KH", "柬埔寨", "柬埔寨", "Cambodia", "Phnom Penh", "金边", "金邊", "KH"),
            new AliasDefinition("LA", "老挝", "老挝", "老撾", "Laos", "Vientiane", "万象", "萬象"),
            new AliasDefinition("MN", "蒙古", "蒙古", "Mongolia", "Ulaanbaatar", "乌兰巴托", "烏蘭巴托", "MN"),
            new AliasDefinition("KZ", "哈萨克斯坦", "哈萨克斯坦", "哈薩克斯坦", "Kazakhstan", "Almaty", "阿拉木图", "阿拉木圖", "KZ"),
            new AliasDefinition("UZ", "乌兹别克斯坦", "乌兹别克斯坦", "烏茲別克斯坦", "Uzbekistan", "Tashkent", "塔什干", "UZ"),
            new AliasDefinition("AE", "阿联酋", "阿联酋", "阿聯酋", "UAE", "United Arab Emirates", "Dubai", "迪拜", "Abu Dhabi", "阿布扎比", "DXB", "AE"),
            new AliasDefinition("SA", "沙特阿拉伯", "沙特", "Saudi Arabia", "Riyadh", "利雅得", "SA"),
            new AliasDefinition("QA", "卡塔尔", "卡塔尔", "卡塔爾", "Qatar", "Doha", "多哈", "QA"),
            new AliasDefinition("IL", "以色列", "以色列", "Israel", "Tel Aviv", "特拉维夫", "特拉維夫", "IL"),
            new AliasDefinition("TR", "土耳其", "土耳其", "Türkiye", "Turkey", "Istanbul", "伊斯坦布尔", "伊斯坦堡", "TR"),
            new AliasDefinition("IR", "伊朗", "伊朗", "Iran", "Tehran", "德黑兰", "德黑蘭", "IR"),
            new AliasDefinition("US", "美国", "美国", "美國", "United States", "USA", "America", "Los Angeles", "洛杉矶", "洛杉磯", "San Jose", "圣何塞", "聖荷西", "Seattle", "西雅图", "西雅圖", "New York", "纽约", "紐約", "Chicago", "芝加哥", "Dallas", "达拉斯", "達拉斯", "Miami", "迈阿密", "邁阿密", "Washington", "华盛顿", "華盛頓", "Silicon Valley", "硅谷", "矽谷", "LAX", "SJC", "SFO", "SEA", "JFK", "IAD", "ORD", "DFW", "US"),
            new AliasDefinition("CA", "加拿大", "加拿大", "Canada", "Toronto", "多伦多", "多倫多", "Vancouver", "温哥华", "溫哥華", "Montreal", "蒙特利尔", "蒙特利爾", "YYZ", "YVR", "CA"),
            new AliasDefinition("MX", "墨西哥", "墨西哥", "Mexico", "Mexico City", "墨西哥城", "MX"),
            new AliasDefinition("BR", "巴西", "巴西", "Brazil", "São Paulo", "Sao Paulo", "圣保罗", "聖保羅", "Rio de Janeiro", "里约", "里約", "BR"),
            new AliasDefinition("AR", "阿根廷", "阿根廷", "Argentina", "Buenos Aires", "布宜诺斯艾利斯", "布宜諾斯艾利斯", "AR"),
            new AliasDefinition("CL", "智利", "智利", "Chile", "Santiago", "圣地亚哥", "聖地牙哥", "CL"),
            new AliasDefinition("PE", "秘鲁", "秘鲁", "秘魯", "Peru", "Lima", "利马", "利馬", "PE"),
            new AliasDefinition("CO", "哥伦比亚", "哥伦比亚", "哥倫比亞", "Colombia", "Bogota", "Bogotá", "波哥大", "CO"),
            new AliasDefinition("GB", "英国", "英国", "英國", "United Kingdom", "Great Britain", "Britain", "England", "UK", "London", "伦敦", "倫敦", "Manchester", "曼彻斯特", "曼徹斯特", "LHR", "GB"),
            new AliasDefinition("IE", "爱尔兰", "爱尔兰", "愛爾蘭", "Ireland", "Dublin", "都柏林", "IE"),
            new AliasDefinition("FR", "法国", "法国", "法國", "France", "Paris", "巴黎", "Marseille", "马赛", "馬賽", "CDG", "FR"),
            new AliasDefinition("DE", "德国", "德国", "德國", "Germany", "Frankfurt", "法兰克福", "法蘭克福", "Berlin", "柏林", "Munich", "慕尼黑", "FRA", "DE"),
            new AliasDefinition("NL", "荷兰", "荷兰", "荷蘭", "Netherlands", "Holland", "Amsterdam", "阿姆斯特丹", "AMS", "NL"),
            new AliasDefinition("BE", "比利时", "比利时", "比利時", "Belgium", "Brussels", "布鲁塞尔", "布魯塞爾", "BE"),
            new AliasDefinition("LU", "卢森堡", "卢森堡", "盧森堡", "Luxembourg", "LU"),
            new AliasDefinition("CH", "瑞士", "瑞士", "Switzerland", "Zurich", "苏黎世", "蘇黎世", "Geneva", "日内瓦", "日內瓦", "CH"),
            new AliasDefinition("AT", "奥地利", "奥地利", "奧地利", "Austria", "Vienna", "维也纳", "維也納", "AT"),
            new AliasDefinition("IT", "意大利", "意大利", "義大利", "Italy", "Milan", "米兰", "米蘭", "Rome", "罗马", "羅馬", "IT"),
            new AliasDefinition("ES", "西班牙", "西班牙", "Spain", "Madrid", "马德里", "馬德里", "Barcelona", "巴塞罗那", "巴塞隆納", "ES"),
            new AliasDefinition("PT", "葡萄牙", "葡萄牙", "Portugal", "Lisbon", "里斯本", "PT"),
            new AliasDefinition("GR", "希腊", "希腊", "希臘", "Greece", "Athens", "雅典", "GR"),
            new AliasDefinition("PL", "波兰", "波兰", "波蘭", "Poland", "Warsaw", "华沙", "華沙", "PL"),
            new AliasDefinition("CZ", "捷克", "捷克", "Czech", "Czechia", "Prague", "布拉格", "CZ"),
            new AliasDefinition("HU", "匈牙利", "匈牙利", "Hungary", "Budapest", "布达佩斯", "布達佩斯", "HU"),
            new AliasDefinition("RO", "罗马尼亚", "罗马尼亚", "羅馬尼亞", "Romania", "Bucharest", "布加勒斯特", "RO"),
            new AliasDefinition("FI", "芬兰", "芬兰", "芬蘭", "Finland", "Helsinki", "赫尔辛基", "赫爾辛基", "FI"),
            new AliasDefinition("SE", "瑞典", "瑞典", "Sweden", "Stockholm", "斯德哥尔摩", "斯德哥爾摩", "SE"),
            new AliasDefinition("NO", "挪威", "挪威", "Norway", "Oslo", "奥斯陆", "奧斯陸", "NO"),
            new AliasDefinition("DK", "丹麦", "丹麦", "丹麥", "Denmark", "Copenhagen", "哥本哈根", "DK"),
            new AliasDefinition("IS", "冰岛", "冰岛", "冰島", "Iceland", "Reykjavik", "雷克雅未克", "IS"),
            new AliasDefinition("RU", "俄罗斯", "俄罗斯", "俄羅斯", "Russia", "Russian", "Moscow", "莫斯科", "Saint Petersburg", "圣彼得堡", "聖彼得堡", "RU"),
            new AliasDefinition("UA", "乌克兰", "乌克兰", "烏克蘭", "Ukraine", "Kyiv", "Kiev", "基辅", "基輔", "UA"),
            new AliasDefinition("AU", "澳大利亚", "澳大利亚", "澳大利亞", "澳洲", "Australia", "Sydney", "悉尼", "雪梨", "Melbourne", "墨尔本", "墨爾本", "Brisbane", "布里斯班", "AU"),
            new AliasDefinition("NZ", "新西兰", "新西兰", "紐西蘭", "New Zealand", "Auckland", "奥克兰", "奧克蘭", "NZ"),
            new AliasDefinition("ZA", "南非", "南非", "South Africa", "Johannesburg", "约翰内斯堡", "約翰尼斯堡", "Cape Town", "开普敦", "開普敦", "ZA"),
            new AliasDefinition("EG", "埃及", "埃及", "Egypt", "Cairo", "开罗", "開羅", "EG"),
            new AliasDefinition("MA", "摩洛哥", "摩洛哥", "Morocco", "Casablanca", "卡萨布兰卡", "卡薩布蘭卡", "MA"),
            new AliasDefinition("NG", "尼日利亚", "尼日利亚", "尼日利亞", "Nigeria", "Lagos", "拉各斯", "NG"),
            new AliasDefinition("KE", "肯尼亚", "肯尼亚", "肯尼亞", "Kenya", "Nairobi", "内罗毕", "奈洛比", "KE")
        };
    }

    private static IReadOnlyDictionary<string, string> BuildRegionNames()
    {
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                RegionInfo region = new(culture.Name);
                names.TryAdd(region.TwoLetterISORegionName.ToUpperInvariant(), region.DisplayName);
            }
            catch (ArgumentException)
            {
                // 忽略没有地区信息的文化。
            }
        }

        foreach (AliasDefinition definition in CustomDefinitions())
        {
            names[definition.IsoCode] = definition.CountryName;
        }

        return names;
    }

    private static string GetCountryName(string isoCode)
    {
        return RegionNames.TryGetValue(isoCode, out string? countryName)
            ? countryName
            : "其他地区";
    }

    private static void AddAlias(
        IDictionary<string, CountryAlias> aliases,
        string? alias,
        string isoCode,
        string countryName)
    {
        string value = (alias ?? string.Empty).Trim();
        if (value.Length < 2)
        {
            return;
        }

        aliases.TryAdd(value, new CountryAlias(value, isoCode.ToUpperInvariant(), countryName));
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

    private sealed record AliasDefinition(
        string IsoCode,
        string CountryName,
        params string[] Aliases);
}

public sealed record NodeLocation(
    string Flag,
    string CountryCode,
    string CountryName,
    string DisplayName);
