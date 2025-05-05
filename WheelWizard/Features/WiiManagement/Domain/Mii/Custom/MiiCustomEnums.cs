namespace WheelWizard.WiiManagement.Domain.Mii.Custom;

//2 bits so only 4 values
public enum MiiPreferredCameraAngle : uint
{
    None = 0,
    CameraAngle1 = 1,
    CameraAngle2 = 2,
    CameraAngle3 = 3,
}

//this value is only stored in 3 bits, so there can be only 8 values
public enum MiiPreferredFacialExpression : uint
{
    None = 0,
    FacialExpression1 = 1,
    FacialExpression2 = 2,
    FacialExpression3 = 3,
    FacialExpression4 = 4,
    FacialExpression5 = 5,
    FacialExpression6 = 6,
    FacialExpression7 = 7,
}

/// <summary>
/// Enumeration representing the color of a Mii profile.
/// This only takes up 4 bits, so in total 15 colors are possible. (+1 for none)
/// </summary>
public enum MiiProfileColor : uint
{
    None = 0,
    Color1 = 1,
    Color2 = 2,
    Color3 = 3,
    Color4 = 4,
    Color5 = 5,
    Color6 = 6,
    Color7 = 7,
    Color8 = 8,
    Color9 = 9,
    Color10 = 10,
    Color11 = 11,
    Color12 = 12,
    Color13 = 13,
    Color14 = 14,
    Color15 = 15,
}

//this is 5 bits so it can be 0-31
public enum MiiPreferredTagline
{
    None = 0,
    Tagline1 = 1,
    Tagline2 = 2,
    Tagline3 = 3,
    Tagline4 = 4,
    Tagline5 = 5,
    Tagline6 = 6,
    Tagline7 = 7,
    Tagline8 = 8,
    Tagline9 = 9,
    Tagline10 = 10,
    Tagline11 = 11,
    Tagline12 = 12,
    Tagline13 = 13,
    Tagline14 = 14,
    Tagline15 = 15,
    Tagline16 = 16,
    Tagline17 = 17,
    Tagline18 = 18,
    Tagline19 = 19,
    Tagline20 = 20,
    Tagline21 = 21,
    Tagline22 = 22,
    Tagline23 = 23,
    Tagline24 = 24,
    Tagline25 = 25,
    Tagline26 = 26,
    Tagline27 = 27,
    Tagline28 = 28,
    Tagline29 = 29,
    Tagline30 = 30,
    Tagline31 = 31,
}
