namespace WheelWizard.Helpers;

// From now on we to have this FileHelper as a middle man whenever we do anything file related. This makes
// it easier to create helper methods, mock data, and most importantly, easy to make it multi-platform later on
public static class FileHelper
{
    public static bool FileExists(string path) => File.Exists(path);
    public static bool DirectoryExists(string path) => Directory.Exists(path);
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    
    public static string ReadAllText(string path) => File.ReadAllText(path);
    public static string? ReadAllTextSafe(string path) => FileExists(path) ? ReadAllText(path) : null;
    
    public static string[] ReadAllLines(string path) => File.ReadAllLines(path);
    public static string[]? ReadAllLinesSafe(string path) => FileExists(path) ? ReadAllLines(path) : null;
    
    public static void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
    public static void WriteAllTextSafe(string path, string contents)
    {
        var directoryPath = Path.GetDirectoryName(path);
        
        if (!string.IsNullOrEmpty(directoryPath) && !DirectoryExists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        WriteAllText(path, contents);
    }
    
    public static void WriteAllLines(string path, IEnumerable<string> contents) => File.WriteAllLines(path, contents);
    public static void WriteAllLinesSafe(string path, IEnumerable<string> contents)
    {
        var directoryPath = Path.GetDirectoryName(path);
        
        if (!string.IsNullOrEmpty(directoryPath) && !DirectoryExists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        WriteAllLines(path, contents);
    }
    
    public static void Touch(string path, string defaultValue = "")
    {
        if (DirectoryExists(path))
            TouchDirectory(path);
        else if (FileExists(path))
            TouchFile(path, defaultValue);
        else
        {
            var likelyDirectory = (path.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                               path.EndsWith(Path.AltDirectorySeparatorChar.ToString())) || !Path.HasExtension(path);
            if (likelyDirectory)
                TouchDirectory(path);
            else
                TouchFile(path, defaultValue);
        }
    }
    
    public static void TouchFile(string path, string defaultValue = "")
    {
        if (FileExists(path))
            File.SetLastAccessTime(path, DateTime.Now);
        else
            WriteAllTextSafe(path, defaultValue);
    }

    public static void TouchDirectory(string path)
    {
        if (DirectoryExists(path))
        {
            Directory.SetLastAccessTime(path, DateTime.Now);
            Directory.SetLastWriteTime(path, DateTime.Now);
        }
        else
            Directory.CreateDirectory(path);
    }
}
