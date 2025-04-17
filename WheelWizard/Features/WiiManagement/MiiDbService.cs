using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement;

/// <summary>
/// Provides high-level operations for managing Miis in the Wii Mii database.
/// </summary>
public interface IMiiDbService
{
    /// <summary>
    /// Retrieves all Miis stored in the database.
    /// </summary>
    /// <returns>A list of fully deserialized <see cref="Mii"/> instances.</returns>
    List<Mii> GetAllMiis();

    /// <summary>
    /// Retrieves a specific Mii from the database using its unique client ID.
    /// </summary>
    /// <param name="avatarId">The unique identifier of the Mii to retrieve.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the <see cref="Mii"/> if found and valid.</returns>
    OperationResult<Mii> GetByAvatarId(uint avatarId);

    /// <summary>
    /// Updates an existing Mii in the database with new data.
    /// </summary>
    /// <param name="updatedMii">The updated <see cref="Mii"/> object to store.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    OperationResult Update(Mii updatedMii);

    /// <summary>
    /// Updates the name of an existing Mii in the database.
    /// </summary>
    /// <param name="clientId">The unique identifier of the Mii to update.</param>
    /// <param name="newName">The new name to assign to the Mii.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    OperationResult UpdateName(uint clientId, string newName);
}

public class MiiDbService : IMiiDbService
{
    private readonly IMiiRepository _repository;

    public MiiDbService(IMiiRepository repository)
    {
        _repository = repository;
    }

    public List<Mii> GetAllMiis()
    {
        var result = new List<Mii>();
        var blocks = _repository.LoadAllBlocks();

        foreach (var block in blocks)
        {
            var miiResult = MiiSerializer.Deserialize(block);
            if (miiResult.IsSuccess)
                result.Add(miiResult.Value);
        }

        return result;
    }

    public OperationResult<Mii> GetByAvatarId(uint avatarId)
    {
        var raw = _repository.GetRawBlockByAvatarId(avatarId);
        if (raw == null || raw.Length != MiiSerializer.MiiBlockSize)
            return "Mii block not found or invalid.";

        return MiiSerializer.Deserialize(raw);
    }

    public OperationResult Update(Mii updatedMii)
    {
        var serialized = MiiSerializer.Serialize(updatedMii);
        if (serialized.IsFailure)
            return serialized;
        var value = _repository.UpdateBlockByClientId(updatedMii.MiiId, serialized.Value);
        return value;
    }

    public OperationResult UpdateName(uint clientId, string newName)
    {
        var result = GetByAvatarId(clientId);
        if (result.IsFailure)
            return result;

        var mii = result.Value;

        var nameResult = MiiName.Create(newName);
        if (nameResult.IsFailure)
            return nameResult;

        mii.Name = nameResult.Value;
        return Update(mii);
    }
}
