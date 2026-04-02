using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace WheelWizard.Converters;

public class RankToMedalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int rank)
        {
            return rank switch
            {
                1 => "🥇",
                2 => "🥈", 
                3 => "🥉",
                _ => $"#{rank}"
            };
        }
        return "#?";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CountryCodeToFlagConverter : IValueConverter
{
    private static readonly Dictionary<string, string> CountryFlags = new()
    {
        { "US", "🇺🇸" }, { "GB", "🇬🇧" }, { "CA", "🇨🇦" }, { "FR", "🇫🇷" }, { "DE", "🇩🇪" },
        { "IT", "🇮🇹" }, { "ES", "🇪🇸" }, { "JP", "🇯🇵" }, { "KR", "🇰🇷" }, { "CN", "🇨🇳" },
        { "AU", "🇦🇺" }, { "BR", "🇧🇷" }, { "MX", "🇲🇽" }, { "NL", "🇳🇱" }, { "SE", "🇸🇪" },
        { "NO", "🇳🇴" }, { "DK", "🇩🇰" }, { "FI", "🇫🇮" }, { "CH", "🇨🇭" }, { "AT", "🇦🇹" },
        { "BE", "🇧🇪" }, { "PT", "🇵🇹" }, { "PL", "🇵🇱" }, { "RU", "🇷🇺" }, { "CZ", "🇨🇿" },
        { "HU", "🇭🇺" }, { "GR", "🇬🇷" }, { "TR", "🇹🇷" }, { "IL", "🇮🇱" }, { "IN", "🇮🇳" },
        { "TH", "🇹🇭" }, { "SG", "🇸🇬" }, { "MY", "🇲🇾" }, { "PH", "🇵🇭" }, { "ID", "🇮🇩" },
        { "VN", "🇻🇳" }, { "TW", "🇹🇼" }, { "HK", "🇭🇰" }, { "NZ", "🇳🇿" }, { "ZA", "🇿🇦" },
        { "EG", "🇪🇬" }, { "NG", "🇳🇬" }, { "KE", "🇰🇪" }, { "MA", "🇲🇦" }, { "AR", "🇦🇷" },
        { "CL", "🇨🇱" }, { "CO", "🇨🇴" }, { "PE", "🇵🇪" }, { "VE", "🇻🇪" }, { "UY", "🇺🇾" },
        { "EC", "🇪🇨" }, { "BO", "🇧🇴" }, { "PY", "🇵🇾" }, { "CR", "🇨🇷" }, { "PA", "🇵🇦" },
        { "GT", "🇬🇹" }, { "HN", "🇭🇳" }, { "SV", "🇸🇻" }, { "NI", "🇳🇮" }, { "BZ", "🇧🇿" },
        { "JM", "🇯🇲" }, { "TT", "🇹🇹" }, { "BB", "🇧🇧" }, { "GY", "🇬🇾" }, { "SR", "🇸🇷" },
        { "UE", "🇪🇺" }, { "IE", "🇮🇪" }, { "IS", "🇮🇸" }, { "LU", "🇱🇺" }, { "MC", "🇲🇨" },
        { "AD", "🇦🇩" }, { "SM", "🇸🇲" }, { "VA", "🇻🇦" }, { "MT", "🇲🇹" }, { "CY", "🇨🇾" }
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string countryCode && !string.IsNullOrEmpty(countryCode))
        {
            return CountryFlags.TryGetValue(countryCode.ToUpper(), out var flag) ? flag : countryCode;
        }
        return "🌍";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CharacterIdToNameConverter : IValueConverter
{
    private static readonly Dictionary<int, string> CharacterNames = new()
    {
        // https://wiki.tockdom.com/wiki/List_of_Identifiers
        { 0x00, "Mario" },
        { 0x01, "Baby Peach" },
        { 0x02, "Waluigi" },
        { 0x03, "Bowser" },
        { 0x04, "Baby Daisy" },
        { 0x05, "Dry Bones" },
        { 0x06, "Baby Mario" },
        { 0x07, "Luigi" },
        { 0x08, "Toad" },
        { 0x09, "Donkey Kong" },
        { 0x0A, "Yoshi" },
        { 0x0B, "Wario" },
        { 0x0C, "Baby Luigi" },
        { 0x0D, "Toadette" },
        { 0x0E, "Koopa Troopa" },
        { 0x0F, "Daisy" },
        { 0x10, "Peach" },
        { 0x11, "Birdo" },
        { 0x12, "Diddy Kong" },
        { 0x13, "King Boo" },
        { 0x14, "Bowser Jr." },
        { 0x15, "Dry Bowser" },
        { 0x16, "Funky Kong" },
        { 0x17, "Rosalina" },
        { 0x18, "Small Mii A (Male)" },
        { 0x19, "Small Mii A (Female)" },
        { 0x1A, "Small Mii B (Male)" },
        { 0x1B, "Small Mii B (Female)" },
        { 0x1C, "Small Mii C (Male)" },
        { 0x1D, "Small Mii C (Female)" },
        { 0x1E, "Medium Mii A (Male)" },
        { 0x1F, "Medium Mii A (Female)" },
        { 0x20, "Medium Mii B (Male)" },
        { 0x21, "Medium Mii B (Female)" },
        { 0x22, "Medium Mii C (Male)" },
        { 0x23, "Medium Mii C (Female)" },
        { 0x24, "Large Mii A (Male)" },
        { 0x25, "Large Mii A (Female)" },
        { 0x26, "Large Mii B (Male)" },
        { 0x27, "Large Mii B (Female)" },
        { 0x28, "Large Mii C (Male)" },
        { 0x29, "Large Mii C (Female)" },
        { 0x2A, "Medium Mii" },
        { 0x2B, "Small Mii" },
        { 0x2C, "Large Mii" },
        { 0x2D, "Peach" },
        { 0x2E, "Daisy" },
        { 0x2F, "Rosalina" }
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int characterId)
        {
            return CharacterNames.TryGetValue(characterId, out var name) ? name : $"Character {characterId:X2}";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class VehicleIdToNameConverter : IValueConverter
{
    private static readonly Dictionary<int, string> VehicleNames = new()
    {
        // https://wiki.tockdom.com/wiki/List_of_Identifiers
        { 0x00, "Standard Kart S" },
        { 0x01, "Standard Kart M" },
        { 0x02, "Standard Kart L" },
        { 0x03, "Booster Seat" },
        { 0x04, "Classic Dragster" },
        { 0x05, "Offroader" },
        { 0x06, "Mini Beast" },
        { 0x07, "Wild Wing" },
        { 0x08, "Flame Flyer" },
        { 0x09, "Cheep Charger" },
        { 0x0A, "Super Blooper" },
        { 0x0B, "Piranha Prowler" },
        { 0x0C, "Tiny Titan" },
        { 0x0D, "Daytripper" },
        { 0x0E, "Jetsetter" },
        { 0x0F, "Blue Falcon" },
        { 0x10, "Sprinter" },
        { 0x11, "Honeycoupe" },
        { 0x12, "Standard Bike S" },
        { 0x13, "Standard Bike M" },
        { 0x14, "Standard Bike L" },
        { 0x15, "Bullet Bike" },
        { 0x16, "Mach Bike" },
        { 0x17, "Flame Runner" },
        { 0x18, "Bit Bike" },
        { 0x19, "Sugarscoot" },
        { 0x1A, "Wario Bike" },
        { 0x1B, "Quacker" },
        { 0x1C, "Zip Zip" },
        { 0x1D, "Shooting Star" },
        { 0x1E, "Magikruiser" },
        { 0x1F, "Sneakster" },
        { 0x20, "Spear" },
        { 0x21, "Jet Bubble" },
        { 0x22, "Dolphin Dasher" },
        { 0x23, "Phantom" }
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int vehicleId)
        {
            return VehicleNames.TryGetValue(vehicleId, out var name) ? name : $"Vehicle {vehicleId:X2}";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DriftTypeToNameConverter : IValueConverter
{
    private static readonly Dictionary<int, string> DriftTypes = new()
    {
        { 0, "Manual" }, { 1, "Automatic" }
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int driftType)
        {
            return DriftTypes.TryGetValue(driftType, out var name) ? name : $"Drift {driftType}";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ControllerTypeToNameConverter : IValueConverter
{
    private static readonly Dictionary<int, string> ControllerTypes = new()
    {
        { 0, "Wii Wheel" }, { 1, "Nunchuk" }, { 2, "Classic Controller" }, 
        { 3, "GameCube Controller" }
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int controllerType)
        {
            return ControllerTypes.TryGetValue(controllerType, out var name) ? name : $"Controller {controllerType}";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LapTimesToAverageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is List<int> lapTimesMs && lapTimesMs.Count > 0)
        {
            var averageMs = lapTimesMs.Average();
            var minutes = (int)(averageMs / 60000);
            var seconds = (averageMs % 60000) / 1000;
            return $"{minutes}:{seconds:00.000}";
        }
        return "0:00.000";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}