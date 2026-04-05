#include <windows.h>
#include <fwpmu.h>
#include <winsvc.h>
#include <strsafe.h>

#include <array>
#include <functional>
#include <iostream>
#include <string>
#include <string_view>
#include <vector>

#include "..\..\Common\AppTunnelWfpShared.h"

namespace
{
    struct FilterDefinition
    {
        const GUID* FilterKey;
        const GUID* CalloutKey;
        const GUID* LayerKey;
        PCWSTR CalloutName;
        PCWSTR FilterName;
    };

    constexpr std::array<FilterDefinition, 8> Filters =
    {{
        { &APPTUNNEL_WFP_FILTER_AUTH_CONNECT_V4, &APPTUNNEL_WFP_CALLOUT_AUTH_CONNECT_V4, &FWPM_LAYER_ALE_AUTH_CONNECT_V4, L"App Tunnel Auth Connect v4", L"App Tunnel Auth Connect v4 Filter" },
        { &APPTUNNEL_WFP_FILTER_AUTH_CONNECT_V6, &APPTUNNEL_WFP_CALLOUT_AUTH_CONNECT_V6, &FWPM_LAYER_ALE_AUTH_CONNECT_V6, L"App Tunnel Auth Connect v6", L"App Tunnel Auth Connect v6 Filter" },
        { &APPTUNNEL_WFP_FILTER_RECV_ACCEPT_V4, &APPTUNNEL_WFP_CALLOUT_RECV_ACCEPT_V4, &FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4, L"App Tunnel Recv Accept v4", L"App Tunnel Recv Accept v4 Filter" },
        { &APPTUNNEL_WFP_FILTER_RECV_ACCEPT_V6, &APPTUNNEL_WFP_CALLOUT_RECV_ACCEPT_V6, &FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6, L"App Tunnel Recv Accept v6", L"App Tunnel Recv Accept v6 Filter" },
        { &APPTUNNEL_WFP_FILTER_FLOW_ESTABLISHED_V4, &APPTUNNEL_WFP_CALLOUT_FLOW_ESTABLISHED_V4, &FWPM_LAYER_ALE_FLOW_ESTABLISHED_V4, L"App Tunnel Flow Established v4", L"App Tunnel Flow Established v4 Filter" },
        { &APPTUNNEL_WFP_FILTER_FLOW_ESTABLISHED_V6, &APPTUNNEL_WFP_CALLOUT_FLOW_ESTABLISHED_V6, &FWPM_LAYER_ALE_FLOW_ESTABLISHED_V6, L"App Tunnel Flow Established v6", L"App Tunnel Flow Established v6 Filter" },
        { &APPTUNNEL_WFP_FILTER_CONNECT_REDIRECT_V4, &APPTUNNEL_WFP_CALLOUT_CONNECT_REDIRECT_V4, &FWPM_LAYER_ALE_CONNECT_REDIRECT_V4, L"App Tunnel Connect Redirect v4", L"App Tunnel Connect Redirect v4 Filter" },
        { &APPTUNNEL_WFP_FILTER_CONNECT_REDIRECT_V6, &APPTUNNEL_WFP_CALLOUT_CONNECT_REDIRECT_V6, &FWPM_LAYER_ALE_CONNECT_REDIRECT_V6, L"App Tunnel Connect Redirect v6", L"App Tunnel Connect Redirect v6 Filter" },
    }};

    bool Equals(std::wstring_view left, std::wstring_view right)
    {
        return _wcsicmp(left.data(), right.data()) == 0;
    }

    std::wstring EscapeJson(std::wstring_view value)
    {
        std::wstring escaped;
        escaped.reserve(value.size() + 8);

        for (const auto ch : value)
        {
            switch (ch)
            {
            case L'\\':
                escaped += L"\\\\";
                break;
            case L'"':
                escaped += L"\\\"";
                break;
            case L'\r':
                escaped += L"\\r";
                break;
            case L'\n':
                escaped += L"\\n";
                break;
            case L'\t':
                escaped += L"\\t";
                break;
            default:
                escaped += ch;
                break;
            }
        }

        return escaped;
    }

    void PrintJsonResult(bool succeeded, std::wstring_view operation, std::wstring_view message)
    {
        std::wcout
            << L"{"
            << L"\"succeeded\":" << (succeeded ? L"true" : L"false")
            << L",\"operation\":\"" << EscapeJson(operation) << L"\""
            << L",\"message\":\"" << EscapeJson(message) << L"\""
            << L"}"
            << std::endl;
    }

    DWORD WithEngine(const std::function<DWORD(HANDLE)>& action)
    {
        HANDLE engine = nullptr;
        const auto status = FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, nullptr, &engine);
        if (status != ERROR_SUCCESS)
        {
            return status;
        }

        const auto result = action(engine);
        FwpmEngineClose0(engine);
        return result;
    }

    DWORD WithTransaction(HANDLE engine, const std::function<DWORD()>& action)
    {
        auto status = FwpmTransactionBegin0(engine, 0);
        if (status != ERROR_SUCCESS)
        {
            return status;
        }

        status = action();
        if (status == ERROR_SUCCESS)
        {
            const auto commitStatus = FwpmTransactionCommit0(engine);
            if (commitStatus == ERROR_SUCCESS)
            {
                return ERROR_SUCCESS;
            }

            FwpmTransactionAbort0(engine);
            return commitStatus;
        }

        FwpmTransactionAbort0(engine);
        return status;
    }

    DWORD EnsureProviderAndSublayer(HANDLE engine)
    {
        FWPM_PROVIDER0 provider = {};
        provider.providerKey = const_cast<GUID*>(&APPTUNNEL_WFP_PROVIDER_GUID);
        provider.displayData.name = const_cast<wchar_t*>(L"App Tunnel");
        provider.displayData.description = const_cast<wchar_t*>(L"App Tunnel WFP provider");

        auto status = FwpmProviderAdd0(engine, &provider, nullptr);
        if (status != ERROR_SUCCESS && status != FWP_E_ALREADY_EXISTS)
        {
            return status;
        }

        FWPM_SUBLAYER0 sublayer = {};
        sublayer.subLayerKey = APPTUNNEL_WFP_SUBLAYER_GUID;
        sublayer.displayData.name = const_cast<wchar_t*>(L"App Tunnel Sublayer");
        sublayer.displayData.description = const_cast<wchar_t*>(L"App Tunnel routing filters");
        sublayer.providerKey = const_cast<GUID*>(&APPTUNNEL_WFP_PROVIDER_GUID);
        sublayer.weight = 0x500;

        status = FwpmSubLayerAdd0(engine, &sublayer, nullptr);
        if (status != ERROR_SUCCESS && status != FWP_E_ALREADY_EXISTS)
        {
            return status;
        }

        return ERROR_SUCCESS;
    }

    DWORD RemoveProviderAndSublayer(HANDLE engine)
    {
        DWORD status = ERROR_SUCCESS;

        const auto sublayerStatus = FwpmSubLayerDeleteByKey0(engine, &APPTUNNEL_WFP_SUBLAYER_GUID);
        if (sublayerStatus != ERROR_SUCCESS && sublayerStatus != FWP_E_SUBLAYER_NOT_FOUND)
        {
            status = sublayerStatus;
        }

        const auto providerStatus = FwpmProviderDeleteByKey0(engine, &APPTUNNEL_WFP_PROVIDER_GUID);
        if (providerStatus != ERROR_SUCCESS && providerStatus != FWP_E_PROVIDER_NOT_FOUND)
        {
            status = providerStatus;
        }

        return status;
    }

    DWORD AddCalloutAndFilter(HANDLE engine, const FilterDefinition& definition)
    {
        FWPM_CALLOUT0 callout = {};
        callout.calloutKey = *definition.CalloutKey;
        callout.displayData.name = const_cast<wchar_t*>(definition.CalloutName);
        callout.displayData.description = const_cast<wchar_t*>(definition.CalloutName);
        callout.providerKey = const_cast<GUID*>(&APPTUNNEL_WFP_PROVIDER_GUID);
        callout.applicableLayer = *definition.LayerKey;

        auto status = FwpmCalloutAdd0(engine, &callout, nullptr, nullptr);
        if (status != ERROR_SUCCESS && status != FWP_E_ALREADY_EXISTS)
        {
            return status;
        }

        FWPM_FILTER0 filter = {};
        filter.filterKey = *definition.FilterKey;
        filter.displayData.name = const_cast<wchar_t*>(definition.FilterName);
        filter.providerKey = const_cast<GUID*>(&APPTUNNEL_WFP_PROVIDER_GUID);
        filter.layerKey = *definition.LayerKey;
        filter.subLayerKey = APPTUNNEL_WFP_SUBLAYER_GUID;
        filter.weight.type = FWP_UINT8;
        filter.weight.uint8 = 15;
        filter.action.type = FWP_ACTION_CALLOUT_TERMINATING;
        filter.action.calloutKey = *definition.CalloutKey;

        status = FwpmFilterAdd0(engine, &filter, nullptr, nullptr);
        if (status != ERROR_SUCCESS && status != FWP_E_ALREADY_EXISTS)
        {
            return status;
        }

        return ERROR_SUCCESS;
    }

    DWORD RemoveCalloutAndFilter(HANDLE engine, const FilterDefinition& definition)
    {
        DWORD status = ERROR_SUCCESS;

        const auto filterStatus = FwpmFilterDeleteByKey0(engine, definition.FilterKey);
        if (filterStatus != ERROR_SUCCESS && filterStatus != FWP_E_FILTER_NOT_FOUND)
        {
            status = filterStatus;
        }

        const auto calloutStatus = FwpmCalloutDeleteByKey0(engine, definition.CalloutKey);
        if (calloutStatus != ERROR_SUCCESS && calloutStatus != FWP_E_CALLOUT_NOT_FOUND)
        {
            status = calloutStatus;
        }

        return status;
    }

    DWORD EnableFilters()
    {
        return WithEngine([](HANDLE engine)
        {
            return WithTransaction(engine, [engine]()
            {
                auto status = EnsureProviderAndSublayer(engine);
                if (status != ERROR_SUCCESS)
                {
                    return status;
                }

                for (const auto& definition : Filters)
                {
                    status = AddCalloutAndFilter(engine, definition);
                    if (status != ERROR_SUCCESS)
                    {
                        return status;
                    }
                }

                return ERROR_SUCCESS;
            });
        });
    }

    DWORD DisableFilters(bool removeProviderAndSublayer)
    {
        return WithEngine([removeProviderAndSublayer](HANDLE engine)
        {
            return WithTransaction(engine, [engine, removeProviderAndSublayer]()
            {
                DWORD status = ERROR_SUCCESS;

                for (const auto& definition : Filters)
                {
                    const auto deleteStatus = RemoveCalloutAndFilter(engine, definition);
                    if (deleteStatus != ERROR_SUCCESS)
                    {
                        status = deleteStatus;
                    }
                }

                if (removeProviderAndSublayer)
                {
                    const auto providerStatus = RemoveProviderAndSublayer(engine);
                    if (providerStatus != ERROR_SUCCESS)
                    {
                        status = providerStatus;
                    }
                }

                return status;
            });
        });
    }

    bool WaitForServiceState(SC_HANDLE service, DWORD desiredState, DWORD timeoutMs)
    {
        SERVICE_STATUS_PROCESS status = {};
        DWORD bytesNeeded = 0;
        const auto start = GetTickCount64();

        while (GetTickCount64() - start < timeoutMs)
        {
            if (!QueryServiceStatusEx(
                service,
                SC_STATUS_PROCESS_INFO,
                reinterpret_cast<LPBYTE>(&status),
                sizeof(status),
                &bytesNeeded))
            {
                return false;
            }

            if (status.dwCurrentState == desiredState)
            {
                return true;
            }

            Sleep(250);
        }

        return false;
    }

    DWORD EnsureDriverServiceStarted(SC_HANDLE service)
    {
        SERVICE_STATUS_PROCESS status = {};
        DWORD bytesNeeded = 0;

        if (!QueryServiceStatusEx(
            service,
            SC_STATUS_PROCESS_INFO,
            reinterpret_cast<LPBYTE>(&status),
            sizeof(status),
            &bytesNeeded))
        {
            return GetLastError();
        }

        if (status.dwCurrentState == SERVICE_RUNNING)
        {
            return ERROR_SUCCESS;
        }

        if (status.dwCurrentState == SERVICE_STOP_PENDING)
        {
            if (!WaitForServiceState(service, SERVICE_STOPPED, 20000))
            {
                return ERROR_SERVICE_REQUEST_TIMEOUT;
            }
        }

        if (!StartServiceW(service, 0, nullptr))
        {
            const auto error = GetLastError();
            if (error != ERROR_SERVICE_ALREADY_RUNNING)
            {
                return error;
            }
        }

        return WaitForServiceState(service, SERVICE_RUNNING, 20000)
            ? ERROR_SUCCESS
            : ERROR_SERVICE_REQUEST_TIMEOUT;
    }

    DWORD InstallDriverService(const std::wstring& driverPath)
    {
        const auto scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
        if (scm == nullptr)
        {
            return GetLastError();
        }

        auto service = CreateServiceW(
            scm,
            APPTUNNEL_WFP_DRIVER_SERVICE_NAME,
            APPTUNNEL_WFP_DRIVER_DISPLAY_NAME,
            SERVICE_START | DELETE | SERVICE_STOP | SERVICE_QUERY_STATUS | SERVICE_CHANGE_CONFIG,
            SERVICE_KERNEL_DRIVER,
            SERVICE_DEMAND_START,
            SERVICE_ERROR_NORMAL,
            driverPath.c_str(),
            nullptr,
            nullptr,
            nullptr,
            nullptr,
            nullptr);

        if (service == nullptr)
        {
            const auto error = GetLastError();
            if (error != ERROR_SERVICE_EXISTS)
            {
                CloseServiceHandle(scm);
                return error;
            }

            service = OpenServiceW(
                scm,
                APPTUNNEL_WFP_DRIVER_SERVICE_NAME,
                SERVICE_START | DELETE | SERVICE_STOP | SERVICE_QUERY_STATUS | SERVICE_CHANGE_CONFIG);
            if (service == nullptr)
            {
                const auto openError = GetLastError();
                CloseServiceHandle(scm);
                return openError;
            }

            if (!ChangeServiceConfigW(
                service,
                SERVICE_NO_CHANGE,
                SERVICE_DEMAND_START,
                SERVICE_NO_CHANGE,
                driverPath.c_str(),
                nullptr,
                nullptr,
                nullptr,
                nullptr,
                nullptr,
                APPTUNNEL_WFP_DRIVER_DISPLAY_NAME))
            {
                const auto configError = GetLastError();
                CloseServiceHandle(service);
                CloseServiceHandle(scm);
                return configError;
            }
        }

        const auto status = EnsureDriverServiceStarted(service);
        CloseServiceHandle(service);
        CloseServiceHandle(scm);
        return status;
    }

    DWORD UninstallDriverService()
    {
        const auto scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (scm == nullptr)
        {
            return GetLastError();
        }

        const auto service = OpenServiceW(
            scm,
            APPTUNNEL_WFP_DRIVER_SERVICE_NAME,
            SERVICE_STOP | DELETE | SERVICE_QUERY_STATUS);
        if (service == nullptr)
        {
            const auto error = GetLastError();
            CloseServiceHandle(scm);
            return error == ERROR_SERVICE_DOES_NOT_EXIST ? ERROR_SUCCESS : error;
        }

        SERVICE_STATUS status = {};
        ControlService(service, SERVICE_CONTROL_STOP, &status);
        WaitForServiceState(service, SERVICE_STOPPED, 20000);

        const auto deleted = DeleteService(service) != FALSE;
        const auto error = deleted ? ERROR_SUCCESS : GetLastError();
        CloseServiceHandle(service);
        CloseServiceHandle(scm);
        return error;
    }

    HANDLE OpenDriverDevice()
    {
        return CreateFileW(
            APPTUNNEL_WFP_DEVICE_NAME,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
    }

    DWORD SendIoctl(DWORD controlCode, LPVOID inputBuffer, DWORD inputLength, LPVOID outputBuffer, DWORD outputLength)
    {
        const auto device = OpenDriverDevice();
        if (device == INVALID_HANDLE_VALUE)
        {
            return GetLastError();
        }

        DWORD bytesReturned = 0;
        const auto ok = DeviceIoControl(
            device,
            controlCode,
            inputBuffer,
            inputLength,
            outputBuffer,
            outputLength,
            &bytesReturned,
            nullptr);

        const auto error = ok ? ERROR_SUCCESS : GetLastError();
        CloseHandle(device);
        return error;
    }

    DWORD QueryDriverDiagnostics(APPTUNNEL_WFP_DIAGNOSTICS& diagnostics)
    {
        return SendIoctl(
            IOCTL_APPTUNNEL_WFP_GET_DIAGNOSTICS,
            nullptr,
            0,
            &diagnostics,
            sizeof(diagnostics));
    }

    DWORD QueryDriverRuntimeState(APPTUNNEL_WFP_RUNTIME_STATE& state)
    {
        APPTUNNEL_WFP_DIAGNOSTICS diagnostics = {};
        const auto status = QueryDriverDiagnostics(diagnostics);
        if (status != ERROR_SUCCESS)
        {
            return status;
        }

        state.FiltersEnabled = diagnostics.FiltersEnabled;
        state.TunnelConnected = diagnostics.TunnelConnected;
        state.Reserved[0] = 0;
        state.Reserved[1] = 0;
        return ERROR_SUCCESS;
    }

    DWORD SetRuntimeState(bool filtersEnabled, bool tunnelConnected)
    {
        APPTUNNEL_WFP_RUNTIME_STATE state = {};
        state.FiltersEnabled = filtersEnabled ? TRUE : FALSE;
        state.TunnelConnected = tunnelConnected ? TRUE : FALSE;
        return SendIoctl(
            IOCTL_APPTUNNEL_WFP_SET_RUNTIME_STATE,
            &state,
            sizeof(state),
            nullptr,
            0);
    }

    DWORD AddRule(const APPTUNNEL_WFP_RULE& rule)
    {
        auto mutableRule = rule;
        return SendIoctl(
            IOCTL_APPTUNNEL_WFP_ADD_RULE,
            &mutableRule,
            sizeof(mutableRule),
            nullptr,
            0);
    }

    DWORD RemoveRule(const GUID& ruleId)
    {
        APPTUNNEL_WFP_RULE_KEY key = {};
        key.RuleId = ruleId;
        return SendIoctl(
            IOCTL_APPTUNNEL_WFP_REMOVE_RULE,
            &key,
            sizeof(key),
            nullptr,
            0);
    }

    bool ParseGuid(const std::wstring& text, GUID& guid)
    {
        return SUCCEEDED(CLSIDFromString(text.c_str(), &guid));
    }

    std::wstring NormalizeArg(const wchar_t* value)
    {
        return (value != nullptr && wcscmp(value, L"-") != 0)
            ? std::wstring(value)
            : std::wstring();
    }

    bool ParseRule(int argc, wchar_t** argv, APPTUNNEL_WFP_RULE& rule)
    {
        if (argc < 10)
        {
            return false;
        }

        RtlZeroMemory(&rule, sizeof(rule));
        if (!ParseGuid(argv[2], rule.RuleId) || !ParseGuid(argv[3], rule.ProfileId))
        {
            return false;
        }

        const auto matchKind = std::wstring(argv[4]);
        if (Equals(matchKind, L"win32"))
        {
            rule.MatchKind = AppTunnelMatchKindWin32Path;
        }
        else if (Equals(matchKind, L"packaged"))
        {
            rule.MatchKind = AppTunnelMatchKindPackagedIdentity;
        }
        else
        {
            return false;
        }

        rule.Flags = wcstoul(argv[5], nullptr, 10);

        const auto displayName = NormalizeArg(argv[6]);
        const auto executablePath = NormalizeArg(argv[7]);
        const auto packageFamilyName = NormalizeArg(argv[8]);
        const auto packageIdentity = NormalizeArg(argv[9]);

        if (FAILED(StringCchCopyW(rule.DisplayName, RTL_NUMBER_OF(rule.DisplayName), displayName.c_str())))
        {
            return false;
        }

        if (FAILED(StringCchCopyW(rule.ExecutablePath, RTL_NUMBER_OF(rule.ExecutablePath), executablePath.c_str())))
        {
            return false;
        }

        if (FAILED(StringCchCopyW(rule.PackageFamilyName, RTL_NUMBER_OF(rule.PackageFamilyName), packageFamilyName.c_str())))
        {
            return false;
        }

        if (FAILED(StringCchCopyW(rule.PackageIdentity, RTL_NUMBER_OF(rule.PackageIdentity), packageIdentity.c_str())))
        {
            return false;
        }

        return true;
    }

    bool IsDriverServiceRunning()
    {
        const auto scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (scm == nullptr)
        {
            return false;
        }

        const auto service = OpenServiceW(scm, APPTUNNEL_WFP_DRIVER_SERVICE_NAME, SERVICE_QUERY_STATUS);
        if (service == nullptr)
        {
            CloseServiceHandle(scm);
            return false;
        }

        SERVICE_STATUS_PROCESS status = {};
        DWORD bytesNeeded = 0;
        const auto ok = QueryServiceStatusEx(
            service,
            SC_STATUS_PROCESS_INFO,
            reinterpret_cast<LPBYTE>(&status),
            sizeof(status),
            &bytesNeeded);

        CloseServiceHandle(service);
        CloseServiceHandle(scm);
        return ok && status.dwCurrentState == SERVICE_RUNNING;
    }

    void PrintDiagnosticsJson(const APPTUNNEL_WFP_DIAGNOSTICS& diagnostics)
    {
        std::wcout
            << L"{"
            << L"\"succeeded\":true"
            << L",\"operation\":\"diagnostics\""
            << L",\"message\":\"Diagnostics ready.\""
            << L",\"driverServiceInstalled\":" << (IsDriverServiceRunning() ? L"true" : L"false")
            << L",\"installedRuleCount\":" << diagnostics.InstalledRuleCount
            << L",\"activeFlowCount\":" << diagnostics.ActiveFlowCount
            << L",\"droppedConnectCount\":" << diagnostics.DroppedConnectCount
            << L",\"droppedRecvAcceptCount\":" << diagnostics.DroppedRecvAcceptCount
            << L",\"tunnelRedirectCount\":" << diagnostics.TunnelRedirectCount
            << L",\"filtersEnabled\":" << (diagnostics.FiltersEnabled ? L"true" : L"false")
            << L",\"tunnelConnected\":" << (diagnostics.TunnelConnected ? L"true" : L"false")
            << L"}"
            << std::endl;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2)
    {
        PrintJsonResult(false, L"usage", L"Expected: install, uninstall, enable-filters, disable-filters, set-tunnel-state, add-rule, remove-rule, diagnostics.");
        return 1;
    }

    const std::wstring command = argv[1];
    if (Equals(command, L"install"))
    {
        if (argc < 3)
        {
            PrintJsonResult(false, L"install", L"Usage: AppTunnel.Router.WfpBridge.exe install <driver-sys-path>");
            return 1;
        }

        const auto status = InstallDriverService(argv[2]);
        PrintJsonResult(status == ERROR_SUCCESS, L"install", status == ERROR_SUCCESS ? L"Driver service installed." : L"Driver service install failed.");
        return status == ERROR_SUCCESS ? 0 : 1;
    }

    if (Equals(command, L"uninstall"))
    {
        const auto filterStatus = DisableFilters(true);
        const auto stateStatus = SetRuntimeState(false, false);
        const auto uninstallStatus = UninstallDriverService();
        const auto success = (filterStatus == ERROR_SUCCESS || filterStatus == FWP_E_SUBLAYER_NOT_FOUND)
            && (stateStatus == ERROR_SUCCESS || stateStatus == ERROR_FILE_NOT_FOUND || stateStatus == ERROR_PATH_NOT_FOUND)
            && uninstallStatus == ERROR_SUCCESS;
        PrintJsonResult(success, L"uninstall", success ? L"Driver service removed." : L"Driver service uninstall failed.");
        return success ? 0 : 1;
    }

    if (Equals(command, L"enable-filters"))
    {
        auto status = EnableFilters();
        if (status == ERROR_SUCCESS)
        {
            APPTUNNEL_WFP_RUNTIME_STATE state = {};
            if (QueryDriverRuntimeState(state) == ERROR_SUCCESS)
            {
                status = SetRuntimeState(true, state.TunnelConnected != FALSE);
            }
        }

        PrintJsonResult(status == ERROR_SUCCESS, L"enable-filters", status == ERROR_SUCCESS ? L"Filters enabled." : L"Failed to enable filters.");
        return status == ERROR_SUCCESS ? 0 : 1;
    }

    if (Equals(command, L"disable-filters"))
    {
        auto status = DisableFilters(false);
        if (status == ERROR_SUCCESS)
        {
            APPTUNNEL_WFP_RUNTIME_STATE state = {};
            if (QueryDriverRuntimeState(state) == ERROR_SUCCESS)
            {
                status = SetRuntimeState(false, state.TunnelConnected != FALSE);
            }
        }

        PrintJsonResult(status == ERROR_SUCCESS, L"disable-filters", status == ERROR_SUCCESS ? L"Filters disabled." : L"Failed to disable filters.");
        return status == ERROR_SUCCESS ? 0 : 1;
    }

    if (Equals(command, L"set-tunnel-state"))
    {
        if (argc < 3)
        {
            PrintJsonResult(false, L"set-tunnel-state", L"Usage: AppTunnel.Router.WfpBridge.exe set-tunnel-state <connected|disconnected>");
            return 1;
        }

        APPTUNNEL_WFP_RUNTIME_STATE state = {};
        auto status = QueryDriverRuntimeState(state);
        if (status == ERROR_SUCCESS)
        {
            status = SetRuntimeState(
                state.FiltersEnabled != FALSE,
                Equals(argv[2], L"connected"));
        }

        PrintJsonResult(status == ERROR_SUCCESS, L"set-tunnel-state", status == ERROR_SUCCESS ? L"Tunnel state updated." : L"Failed to update tunnel state.");
        return status == ERROR_SUCCESS ? 0 : 1;
    }

    if (Equals(command, L"add-rule"))
    {
        APPTUNNEL_WFP_RULE rule = {};
        if (!ParseRule(argc, argv, rule))
        {
            PrintJsonResult(false, L"add-rule", L"Usage: AppTunnel.Router.WfpBridge.exe add-rule <rule-id> <profile-id> <win32|packaged> <flags> <display-name> <exe-path|-> <package-family|-> <package-identity|->");
            return 1;
        }

        const auto status = AddRule(rule);
        PrintJsonResult(status == ERROR_SUCCESS, L"add-rule", status == ERROR_SUCCESS ? L"Rule synchronized." : L"Failed to synchronize rule.");
        return status == ERROR_SUCCESS ? 0 : 1;
    }

    if (Equals(command, L"remove-rule"))
    {
        if (argc < 3)
        {
            PrintJsonResult(false, L"remove-rule", L"Usage: AppTunnel.Router.WfpBridge.exe remove-rule <rule-id>");
            return 1;
        }

        GUID ruleId = {};
        if (!ParseGuid(argv[2], ruleId))
        {
            PrintJsonResult(false, L"remove-rule", L"Rule ID must be a valid GUID.");
            return 1;
        }

        const auto status = RemoveRule(ruleId);
        PrintJsonResult(status == ERROR_SUCCESS, L"remove-rule", status == ERROR_SUCCESS ? L"Rule removed." : L"Failed to remove rule.");
        return status == ERROR_SUCCESS ? 0 : 1;
    }

    if (Equals(command, L"diagnostics"))
    {
        APPTUNNEL_WFP_DIAGNOSTICS diagnostics = {};
        const auto status = QueryDriverDiagnostics(diagnostics);
        if (status != ERROR_SUCCESS)
        {
            PrintJsonResult(false, L"diagnostics", L"Unable to query driver diagnostics.");
            return 1;
        }

        PrintDiagnosticsJson(diagnostics);
        return 0;
    }

    PrintJsonResult(false, L"command", L"Unsupported bridge command.");
    return 1;
}
