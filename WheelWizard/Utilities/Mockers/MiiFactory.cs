﻿using WheelWizard.Models.MiiImages;

namespace WheelWizard.Utilities.Mockers;

public class MiiFactory : MockingDataFactory<Mii, MiiFactory>
{
    protected override string DictionaryKeyGenerator(Mii value) => value.Name;
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
    
    public override Mii Create(int? seed = null)
    {
        return new()
        {
            Name = $"Mii {_miiCount++}", 
            Data = dataList[(int)(Rand(seed).NextDouble() * dataList.Length)]
        };
    }
}
