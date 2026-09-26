using System.Buffers.Binary;
using WheelWizard.Settings;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.CloudSync.Mii;

/// <summary>Moves one raw 74-byte Mii block only; it never copies RFL_DB.dat between devices.</summary>
public sealed class MiiProfileService(IMiiRepositoryService repository, ISettingsManager settings) : IMiiProfileService
{
    public Task<byte[]> ExtractMiiAsync(MiiIdentifier id)
    {
        var raw =
            repository.GetRawBlockByAvatarId(id.Value)
            ?? throw new InvalidDataException("The profile Mii is not present in the local RFL_DB.dat.");
        return Task.FromResult(raw);
    }

    public Task<bool> IsMiiPresentAsync(MiiIdentifier id) => Task.FromResult(repository.GetRawBlockByAvatarId(id.Value) is not null);

    public Task<ProfileMiiExtraction> ExtractProfileMiiAsync(byte[] rksysData)
    {
        // Mario Kart Wii stores the selected Mii's Avatar ID in the RKPD header.  It does
        // not embed a full Mii block in rksys.dat, so matching arbitrary byte sequences can
        // select a wrong local Mii.
        const int rksysHeaderSize = 0x08;
        const int rkpdSize = 0x8CC0;
        const int avatarIdOffset = 0x28;
        var userIndex = Math.Clamp(settings.Get<int>(settings.FOCUSED_USER), 0, 3);
        var offset = rksysHeaderSize + userIndex * rkpdSize + avatarIdOffset;
        if (rksysData.Length < offset + sizeof(uint))
            throw new InvalidDataException("rksys.dat is too small to read the selected license Mii.");

        var id = BinaryPrimitives.ReadUInt32BigEndian(rksysData.AsSpan(offset, sizeof(uint)));
        if (id == 0)
            return Task.FromResult(new ProfileMiiExtraction(null, null));

        var identifier = new MiiIdentifier(id);
        return Task.FromResult(new ProfileMiiExtraction(identifier, repository.GetRawBlockByAvatarId(id)));
    }

    public Task EnsureMiiPresentAsync(byte[] mii)
    {
        if (mii.Length != MiiSerializer.MiiBlockSize)
            throw new InvalidDataException("Cloud profile contains an invalid Mii block.");

        var id = BinaryPrimitives.ReadUInt32BigEndian(mii.AsSpan(0x18, 4));
        if (id == 0)
            throw new InvalidDataException("Cloud profile Mii has no identifier.");

        var existing = repository.GetRawBlockByAvatarId(id);
        OperationResult result = existing is null ? repository.AddMiiToBlocks(mii) : repository.UpdateBlockByClientId(id, mii);
        if (result.IsFailure)
            throw new InvalidDataException(result.Error.Message);

        return Task.CompletedTask;
    }
}
