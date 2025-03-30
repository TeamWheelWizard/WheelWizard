﻿using System.Text;

namespace WheelWizard.WiiManagement.Domain.Enums;

public enum MiiFavoriteColor { Red, Orange, Yellow, Green, Blue, LightBlue, Pink, Purple, Brown, White, Black, Gray }

public class MiiName
{
    private readonly string _value;
    private MiiName(string value)
    {
        _value = value;
    }
    public static OperationResult<MiiName> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) 
            return OperationResult.Fail<MiiName>("Mii name cannot be empty");
        
        if (value.Length > 10) 
            return OperationResult.Fail<MiiName>("Mii name too long");
        
        return OperationResult.Ok(new MiiName(value));
    }

    public byte[] ToBytes() => Encoding.BigEndianUnicode.GetBytes(_value.PadRight(10, '\0'));

    public static MiiName FromBytes(byte[] data, int offset) =>
        new(Encoding.BigEndianUnicode.GetString(data, offset, 20).TrimEnd('\0'));
    public override string ToString() => _value;
}


public class MiiScale
{
    public byte Value { get; }

    private MiiScale(byte value) => Value = value;

    public static OperationResult<MiiScale> Create(byte value)
    {
        if (value > 127)
            return new OperationError() { Message = "Scale must be between 0 and 127." };

        return OperationResult.Ok(new MiiScale(value));
    }
}

public class MiiFacialFeatures
{
    public int FaceShape { get; }
    public int SkinColor { get; }
    public int FacialFeature { get; }
    public bool MingleOff { get; }
    public bool Downloaded { get; }

    private MiiFacialFeatures(int faceShape, int skinColor, int facialFeature, bool mingleOff, bool downloaded)
    {
        FaceShape = faceShape;
        SkinColor = skinColor;
        FacialFeature = facialFeature;
        MingleOff = mingleOff;
        Downloaded = downloaded;
    }

    public static OperationResult<MiiFacialFeatures> Create(int faceShape, int skinColor, int facialFeature, bool mingleOff, bool downloaded)
    {
        if (faceShape is < 0 or > 7)
            return OperationResult.Fail<MiiFacialFeatures>("FaceShape out of range");
        if (skinColor is < 0 or > 5)
            return OperationResult.Fail<MiiFacialFeatures>("SkinColor out of range");
        if (facialFeature is < 0 or > 11)
            return OperationResult.Fail<MiiFacialFeatures>("FacialFeature out of range");

        return OperationResult.Ok(new MiiFacialFeatures(faceShape, skinColor, facialFeature, mingleOff, downloaded));
    }
}

public class MiiHair
{
    public int HairType { get; }
    public int HairColor { get; }
    public bool HairFlipped { get; }

    private MiiHair(int hairType, int hairColor, bool hairFlipped)
    {
        HairType = hairType;
        HairColor = hairColor;
        HairFlipped = hairFlipped;
    }

    public static OperationResult<MiiHair> Create(int hairType, int hairColor, bool hairFlipped)
    {
        if (hairType is < 0 or > 71)
            return OperationResult.Fail<MiiHair>("HairType out of range");
        if (hairColor is < 0 or > 7)
            return OperationResult.Fail<MiiHair>("HairColor out of range");

        return OperationResult.Ok(new MiiHair(hairType, hairColor, hairFlipped));
    }
}


public class MiiEyebrow
{
    public int Type { get; }
    public int Rotation { get; }
    public int Color { get; }
    public int Size { get; }
    public int Vertical { get; }
    public int Spacing { get; }

    private MiiEyebrow(int type, int rotation, int color, int size, int vertical, int spacing)
    {
        Type = type;
        Rotation = rotation;
        Color = color;
        Size = size;
        Vertical = vertical;
        Spacing = spacing;
    }

    public static OperationResult<MiiEyebrow> Create(int type, int rotation, int color, int size, int vertical, int spacing)
    {
        if (type is < 0 or > 23)
            return OperationResult.Fail<MiiEyebrow>("Eyebrow type invalid");
        if (rotation is < 0 or > 11)
            return OperationResult.Fail<MiiEyebrow>("Rotation invalid");
        if (color is < 0 or > 7)
            return OperationResult.Fail<MiiEyebrow>("Color invalid");
        if (size is < 0 or > 8)
            return OperationResult.Fail<MiiEyebrow>("Size invalid");
        if (vertical is < 0 or > 18)
            return OperationResult.Fail<MiiEyebrow>("Vertical position invalid");
        if (spacing is < 0 or > 12)
            return OperationResult.Fail<MiiEyebrow>("Spacing invalid");

        return OperationResult.Ok(new MiiEyebrow(type, rotation, color, size, vertical, spacing));
    }
}


public class MiiEye
{
    public int Type { get; }
    public int Rotation { get; }
    public int Vertical { get; }
    public int Color { get; }
    public int Size { get; }
    public int Spacing { get; }

    private MiiEye(int type, int rotation, int vertical, int color, int size, int spacing)
    {
        Type = type;
        Rotation = rotation;
        Vertical = vertical;
        Color = color;
        Size = size;
        Spacing = spacing;
    }

    public static OperationResult<MiiEye> Create(int type, int rotation, int vertical, int color, int size, int spacing)
    {
        if (type is < 0 or > 47) return OperationResult.Fail<MiiEye>("Eye type invalid");
        if (rotation is < 0 or > 7) return OperationResult.Fail<MiiEye>("Rotation invalid");
        if (vertical is < 0 or > 18) return OperationResult.Fail<MiiEye>("Vertical position invalid");
        if (color is < 0 or > 5) return OperationResult.Fail<MiiEye>("Color invalid");
        if (size is < 0 or > 7) return OperationResult.Fail<MiiEye>("Size invalid");
        if (spacing is < 0 or > 12) return OperationResult.Fail<MiiEye>("Spacing invalid");

        return OperationResult.Ok(new MiiEye(type, rotation, vertical, color, size, spacing));
    }
}

public class MiiNose
{
    public int Type { get; }
    public int Size { get; }
    public int Vertical { get; }

    private MiiNose(int type, int size, int vertical)
    {
        Type = type;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiNose> Create(int type, int size, int vertical)
    {
        if (type is < 0 or > 11) return OperationResult.Fail<MiiNose>("Nose type invalid");
        if (size is < 0 or > 8) return OperationResult.Fail<MiiNose>("Nose size invalid");
        if (vertical is < 0 or > 18) return OperationResult.Fail<MiiNose>("Nose vertical position invalid");

        return OperationResult.Ok(new MiiNose(type, size, vertical));
    }
}


public class MiiLip
{
    public int Type { get; }
    public int Color { get; }
    public int Size { get; }
    public int Vertical { get; }

    private MiiLip(int type, int color, int size, int vertical)
    {
        Type = type;
        Color = color;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiLip> Create(int type, int color, int size, int vertical)
    {
        if (type is < 0 or > 23) return OperationResult.Fail<MiiLip>("Lip type invalid");
        if (color is < 0 or > 2) return OperationResult.Fail<MiiLip>("Lip color invalid");
        if (size is < 0 or > 8) return OperationResult.Fail<MiiLip>("Lip size invalid");
        if (vertical is < 0 or > 18) return OperationResult.Fail<MiiLip>("Lip vertical position invalid");

        return OperationResult.Ok(new MiiLip(type, color, size, vertical));
    }
}


public class MiiGlasses
{
    public int Type { get; }
    public int Color { get; }
    public int Size { get; }
    public int Vertical { get; }

    private MiiGlasses(int type, int color, int size, int vertical)
    {
        Type = type;
        Color = color;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiGlasses> Create(int type, int color, int size, int vertical)
    {
        if (type is < 0 or > 8) return OperationResult.Fail<MiiGlasses>("Glasses type invalid");
        if (color is < 0 or > 5) return OperationResult.Fail<MiiGlasses>("Glasses color invalid");
        if (size is < 0 or > 7) return OperationResult.Fail<MiiGlasses>("Glasses size invalid");
        if (vertical is < 0 or > 20) return OperationResult.Fail<MiiGlasses>("Glasses vertical position invalid");

        return OperationResult.Ok(new MiiGlasses(type, color, size, vertical));
    }
}
public class MiiFacialHair
{
    public int MustacheType { get; }
    public int BeardType { get; }
    public int Color { get; }
    public int Size { get; }
    public int Vertical { get; }

    private MiiFacialHair(int mustacheType, int beardType, int color, int size, int vertical)
    {
        MustacheType = mustacheType;
        BeardType = beardType;
        Color = color;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiFacialHair> Create(int mustacheType, int beardType, int color, int size, int vertical)
    {
        if (mustacheType is < 0 or > 3) return OperationResult.Fail<MiiFacialHair>("Mustache type invalid");
        if (beardType is < 0 or > 3) return OperationResult.Fail<MiiFacialHair>("Beard type invalid");
        if (color is < 0 or > 7) return OperationResult.Fail<MiiFacialHair>("Facial hair color invalid");
        if (size is < 0 or > 8) return OperationResult.Fail<MiiFacialHair>("Facial hair size invalid");
        if (vertical is < 0 or > 16) return OperationResult.Fail<MiiFacialHair>("Facial hair vertical position invalid");

        return OperationResult.Ok(new MiiFacialHair(mustacheType, beardType, color, size, vertical));
    }
}

public class MiiMole
{
    public bool Exists { get; }
    public int Size { get; }
    public int Vertical { get; }
    public int Horizontal { get; }

    private MiiMole(bool exists, int size, int vertical, int horizontal)
    {
        Exists = exists;
        Size = size;
        Vertical = vertical;
        Horizontal = horizontal;
    }

    public static OperationResult<MiiMole> Create(bool exists, int size, int vertical, int horizontal)
    {
        if (size is < 0 or > 8) return OperationResult.Fail<MiiMole>("Mole size invalid");
        if (vertical is < 0 or > 30) return OperationResult.Fail<MiiMole>("Mole vertical position invalid");
        if (horizontal is < 0 or > 16) return OperationResult.Fail<MiiMole>("Mole horizontal position invalid");

        return OperationResult.Ok(new MiiMole(exists, size, vertical, horizontal));
    }
}
