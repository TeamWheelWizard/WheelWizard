namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

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
    None = 0, //Off
    Tagline1 = 1, // Hey there! I am using WheelWizard. (default)
    Tagline2 = 2, // "hello world"
    Tagline3 = 3, // game on!
    Tagline4 = 4, // gotta go fast
    Tagline5 = 5, // UwU
    Tagline6 = 6, // You found me!
    Tagline7 = 7, // how did we get here?
    Tagline8 = 8, // return to sender
    Tagline9 = 9, // loading...
    Tagline10 = 10, // Funky FTW
    Tagline11 = 11, // I'm beefbai
    Tagline12 = 12, // Tonight... We steal the moon!
    Tagline13 = 13, // ok
    Tagline14 = 14, // BRB, grabbing snacks
    Tagline15 = 15, // Ctrl + Alt + Mii
    Tagline16 = 16, // Mii think therefore Mii am
    Tagline17 = 17, // To Mii or not to Mii, that is the question
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
