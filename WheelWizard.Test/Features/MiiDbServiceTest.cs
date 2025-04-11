using NSubstitute.ExceptionExtensions;
using WheelWizard.Shared;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features
{
    public class MiiDbServiceTests
    {
        private readonly IMiiRepository _repository;
        private readonly MiiDbService _service;

        // --- Test Setup ---

        public MiiDbServiceTests()
        {
            _repository = Substitute.For<IMiiRepository>();
            _service = new(_repository);
        }

        // --- Helper Methods ---

        private OperationResult<Mii> CreateValidMii(uint id = 1, string name = "TestMii")
        {
            var miiname = MiiName.Create(name);
            var miiId = id;
            var height = MiiScale.Create(60);
            var weight = MiiScale.Create(50);
            var miiFacial = MiiFacialFeatures.Create(MiiFaceShape.Bread, MiiSkinColor.Brown, MiiFacialFeature.Beard, false, false);
            var miiHair = MiiHair.Create(1, HairColor.Black, false);
            var miiEyebrows = MiiEyebrow.Create(1, 1, EyebrowColor.Black, 1, 1, 1);
            var miiEyes = MiiEye.Create(1, 1, 1, EyeColor.Black, 1, 1);
            var miiNose = MiiNose.Create(NoseType.Default, 1, 1);
            var miiLips = MiiLip.Create(1, LipColor.Pink, 1, 1);
            var miiGlasses = MiiGlasses.Create(GlassesType.None, GlassesColor.Blue, 1, 1);
            var miiFacialHair = MiiFacialHair.Create(MustacheType.None, BeardType.None, MustacheColor.Black, 1, 1);
            var miiMole = MiiMole.Create(true, 1, 1, 1);
            var creatorName = MiiName.Create("Creator");
            var miiFavoriteColor = MiiFavoriteColor.Red;
            var EveryResult = new List<OperationResult>
            {
                miiname,
                height,
                weight,
                miiFacial,
                miiHair,
                miiEyebrows,
                miiEyes,
                miiNose,
                miiLips,
                miiGlasses,
                miiFacialHair,
                miiMole,
                creatorName,
            };
            if (EveryResult.Any(r => r.IsFailure))
            {
                return Fail<Mii>(EveryResult.First(r => r.IsFailure).Error);
            }

            return Ok(
                new Mii
                {
                    Name = miiname.Value,
                    MiiId = miiId,
                    Height = height.Value,
                    Weight = weight.Value,
                    MiiFacial = miiFacial.Value,
                    MiiHair = miiHair.Value,
                    MiiEyebrows = miiEyebrows.Value,
                    MiiEyes = miiEyes.Value,
                    MiiNose = miiNose.Value,
                    MiiLips = miiLips.Value,
                    MiiGlasses = miiGlasses.Value,
                    MiiFacialHair = miiFacialHair.Value,
                    MiiMole = miiMole.Value,
                    CreatorName = creatorName.Value,
                    MiiFavoriteColor = miiFavoriteColor,
                }
            );
        }

        // Helper to get serialized bytes for a valid Mii
        private byte[] GetSerializedBytes(Mii mii)
        {
            var serializedResult = MiiSerializer.Serialize(mii);
            Assert.True(
                serializedResult.IsSuccess,
                $"Serialization failed during test setup for Mii {mii.MiiId}: {serializedResult.Error?.Message}"
            );
            return serializedResult.Value;
        }

        [Fact]
        public void MiiSerializer_ShouldSerializeAndDeserializeSuccessfully_ForValidMii()
        {
            // Arrange
            var originalResult = CreateValidMii(999, "RoundMii");
            Assert.True(originalResult.IsSuccess, $"Setup Failed: {originalResult.Error?.Message}");
            var originalMii = originalResult.Value;

            // Act
            var serializedResult = MiiSerializer.Serialize(originalMii);
            Assert.True(serializedResult.IsSuccess, $"Serialization Failed: {serializedResult.Error?.Message}");
            var serializedBytes = serializedResult.Value;

            var deserializedResult = MiiSerializer.Deserialize(serializedBytes);

            // Assert
            Assert.True(deserializedResult.IsSuccess, $"Deserialization Failed: {deserializedResult.Error?.Message}");
            var deserializedMii = deserializedResult.Value;

            Assert.Equal(originalMii.MiiId, deserializedMii.MiiId);
            Assert.Equal(originalMii.Name.ToString(), deserializedMii.Name.ToString());
            Assert.Equal(originalMii.Height.Value, deserializedMii.Height.Value);
            Assert.Equal(originalMii.Weight.Value, deserializedMii.Weight.Value);
            Assert.Equal(originalMii.MiiFavoriteColor, deserializedMii.MiiFavoriteColor);
        }

        [Fact]
        public void MiiSerializer_Deserialize_ShouldFail_ForInvalidDataLength()
        {
            // Arrange
            var invalidData = new byte[MiiSerializer.MiiBlockSize - 1]; // Incorrect length

            // Act
            var result = MiiSerializer.Deserialize(invalidData);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(result.IsFailure, true);
        }

        [Fact]
        public void MiiSerializer_Deserialize_ShouldFail_ForNullData()
        {
            // Arrange
            byte[]? invalidData = null;

            // Act
            var result = MiiSerializer.Deserialize(invalidData);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(result.IsFailure, true);
        }

        [Fact]
        public void GetAllMiis_ShouldReturnEmptyList_WhenRepositoryReturnsEmptyList()
        {
            // Arrange
            _repository.LoadAllBlocks().Returns([]);

            // Act
            var result = _service.GetAllMiis();

            // Assert
            Assert.Empty(result);
            _repository.Received(1).LoadAllBlocks();
        }

        [Fact]
        public void GetAllMiis_ShouldReturnListOfMiis_WhenRepositoryReturnsValidBlocks()
        {
            // Arrange
            var mii1Result = CreateValidMii(1, "MiiOne");
            var mii2Result = CreateValidMii(2, "MiiTwo");
            Assert.True(mii1Result.IsSuccess && mii2Result.IsSuccess, "Setup Failed: Could not create valid Miis");
            var mii1Bytes = GetSerializedBytes(mii1Result.Value);
            var mii2Bytes = GetSerializedBytes(mii2Result.Value);

            _repository.LoadAllBlocks().Returns([mii1Bytes, mii2Bytes]);

            // Act
            var result = _service.GetAllMiis();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(result, m => m.MiiId == 1 && m.Name.ToString() == "MiiOne");
            Assert.Contains(result, m => m.MiiId == 2 && m.Name.ToString() == "MiiTwo");
            _repository.Received(1).LoadAllBlocks();
        }

        [Fact]
        public void GetAllMiis_ShouldSkipInvalidBlocks_AndReturnOnlyValidMiis()
        {
            // Arrange
            var mii1Result = CreateValidMii(1, "ValidMii");
            Assert.True(mii1Result.IsSuccess, "Setup Failed: Could not create valid Mii");
            var mii1Bytes = GetSerializedBytes(mii1Result.Value);
            var invalidBytesShort = new byte[10]; // Invalid length
            var invalidBytesNull = (byte[])null; // Null entry (if possible from repo)
            // Simulate a block that's the right size but contains garbage data causing deserialization failure
            var potentiallyBadBytes = new byte[MiiSerializer.MiiBlockSize];
            _repository.LoadAllBlocks().Returns([invalidBytesShort, mii1Bytes, potentiallyBadBytes, invalidBytesNull!]);

            // Act
            var result = _service.GetAllMiis();

            // Assert
            // Only the valid Mii should be returned. Invalid length, null, and deserialization failures are skipped.
            Assert.Single(result);
            Assert.Equal(1u, result[0].MiiId);
            Assert.Equal("ValidMii", result[0].Name.ToString());
            _repository.Received(1).LoadAllBlocks();
        }

        [Fact]
        public void GetAllMiis_ShouldHandleRepositoryException()
        {
            // Arrange
            var expectedException = new IOException("Disk read error");
            _repository.LoadAllBlocks().Throws(expectedException);

            // Act & Assert
            var actualException = Assert.Throws<IOException>(() => _service.GetAllMiis());
            Assert.Same(expectedException, actualException); // Ensure the original exception is propagated
            _repository.Received(1).LoadAllBlocks();
        }

        [Fact]
        public void GetByClientId_ShouldReturnMii_WhenRepositoryReturnsValidBlock()
        {
            // Arrange
            uint targetId = 123;
            var miiResult = CreateValidMii(targetId, "TargetMii");
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create valid Mii");
            var miiBytes = GetSerializedBytes(miiResult.Value);

            _repository.GetRawBlockByClientId(targetId).Returns(miiBytes);

            // Act
            var result = _service.GetByClientId(targetId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(targetId, result.Value.MiiId);
            Assert.Equal("TargetMii", result.Value.Name.ToString());
            _repository.Received(1).GetRawBlockByClientId(targetId);
        }

        [Fact]
        public void GetByClientId_ShouldReturnFailure_WhenRepositoryReturnsNull()
        {
            // Arrange
            uint targetId = 404;
            _repository.GetRawBlockByClientId(targetId).Returns((byte[]?)null);

            // Act
            var result = _service.GetByClientId(targetId);

            // Assert
            Assert.True(result.IsFailure);
            _repository.Received(1).GetRawBlockByClientId(targetId);
        }

        [Fact]
        public void GetByClientId_ShouldReturnFailure_WhenRepositoryReturnsInvalidLengthBlock()
        {
            // Arrange
            uint targetId = 500;
            var invalidBytes = new byte[MiiSerializer.MiiBlockSize + 10]; // Wrong size
            _repository.GetRawBlockByClientId(targetId).Returns(invalidBytes);

            // Act
            var result = _service.GetByClientId(targetId);

            // Assert
            Assert.True(result.IsFailure);
            _repository.Received(1).GetRawBlockByClientId(targetId);
        }

        [Fact]
        public void GetByClientId_ShouldReturnFailure_WhenDeserializationFails()
        {
            // Arrange
            uint targetId = 666;
            var badBytes = new byte[MiiSerializer.MiiBlockSize];
            for (int i = 0; i < badBytes.Length; i++)
            {
                badBytes[i] = (byte)(i % 256);
            }

            _repository.GetRawBlockByClientId(targetId).Returns(badBytes);

            // Act
            var result = _service.GetByClientId(targetId);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(result.IsFailure, true);
            _repository.Received(1).GetRawBlockByClientId(targetId);
        }

        [Fact]
        public void GetByClientId_ShouldHandleRepositoryException()
        {
            // Arrange
            uint targetId = 777;
            var expectedException = new InvalidOperationException("Repository error");
            _repository.GetRawBlockByClientId(targetId).Throws(expectedException);

            // Act & Assert
            var actualException = Assert.Throws<InvalidOperationException>(() => _service.GetByClientId(targetId));
            Assert.Same(expectedException, actualException);
            _repository.Received(1).GetRawBlockByClientId(targetId);
        }

        // --- Update Tests ---

        [Fact]
        public void Update_ShouldReturnSuccess_WhenSerializationAndRepositorySucceed()
        {
            // Arrange
            var miiResult = CreateValidMii(321, "UpdateMe");
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create valid Mii");
            var miiToUpdate = miiResult.Value;
            var expectedBytes = GetSerializedBytes(miiToUpdate); // Get expected bytes *before* setting up mock

            _repository.UpdateBlockByClientId(miiToUpdate.MiiId, Arg.Is<byte[]>(b => b.SequenceEqual(expectedBytes))).Returns(Ok());

            // Act
            var result = _service.Update(miiToUpdate);

            // Assert
            Assert.True(result.IsSuccess);
            _repository.Received(1).UpdateBlockByClientId(miiToUpdate.MiiId, Arg.Is<byte[]>(b => b.SequenceEqual(expectedBytes)));
        }

        [Fact]
        public void Update_ShouldReturnFailure_WhenRepositoryUpdateFails()
        {
            // Arrange
            var miiResult = CreateValidMii(123, "FailUpdate");
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create valid Mii");
            var miiToUpdate = miiResult.Value;
            var expectedBytes = GetSerializedBytes(miiToUpdate);
            var repoError = Fail("Repository write failed");

            _repository.UpdateBlockByClientId(miiToUpdate.MiiId, Arg.Is<byte[]>(b => b.SequenceEqual(expectedBytes))).Returns(repoError);

            // Act
            var result = _service.Update(miiToUpdate);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(repoError.Error, result.Error); // Propagate the exact error
            _repository.Received(1).UpdateBlockByClientId(miiToUpdate.MiiId, Arg.Is<byte[]>(b => b.SequenceEqual(expectedBytes)));
        }

        [Fact]
        public void Update_ShouldHandleRepositoryExceptionDuringUpdate()
        {
            // Arrange
            var miiResult = CreateValidMii(987, "ExcUpdate");
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create valid Mii");
            var miiToUpdate = miiResult.Value;
            var expectedBytes = GetSerializedBytes(miiToUpdate);
            var expectedException = new IOException("Cannot write to file");

            _repository
                .UpdateBlockByClientId(miiToUpdate.MiiId, Arg.Is<byte[]>(b => b.SequenceEqual(expectedBytes)))
                .Throws(expectedException);

            // Act & Assert
            var actualException = Assert.Throws<IOException>(() => _service.Update(miiToUpdate));
            Assert.Same(expectedException, actualException);
            _repository.Received(1).UpdateBlockByClientId(miiToUpdate.MiiId, Arg.Is<byte[]>(b => b.SequenceEqual(expectedBytes)));
        }

        // Note: Testing serialization failure within Update is harder if CreateValidMii guarantees a serializable Mii.
        // If Mii could become invalid *after* creation but before Update, that scenario could be tested.
        // For now, we assume Mii objects passed to Update are valid and serializable.

        // --- UpdateName Tests ---

        [Fact]
        public void UpdateName_ShouldReturnSuccess_WhenGetAndUpdateSucceed()
        {
            // Arrange
            uint targetId = 333;
            string oldName = "OldName";
            string newName = "NewName";
            var miiResult = CreateValidMii(targetId, oldName);
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create original Mii");
            var originalMii = miiResult.Value;
            var originalBytes = GetSerializedBytes(originalMii);

            // Setup Get
            _repository.GetRawBlockByClientId(targetId).Returns(originalBytes);

            // Setup Update (capture the updated Mii's bytes)
            byte[]? updatedBytes = null;
            _repository.UpdateBlockByClientId(targetId, Arg.Do<byte[]>(bytes => updatedBytes = bytes)).Returns(Ok());

            // Act
            var result = _service.UpdateName(targetId, newName);

            // Assert
            Assert.True(result.IsSuccess);
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.Received(1).UpdateBlockByClientId(targetId, Arg.Any<byte[]>());

            // Verify the name was actually changed in the serialized data sent to the repo
            Assert.NotNull(updatedBytes);
            var updatedMiiResult = MiiSerializer.Deserialize(updatedBytes!);
            Assert.True(updatedMiiResult.IsSuccess, "Failed to deserialize bytes passed to repository update");
            Assert.Equal(newName, updatedMiiResult.Value.Name.ToString());
            Assert.Equal(targetId, updatedMiiResult.Value.MiiId); // Ensure ID didn't change
        }

        [Fact]
        public void UpdateName_ShouldReturnFailure_WhenGetByClientIdFails_NotFound()
        {
            // Arrange
            uint targetId = 404;
            string newName = "NewName";
            _repository.GetRawBlockByClientId(targetId).Returns((byte[]?)null);

            // Act
            var result = _service.UpdateName(targetId, newName);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal("Mii block not found or invalid.", result.Error.Message); // Error from GetByClientId
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.DidNotReceive().UpdateBlockByClientId(Arg.Any<uint>(), Arg.Any<byte[]>());
        }

        [Fact]
        public void UpdateName_ShouldReturnFailure_WhenGetByClientIdFails_Deserialization()
        {
            // Arrange
            uint targetId = 666;
            string newName = "NewName";
            var badBytes = new byte[MiiSerializer.MiiBlockSize]; // Correct size, bad content
            _repository.GetRawBlockByClientId(targetId).Returns(badBytes);

            // Act
            var result = _service.UpdateName(targetId, newName);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains("Invalid MiiName", result.Error.Message); // Error from MiiSerializer via GetByClientId
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.DidNotReceive().UpdateBlockByClientId(Arg.Any<uint>(), Arg.Any<byte[]>());
        }

        [Fact]
        public void UpdateName_ShouldReturnFailure_WhenNewNameIsInvalid()
        {
            // Arrange
            uint targetId = 555;
            string oldName = "ValidOld";
            string invalidNewName = "ThisNameIsDefinitelyTooLongForTheMii"; // > 10 chars
            var miiResult = CreateValidMii(targetId, oldName);
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create original Mii");
            var originalBytes = GetSerializedBytes(miiResult.Value);

            _repository.GetRawBlockByClientId(targetId).Returns(originalBytes);

            // Act
            var result = _service.UpdateName(targetId, invalidNewName);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(result.IsFailure, true);
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.DidNotReceive().UpdateBlockByClientId(Arg.Any<uint>(), Arg.Any<byte[]>());
        }

        [Fact]
        public void UpdateName_ShouldReturnFailure_WhenRepositoryUpdateFails()
        {
            // Arrange
            uint targetId = 777;
            string oldName = "Old";
            string newName = "New";
            var miiResult = CreateValidMii(targetId, oldName);
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create original Mii");
            var originalBytes = GetSerializedBytes(miiResult.Value);
            var repoError = Fail("Disk full");

            _repository.GetRawBlockByClientId(targetId).Returns(originalBytes);
            _repository
                .UpdateBlockByClientId(targetId, Arg.Any<byte[]>()) // We know name is valid, get succeeds
                .Returns(repoError);

            // Act
            var result = _service.UpdateName(targetId, newName);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Equal(repoError.Error, result.Error); // Error from the repository update propagated
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.Received(1).UpdateBlockByClientId(targetId, Arg.Any<byte[]>());
        }

        [Fact]
        public void UpdateName_ShouldHandleExceptionDuringGet()
        {
            // Arrange
            uint targetId = 111;
            string newName = "New";
            var expectedException = new TimeoutException("Timeout contacting repository");
            _repository.GetRawBlockByClientId(targetId).Throws(expectedException);

            // Act & Assert
            var actualException = Assert.Throws<TimeoutException>(() => _service.UpdateName(targetId, newName));
            Assert.Same(expectedException, actualException);
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.DidNotReceive().UpdateBlockByClientId(Arg.Any<uint>(), Arg.Any<byte[]>());
        }

        [Fact]
        public void UpdateName_ShouldHandleExceptionDuringUpdate()
        {
            // Arrange
            uint targetId = 222;
            string oldName = "Old";
            string newName = "New";
            var miiResult = CreateValidMii(targetId, oldName);
            Assert.True(miiResult.IsSuccess, "Setup Failed: Could not create original Mii");
            var originalBytes = GetSerializedBytes(miiResult.Value);
            var expectedException = new UnauthorizedAccessException("Access denied");

            _repository.GetRawBlockByClientId(targetId).Returns(originalBytes);
            _repository.UpdateBlockByClientId(targetId, Arg.Any<byte[]>()).Throws(expectedException);

            // Act & Assert
            var actualException = Assert.Throws<UnauthorizedAccessException>(() => _service.UpdateName(targetId, newName));
            Assert.Same(expectedException, actualException);
            _repository.Received(1).GetRawBlockByClientId(targetId);
            _repository.Received(1).UpdateBlockByClientId(targetId, Arg.Any<byte[]>());
        }
    }
}
