// using CT_MKWII_WPF.Models.Settings;
// using Newtonsoft.Json;
// using System;
// using System.IO;
//
// namespace CT_MKWII_WPF.Services.Settings;
//
// //TODO: Rework mods
// //    - Mod and ModData must be combined in to 1 class
// //    - Maybe look a little bit in to the way mods are saved and read currently, but it all seems fine by me for now
// //    - All logic should be in this file. not in the page. E.G loading the mods. or changing the mods
// public class ModConfigManager
// {
//     // I think it would be better/easier/faster to have an internal state of all the mods.
//     // AKA, a list of all the mods.
//     // Everytime you make a change, it writes this change to the file
//     //      (which for now can be in sync, but in the future if this file starts to grow we can make that async with a lock on the file since)
//     // This is also how reading and writing settings is currently done.
//     // and it has a lot more benefits than drawbacks.
//     // Also because Mod and ModData should be in 1 class, you also always have the list of mods already ready for both startup
//     // but also for the my stuff page.
//
//     private static string ModConfigFilePath => Path.Combine(PathManager.WheelWizardAppdataPath, "Mods", "modconfig.json");
//
//     public static ModData[] GetMods()
//     {
//         if (!File.Exists(ModConfigFilePath))
//             return Array.Empty<ModData>();
//
//         var json = File.ReadAllText(ModConfigFilePath);
//         var mods = JsonConvert.DeserializeObject<ModData[]>(json);
//         return mods ?? Array.Empty<ModData>();
//     }
// }
