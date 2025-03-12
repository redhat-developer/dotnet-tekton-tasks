using YamlDotNet.RepresentationModel;
using Newtonsoft.Json.Linq;
using SimpleExec;

namespace DotnetTekton.UnitTests;

// These test verify the logic of the script task.
// The script task receives information about the Task params and workspaces through environment variables and arguments.
// That is the interface used by these tests.
public abstract class DotnetPublishImageTaskScriptTests : FileCleanupBase
{
    // Dummy names used by tests.
    protected const string TestCurrentNamespace ="test-namespace";
    protected const string TestImageRegistry = "test-image-registry.svc:5000";
    protected const string TestDotnetNamespace = "dotnet-images";
    protected const string TestDotnetSdkRepository = "sdk";

    // Helper test script snippits.
    private const string PrintVersion = """if [ "$1" = "--version" ]; then echo "9.0" && exit 0; fi""";
    private const string WriteImageDigest = $"echo 'sha256:deadbeef' >{ImageDigestPath}";

    // Paths used by the Task script implementation.
    private const string ImageDigestPath = "/tmp/IMAGE_DIGEST";
    private const string OverrideBaseImageTargetsPath = "/tmp/OverrideBaseImage.targets";

    // SDK image and its version under test.
    protected abstract string SdkImage { get; }
    protected abstract string DotnetVersion { get; }

    // Tekton Task under test.
    private readonly DotnetTektonTasks _tektonTasks;
    protected string Script => _tektonTasks.GetTektonTask(DotnetTektonTasks.DotnetPublishImageTaskName).Script;
    protected YamlNode Yaml => _tektonTasks.GetTektonTask(DotnetTektonTasks.DotnetPublishImageTaskName).Yaml;

    public DotnetPublishImageTaskScriptTests(DotnetTektonTasks tektonTasks)
    {
        _tektonTasks = tektonTasks;
    }

    [Fact]
    public void SuccessOnSuccess()
    {
        var runResult = RunWithDotnetAsScript("exit 0");
        Assert.Empty(runResult.StandardError);
        Assert.Empty(runResult.StandardOutput);
        Assert.Equal(0, runResult.ExitCode);
    }

    [Fact]
    public void FailOnFail()
    {
        var runResult = RunWithDotnetAsScript("exit 1");
        Assert.Empty(runResult.StandardError);
        Assert.Empty(runResult.StandardOutput);
        Assert.Equal(1, runResult.ExitCode);
    }

    // params: ENV_VARS
    [MemberData(nameof(ParamEnvvarsData))]
    [Theory]
    public void ParamEnvvars(string[] envvars)
    {
        var runResult = RunWithDotnetAsScript("env", args: ["--env-vars", ..envvars]);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        string[] lines = runResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var envvar in envvars)
        {
            Assert.Contains(envvar, lines);
        }
    }

    public static IEnumerable<object[]> ParamEnvvarsData =>
        new string[][]
        {
            [ "ENV1=VAL1" ],
            [ "ENV1=VAL1", "ENV2=VAL2"],
            [ "ENV1=VAL 1", "ENV2=VAL 2"]
        }.Select(envvars => new object[] { envvars });

    // workspaces: source
    [Fact]
    public void WorkingDirectorySourceWorkspace()
    {
        string homeDirectory = CreateDirectory();
        string sourcePath = CreateDirectory();
        var runResult = RunWithDotnetAsScript("pwd",
            envvars: new()
            {
                { "WORKSPACE_SOURCE_BOUND", "true"},
                { "WORKSPACE_SOURCE_PATH", sourcePath}
            });
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        Assert.Equal(sourcePath, runResult.StandardOutput.Trim());
    }

    // workspaces: dockerconfig
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Theory]
    public void DockerConfigBound(bool addConfigJson, bool addDockerConfigJson)
    {
        string homeDirectory = CreateDirectory();
        string dockerConfigPath = CreateDirectory();
        if (addConfigJson)
        {
            File.WriteAllText(Path.Combine(dockerConfigPath, "config.json"), "");
        }
        if (addDockerConfigJson)
        {
            File.WriteAllText(Path.Combine(dockerConfigPath, ".dockerconfigjson"), "");
        }
        var runResult = RunWithDotnetAsScript("exit 0",
            envvars: new()
            {
                { "WORKSPACE_DOCKERCONFIG_BOUND", "true"},
                { "WORKSPACE_DOCKERCONFIG_PATH", dockerConfigPath}
            },
            homeDirectory: homeDirectory);
        if (addConfigJson && addDockerConfigJson)
        {
            Assert.Equal(1, runResult.ExitCode);
            Assert.Equal("""
                         error: 'dockerconfig' workspace provides multiple config files.
                         The config must provided using a single '.dockerconfigjson' or a single 'config.json' file.
                         """, runResult.StandardError.Trim());
            Assert.Empty(runResult.StandardOutput);
        }
        else if (!addConfigJson && !addDockerConfigJson)
        {
            Assert.Empty(runResult.StandardError);
            Assert.Empty(runResult.StandardOutput);
            Assert.Equal(0, runResult.ExitCode);
        }
        else
        {
            string containersAuthConfigPath = Path.Combine(homeDirectory, ".config/containers/auth.json");
            FileInfo fi = new FileInfo(containersAuthConfigPath);

            Assert.True(fi.Exists);
            Assert.True((fi.Attributes & FileAttributes.ReparsePoint) != 0); // Check the file is a link.
            Assert.Equal(addConfigJson ? $"{dockerConfigPath}/config.json" : $"{dockerConfigPath}/.dockerconfigjson", fi.LinkTarget);

            Assert.Empty(runResult.StandardError);
            Assert.Equal(0, runResult.ExitCode);
        }
    }

    // result: IMAGE_DIGEST/IMAGE
    [InlineData("sha256:82xyza4f", "quay.io", "username/image-name", "latest")]
    [InlineData("sha256:82xyza4f", "quay.io", "username/image-name", null)]
    [Theory]
    public void Results(string sha, string registry, string repo, string? tag)
    {
        string resultsDirectory = CreateDirectory();
        var runResult = RunTask(
            envvars: new()
            {
                { "PARAM_IMAGE_NAME", $"{registry}/{repo}{(tag?.Length > 0 ? ':' : "")}{tag}"}
            },
            dotnetStubScript:
                $"""
                    {PrintVersion}
                    echo '{sha}' >{ImageDigestPath}
                    exit 0
                """,
                tektonResultsDirectory: resultsDirectory);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);
        Assert.Empty(runResult.StandardOutput);

        string imageDigestPath = $"{resultsDirectory}/IMAGE_DIGEST";
        Assert.True(File.Exists(imageDigestPath));
        Assert.Equal(sha, File.ReadAllText(imageDigestPath));

        string imagePath = $"{resultsDirectory}/IMAGE";
        Assert.True(File.Exists(imagePath));
        Assert.Equal($"{registry}/{repo}@{sha}", File.ReadAllText(imagePath));

        Assert.Equal(2, Directory.GetFileSystemEntries(resultsDirectory).Length);
    }

    // params: BUILD_PROPS in publish command
    [MemberData(nameof(ParamBuildPropsData))]
    [Theory]
    public void ParamBuildProps(string[] buildprops)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(args: ["--build-props", ..buildprops]);

        string[] expectedStartArgs = [
            "publish",
            ..buildprops.Select(p => $"-p:{p}"),
            "--getProperty:GeneratedContainerDigest"
        ];

        Assert.Equal(expectedStartArgs, publishCommandArgs.Take(expectedStartArgs.Length));
    }

    public static IEnumerable<object[]> ParamBuildPropsData =>
        new string[][]
        {
            [ "Prop1=Value1" ],
            [ "Prop1=Value1", "Prop2=Value2"],
            [ "Prop1=Value 1", "Prop2=Value 2"],
            [ "Prop1=\"Value 1;\""]
        }.Select(envvars => new object[] { envvars });

    [InlineData("Prop1=Value1;Value2")]
    [Theory]
    public void ParamBuildPropsDoesNotAcceptSemicolonWithoutQuotes(string prop)
    {
        var runResult = RunTask(envvars: [], args: ["--build-props", prop]);
        Assert.Equal(1, runResult.ExitCode);
        string expected =
        $"""
        error: Invalid BUILD_PROPS property: '{prop}'.
        To assign a list of values, the values must be enclosed with double quotes. For example: MyProperty="Value1;Value2".

        """;
        Assert.Equal(expected, runResult.StandardError);
    }

    // params: PRE_PUBLISH_SCRIPT
    [Fact]
    public void ParamPrePublishScriptPrintsOutput()
    {
        var runResult = RunTask(
            envvars: new()
            {
                { "PARAM_PRE_PUBLISH_SCRIPT",
                    $"""
                    echo "hello"
                    echo "world"
                    """}
            },
            dotnetStubScript:
                $"""
                {PrintVersion}
                echo 'dotnet'
                {WriteImageDigest}
                """);
        Assert.Equal(0, runResult.ExitCode);
        Assert.Equal("", runResult.StandardError);
        string[] lines = runResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("hello", lines[0]);
        Assert.Equal("world", lines[1]);
        Assert.Equal("dotnet", lines[2]);
    }

    [Fact]
    public void ParamPrePublishScriptTaskExitsOnFailure()
    {
        var runResult = RunTask(
            envvars: new()
            {
                { "PARAM_PRE_PUBLISH_SCRIPT",
                    $"""
                    echo "hello"
                    exit 1
                    """}
            },
            dotnetStubScript:
                $"""
                {PrintVersion}
                echo 'dotnet'
                {WriteImageDigest}
                """);
        Assert.Equal(1, runResult.ExitCode);
        Assert.Equal("", runResult.StandardError);
        string[] lines = runResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public void ParamPrePublishScriptResetEnvAfterScript()
    {
        var runResult = RunTask(
            envvars: new()
            {
                { "PARAM_PRE_PUBLISH_SCRIPT",
                    $"""
                    set +e
                    cd /

                    echo "script"
                    pwd
                    [[ $- == *e* ]] && echo "errexit enabled" || echo "errexit disabled"
                    """}
            },
            dotnetStubScript:
                $"""
                {PrintVersion}
                echo "dotnet"
                pwd
                {WriteImageDigest}
                exit 1
                """);
        // Assert.Equal(0, runResult.ExitCode);
        Assert.Equal("", runResult.StandardError);
        string[] lines = runResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Equal("script", lines[0]);
        Assert.Equal("/", lines[1]);
        Assert.Equal("errexit disabled", lines[2]);
        Assert.Equal("dotnet", lines[3]);
        Assert.EndsWith("/src", lines[4]);
        Assert.Equal(1, runResult.ExitCode);
    }

    // params: IMAGE_NAME in publish command
    [MemberData(nameof(ParamImageNameData))]
    [Theory]
    public void ParamImageName(string imageName, string[] expectedProperties)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_IMAGE_NAME", imageName}
            }
        );

        Assert.Superset(expectedProperties.ToHashSet(StringComparer.Ordinal), publishCommandArgs.ToHashSet(StringComparer.Ordinal));
    }

    public static IEnumerable<object[]> ParamImageNameData
    {
        get
        {
            foreach (var tag in new[] { "", "latest", "tag1", "\"tag1;tag2\""})
            {
                string tagSuffix = tag.Length > 0 ? $":{tag}" : "";
                string expectedTags = tag.Length > 0 ? tag : "latest";

                // no name specified.
                yield return new object[] { $"{tagSuffix}", new string[] { $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository=", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string name = "image-name";
                yield return new object[] { $"{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository={name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string @namespace = "other-namespace";
                yield return new object[] { $"{@namespace}/{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository={@namespace}/{name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string registry = "my-registry.com";
                yield return new object[] { $"{registry}/{@namespace}/{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={registry}", $"-p:ContainerRepository={@namespace}/{name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };
            }
        }
    }

    // params: VERBOSITY in publish command
    [Theory]
    [InlineData("minimal")]
    [InlineData("some value")]
    public void ParamVerbosity(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_VERBOSITY", value}
            });

        Assert.Single(publishCommandArgs, arg => arg == "-v");

        int indexOfVerbosityValue = Array.IndexOf(publishCommandArgs, "-v") + 1;
        Assert.Equal(value, publishCommandArgs[indexOfVerbosityValue]);
    }

    // params: PROJECT in publish command
    [Theory]
    [InlineData("")]
    [InlineData("project.csproj")]
    [InlineData("src/path to/web.fsproj")]
    public void ParamProject(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_PROJECT", value}
            });

        Assert.Equal(value, publishCommandArgs[^1]);
    }

    // params: BASE_IMAGE configures to override base image.
    [Theory]
    [InlineData("base-image")]
    [InlineData("")]
    public void ParamOverrideBaseImage(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_BASE_IMAGE", value}
            });
        
        bool expectCustomBeforeDirectoryBuildProps = !string.IsNullOrEmpty(value);

        Assert.Equal(expectCustomBeforeDirectoryBuildProps, publishCommandArgs.Contains($"-p:CustomBeforeDirectoryBuildProps={OverrideBaseImageTargetsPath}"));

        // ContainerFamily gets passed when a base image is specified.
        string? containerFamilyProp = publishCommandArgs.LastOrDefault(p => p.StartsWith("-p:ContainerFamily="))?.Substring("-p:ContainerFamily=".Length);
        string? expectedContainerFamily = string.IsNullOrEmpty(value) ? null : "";
        Assert.Equal(expectedContainerFamily, containerFamilyProp);
    }

    // params: BASE_IMAGE expansion by script.
    [Theory]
    [InlineData("runtime-repo", $"{TestImageRegistry}/runtime-repo", null)]
    [InlineData("ns/runtime-repo", $"{TestImageRegistry}/ns/runtime-repo", null)]
    [InlineData("server.io/ns/runtime-repo", $"server.io/ns/runtime-repo", null)]
    [InlineData("runtime-repo:tag1", $"{TestImageRegistry}/runtime-repo:tag1", null)]
    [InlineData("ns/runtime-repo:tag1", $"{TestImageRegistry}/ns/runtime-repo:tag1", null)]
    [InlineData("server.io/ns/runtime-repo:tag1", $"server.io/ns/runtime-repo:tag1", null)]
    [InlineData("runtime-repo@sha256:deadbeef", $"{TestImageRegistry}/runtime-repo@sha256:deadbeef", null)]
    [InlineData("ns/runtime-repo@sha256:deadbeef", $"{TestImageRegistry}/ns/runtime-repo@sha256:deadbeef", null)]
    [InlineData("server.io/ns/runtime-repo@sha256:deadbeef", $"server.io/ns/runtime-repo@sha256:deadbeef", null)]
    [InlineData("runtime-repo:tag1@sha256:deadbeef", $"{TestImageRegistry}/runtime-repo:tag1@sha256:deadbeef", null)]
    [InlineData("ns/runtime-repo:tag1@sha256:deadbeef", $"{TestImageRegistry}/ns/runtime-repo:tag1@sha256:deadbeef", null)]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", $"server.io/ns/runtime-repo:tag1@sha256:deadbeef", null)]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", $"server.io/ns/runtime-repo:tag1@sha256:deadbeef", "")]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", $"server.io/ns/runtime-repo:tag1@sha256:deadbeef", "ubi8")]
    public void ParamBaseImage(string baseImageParam, string expectedBaseImage, string? containerFamily)
    {
        IEnumerable<string>? args =
            !string.IsNullOrEmpty(containerFamily)
            ? new[] { "--build-props", $"ContainerFamily={containerFamily}" }
            : null;

        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_BASE_IMAGE", baseImageParam}
            },
            args: args);

        string? baseImage = publishCommandArgs.FirstOrDefault(p => p.StartsWith("-p:BASE_IMAGE="))?.Substring("-p:BASE_IMAGE=".Length);
        Assert.NotNull(baseImage);

        Assert.Equal(expectedBaseImage, baseImage);

        string expectedContainerFamily = containerFamily ?? "";
        string? containerFamilyProp = publishCommandArgs.LastOrDefault(p => p.StartsWith("-p:ContainerFamily="))?.Substring("-p:ContainerFamily=".Length);
        Assert.NotNull(containerFamilyProp);

        Assert.Equal(expectedContainerFamily, containerFamilyProp);
    }

    // params: BASE_IMAGE to MSBuild properties by .targets.
    [Theory]
    [InlineData("server.io/ns/runtime-repo", "", "server.io/ns/runtime-repo:<<version>>")]
    [InlineData("server.io/ns/runtime-repo:tag1", "", "server.io/ns/runtime-repo:tag1")]
    [InlineData("server.io/ns/runtime-repo@sha256:deadbeef", "", "server.io/ns/runtime-repo@sha256:deadbeef")]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", "", "server.io/ns/runtime-repo:tag1@sha256:deadbeef")]
    [InlineData("server.io/ns/runtime-repo", "ubi8", "server.io/ns/runtime-repo:<<version>>-ubi8")]
    [InlineData("server.io/ns/runtime-repo:tag1", "ubi8", "server.io/ns/runtime-repo:tag1")]
    [InlineData("server.io/ns/runtime-repo@sha256:deadbeef", "ubi8", "server.io/ns/runtime-repo@sha256:deadbeef")]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", "ubi8", "server.io/ns/runtime-repo:tag1@sha256:deadbeef")]
    public void BaseImageToContainerBaseImage(string baseImage, string containerFamily, string expectedContainerBaseImage)
    {
        expectedContainerBaseImage = expectedContainerBaseImage.Replace("<<version>>", DotnetVersion);

        // Run a script that writes the ContainerBaseImage to a file.
        string homeDirectory = CreateDirectory();
        string baseImageFile = $"{homeDirectory}/base_image";
        var runResult = RunTask(
            new()
            {
                { "PARAM_BASE_IMAGE", "dummy"}
            },
            dotnetStubScript:
            $"""
            #!/bin/sh
            set -e
            {PrintVersion}
            alias dotnet="/usr/bin/dotnet"
            dotnet new web -o /tmp/web
            dotnet publish /t:ComputeContainerBaseImage -p:CustomBeforeDirectoryBuildProps={OverrideBaseImageTargetsPath} -p:BASE_IMAGE={baseImage} -p:ContainerFamily={containerFamily} --getProperty:ContainerBaseImage /tmp/web --getResultOutputFile:{baseImageFile}
            {WriteImageDigest}
            """,
            homeDirectory: homeDirectory);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        Assert.True(File.Exists(baseImageFile));
        Assert.Equal(expectedContainerBaseImage, File.ReadAllText(baseImageFile).Trim());
    }

    // publish command
    [Fact]
    public void PublishCommandArgsMinimalParams()
    {
        string[] publishCommandArgs = GetPublishCommandArgs();
        string[] expected =
        [
            "publish",
            "--getProperty:GeneratedContainerDigest", $"--getResultOutputFile:{ImageDigestPath}",
            "-v", "",
            $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository=", "-p:ContainerImageTag=", "-p:ContainerImageTags=latest",
            "/t:PublishContainer",
            ""
        ];
        Assert.Equal(expected, publishCommandArgs);
    }

    // Helper to determine command line args passed to 'dotnet' when script runs with these envvars/args.
    private string[] GetPublishCommandArgs(
        Dictionary<string, string?>? envvars = null,
        IEnumerable<string>? args = null
    )
    {
        var runResult = RunWithDotnetAsScript(
            $$"""
            set -e
            {{WriteImageDigest}}
            ARGS=( "$@" )
            printf '%s\n' "${ARGS[@]}"
            """,
            envvars, args);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        // Trim the last newline from stdout.
        string stdout = runResult.StandardOutput;
        Assert.Equal('\n', stdout[^1]);
        stdout = stdout[0..^1];

        return stdout.Split('\n');
    }

    // Helper to check the output and exit code when 'dotnet' behaves as specified by the 'script' argument.
    private
        (string StandardOutput, string StandardError, int ExitCode)
        RunWithDotnetAsScript(
            string script,
            Dictionary<string, string?>? envvars = null,
            IEnumerable<string>? args = null,
            string? homeDirectory = null,
            string? tektonResultsDirectory = null)
    {
        string dotnetStubScript =
            $"""
            {PrintVersion}
            {WriteImageDigest}
            {script}
            """;
        return RunTask(envvars ?? new(), args, homeDirectory, tektonResultsDirectory, dotnetStubScript);
    }

    // Helper to run the Task script in a container with the provided args and environment.
    private
        (string StandardOutput, string StandardError, int ExitCode)
        RunTask(Dictionary<string, string?> envvars, IEnumerable<string>? args = null, string? homeDirectory = null, string? tektonResultsDirectory = null, string? dotnetStubScript = null)
    {
        // Throw an exception on Windows so compiler doesn't emit warnings for the UnixFileMode usage.
        if (OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("Running these tests on Windows is not supported");
        }

        string scriptFilePath = GenerateTempFilePath("script.sh");
        File.WriteAllText(scriptFilePath, Script);
        new FileInfo(scriptFilePath).UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        dotnetStubScript ??=
            """
            echo "No dotnet stub" >&2
            exit 1
            """;
        string dotnetStubFilePath = GenerateTempFilePath("dotnet");
        File.WriteAllText(dotnetStubFilePath, dotnetStubScript);
        new FileInfo(dotnetStubFilePath).UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        homeDirectory ??= CreateDirectory();
        new DirectoryInfo(homeDirectory).UnixFileMode |= UnixFileMode.GroupWrite;

        tektonResultsDirectory ??= CreateDirectory();
        new DirectoryInfo(tektonResultsDirectory).UnixFileMode |= UnixFileMode.GroupWrite;

        List<string> envvarArgs = new();

        // Add all envvars that are defined by the task to avoid unbound variable errors from bash.
        YamlSequenceNode taskEnvvars = (Yaml["spec"]["steps"][0]["env"] as YamlSequenceNode)!;
        foreach (var envvar in taskEnvvars)
        {
            envvarArgs.Add("-e");
            string envvarName = (string)envvar["name"]!;
            switch (envvarName)
            {
                case "PARAM_SDK_IMAGE":
                    envvarArgs.Add($"{envvarName}={TestImageRegistry}/{TestDotnetNamespace}/{TestDotnetSdkRepository}");
                    break;
                case "CurrentKubernetesNamespace":
                    envvarArgs.Add($"{envvarName}={TestCurrentNamespace}");
                    break;
                default:
                    envvarArgs.Add($"{envvarName}=");
                    break;
            }
        }

        foreach (var envvar in envvars)
        {
            if (envvar.Value is not null)
            {
                envvarArgs.Add("-e");
                envvarArgs.Add($"{envvar.Key}={envvar.Value}");

                // Mount workspaces at the same location in the container.
                if (envvar.Key.StartsWith("WORKSPACE_") && envvar.Key.EndsWith("_PATH"))
                {
                    envvarArgs.Add($"-v");
                    envvarArgs.Add($"{envvar.Value}:{envvar.Value}:z");
                }
            }
        }

        args ??= [];
        List<string> podmanArgs =
        [
            "run",
            "-q",
            "--rm",
            ..envvarArgs,
            "-e", $"HOME={homeDirectory}", "-v", $"{homeDirectory}:/{homeDirectory}/:z",
            "-v", $"{tektonResultsDirectory}:/tekton/results:z",
            "-v", $"{Path.GetDirectoryName(scriptFilePath)}:/task-script/:z",
            "-v", $"{Path.GetDirectoryName(dotnetStubFilePath)}:/dotnet-stub/:z",
            "-e", "PATH=/dotnet-stub:/usr/bin:/bin",
            SdkImage,
            "/task-script/script.sh",
            "--",
            ..args
        ];

        int exitCode = 255;
        var readTask = Command.ReadAsync(
            "podman",
            podmanArgs,
            handleExitCode: (val) => { exitCode = val; return true; });
        var readResult = readTask.GetAwaiter().GetResult();

        return (readResult.StandardOutput, readResult.StandardError, exitCode);
    }
}

// Run DotnetPublishImageTaskScriptTests tests against Red Hat .NET 9 image.
[Collection(nameof(TektonTaskTestCollection))]
public class DotnetPublishImageTaskScriptTestsRedHatNet9 : DotnetPublishImageTaskScriptTests
{
    protected override string SdkImage => "registry.access.redhat.com/ubi8/dotnet-90";
    protected override string DotnetVersion => "9.0";

    public DotnetPublishImageTaskScriptTestsRedHatNet9(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}

// Run DotnetPublishImageTaskScriptTests tests against Microsoft .NET 9 image.
[Collection(nameof(TektonTaskTestCollection))]
public class DotnetPublishImageTaskScriptTestsMicrosoftNet9 : DotnetPublishImageTaskScriptTests
{
    protected override string SdkImage => "mcr.microsoft.com/dotnet/sdk:9.0";
    protected override string DotnetVersion => "9.0";

    public DotnetPublishImageTaskScriptTestsMicrosoftNet9(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}