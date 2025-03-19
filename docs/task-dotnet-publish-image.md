# `dotnet-publish-image` Tekton Task

The `dotnet-publish-image` task builds a container image from a .NET project and pushes it to a container registry using the .NET SDK's built-in container tooling.

Please consider the [Workspaces](#workspaces), [Parameters](#parameters) and [Results](#results) described below.

# Workspaces

A single `source` workspace is required for this task, while the optional `docker` workspaces will allow providing additional container registry credentials.

## `source`

The `source` is a required workspace from where the .NET source code will be provided. The `source` workspace is be populated by an earlier task, like `git-clone`.

### `dockerconfig`

The `dockerconfig` is a optional workspace to provide container registry credential in addition to those assigned to pipeline service account.

A file should be placed at the root of the workspace with a name of `config.json` or `.dockerconfigjson`. The file must use the `.docker/config.json` format.

# Parameters

The following parameters are supported by this task:

| Parameter                     |   Type   | Default         | Description                                                                                                               |
| :---------------------------- | :------: | :-------------- | :------------------------------------------------------------------------------------------------------------------------ |
| `SDK_IMAGE`                   | `string` | (required)      | Fully qualified name of the .NET SDK image used to build the application image. |
| `PROJECT`                     | `string` | (required)      | Path of the .NET project file in the source workspace. |
| `IMAGE_NAME`                  | `string` | (required)      | Name of the application image repository to push to.<br/>When the name does not include a registry, the `SDK_IMAGE` registry is used. |
| `BASE_IMAGE`                  | `string` | `""` (empty)    | When set, overrides the base image used for the application image.<br/>When the name does not include a registry, the `SDK_IMAGE` registry is used.<br/>If the name does not include a tag, the .NET project target version is used (for example: `9.0`).<br/>The current Kubernetes namespace can be used in the name via `$(context.taskRun.namespace)`. |
| `PRE_PUBLISH_SCRIPT`          | `string` | `""` (empty)    | Shell commands to run before publishing the image.<br/>The shell is configured to exit immediately when commands fail with a non-zero status. |
| `VERBOSITY`                   | `string` | `minimal`       | MSBuild verbosity level. Allowed values are `q[uiet]`, `m[inimal]`, `n[ormal]`, `d[etailed]`, and `diag[nostic]`. |
| `BUILD_PROPS`                 | `array` of `string` | `[]` (empty) | MSBuild properties to pass to the publish command. |
| `ENV_VARS`                    | `array` of `string` | `[]` (empty) | Environment variables. |

# Results

The following results are produced by this task:

| Name             | Description                              |
| :--------------- | :--------------------------------------- |
| `IMAGE_DIGEST`   | Digest of the application image. |
| `IMAGE`          | Fully qualified application image name with digest. |
