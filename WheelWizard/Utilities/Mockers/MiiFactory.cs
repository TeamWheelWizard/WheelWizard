﻿using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Utilities.Mockers;

public class MiiFactory : MockingDataFactory<Mii, MiiFactory>
{
    protected override string DictionaryKeyGenerator(Mii value) => value.Name.ToString();

    private static int _miiCount = 1;

    private readonly string[] dataList =
    [
        "AAAAQgBlAGUAAAAAAAAAAAAAAAAAAEBAgeGIAcKv7BAABEJBMb0oogiMCEgUTbiNAIoAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "wBAASAOzA8EDtQByACADtQB4AAAAAAAAgAAAAAAAAAAgF4+gmVMm1SCSjpgAbWAvAIoAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gBYDngBxAHUAaQAAAAAAAAAAAAAAAH9QgAAAAAAAAAAAFxAAItQQPBiODhgIZVEPcKBhDSUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "wBbgFwBsAHUAbQBp4BcAAAAAAAAAAF89gAAAAAAAAAAAFTqAmY4IwSCQDngAbWAQZOAAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAwAUQBGAFMARgBZAFMATQBHAAAAAAAAgAAAAAAAAACgbERAAKQHIEhvCTglXZitAIoAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAAAbgBvACAAbgBhAG0AZQAAAAAAAEBAgAAAAuz/gtIEF0JAMZQoogiMCFgUTbiNAIoAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gBAARABhAHgAdABlAHIAAAAAAAAAAG5VgAAAAAAAAAAgF3hAAVQosgiMCFgUTbiNAIoAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAgAbgBvACAAbgBhAG0AZQAAAAAAAEBAgAAAAOz/gtIQHogAMZcIogiMCFgUTbiNAIoAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAoARABlAGUAbgBlAAAAAAAAAAAAAAAmgAAAAAAAAACALE/AuWQoolRRBPgAjUjNJnAAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAomBgBNAGEAcgO6JmoAAAAAAAAAAEEmgAAAAAAAAAAAF2ZgMZQokgitCFgUVbJtgIoKiiTMAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAzwYAAAAAAAAAAAAAAAAAAAAAAAAEBAgAAAAAAAAAAEDEIAMYUIogiMCFgTTbhtIGAAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gAgAbgBvACAAbgBhAG0AZQAAAAAAAEBAgAAAAOz/gtIQF4gAMZQIogiMCFgUTbiNAIoAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        " wBbgFwBMA7EAbgBjAGUAWCEiAAAAAEBBgAAAAAAAAAAgFzoAuVMIooxQDlgAfZgPZOMAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "gBYATQBpAG4AaQBuAGcAAAAAAAAAAEBOgAAAAAAAAAAg10KAuRQoopSMSFiiTZhtIIoAiiUEAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "wBAAZwBhAG4AZwBuAGUAdwB3AHMDyAAAgAAAAAAAAAAEbDZAqaQosmBsCFgUTQCNAAoAgCIFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "wBIATABpAGMAbwByAGkAYwBlAAAAAAosgAAAAAAAAAAgTH5AuUUo8kiRCtgAbUALguAAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
    ];

    public override Mii Create(int? seed = null)
    {
        var deserializerResult = MiiSerializer.Deserialize(Convert.FromBase64String(dataList[_miiCount++ % dataList.Length]));
        if (deserializerResult.IsFailure)
            throw new("Failed to deserialize Mii data");
        return deserializerResult.Value;
    }
}
