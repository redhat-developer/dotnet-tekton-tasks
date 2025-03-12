using System.Reflection;

namespace DotnetTekton.UnitTests;

static class Paths
{
    private static string? _srcDirectory;

    public static string SrcDirectory
    {
        get
        {
            if (_srcDirectory == null)
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string? directory = Path.GetDirectoryName(assemblyPath)!;
                while (!Directory.Exists(Path.Combine(directory!, ".git")))
                {
                    directory = Path.GetDirectoryName(directory);
                    if (directory is null)
                    {
                        throw new InvalidOperationException("Could not find git root directory.");
                    }
                }
                _srcDirectory = Path.Combine(directory, "src");
            }
            return _srcDirectory;
        }
    }
}