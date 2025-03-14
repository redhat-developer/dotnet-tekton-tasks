# Releasing

This repository is added to openshift-pipelines/tektoncd-catalog [externals.yaml](https://github.com/openshift-pipelines/tektoncd-catalog/blob/main/externals.yaml) which causes the Tekton resources to be automatically be pulled from GitHub releases. The contract for this is specified in https://github.com/openshift-pipelines/tektoncd-catalog/blob/main/docs/workflow-provide-your-tekton-resources.md.

To facilitate the process, this repository contains a GitHub workflow that creates a GitHub draft release when a `v*` tag is pushed to the repository.  The draft release includes the expected assets for tektoncd-catalog as well as release notes based on the commit log.

For the tag name, pick a version that makes sense based on the changes that were made since the previous release.

Example `git` commands for pushing a tag:

```
git tag v0.0.1
git push v0.0.1
```

Once the tag was pushed, progress for the workflow can be followed under [GitHub Actions](https://github.com/redhat-developer/dotnet-tekton-tasks/actions). When complete, the draft release shows up on the [GitHub Releases](https://github.com/redhat-developer/dotnet-tekton-tasks/releases) page.

When the draft release is available, the release notes can redacted.

Before publishing the release, verify the assets contain both a `resources.tar.gz` and a `catalog.yaml` file. The `catalog.yaml` must include all tasks defined in the repository.
