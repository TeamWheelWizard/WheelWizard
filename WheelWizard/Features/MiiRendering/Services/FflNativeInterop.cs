using System.Runtime.InteropServices;

namespace WheelWizard.MiiRendering.Services;

internal static class FflNativeInterop
{
    internal const int FflCullNone = 0;
    internal const int FflCullBack = 1;
    internal const int FflCullFront = 2;

    internal const int PrimitiveTriangles = 0x0004;
    internal const int PrimitiveTriangleStrip = 0x0005;

    internal const uint CommonColorEnableMask = 0x80000000;

    internal const int ModulateTypeShapeFaceline = 0;
    internal const int ModulateTypeShapeBeard = 1;
    internal const int ModulateTypeShapeNose = 2;
    internal const int ModulateTypeShapeForehead = 3;
    internal const int ModulateTypeShapeHair = 4;
    internal const int ModulateTypeShapeCap = 5;
    internal const int ModulateTypeShapeMask = 6;
    internal const int ModulateTypeShapeNoseLine = 7;
    internal const int ModulateTypeShapeGlass = 8;
    internal const int ModulateTypeCustomBody = 9;
    internal const int ModulateTypeCustomPants = 10;

    internal const int ParameterModeColor = 0;
    internal const int ParameterModeDefault1 = 1;
    internal const int ParameterModeDefault2 = 2;

    internal const int SpecularModeBlinn = 0;
    internal const int SpecularModeAnisotropic = 1;

    internal const int FflExpressionNormal = 0;
    internal const int FflExpressionOpenMouth = 6;
    internal const int FflExpressionSmile = 1;
    internal const int FflExpressionSmileOpenMouth = 7;
    internal const int FflExpressionFrustrated = 18;
    internal const int FflExpressionAnger = 2;
    internal const int FflExpressionAngerOpenMouth = 8;
    internal const int FflExpressionBlink = 5;
    internal const int FflExpressionBlinkOpenMouth = 11;
    internal const int FflExpressionSorrow = 3;
    internal const int FflExpressionSorrowOpenMouth = 9;
    internal const int FflExpressionSurprise = 4;
    internal const int FflExpressionSurpriseOpenMouth = 10;
    internal const int FflExpressionWinkRight = 13;
    internal const int FflExpressionWinkLeft = 12;
    internal const int FflExpressionLikeWinkLeft = 16;
    internal const int FflExpressionLikeWinkRight = 17;
    internal const int FflExpressionWinkLeftOpenMouth = 14;
    internal const int FflExpressionWinkRightOpenMouth = 15;

    internal const int TextureFormatR8 = 0;
    internal const int TextureFormatRg8 = 1;
    internal const int TextureFormatRgba8 = 2;

    internal static readonly IntPtr FflTexturePlaceholder = new(1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLColor
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLAttributeBuffer
    {
        public uint size;
        public uint stride;
        public IntPtr ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLAttributeBufferParam
    {
        public FFLAttributeBuffer position;
        public FFLAttributeBuffer texcoord;
        public FFLAttributeBuffer normal;
        public FFLAttributeBuffer tangent;
        public FFLAttributeBuffer color;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLModulateParam
    {
        public int mode;
        public int type;
        public IntPtr pColorR;
        public IntPtr pColorG;
        public IntPtr pColorB;
        public IntPtr pTexture2D;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLPrimitiveParam
    {
        public uint primitiveType;
        public uint indexCount;
        public uint reserved;
        public IntPtr pIndexBuffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLDrawParam
    {
        public FFLAttributeBufferParam attributeBufferParam;
        public FFLModulateParam modulateParam;
        public int cullMode;
        public FFLPrimitiveParam primitiveParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLiCharInfoParts
    {
        public int faceType;
        public int facelineColor;
        public int faceLine;
        public int faceMakeup;
        public int hairType;
        public int hairColor;
        public int hairDir;
        public int eyeType;
        public int eyeColor;
        public int eyeScale;
        public int eyeScaleY;
        public int eyeRotate;
        public int eyeSpacingX;
        public int eyePositionY;
        public int eyebrowType;
        public int eyebrowColor;
        public int eyebrowScale;
        public int eyebrowScaleY;
        public int eyebrowRotate;
        public int eyebrowSpacingX;
        public int eyebrowPositionY;
        public int noseType;
        public int noseScale;
        public int nosePositionY;
        public int mouthType;
        public int mouthColor;
        public int mouthScale;
        public int mouthScaleY;
        public int mouthPositionY;
        public int mustacheType;
        public int beardType;
        public int beardColor;
        public int mustacheScale;
        public int mustachePositionY;
        public int glassType;
        public int glassColor;
        public int glassScale;
        public int glassPositionY;
        public int moleType;
        public int moleScale;
        public int molePositionX;
        public int molePositionY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FFLiCharInfo
    {
        public int miiVersion;
        public FFLiCharInfoParts parts;
        public int height;
        public int build;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public ushort[] name;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public ushort[] creatorName;

        public int gender;
        public int birthMonth;
        public int birthDay;
        public int favoriteColor;

        public byte favoriteMii;
        public byte copyable;
        public byte ngWord;
        public byte localOnly;

        public int regionMove;
        public int fontRegion;
        public int pageIndex;
        public int slotIndex;
        public int birthPlatform;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public byte[] createId;

        public ushort padding;
        public int authorType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] authorId;
    }
}
