using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Models.RRInfo;

public class RrRoom
{
    public required string Id { get; set; }
    public required DateTime Created { get; set; }
    public required string Type { get; set; }
    public required bool Suspend { get; set; }
    public string? Rk { get; set; } // RK does not exists in private rooms
    public required List<RrPlayer> Players { get; set; }

    public int PlayerCount => Players.Count;

    public string TimeOnline => tTime(DateTime.UtcNow - Created);
    public bool IsPublic => Type != "private";

    public string GameModeAbbrev =>
        Rk switch
        {
            // Retro Rewind
            "vs_10" => "RR",
            "vs_11" => "TT",
            "vs_12" => "200",
            "vs_13" => "IR",
            "vs_14" => "BT",
            "vs_15" => "EBT",
            "vs_20" => "RR Ct",
            "vs_21" => "RR V",

            // CTGP_C
            "vs_668" => "CTGP-C",

            // Insane Kart Wii
            "vs_69" => "IKW",
            "vs_70" => "Ultras",
            "vs_71" => "Crazy",
            "vs_72" => "Bomb",
            "vs_73" => "Accel",
            "vs_74" => "Banana",
            "vs_75" => "RndItm",
            "vs_76" => "Unfair",
            "vs_77" => "Blue",
            "vs_78" => "Shroom",
            "vs_79" => "Bumper",
            "vs_80" => "Rampage",
            "vs_81" => "Rain",
            "vs_82" => "Break",
            "vs_83" => "Riibal",

            // Luminous
            "vs_666" => "Lumi",
            "vs_667" => "Lumi TT",

            // OptPack
            "vs_875" => "OP 150",
            "vs_876" => "OP TT",
            "vs_877" => "OP R1",
            "vs_878" => "OP R2",
            "vs_879" => "OP R3",
            "vs_880" => "OP R4",

            // WTP
            "vs_1312" => "WTP 150",
            "vs_1313" => "WTP 200",
            "vs_1314" => "WTP TT",
            "vs_1315" => "WTP IR",
            "vs_1316" => "STYD",

            // Generic Versus
            "vs_751" => "VS",
            "vs_-1" => "Reg",
            "vs" => "Reg",

            _ => IsPublic ? "??" : "Lock",
        };

    public string GameMode =>
        Rk switch
        {
            //Max Size:"----------------------"
            // Retro Rewind
            "vs_10" => t("game_mode.rr_150cc"),
            "vs_11" => t("game_mode.rr_tt"),
            "vs_12" => t("game_mode.rr_200cc"),
            "vs_13" => t("game_mode.rr_item_rain"),
            "vs_14" => t("game_mode.rr_battle"),
            "vs_15" => t("game_mode.rr_battle_elim"),
            "vs_20" => t("game_mode.rr_150cc_ct"),
            "vs_21" => t("game_mode.rr_vanilla"),

            // CTGP
            "vs_668" => t("game_mode.ctgp_c"),

            // Insane Kart Wii
            "vs_69" => t("game_mode.insane_kart"),
            "vs_70" => t("game_mode.insane_ultras_vs"),
            "vs_71" => t("game_mode.insane_crazy_items"),
            "vs_72" => t("game_mode.insane_bob_omb"),
            "vs_73" => t("game_mode.insane_inf_accel"),
            "vs_74" => t("game_mode.insane_banana_slip"),
            "vs_75" => t("game_mode.insane_rand_items"),
            "vs_76" => t("game_mode.insane_unfair_items"),
            "vs_77" => t("game_mode.insane_blue_madness"),
            "vs_78" => t("game_mode.insane_mush_dash"),
            "vs_79" => t("game_mode.insane_bumper_karts"),
            "vs_80" => t("game_mode.insane_item_rampage"),
            "vs_81" => t("game_mode.insane_item_rain"),
            "vs_82" => t("game_mode.insane_shell_break"),
            "vs_83" => t("game_mode.insane_riibalanced"),

            // Luminous
            "vs_666" => t("game_mode.luminous"),
            "vs_667" => t("game_mode.luminous_tt"),

            // OptPack
            "vs_875" => t("game_mode.opt_150cc"),
            "vs_876" => t("game_mode.opt_tt"),
            "vs_877" => t("game_mode.opt_r1"),
            "vs_878" => t("game_mode.opt_r2"),
            "vs_879" => t("game_mode.opt_r3"),
            "vs_880" => t("game_mode.opt_r4"),

            // WTP
            "vs_1312" => t("game_mode.wtp_150cc"),
            "vs_1313" => t("game_mode.wtp_200cc"),
            "vs_1314" => t("game_mode.wtp_tt"),
            "vs_1315" => t("game_mode.wtp_item_rain"),
            "vs_1316" => t("game_mode.wtp_styd"),

            // Generic
            "vs_751" => t("game_mode.generic_vs"),
            "vs_-1" => t("game_mode.generic_regular"),
            "vs" => t("game_mode.generic_regular"),
            _ => IsPublic ? t("game_mode.unknown") : t("game_mode.private_room"),
        };

    public int AverageVr
    {
        get
        {
            var vrs = Players.Select(p => p.Vr).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return vrs.Count == 0 ? 0 : (int)vrs.Average();
        }
    }

    public RrPlayer? HostPlayer => Players.FirstOrDefault(p => p.IsOpenHost);

    public Mii? HostMii => HostPlayer?.FirstMii;
}
