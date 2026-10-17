using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Testably.Abstractions.Testing;
using WheelWizard.DiscImages;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.MiiRendering.FaceLibrary;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.Test.Features.DiscImages;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features.MiiRendering;

public class MiiFaceLibraryTests
{
    private const string WiiMii = "gAwAUQBGAFMARgBZAFMATQBHAAAAAAAAgAAAAAAAAACgbERAAKQHIEhvCTglXZitAIoAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void Resource_ReadsPartsOutOfTheArchiveFromTheDisc()
    {
        var resource = RflResource.FromArchive(
            U8(
                "RFL_Res.dat",
                Library(
                    new()
                    {
                        [RflArchive.Eye] =
                        [
                            [1, 2, 3],
                            [4, 5],
                        ],
                    }
                )
            )
        );

        Assert.True(resource.IsSuccess, resource.IsFailure ? resource.Error.Message : null);
        Assert.Equal(2, resource.Value.Count(RflArchive.Eye));
        Assert.Equal(new byte[] { 4, 5 }, resource.Value.Get(RflArchive.Eye, 1).ToArray());
        Assert.True(resource.Value.Get(RflArchive.Eye, 2).IsEmpty);
        Assert.Equal(0, resource.Value.Count(RflArchive.Mouth));
    }

    [Fact]
    public void Resource_RejectsArchivesWithoutTheLibrary()
    {
        Assert.True(RflResource.FromArchive(U8("Other.dat", Library([]))).IsFailure);
        Assert.True(RflResource.FromArchive([1, 2, 3]).IsFailure);
        Assert.True(RflResource.Parse(new byte[200]).IsFailure);
    }

    [Fact]
    public void Shape_UnpacksTheDisplayListIntoCounterClockwiseTriangles()
    {
        // One GX (clockwise) triangle: (0,0), (0,1), (1,0) seen from the front.
        var nose = Shape("nose", [new(0, 0, 0), new(0, 256, 0), new(256, 0, 512)], 0x90, [0, 1, 2]);

        var shape = RflShape.Decode(RflArchive.Nose, nose, scale: 2f, translate: new Vector3(0f, 0f, 10f)).Value;
        var flipped = RflShape.Decode(RflArchive.Nose, nose, flipX: true).Value;

        Assert.Equal([new(0f, 0f, 10f), new(0f, 2f, 10f), new(2f, 0f, 14f)], shape.Positions);
        Assert.Equal([0, 2, 1], shape.Indices);
        Assert.Equal(new Vector3(-1f, 0f, 2f), flipped.Positions[2]);
        // Mirroring already turns the triangle around.
        Assert.Equal([0, 1, 2], flipped.Indices);
        Assert.True(RflShape.Decode(RflArchive.Hair, nose).IsFailure);
    }

    [Fact]
    public void Texture_DecodesGxTiles()
    {
        // I4: one 8x8 tile, texel (1, 0) is 0xF and everything else 0.
        var i4 = TextureFile(format: 0, width: 8, height: 8, image: [0x0F, .. new byte[31]]);
        // RGB5A3: one 4x4 tile, an opaque red texel and then a half see-through one.
        var rgb5A3 = TextureFile(format: 5, width: 4, height: 4, image: [0xFC, 0x00, 0x4F, 0x00, .. new byte[28]]);

        var intensity = RflTexture.Decode(i4).Value;
        var color = RflTexture.Decode(rgb5A3).Value;

        Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 255, 255, 255 }, intensity.Rgba[..8]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, color.Rgba[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 146 }, color.Rgba[4..8]);
    }

    [Fact]
    public void CharInfo_ReadsTheWiiValuesBackFromStudioData()
    {
        var data = Convert.FromBase64String(WiiMii);
        var studio = MiiStudioDataSerializer.Serialize(MiiSerializer.Deserialize(data).Value).Value;
        int Bits(int offset, int shift, int width) =>
            (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset)) >> shift) & ((1 << width) - 1);

        var info = RflCharInfo.FromStudioData(studio).Value;

        Assert.Equal(Bits(0x20, 13, 3), info.FaceType);
        Assert.Equal(Bits(0x20, 10, 3), info.FaceColor);
        Assert.Equal(Bits(0x20, 6, 4), info.FaceTex);
        Assert.Equal(Bits(0x22, 9, 7), info.HairType);
        Assert.Equal(Bits(0x22, 6, 3), info.HairColor);
        Assert.Equal(Bits(0x24, 11, 5), info.EyebrowType);
        Assert.Equal(Bits(0x26, 13, 3), info.EyebrowColor);
        Assert.Equal(Bits(0x28, 10, 6), info.EyeType);
        Assert.Equal(Bits(0x2A, 13, 3), info.EyeColor);
        Assert.Equal(Bits(0x2C, 12, 4), info.NoseType);
        Assert.Equal(Bits(0x2E, 11, 5), info.MouthType);
        Assert.Equal(Bits(0x2E, 9, 2), info.MouthColor);
        Assert.Equal(Bits(0x30, 12, 4), info.GlassType);
        Assert.Equal(Bits(0x30, 9, 3), info.GlassColor);
        Assert.Equal(Bits(0x32, 14, 2), info.MustacheType);
        Assert.Equal(Bits(0x32, 9, 3), info.BeardColor);
        Assert.Equal(Bits(0x34, 15, 1) == 1, info.HasMole);
        Assert.True(RflCharInfo.FromStudioData("not studio data").IsFailure);
    }

    [Fact]
    public void Mask_PaintsThePartsWhereTheirQuadsAre()
    {
        // An all white eye; every other part is missing, so painting only the eyes must still work.
        var eye = TextureFile(format: 5, width: 4, height: 4, image: [.. Enumerable.Repeat((byte)0xFF, 32)]);
        var resource = RflResource.Parse(Library(new() { [RflArchive.Eye] = [eye] })).Value;
        var info = new RflCharInfo { EyeY = 9 };

        var mask = RflHead.BuildMask(resource, info, expression: 0, MiiMaskLayers.Eyes, resolution: 64).Value;
        var eyeQuad = RflHead.MaskPartQuads(info)[MiiMaskLayers.Eyes][0];
        var center = eyeQuad.Aggregate(Vector2.Zero, (sum, corner) => sum + corner) / 4f * 64f;

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(mask, (int)center.X, (int)center.Y));
        Assert.Equal(0, Pixel(mask, 0, 0)[3]);
        Assert.Equal(0, Pixel(mask, 32, 60)[3]);
    }

    [Fact]
    public void Library_NeedsAGameFile()
    {
        var (library, discImages, _) = Fixture(gameLocation: "");

        var result = library.Load();

        Assert.True(result.IsFailure);
        Assert.Contains("game file", result.Error.Message);
        discImages.DidNotReceiveWithAnyArgs().ReadFile(default!, default!);
    }

    [Fact]
    public void Library_ReadsTheGameFileOnceUntilItChanges()
    {
        var (library, discImages, fs) = Fixture(gameLocation: "/games/mkw.rvz");
        fs.File.WriteAllBytes("/games/mkw.rvz", [1]);
        discImages.ReadFile("/games/mkw.rvz", RflResource.DiscPath).Returns(_ => U8("RFL_Res.dat", Library([])));

        var first = library.Load();
        var second = library.Load();
        fs.File.WriteAllBytes("/games/mkw.rvz", [1, 2]);
        var third = library.Load();

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Message : null);
        Assert.Same(first.Value, second.Value);
        Assert.NotSame(first.Value, third.Value);
        discImages.Received(2).ReadFile("/games/mkw.rvz", RflResource.DiscPath);
    }

    [Fact]
    public void Library_ReadsTheRealDiscLayout()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/games");
        var disc = new SyntheticWiiDisc { Files = new() { ["contents/RFLRes01.arc"] = U8("RFL_Res.dat", Library([])) } };
        fs.File.WriteAllBytes("/games/mkw.iso", disc.BuildIso());
        var (library, _, _) = Fixture("/games/mkw.iso", fs, new DiscImageService(fs));

        var result = library.Load();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void Renderer_ReportsAMissingFaceLibrary()
    {
        var library = Substitute.For<IMiiFaceLibrary>();
        library.Load().Returns(new OperationError { Message = "No game file is set." });
        var studio = MiiStudioDataSerializer.Serialize(MiiSerializer.Deserialize(Convert.FromBase64String(WiiMii)).Value).Value;
        var renderer = new NativeMiiRenderer(library);

        var image = renderer.RenderToBuffer(new Mii(), studio, new MiiImageSpecifications());
        var head = renderer.BuildHeadModel(studio, 0);

        Assert.Equal("No game file is set.", image.Error!.Message);
        Assert.Equal("No game file is set.", head.Error!.Message);
        Assert.Equal("Studio data is empty.", renderer.RenderToBuffer(new Mii(), "", new MiiImageSpecifications()).Error!.Message);
    }

    #region Synthetic face library

    private static (MiiFaceLibrary Library, IDiscImageService DiscImages, MockFileSystem Fs) Fixture(
        string gameLocation,
        MockFileSystem? fs = null,
        IDiscImageService? discImages = null
    )
    {
        fs ??= new MockFileSystem();
        fs.Directory.CreateDirectory("/games");
        discImages ??= Substitute.For<IDiscImageService>();
        var settings = Substitute.For<ISettingsManager>();
        var setting = new WhWzSetting<string>("GameLocation", gameLocation);
        settings.GAME_LOCATION.Returns(setting);
        settings.Get<string>(setting).Returns(gameLocation);
        return (new MiiFaceLibrary(settings, discImages, fs), discImages, fs);
    }

    private static byte[] Pixel(MiiTexture texture, int x, int y) => texture.Rgba.AsSpan((y * texture.Width + x) * 4, 4).ToArray();

    /// <summary>An RFL_Res.dat with the given files (every other archive empty).</summary>
    private static byte[] Library(Dictionary<RflArchive, byte[][]> files)
    {
        var archives = Enum.GetValues<RflArchive>();
        using var data = new MemoryStream();
        var header = new byte[4 + archives.Length * 4];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)archives.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), 0x39D);
        data.Write(header);
        foreach (var archive in archives)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4 + (int)archive * 4), (uint)data.Length);
            var parts = files.GetValueOrDefault(archive) ?? [];
            var table = new byte[8 + parts.Length * 4];
            BinaryPrimitives.WriteUInt16BigEndian(table, (ushort)parts.Length);
            var offset = 0;
            for (var i = 0; i <= parts.Length; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4 + i * 4), (uint)offset);
                if (i < parts.Length)
                    offset += parts[i].Length;
            }

            data.Write(table);
            foreach (var part in parts)
                data.Write(part);
        }

        var bytes = data.ToArray();
        header.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>A U8 archive holding one file.</summary>
    private static byte[] U8(string name, byte[] file)
    {
        var names = Encoding.ASCII.GetBytes("\0" + name + "\0");
        const int nodes = 0x20;
        var dataOffset = nodes + 2 * 12 + names.Length;
        var archive = new byte[dataOffset + file.Length];
        BinaryPrimitives.WriteUInt32BigEndian(archive, 0x55AA382D);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(4), nodes);
        archive[nodes] = 1; // root directory
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(nodes + 8), 2);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(nodes + 12), 1); // name offset
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(nodes + 16), (uint)dataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(archive.AsSpan(nodes + 20), (uint)file.Length);
        names.CopyTo(archive, nodes + 24);
        file.CopyTo(archive, dataOffset);
        return archive;
    }

    /// <summary>A shape file without texcoords: positions (fixed point, 8 fraction bits), one normal and one primitive.</summary>
    private static byte[] Shape(string magic, (short X, short Y, short Z)[] positions, byte primitive, byte[] corners)
    {
        using var data = new MemoryStream();
        void U16(int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(bytes, (short)value);
            data.Write(bytes);
        }

        data.Write(Encoding.ASCII.GetBytes(magic));
        U16(positions.Length);
        foreach (var (x, y, z) in positions)
        {
            U16(x);
            U16(y);
            U16(z);
        }

        U16(1);
        U16(0);
        U16(0);
        U16(1 << 14);
        data.WriteByte(1);
        data.WriteByte((byte)corners.Length);
        data.WriteByte(primitive);
        foreach (var corner in corners)
        {
            data.WriteByte(corner);
            data.WriteByte(0);
        }

        return data.ToArray();
    }

    private static byte[] TextureFile(byte format, ushort width, ushort height, byte[] image)
    {
        var file = new byte[0x20 + image.Length];
        file[0] = format;
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(2), width);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), height);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(0x1C), 0x20);
        image.CopyTo(file, 0x20);
        return file;
    }

    #endregion
}
