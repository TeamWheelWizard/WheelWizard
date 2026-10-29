namespace WheelWizard.WheelWizardData.Domain;

/// <summary>
/// Badge ids as returned by the RWFC API. The numeric values mirror wfc-bot's BadgeType
/// (src/commands/shared/badges.ts) and rwfc-web's BadgeId, so they must never be reordered.
/// </summary>
public enum BadgeVariant
{
    // None is not a real badge id, so it sits outside the RWFC id range.
    None = -1,

    RetroRewindDeveloper = 0,
    WheelWizardDeveloper = 1,
    MajorContributor = 2,

    RWFCModerator = 100,
    DiscordStaff = 101,

    Contributor = 1000,
    Translator = 1001,
    Supporter = 1002,
    BetaTester = 1003,
    Heart = 1004,

    FireStarterGold = 2000,
    FireStarterSilver = 2001,
    FireStarterBronze = 2002,
    LeafStruckGold = 2003,
    LeafStruckSilver = 2004,
    LeafStruckBronze = 2005,
    SummitShowdownGold = 2006,
    SummitShowdownSilver = 2007,
    SummitShowdownBronze = 2008,
    HorizonGold = 2009,
    HorizonSilver = 2010,
    HorizonBronze = 2011,
    SunblossomGold = 2012,
    SunblossomSilver = 2013,
    SunblossomBronze = 2014,
    EarthboundGold = 2015,
    EarthboundSilver = 2016,
    EarthboundBronze = 2017,
    BotBGold = 2018,
    BotBSilver = 2019,
    BotBBronze = 2020,
}
