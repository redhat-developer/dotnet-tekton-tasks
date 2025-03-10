public abstract class FileCleanupBase : IDisposable
{
    private readonly List<string> _directoriesToDelete = new();

    public void Dispose()
    {
        foreach (var dir in _directoriesToDelete)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            { }
        }
    }

    public string CreateDirectory()
    {
        // These paths are directly under `/tmp` and in a container we map them to the exact same path.
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        _directoriesToDelete.Add(path);
        return path;
    }

    public string GenerateTempFilePath(string filename = "file")
    {
        // These files are created in a directory which we can then mount in a container.
        var dir = CreateDirectory();
        return Path.Combine(dir, filename);;
    }
}