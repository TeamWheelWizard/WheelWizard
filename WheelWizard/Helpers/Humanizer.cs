using WheelWizard.Resources.Languages;

namespace WheelWizard.Helpers;

public static class Humanizer
{
    public static string? ReplaceDynamic(string? langString, params object[] replacements)
    {
        // any dynamic part should be as follows: {$1}, {$2}, etc.
        for (var i = 0; i < replacements.Length; i++)
        {
            langString = langString?.Replace("{$" + (i + 1) + "}", replacements[i]?.ToString() ?? "");
        }

        return langString;
    }

    public static string HumanizeTimeSpan(TimeSpan timeSpan)
    {
        // we use language to do the words like Phrases.Time_Days_1 or Phrases.Time_Days_x
        // however, the one with x has to be put in the method: ReplaceDynamic(Phrases.Time_Days_x, 10);

        // now e need to replace all the old with the new language versions

        if (Math.Abs(timeSpan.TotalDays) >= 1)
        {
            var days = timeSpan.Days;
            var hours = timeSpan.Hours;
            var dayText = days == 1 ? Phrases.Time_Days_1 : ReplaceDynamic(Phrases.Time_Days_x, days);
            if (hours == 0)
                return dayText!;
            var hourText = hours == 1 ? Phrases.Time_Hours_1 : ReplaceDynamic(Phrases.Time_Hours_x, hours);
            return $"{dayText} {hourText}";
        }

        if (Math.Abs(timeSpan.TotalHours) >= 1)
        {
            var hours = timeSpan.Hours;
            var minutes = timeSpan.Minutes;
            var hourText = hours == 1 ? Phrases.Time_Hours_1 : ReplaceDynamic(Phrases.Time_Hours_x, hours);
            if (minutes == 0)
                return hourText!;
            var minuteText = minutes == 1 ? Phrases.Time_Minutes_1 : ReplaceDynamic(Phrases.Time_Minutes_x, minutes);
            return $"{hourText} {minuteText}";
        }
        if (Math.Abs(timeSpan.TotalMinutes) >= 1)
        {
            var minutes = timeSpan.Minutes;
            var seconds = timeSpan.Seconds;
            var minuteText = minutes == 1 ? Phrases.Time_Minutes_1 : ReplaceDynamic(Phrases.Time_Minutes_x, minutes);
            if (seconds == 0)
                return minuteText!;
            var secondText = seconds == 1 ? Phrases.Time_Seconds_1 : ReplaceDynamic(Phrases.Time_Seconds_x, seconds);
            return $"{minuteText} {secondText}";
        }

        return ReplaceDynamic(Phrases.Time_Seconds_x, timeSpan.Seconds)!;

        // internal method to simplify the pluralization of words
        string P(int count) => Math.Abs(count) != 1 ? "s" : "";
    }

    public static string HumanizeSeconds(int seconds) => HumanizeTimeSpan(TimeSpan.FromSeconds(seconds));

    public static string GetRegionName(uint regionID)
    {
        return regionID switch
        {
            0 => Common.Region_Japan,
            1 => Common.Region_America,
            2 => Common.Region_Europe,
            3 => Common.Region_Australia,
            4 => Common.Region_Taiwan,
            5 => Common.Region_SouthKorea,
            6 => Common.Region_China,
            _ => Common.State_Unknown,
        };
    }

    public static string GetCountryEmoji(byte countryId)
    {
        return countryId switch
        {
            // Japan
            1 => "🇯🇵",

            // Americas
            8 => "🇦🇮", // Anguilla
            9 => "🇦🇬", // Antigua and Barbuda
            10 => "🇦🇷", // Argentina
            11 => "🇦🇲", // Aruba
            12 => "🇧🇸", // Bahamas
            13 => "🇧🇧", // Barbados
            14 => "🇧🇿", // Belize
            15 => "🇧🇴", // Bolivia
            16 => "🇧🇷", // Brazil
            17 => "🇻🇬", // British Virgin Islands
            18 => "🇨🇦", // Canada
            19 => "🇰🇾", // Cayman Islands
            20 => "🇨🇱", // Chile
            21 => "🇨🇴", // Colombia
            22 => "🇨🇷", // Costa Rica
            23 => "🇩🇲", // Dominica
            24 => "🇩🇴", // Dominican Republic
            25 => "🇪🇨", // Ecuador
            26 => "🇸🇻", // El Salvador
            27 => "🇫🇷", // French Guiana
            28 => "🇬🇩", // Grenada
            29 => "🇲🇶", // Guadeloupe
            30 => "🇵🇪", // Guatemala
            31 => "🇬🇾", // Guyana
            32 => "🇭🇹", // Haiti
            33 => "🇭🇳", // Honduras
            34 => "🇯🇲", // Jamaica
            35 => "🇲🇶", // Martinique
            36 => "🇲🇽", // Mexico
            37 => "🇲🇸", // Montserrat
            38 => "🇳🇱", // Netherlands Antilles
            39 => "🇳🇮", // Nicaragua
            40 => "🇵🇦", // Panama
            41 => "🇵🇾", // Paraguay
            42 => "🇵🇪", // Peru
            43 => "🇰🇳", // St. Kitts and Nevis
            44 => "🇱🇨", // St. Lucia
            45 => "🇻🇨", // St. Vincent and the Grenadines
            46 => "🇸🇷", // Suriname
            47 => "🇹🇹", // Trinidad and Tobago
            48 => "🇹🇨", // Turks and Caicos Islands
            49 => "🇺🇸", // United States
            50 => "🇺🇾", // Uruguay
            51 => "🇻🇮", // US Virgin Islands
            52 => "🇻🇪", // Venezuela

            // Europe & Africa
            64 => "🇦🇱", // Albania
            65 => "🇦🇺", // Australia
            66 => "🇦🇹", // Austria
            67 => "🇧🇪", // Belgium
            68 => "🇧🇦", // Bosnia and Herzegovina
            69 => "🇧🇼", // Botswana
            70 => "🇧🇬", // Bulgaria
            71 => "🇭🇷", // Croatia
            72 => "🇨🇾", // Cyprus
            73 => "🇨🇿", // Czech Republic
            74 => "🇩🇰", // Denmark
            75 => "🇪🇪", // Estonia
            76 => "🇫🇮", // Finland
            77 => "🇫🇷", // France
            78 => "🇩🇪", // Germany
            79 => "🇬🇷", // Greece
            80 => "🇭🇺", // Hungary
            81 => "🇮🇸", // Iceland
            82 => "🇮🇪", // Ireland
            83 => "🇮🇹", // Italy
            84 => "🇱🇻", // Latvia
            85 => "🇱🇸", // Lesotho
            86 => "🇱🇮", // Liechtenstein
            87 => "🇱🇹", // Lithuania
            88 => "🇱🇺", // Luxembourg
            89 => "🇲🇰", // North Macedonia
            90 => "🇲🇹", // Malta
            91 => "🇲🇪", // Montenegro
            92 => "🇲🇿", // Mozambique
            93 => "🇳🇦", // Namibia
            94 => "🇳🇱", // Netherlands
            95 => "🇳🇿", // New Zealand
            96 => "🇳🇴", // Norway
            97 => "🇵🇱", // Poland
            98 => "🇵🇹", // Portugal
            99 => "🇷🇴", // Romania
            100 => "🇷🇺", // Russia
            101 => "🇷🇸", // Serbia
            102 => "🇸🇰", // Slovakia
            103 => "🇸🇮", // Slovenia
            104 => "🇿🇦", // South Africa
            105 => "🇪🇸", // Spain
            106 => "🇸🇿", // Eswatini
            107 => "🇸🇪", // Sweden
            108 => "🇨🇭", // Switzerland
            109 => "🇹🇷", // Turkey
            110 => "🇬🇧", // United Kingdom
            111 => "🇿🇲", // Zambia
            112 => "🇿🇼", // Zimbabwe
            113 => "🇦🇿", // Azerbaijan
            114 => "🇲🇷", // Mauritania
            115 => "🇲🇱", // Mali
            116 => "🇳🇪", // Niger
            117 => "🇹🇩", // Chad
            118 => "🇸🇩", // Sudan
            119 => "🇪🇷", // Eritrea
            120 => "🇩🇯", // Djibouti
            121 => "🇸🇴", // Somalia

            // Southeast Asia
            128 => "🇹🇼", // Taiwan
            136 => "🇰🇷", // South Korea
            144 => "🇭🇰", // Hong Kong
            145 => "🇲🇴", // Macao
            152 => "🇮🇩", // Indonesia
            153 => "🇸🇬", // Singapore
            154 => "🇹🇭", // Thailand
            155 => "🇵🇭", // Philippines
            156 => "🇲🇾", // Malaysia
            160 => "🇨🇳", // China

            // Middle East
            168 => "🇦🇪", // U.A.E.
            169 => "🇮🇳", // India
            170 => "🇪🇬", // Egypt
            171 => "🇴🇲", // Oman
            172 => "🇶🇦", // Qatar
            173 => "🇰🇼", // Kuwait
            174 => "🇸🇦", // Saudi Arabia
            175 => "🇸🇾", // Syria
            176 => "🇧🇭", // Bahrain
            177 => "🇯🇴", // Jordan

            _ => "🏳️",
        };
    }
}
