#!/usr/bin/env bash
set -euo pipefail

toolchain_bin="${HOME}/retro68-build/toolchain/bin"
action="${1:-}"
disk_image="${2:-}"
host_file="${3:-}"
mounted=0
temporary_directory=""

for tool in hmount hcopy humount hcd hmkdir; do
    if [[ ! -x "${toolchain_bin}/${tool}" ]]; then
        echo "Retro68 HFS utility is missing: ${toolchain_bin}/${tool}" >&2
        exit 1
    fi
done

cleanup() {
    if [[ "${mounted}" -eq 1 ]]; then
        "${toolchain_bin}/humount"
        mounted=0
    fi
    if [[ -n "${temporary_directory}" ]]; then
        rm -f -- \
            "${temporary_directory}/application.bin" \
            "${temporary_directory}/AgentBaudRate"
        rmdir -- "${temporary_directory}"
    fi
}
trap cleanup EXIT

escape_hfs_path() {
    local path="$1"
    path="${path//\\/\\\\}"
    path="${path// /\\ }"
    path="${path//(/\\(}"
    path="${path//)/\\)}"
    printf '%s' "${path}"
}

case "${action}" in
    stage-request)
        if [[ $# -ne 3 || -z "${disk_image}" || -z "${host_file}" ]]; then
            echo "Usage: mac-test-bridge.sh stage-request <hfs-disk> <request-file>" >&2
            exit 2
        fi
        if [[ ! -f "${disk_image}" || ! -f "${host_file}" ]]; then
            echo "HFS disk image and request file must exist." >&2
            exit 1
        fi
        "${toolchain_bin}/hmount" "${disk_image}"
        mounted=1
        "${toolchain_bin}/hcopy" -r "${host_file}" ":MacStudio.in"
        echo "Staged request file on HFS disk: MacStudio.in"
        ;;
    extract-response)
        if [[ $# -ne 3 || -z "${disk_image}" || -z "${host_file}" ]]; then
            echo "Usage: mac-test-bridge.sh extract-response <hfs-disk> <response-file>" >&2
            exit 2
        fi
        if [[ ! -f "${disk_image}" ]]; then
            echo "HFS disk image was not found: ${disk_image}" >&2
            exit 1
        fi
        "${toolchain_bin}/hmount" "${disk_image}"
        mounted=1
        "${toolchain_bin}/hcopy" -r ":MacStudio.out" "${host_file}"
        echo "Extracted guest telemetry to: ${host_file}"
        ;;
    export-macbinary)
        if [[ $# -ne 4 ||
            -z "${disk_image}" || -z "${3:-}" || -z "${4:-}" ]]; then
            echo "Usage: mac-test-bridge.sh export-macbinary <hfs-disk> <app-name> <package-file>" >&2
            exit 2
        fi
        app_name="$3"
        package_file="$4"
        if [[ ! -f "${disk_image}" ]]; then
            echo "HFS disk image was not found: ${disk_image}" >&2
            exit 1
        fi
        if [[ ! "${app_name}" =~ ^[A-Za-z][A-Za-z0-9_]{0,30}$ ]]; then
            echo "Application name is invalid: ${app_name}" >&2
            exit 2
        fi
        package_directory="$(dirname -- "${package_file}")"
        if [[ ! -d "${package_directory}" ]]; then
            echo "Package output directory was not found: ${package_directory}" >&2
            exit 1
        fi

        temporary_directory="$(mktemp -d "${package_directory}/.macstudio-export.XXXXXX")"
        application_file="${temporary_directory}/application.bin"
        "${toolchain_bin}/hmount" "${disk_image}"
        mounted=1
        "${toolchain_bin}/hcopy" -m ":${app_name}" "${application_file}"
        "${toolchain_bin}/humount"
        mounted=0

        if [[ ! -s "${application_file}" ]]; then
            echo "MacBinary export was empty: ${app_name}" >&2
            exit 1
        fi
        package_size="$(wc -c < "${application_file}")"
        if [[ "${package_size}" -lt 128 ]]; then
            echo "MacBinary export is shorter than its 128-byte header: ${package_size} bytes." >&2
            exit 1
        fi
        mv -f -- "${application_file}" "${package_file}"
        echo "Exported ${app_name} as ${package_size}-byte MacBinary II package: ${package_file}"
        ;;
    install-startup-app)
        if [[ ( $# -lt 5 || $# -gt 7 ) ||
            -z "${2:-}" || -z "${3:-}" || -z "${4:-}" || -z "${5:-}" ]]; then
            echo "Usage: mac-test-bridge.sh install-startup-app <system-disk> <app-disk> <app-name> <startup-items-path> [request-file [agent-baud-rate]]" >&2
            exit 2
        fi
        system_disk="$2"
        app_disk="$3"
        app_name="$4"
        startup_items_path="$5"
        request_file="${6:-}"
        agent_baud_rate="${7:-}"
        if [[ "${request_file}" == "__MACSTUDIO_NO_REQUEST__" ]]; then
            request_file=""
        fi
        if [[ ! -f "${system_disk}" || ! -f "${app_disk}" ]]; then
            echo "System and application HFS disk images must exist." >&2
            exit 1
        fi
        if [[ -n "${request_file}" && ! -f "${request_file}" ]]; then
            echo "Test request file was not found: ${request_file}" >&2
            exit 1
        fi
        if [[ -n "${agent_baud_rate}" &&
            ! "${agent_baud_rate}" =~ ^(300|600|1200|1800|2400|3600|4800|7200|9600|14400|19200|28800|38400|57600)$ ]]; then
            echo "Unsupported Agent baud rate: ${agent_baud_rate}" >&2
            exit 2
        fi
        if [[ ! "${app_name}" =~ ^[A-Za-z][A-Za-z0-9_]{0,30}$ ]]; then
            echo "Application name is invalid: ${app_name}" >&2
            exit 2
        fi

        temporary_directory="$(mktemp -d)"
        application_file="${temporary_directory}/application.bin"
        "${toolchain_bin}/hmount" "${app_disk}"
        mounted=1
        "${toolchain_bin}/hcopy" -m ":${app_name}" "${application_file}"
        "${toolchain_bin}/humount"
        mounted=0

        "${toolchain_bin}/hmount" "${system_disk}"
        mounted=1
        if [[ -n "${request_file}" ]]; then
            "${toolchain_bin}/hcopy" -r "${request_file}" ":MacStudio.in"
            echo "Staged test request on the disposable System 7 volume."
        fi
        if [[ -n "${agent_baud_rate}" ]]; then
            printf '%s' "${agent_baud_rate}" > "${temporary_directory}/AgentBaudRate"
            if ! "${toolchain_bin}/hcd" ":MacStudio" >/dev/null 2>&1; then
                "${toolchain_bin}/hmkdir" ":MacStudio"
            fi
            "${toolchain_bin}/hcopy" -r \
                "${temporary_directory}/AgentBaudRate" \
                ":MacStudio:AgentBaudRate"
            echo "Configured the Agent for ${agent_baud_rate} baud."
        fi
        escaped_startup_items_path="$(escape_hfs_path "${startup_items_path}")"
        "${toolchain_bin}/hcd" "${escaped_startup_items_path}"
        "${toolchain_bin}/hcopy" -m \
            "${application_file}" \
            ":${app_name}"
        echo "Installed ${app_name} in ${startup_items_path}."
        ;;
    *)
        echo "Unsupported bridge action: ${action}" >&2
        exit 2
        ;;
esac

cleanup
trap - EXIT
