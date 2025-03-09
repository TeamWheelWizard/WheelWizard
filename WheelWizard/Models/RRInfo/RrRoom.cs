using WheelWizard.Helpers;
using WheelWizard.Models.MiiImages;

namespace WheelWizard.Models.RRInfo;

public class RrRoom
{
    public required string Id { get; set; }
    public string? Game { get; set; } // it always exists, but we dont care since we dont use it (and its always "mariokartwii")
    public required DateTime Created { get; set; }
    public required string Type { get; set; }
    public required bool Suspend { get; set; }
    public required string Host { get; set; } // the key of player in the players map (that started the room)
    public string? Rk { get; set; } // RK does not exists in private rooms
    public required Dictionary<string, RrPlayer> Players { get; set; }

    public int PlayerCount => Players.Sum(p => p.Value.PlayerCount);

    public string TimeOnline => Humanizer.HumanizeTimeSpan(DateTime.UtcNow - Created);
    public bool IsPublic => Type != "private";
    public string GameModeAbbrev => Rk switch
    {
        "vs_10" => "RR",
        "vs_11" => "TT",
        "vs_12" => "200",
        "vs_20" => "RR Ct",
        "vs_21" => "TT Ct",
        "vs_22" => "200 Ct",
        
        "vs_668" => "CTGP",
        "vs_69" => "IKW",
        
        "vs_751" => "VS",
        "vs_-1" => "Reg",
        _ =>  IsPublic ? "??" : "Lock"
    };
    public string GameMode => Rk switch
    { 
        //Max Size:"-------------"
        "vs_10" => "RR 150CC",
        "vs_11" => "RR Time Tr",
        "vs_12" => "RR 200CC",
        "vs_20" => "RR 150CC CTs",
        "vs_21" => "RR TT CTs",
        "vs_22" => "RR 200CC CTs",
        
        "vs_668" => "CTGP-C",
        "vs_69" => "Insane Kart",
        
        "vs_751" => "Versus",
        "vs_-1" => "Regular",
        "vs" => "Regular",
        _ => IsPublic ? "Unknown Mode" : "Private Room"
    };
    public Mii? HostMii => Players.GetValueOrDefault(Host)?.FirstMii;
}
