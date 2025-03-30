﻿namespace WheelWizard.Utilities.Mockers;

public class MiiFactory : MockingDataFactory<FullMii, MiiFactory>
{
    protected override string DictionaryKeyGenerator(FullMii value) => value.Name.ToString();
    private static int _miiCount = 1;
    
    private readonly string[] dataList = new[]
    {
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
        "wBIATABpAGMAbwByAGkAYwBlAAAAAAosgAAAAAAAAAAgTH5AuUUo8kiRCtgAbUALguAAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA="
    };
    
    public override FullMii Create(int? seed = null)
    {
        var deserializerResult = MiiSerializer.Deserialize(Convert.FromBase64String(dataList[_miiCount++ % dataList.Length]));
        if (deserializerResult.IsFailure)
            throw new Exception("Failed to deserialize Mii data");
        return deserializerResult.Value;
    }
}
