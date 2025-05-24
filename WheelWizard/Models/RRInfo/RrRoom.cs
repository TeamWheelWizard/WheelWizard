using WheelWizard.Helpers;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Models.RRInfo;

public class RrRoom
{
    public required string Id { get; set; }
    public string? Game { get; set; } // it always exists, but we dont care since we dont use it (and its always "mariokartwii")
    public required DateTime Created { get; set; }
    public required string Type { get; set; }
    public required bool Suspend { get; set; }
    public string? Host { get; set; } // the key of player in the players map (that started the room)
    public string? Rk { get; set; } // RK does not exists in private rooms
    public required Dictionary<string, RrPlayer> Players { get; set; }

    public int PlayerCount => Players.Sum(p => p.Value.PlayerCount);

    public string TimeOnline => Humanizer.HumanizeTimeSpan(DateTime.UtcNow - Created);
    public bool IsPublic => Type != "private";

    public string GameModeAbbrev =>
        Rk switch
        {
            // Retro Rewind
            "vs_10"   => "RR",
            "vs_11"   => "TT",
            "vs_12"   => "200",
            "vs_20"   => "RR Ct",
            "vs_21"   => "TT Ct",
            "vs_22"   => "200 Ct",
    
            // CTGP_C
            "vs_668"  => "CTGP-C",
    
            // Insane Kart Wii
            "vs_69"   => "IKW",
            "vs_70"   => "Ultras",
            "vs_71"   => "Crazy",
            "vs_72"   => "Bomb",
            "vs_73"   => "Accel",
            "vs_74"   => "Banana",
            "vs_75"   => "RndItm",
            "vs_76"   => "Unfair",
            "vs_77"   => "Blue",
            "vs_78"   => "Shroom",
            "vs_79"   => "Bumper",
            "vs_80"   => "Rampage",
            "vs_81"   => "Rain",
            "vs_82"   => "Break",
            "vs_83"   => "Riibal",
    
            // Luminous
            "vs_666"  => "Lumi",
    
            // OptPack
            "vs_875"  => "OP 150",
            "vs_876"  => "OP TT",
            "vs_877"  => "OP R1",
            "vs_878"  => "OP R2",
            "vs_879"  => "OP R3",
            "vs_880"  => "OP R4",
    
            // WTP
            "vs_1312" => "WTP 150",
            "vs_1313" => "WTP 200",
            "vs_1314" => "WTP TT",
    
            // Generic Versus
            "vs_751"  => "VS",
            "vs_-1"   => "Reg",
            "vs"      => "Reg",
    
            _ => IsPublic ? "??" : "Lock",
        };

public string GameMode =>
    Rk switch
    {
        //Max Size:"----------------------"
        // Retro Rewind
        "vs_10"   => "RR 150CC",
        "vs_11"   => "RR Time Tr",
        "vs_12"   => "RR 200CC",
        "vs_20"   => "RR 150CC CTs",
        "vs_21"   => "RR TT CTs",
        "vs_22"   => "RR 200CC CTs",

        // CTGP
        "vs_668"  => "CTGP-C",

        // Insane Kart Wii
        "vs_69"   => "Insane Kart",
        "vs_70"   => "Ultras VS",
        "vs_71"   => "Crazy Items",
        "vs_72"   => "Bob-omb Blast",
        "vs_73"   => "Inf Accel",
        "vs_74"   => "Banan Slip",
        "vs_75"   => "Rand Items",
        "vs_76"   => "Unfair Items",
        "vs_77"   => "Blue Madness",
        "vs_78"   => "Mush Dash",
        "vs_79"   => "Bumper Karts",
        "vs_80"   => "Item Rampage",
        "vs_81"   => "Item Rain",
        "vs_82"   => "Shell Break",
        "vs_83"   => "Riibalanced",

        // Luminous
        "vs_666"  => "Luminous",

        // OptPack
        "vs_875"  => "OP 150",
        "vs_876"  => "OP TT",
        "vs_877"  => "OP R1",
        "vs_878"  => "OP R2",
        "vs_879"  => "OP R3",
        "vs_880"  => "OP R4",


        // WTP
        "vs_1312" => "WTP 150CC",
        "vs_1313" => "WTP 200CC",
        "vs_1314" => "WTP Time Trial",

        // Generic
        "vs_751"  => "Versus",
        "vs_-1"   => "Regular",
        "vs"      => "Regular",
        _ => IsPublic ? "Unknown Mode" : "Private Room",
    };

    public int AverageVr => PlayerCount == 0 ? 0 : Players.Sum(p => p.Value.Vr) / PlayerCount;

    public Mii? HostMii => !string.IsNullOrEmpty(Host) ? Players.GetValueOrDefault(Host)?.FirstMii : null;
}
