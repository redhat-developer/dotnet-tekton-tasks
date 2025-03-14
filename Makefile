BIN = $(CURDIR)/.bin

RELEASE_VERSION ?= v0.0.0

# release directory where the catalog-cd resources are output.
RELEASE_DIR ?= /tmp/dotnet-tekton-tasks/$(RELEASE_VERSION)
VERSION_NUMBER := $(shell echo $(RELEASE_VERSION) | sed 's/^v//')

# bats entry point and default flags
BATS_CORE = ./test/.bats/bats-core/bin/bats
BATS_FLAGS ?= --print-output-on-failure --show-output-of-passing-tests --verbose-run

TEKTON_TASKS = ./src/*/*.yaml

# path to the bats test files, overwite the variables below to tweak the test scope
E2E_TESTS ?= ./test/e2e/*.bats
E2E_TEST_DIR ?= ./test/e2e

# Test resources
E2E_PVC ?= test/e2e/resources/pvc-dotnet.yaml
# Test application repo
E2E_DOTNET_VERSION ?= 8.0
E2E_DOTNET_PARAMS_REVISION ?= dotnet-$(E2E_DOTNET_VERSION)
E2E_DOTNET_PARAMS_URL ?= https://github.com/redhat-developer/s2i-dotnetcore-ex
E2E_DOTNET_PARAMS_PROJECT ?= app
# Test output image
E2E_DOTNET_PARAMS_IMAGE ?= library/app-image:latest
# Test input images
E2E_DOTNET_INSECURE_REGISTRY = registry.registry.svc.cluster.local:32222
E2E_DOTNET_PARAMS_SDK_IMAGE = registry.registry.svc.cluster.local:32222/dotnet-images/dotnet:latest
E2E_DOTNET_PARAMS_BASE_IMAGE = dotnet-images/dotnet-runtime

# generic arguments employed on most of the targets
ARGS ?=

# making sure the variables declared in the Makefile are exported to the excutables/scripts invoked
# on all targets
.EXPORT_ALL_VARIABLES:

$(BIN):
	@mkdir -p $@

CATALOGCD = $(or ${CATALOGCD_BIN},${CATALOGCD_BIN},$(BIN)/catalog-cd)
$(BIN)/catalog-cd: $(BIN)
	curl -fsL https://github.com/openshift-pipelines/catalog-cd/releases/download/v0.3.0/catalog-cd_0.3.0_linux_x86_64.tar.gz | tar xzf - -C $(BIN) catalog-cd

.PHONY: prepare-release
prepare-release:
	mkdir -p $(RELEASE_DIR) || true

.PHONY: release
release: ${CATALOGCD} prepare-release
		$(CATALOGCD) release --output ${RELEASE_DIR} --version $(VERSION_NUMBER) $(TEKTON_TASKS)

.PHONY: github-draft-release
github-draft-release: release
	gh release create --draft $(RELEASE_VERSION) --generate-notes && \
	gh release upload $(RELEASE_VERSION) $(RELEASE_DIR)/catalog.yaml && \
	gh release upload $(RELEASE_VERSION) $(RELEASE_DIR)/resources.tar.gz

clean:

# runs bats-core against the pre-determined tests
.PHONY: bats
bats: install
	$(BATS_CORE) $(BATS_FLAGS) $(ARGS) $(E2E_TESTS)

# install tasks
install:
	kubectl apply -f $(TEKTON_TASKS)

.PHONY: prepare-e2e
prepare-e2e:
	kubectl apply -f ${E2E_PVC}

# run end-to-end tests against the current kuberentes context, it will required a cluster with tekton
# pipelines and other requirements installed, before start testing the target invokes the
# installation of the current project's task (using helm).
.PHONY: test-e2e
test-e2e: prepare-e2e
test-e2e: E2E_TESTS = $(E2E_TEST_DIR)/*.bats
test-e2e: bats
