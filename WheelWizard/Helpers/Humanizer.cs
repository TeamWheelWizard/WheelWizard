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
        if (Math.Abs(timeSpan.TotalDays) >= 1)
        {
            var days = timeSpan.Days;
            var hours = timeSpan.Hours;
            var dayText = t("time.days.n", days);
            if (hours == 0)
                return dayText;
            var hourText = t("time.hours.n", hours);
            return $"{dayText} {hourText}";
        }

        if (Math.Abs(timeSpan.TotalHours) >= 1)
        {
            var hours = timeSpan.Hours;
            var minutes = timeSpan.Minutes;
            var hourText = t("time.hours.n", hours);
            if (minutes == 0)
                return hourText;
            var minuteText = t("time.minutes.n", minutes);
            return $"{hourText} {minuteText}";
        }
        if (Math.Abs(timeSpan.TotalMinutes) >= 1)
        {
            var minutes = timeSpan.Minutes;
            var seconds = timeSpan.Seconds;
            var minuteText = t("time.minutes.n", minutes);
            if (seconds == 0)
                return minuteText;
            var secondText = t("time.seconds.n", seconds);
            return $"{minuteText} {secondText}";
        }

        return t("time.seconds.n", timeSpan.Seconds);
    }

    public static string HumanizeSeconds(int seconds) => HumanizeTimeSpan(TimeSpan.FromSeconds(seconds));

    public static string GetRegionName(uint regionID)
    {
        return regionID switch
        {
            0 => t("region.japan"),
            1 => t("region.america"),
            2 => t("region.europe"),
            3 => t("region.australia"),
            4 => t("region.taiwan"),
            5 => t("region.south_korea"),
            6 => t("region.china"),
            _ => t("state.unknown"),
        };
    }

    public static string GetCountryEmoji(byte countryId)
    {
        return countryId switch
        {
            // Japan
            1 => "ðŸ‡¯ðŸ‡µ",

            // Americas
            8 => "ðŸ‡¦ðŸ‡®", // Anguilla
            9 => "ðŸ‡¦ðŸ‡¬", // Antigua and Barbuda
            10 => "ðŸ‡¦ðŸ‡·", // Argentina
            11 => "ðŸ‡¦ðŸ‡²", // Aruba
            12 => "ðŸ‡§ðŸ‡¸", // Bahamas
            13 => "ðŸ‡§ðŸ‡§", // Barbados
            14 => "ðŸ‡§ðŸ‡¿", // Belize
            15 => "ðŸ‡§ðŸ‡´", // Bolivia
            16 => "ðŸ‡§ðŸ‡·", // Brazil
            17 => "ðŸ‡»ðŸ‡¬", // British Virgin Islands
            18 => "ðŸ‡¨ðŸ‡¦", // Canada
            19 => "ðŸ‡°ðŸ‡¾", // Cayman Islands
            20 => "ðŸ‡¨ðŸ‡±", // Chile
            21 => "ðŸ‡¨ðŸ‡´", // Colombia
            22 => "ðŸ‡¨ðŸ‡·", // Costa Rica
            23 => "ðŸ‡©ðŸ‡²", // Dominica
            24 => "ðŸ‡©ðŸ‡´", // Dominican Republic
            25 => "ðŸ‡ªðŸ‡¨", // Ecuador
            26 => "ðŸ‡¸ðŸ‡»", // El Salvador
            27 => "ðŸ‡«ðŸ‡·", // French Guiana
            28 => "ðŸ‡¬ðŸ‡©", // Grenada
            29 => "ðŸ‡²ðŸ‡¶", // Guadeloupe
            30 => "ðŸ‡µðŸ‡ª", // Guatemala
            31 => "ðŸ‡¬ðŸ‡¾", // Guyana
            32 => "ðŸ‡­ðŸ‡¹", // Haiti
            33 => "ðŸ‡­ðŸ‡³", // Honduras
            34 => "ðŸ‡¯ðŸ‡²", // Jamaica
            35 => "ðŸ‡²ðŸ‡¶", // Martinique
            36 => "ðŸ‡²ðŸ‡½", // Mexico
            37 => "ðŸ‡²ðŸ‡¸", // Montserrat
            38 => "ðŸ‡³ðŸ‡±", // Netherlands Antilles
            39 => "ðŸ‡³ðŸ‡®", // Nicaragua
            40 => "ðŸ‡µðŸ‡¦", // Panama
            41 => "ðŸ‡µðŸ‡¾", // Paraguay
            42 => "ðŸ‡µðŸ‡ª", // Peru
            43 => "ðŸ‡°ðŸ‡³", // St. Kitts and Nevis
            44 => "ðŸ‡±ðŸ‡¨", // St. Lucia
            45 => "ðŸ‡»ðŸ‡¨", // St. Vincent and the Grenadines
            46 => "ðŸ‡¸ðŸ‡·", // Suriname
            47 => "ðŸ‡¹ðŸ‡¹", // Trinidad and Tobago
            48 => "ðŸ‡¹ðŸ‡¨", // Turks and Caicos Islands
            49 => "ðŸ‡ºðŸ‡¸", // United States
            50 => "ðŸ‡ºðŸ‡¾", // Uruguay
            51 => "ðŸ‡»ðŸ‡®", // US Virgin Islands
            52 => "ðŸ‡»ðŸ‡ª", // Venezuela

            // Europe & Africa
            64 => "ðŸ‡¦ðŸ‡±", // Albania
            65 => "ðŸ‡¦ðŸ‡º", // Australia
            66 => "ðŸ‡¦ðŸ‡¹", // Austria
            67 => "ðŸ‡§ðŸ‡ª", // Belgium
            68 => "ðŸ‡§ðŸ‡¦", // Bosnia and Herzegovina
            69 => "ðŸ‡§ðŸ‡¼", // Botswana
            70 => "ðŸ‡§ðŸ‡¬", // Bulgaria
            71 => "ðŸ‡­ðŸ‡·", // Croatia
            72 => "ðŸ‡¨ðŸ‡¾", // Cyprus
            73 => "ðŸ‡¨ðŸ‡¿", // Czech Republic
            74 => "ðŸ‡©ðŸ‡°", // Denmark
            75 => "ðŸ‡ªðŸ‡ª", // Estonia
            76 => "ðŸ‡«ðŸ‡®", // Finland
            77 => "ðŸ‡«ðŸ‡·", // France
            78 => "ðŸ‡©ðŸ‡ª", // Germany
            79 => "ðŸ‡¬ðŸ‡·", // Greece
            80 => "ðŸ‡­ðŸ‡º", // Hungary
            81 => "ðŸ‡®ðŸ‡¸", // Iceland
            82 => "ðŸ‡®ðŸ‡ª", // Ireland
            83 => "ðŸ‡®ðŸ‡¹", // Italy
            84 => "ðŸ‡±ðŸ‡»", // Latvia
            85 => "ðŸ‡±ðŸ‡¸", // Lesotho
            86 => "ðŸ‡±ðŸ‡®", // Liechtenstein
            87 => "ðŸ‡±ðŸ‡¹", // Lithuania
            88 => "ðŸ‡±ðŸ‡º", // Luxembourg
            89 => "ðŸ‡²ðŸ‡°", // North Macedonia
            90 => "ðŸ‡²ðŸ‡¹", // Malta
            91 => "ðŸ‡²ðŸ‡ª", // Montenegro
            92 => "ðŸ‡²ðŸ‡¿", // Mozambique
            93 => "ðŸ‡³ðŸ‡¦", // Namibia
            94 => "ðŸ‡³ðŸ‡±", // Netherlands
            95 => "ðŸ‡³ðŸ‡¿", // New Zealand
            96 => "ðŸ‡³ðŸ‡´", // Norway
            97 => "ðŸ‡µðŸ‡±", // Poland
            98 => "ðŸ‡µðŸ‡¹", // Portugal
            99 => "ðŸ‡·ðŸ‡´", // Romania
            100 => "ðŸ‡·ðŸ‡º", // Russia
            101 => "ðŸ‡·ðŸ‡¸", // Serbia
            102 => "ðŸ‡¸ðŸ‡°", // Slovakia
            103 => "ðŸ‡¸ðŸ‡®", // Slovenia
            104 => "ðŸ‡¿ðŸ‡¦", // South Africa
            105 => "ðŸ‡ªðŸ‡¸", // Spain
            106 => "ðŸ‡¸ðŸ‡¿", // Eswatini
            107 => "ðŸ‡¸ðŸ‡ª", // Sweden
            108 => "ðŸ‡¨ðŸ‡­", // Switzerland
            109 => "ðŸ‡¹ðŸ‡·", // Turkey
            110 => "ðŸ‡¬ðŸ‡§", // United Kingdom
            111 => "ðŸ‡¿ðŸ‡²", // Zambia
            112 => "ðŸ‡¿ðŸ‡¼", // Zimbabwe
            113 => "ðŸ‡¦ðŸ‡¿", // Azerbaijan
            114 => "ðŸ‡²ðŸ‡·", // Mauritania
            115 => "ðŸ‡²ðŸ‡±", // Mali
            116 => "ðŸ‡³ðŸ‡ª", // Niger
            117 => "ðŸ‡¹ðŸ‡©", // Chad
            118 => "ðŸ‡¸ðŸ‡©", // Sudan
            119 => "ðŸ‡ªðŸ‡·", // Eritrea
            120 => "ðŸ‡©ðŸ‡¯", // Djibouti
            121 => "ðŸ‡¸ðŸ‡´", // Somalia

            // Southeast Asia
            128 => "ðŸ‡¹ðŸ‡¼", // Taiwan
            136 => "ðŸ‡°ðŸ‡·", // South Korea
            144 => "ðŸ‡­ðŸ‡°", // Hong Kong
            145 => "ðŸ‡²ðŸ‡´", // Macao
            152 => "ðŸ‡®ðŸ‡©", // Indonesia
            153 => "ðŸ‡¸ðŸ‡¬", // Singapore
            154 => "ðŸ‡¹ðŸ‡­", // Thailand
            155 => "ðŸ‡µðŸ‡­", // Philippines
            156 => "ðŸ‡²ðŸ‡¾", // Malaysia
            160 => "ðŸ‡¨ðŸ‡³", // China

            // Middle East
            168 => "ðŸ‡¦ðŸ‡ª", // U.A.E.
            169 => "ðŸ‡®ðŸ‡³", // India
            170 => "ðŸ‡ªðŸ‡¬", // Egypt
            171 => "ðŸ‡´ðŸ‡²", // Oman
            172 => "ðŸ‡¶ðŸ‡¦", // Qatar
            173 => "ðŸ‡°ðŸ‡¼", // Kuwait
            174 => "ðŸ‡¸ðŸ‡¦", // Saudi Arabia
            175 => "ðŸ‡¸ðŸ‡¾", // Syria
            176 => "ðŸ‡§ðŸ‡­", // Bahrain
            177 => "ðŸ‡¯ðŸ‡´", // Jordan

            _ => "ðŸ³ï¸",
        };
    }
}
