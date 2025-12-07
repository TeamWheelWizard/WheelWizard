namespace WheelWizard.WiiManagement.GameLicense.Domain.Statistics;

public enum CupTrophyType
{
    Gold = 0,
    Silver = 1,
    Bronze = 2,
    None = 3,
}

public enum CupRank
{
    ThreeStars = 0,
    TwoStars = 1,
    OneStar = 2,
    A = 3,
    B = 4,
    C = 5,
    D = 6,
    E = 7,
    F = 8,
}

// The 10 battle stages in the order they appear in RKPD (0x1A6 onward).
public enum Stage : byte
{
    DelfinoPier = 0,
    BlockPlaza,
    ChainChompWheel,
    FunkyStadium,
    ThwompDesert,
    GCNCookieLand,
    DSTwilightHouse,
    SNESBattleCourse4,
    GBABattleCourse3,
    N64Skyscraper,
}

public enum Course : byte
{
    MarioCircuit = 0,
    MooMooMeadows,
    MushroomGorge,
    GrumbleVolcano,
    ToadsFactory,
    CoconutMall,
    DKSummit,
    WariosGoldMine,
    LuigiCircuit,
    DaisyCircuit,
    MoonviewHighway,
    MapleTreeway,
    BowserCastle,
    RainbowRoad,
    DryDryRuins,
    KoopaCape,
    GCNPeachBeach,
    GCNMarioCircuit,
    GCNWaluigiStadium,
    GCNDKMountain,
    DSYoshiFalls,
    DSDesertHills,
    DSPeachGardens,
    DSDelfinoSquare,
    SNESMarioCircuit3,
    SNESGhostValley2,
    N64MarioRaceway,
    N64SherbetLand,
    N64BowsersCastle,
    N64DKsJungleParkway,
    GBABowserCastle3,
    GBAShyGuyBeach,
}

public enum Vehicle : byte
{
    StandardKartS = 0,
    StandardKartM,
    StandardKartL,
    BoosterSeat,
    ClassicDragster,
    Offroader,
    MiniBeast,
    WildWing,
    FlameFlyer,
    CheepCharger,
    SuperBlooper,
    PiranhaProwler,
    TinyTitan,
    Daytripper,
    Jetsetter,
    BlueFalcon,
    Sprinter,
    Honeycoupe,
    StandardBikeS,
    StandardBikeM,
    StandardBikeL,
    BulletBike,
    MachBike,
    FlameRunner,
    BitBike,
    Sugarscoot,
    WarioBike,
    Quacker,
    ZipZip,
    ShootingStar,
    Magikruiser,
    Sneakster,
    Spear,
    JetBubble,
    DolphinDasher,
    Phantom,
}

// The 24 playable characters in the order they appear in RKPD (0xEC onward).
public enum Character : byte
{
    Mario = 0,
    BabyPeach,
    Waluigi,
    Bowser,
    BabyDaisy,
    DryBones,
    BabyMario,
    Luigi,
    Toad,
    DonkeyKong,
    Yoshi,
    Wario,
    BabyLuigi,
    Toadette,
    KoopaTroopa,
    Daisy,
    Peach,
    Birdo,
    DiddyKong,
    KingBoo,
    BowserJr,
    DryBowser,
    FunkyKong,
    Rosalina,
    Mii = 23,
}

public enum DriftType
{
    Standard = 0,
    Manual = 1,
    Automatic = 2,
}
