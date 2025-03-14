#!/usr/bin/env bats

load '../.bats/bats-support/load'
load '../.bats/bats-assert/load'
load '../.bats/bats-file/load'

# root directory for the test cases
export BASE_DIR=""

function setup() {
	# creating a temporary directory before each test run below
	BASE_DIR="$(mktemp -d ${BATS_TMPDIR}/bats.XXXXXX)"

	chmod -R 777 ${BASE_DIR} >/dev/null

	# making sure the tests will "fail fast", when a given test fails the suite stops
	[ -n "${BATS_TEST_COMPLETED}" ] || touch ${BATS_PARENT_TMPNAME}.skip
}

function teardown() {
	rm -rfv ${BASE_DIR} || true
}

# Assert elements of a Tekton resource (taskrun or pipelinerun) using bats framework, the first
# function argument represents the Tekton resource and the rest is passed to "assert_output".
function assert_tekton_resource () {
    declare tmpl_file="${BASE_DIR}/go-template.tpl"
    # the following template is able to extract information from TaskRun and PipelineRun resources,
    # and as well supports the current Tekton Pipeline version using a different `.task.results`
    # attribute
    cat >${tmpl_file} <<EOS
{{- range .status.conditions }}
    {{- if and (eq .type "Succeeded") (eq .status "True") }}
        {{- printf "%s\n" .message -}}
    {{- end -}}
{{- end }}
{{- range .status.results }}
    {{- printf "%s=%s\n" .name .value -}}
{{- end -}}
EOS

    run tkn ${1} describe --last --output=go-template-file --template=${tmpl_file}
    shift
    assert_success
    assert_output ${*}
}
