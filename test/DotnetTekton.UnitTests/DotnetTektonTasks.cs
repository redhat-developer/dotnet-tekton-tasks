using YamlDotNet.RepresentationModel;

namespace DotnetTekton.UnitTests;

[CollectionDefinition(nameof(TektonTaskTestCollection))]
public class TektonTaskTestCollection : ICollectionFixture<DotnetTektonTasks>
{ }

public sealed class TektonTask
{
    private string? _script;

    public required YamlNode Yaml { get; init; }

    public string Script
        => _script ??= Yaml["spec"]?["steps"]?[0]?["script"]?.ToString() ?? throw new KeyNotFoundException("script");
}

public sealed class DotnetTektonTasks
{
    private const string TaskDirectoryPrefix = "task-";

    public const string DotnetPublishImageTaskName = "dotnet-publish-image";

    private readonly Dictionary<string, TektonTask> _tasks = new();

    public DotnetTektonTasks()
    {
        GetTasks();
    }

    public int TaskCount => _tasks.Count;

    public TektonTask GetTektonTask(string taskName)
        => _tasks[taskName];

    public TektonTask DotnetPublishImageTask
        => GetTektonTask(DotnetPublishImageTaskName);

    private void GetTasks()
    {
        foreach (var taskDir in Directory.GetDirectories(Paths.SrcDirectory, $"{TaskDirectoryPrefix}*"))
        {
            string name = Path.GetFileName(taskDir).Substring(TaskDirectoryPrefix.Length);
            string taskDefinitionPath = Path.Combine(taskDir, name) + ".yaml";

            using var file = new StreamReader(File.OpenRead(taskDefinitionPath));
            var yamlStream = new YamlStream();
            yamlStream.Load(file);
            YamlNode node = yamlStream.Documents[0].RootNode;
            _tasks.Add(name!, new TektonTask() { Yaml = node });
        }
    }
}