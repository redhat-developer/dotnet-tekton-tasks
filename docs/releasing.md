# Releasing

This repository is added to openshift-pipelines/tektoncd-catalog [externals.yaml](https://github.com/openshift-pipelines/tektoncd-catalog/blob/main/externals.yaml) which causes the Tekton resources to be automatically be pulled from GitHub releases. The contract for this is specified in https://github.com/openshift-pipelines/tektoncd-catalog/blob/main/docs/workflow-provide-your-tekton-resources.md.

## Creating a draft release

To facilitate the release process, this repository contains a GitHub workflow that creates a GitHub draft release when a `v*` tag is pushed to the repository. The draft release includes the expected assets for tektoncd-catalog as well as release notes based on the commit log.

1. For the tag name, pick a version that makes sense based on the changes that were made since the previous release.

2. `tektoncd-catalog` expects the Task versions to match with the release version. Update `app.kubernetes.io/version` in the Tasks to match the release version and commit it to the repository.
```
...
git commit -m "Update task versions for release."
...
```
3. Pushing a tag for the new version:
```
git checkout main
git pull
git tag vx.y.z
git push <remote> vx.y.z
```
4. Once the tag was pushed, progress for the workflow can be followed under [GitHub Actions](https://github.com/redhat-developer/dotnet-tekton-tasks/actions). When complete, the draft release shows up on the [GitHub Releases](https://github.com/redhat-developer/dotnet-tekton-tasks/releases) page.

## Completing the release

1. When the draft release is available, the release notes can be edited.

1. Before publishing the release, verify the assets contain both a `resources.tar.gz` and a `catalog.yaml` file.

1. Verify the `catalog.yaml` includes all tasks defined in the repository, and ensure the `version` fields matches the release version.

1. Click _Publish Release_ to make the release public.
