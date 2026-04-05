#include "Driver.h"

static APPTUNNEL_DRIVER_GLOBALS g_AppTunnel;

static NTSTATUS AppTunnelRegisterCallout(
    _In_ PDEVICE_OBJECT deviceObject,
    _In_ const GUID* calloutKey,
    _In_ FWPS_CALLOUT_CLASSIFY_FN3 classifyFn,
    _In_opt_ FWPS_CALLOUT_FLOW_DELETE_NOTIFY_FN0 flowDeleteFn,
    _Out_ UINT32* calloutId)
{
    FWPS_CALLOUT3 callout;
    RtlZeroMemory(&callout, sizeof(callout));
    callout.calloutKey = *calloutKey;
    callout.classifyFn = classifyFn;
    callout.flowDeleteFn = flowDeleteFn;
    return FwpsCalloutRegister3(deviceObject, &callout, calloutId);
}

static VOID AppTunnelUnregisterCallouts(VOID)
{
    ULONG index;

    for (index = 0; index < RTL_NUMBER_OF(g_AppTunnel.CalloutIds); ++index)
    {
        if (g_AppTunnel.CalloutIds[index] != 0)
        {
            FwpsCalloutUnregisterById0(g_AppTunnel.CalloutIds[index]);
            g_AppTunnel.CalloutIds[index] = 0;
        }
    }
}

static NTSTATUS AppTunnelCreateDevice(_In_ PDRIVER_OBJECT driverObject)
{
    NTSTATUS status;
    UNICODE_STRING deviceName;

    RtlInitUnicodeString(&deviceName, APPTUNNEL_WFP_NT_DEVICE_NAME);
    RtlInitUnicodeString(&g_AppTunnel.DosDeviceName, APPTUNNEL_WFP_DOS_DEVICE_NAME);

    status = IoCreateDevice(
        driverObject,
        0,
        &deviceName,
        FILE_DEVICE_NETWORK,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &g_AppTunnel.DeviceObject);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    g_AppTunnel.DeviceObject->Flags |= DO_BUFFERED_IO;
    return IoCreateSymbolicLink(&g_AppTunnel.DosDeviceName, &deviceName);
}

static VOID AppTunnelDeleteDevice(VOID)
{
    if (g_AppTunnel.DosDeviceName.Buffer != NULL)
    {
        IoDeleteSymbolicLink(&g_AppTunnel.DosDeviceName);
    }

    if (g_AppTunnel.DeviceObject != NULL)
    {
        IoDeleteDevice(g_AppTunnel.DeviceObject);
        g_AppTunnel.DeviceObject = NULL;
    }
}

static BOOLEAN AppTunnelRuleMatches(
    _In_ const APPTUNNEL_WFP_RULE* rule,
    _In_opt_ const FWP_BYTE_BLOB* appId)
{
    UNICODE_STRING rulePath;
    UNICODE_STRING incomingPath;

    if (rule->MatchKind != AppTunnelMatchKindWin32Path || appId == NULL || appId->data == NULL)
    {
        return FALSE;
    }

    RtlInitUnicodeString(&rulePath, rule->ExecutablePath);
    incomingPath.Buffer = (PWSTR)appId->data;
    incomingPath.Length = (USHORT)((appId->size < (USHRT_MAX - sizeof(WCHAR)))
        ? appId->size
        : (USHRT_MAX - sizeof(WCHAR)));
    incomingPath.MaximumLength = incomingPath.Length;
    return RtlEqualUnicodeString(&rulePath, &incomingPath, TRUE);
}

static const FWP_VALUE0* AppTunnelGetAppIdValue(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ UINT16 layerId)
{
    switch (layerId)
    {
    case FWPS_LAYER_ALE_AUTH_CONNECT_V4:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_AUTH_CONNECT_V6:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V6_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_AUTH_RECV_ACCEPT_V4:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V4_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_AUTH_RECV_ACCEPT_V6:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_RECV_ACCEPT_V6_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_FLOW_ESTABLISHED_V4:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_FLOW_ESTABLISHED_V6:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_CONNECT_REDIRECT_V4:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_ALE_APP_ID].value;
    case FWPS_LAYER_ALE_CONNECT_REDIRECT_V6:
        return &inFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V6_ALE_APP_ID].value;
    default:
        return NULL;
    }
}

static BOOLEAN AppTunnelTryFindMatchingRule(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ UINT16 layerId,
    _Out_ APPTUNNEL_WFP_RULE* matchedRule)
{
    const FWP_VALUE0* appIdValue;
    KIRQL oldIrql;
    ULONG index;

    appIdValue = AppTunnelGetAppIdValue(inFixedValues, layerId);
    if (appIdValue == NULL || appIdValue->type != FWP_BYTE_BLOB_TYPE)
    {
        return FALSE;
    }

    KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);

    for (index = 0; index < g_AppTunnel.RuleCount; ++index)
    {
        if (AppTunnelRuleMatches(&g_AppTunnel.Rules[index], appIdValue->byteBlob))
        {
            *matchedRule = g_AppTunnel.Rules[index];
            KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
            return TRUE;
        }
    }

    KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
    return FALSE;
}

static VOID AppTunnelUpdateRuntimeState(_In_ const APPTUNNEL_WFP_RUNTIME_STATE* state)
{
    KIRQL oldIrql;

    KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);
    g_AppTunnel.Diagnostics.FiltersEnabled = state->FiltersEnabled;
    g_AppTunnel.Diagnostics.TunnelConnected = state->TunnelConnected;
    KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
}

static VOID AppTunnelTrackFlow(
    _In_ const APPTUNNEL_WFP_RULE* rule,
    _In_ UINT64 flowHandle,
    _In_ UINT32 processId)
{
    KIRQL oldIrql;
    ULONG index;

    KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);

    for (index = 0; index < g_AppTunnel.FlowCount; ++index)
    {
        if (g_AppTunnel.Flows[index].FlowHandle == flowHandle)
        {
            g_AppTunnel.Flows[index].RuleId = rule->RuleId;
            g_AppTunnel.Flows[index].ProcessId = processId;
            g_AppTunnel.Flows[index].Active = TRUE;
            g_AppTunnel.Diagnostics.ActiveFlowCount = g_AppTunnel.FlowCount;
            KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
            return;
        }
    }

    if (g_AppTunnel.FlowCount < APPTUNNEL_WFP_MAX_RULES)
    {
        g_AppTunnel.Flows[g_AppTunnel.FlowCount].RuleId = rule->RuleId;
        g_AppTunnel.Flows[g_AppTunnel.FlowCount].FlowHandle = flowHandle;
        g_AppTunnel.Flows[g_AppTunnel.FlowCount].ProcessId = processId;
        RtlStringCchCopyW(
            g_AppTunnel.Flows[g_AppTunnel.FlowCount].DisplayName,
            RTL_NUMBER_OF(g_AppTunnel.Flows[g_AppTunnel.FlowCount].DisplayName),
            rule->DisplayName);
        g_AppTunnel.Flows[g_AppTunnel.FlowCount].Active = TRUE;
        g_AppTunnel.FlowCount += 1;
        g_AppTunnel.Diagnostics.ActiveFlowCount = g_AppTunnel.FlowCount;
    }

    KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
}

static VOID AppTunnelRemoveFlow(_In_ UINT64 flowHandle)
{
    KIRQL oldIrql;
    ULONG index;

    KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);

    for (index = 0; index < g_AppTunnel.FlowCount; ++index)
    {
        if (g_AppTunnel.Flows[index].FlowHandle == flowHandle)
        {
            if (index + 1 < g_AppTunnel.FlowCount)
            {
                RtlMoveMemory(
                    &g_AppTunnel.Flows[index],
                    &g_AppTunnel.Flows[index + 1],
                    sizeof(APPTUNNEL_FLOW_ENTRY) * (g_AppTunnel.FlowCount - index - 1));
            }

            g_AppTunnel.FlowCount -= 1;
            g_AppTunnel.Diagnostics.ActiveFlowCount = g_AppTunnel.FlowCount;
            break;
        }
    }

    KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
}

static BOOLEAN AppTunnelMaybeBlockOnTunnelDrop(
    _In_ const APPTUNNEL_WFP_RULE* rule,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut,
    _In_ BOOLEAN isOutbound)
{
    KIRQL oldIrql;
    BOOLEAN shouldBlock = FALSE;

    KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);

    if ((rule->Flags & AppTunnelRuleFlagBlockOnTunnelDrop) != 0
        && g_AppTunnel.Diagnostics.FiltersEnabled
        && !g_AppTunnel.Diagnostics.TunnelConnected)
    {
        shouldBlock = TRUE;
        if (isOutbound)
        {
            g_AppTunnel.Diagnostics.DroppedConnectCount += 1;
        }
        else
        {
            g_AppTunnel.Diagnostics.DroppedRecvAcceptCount += 1;
        }
    }

    KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);

    if (shouldBlock)
    {
        classifyOut->actionType = FWP_ACTION_BLOCK;
        classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
    }

    return shouldBlock;
}

NTSTATUS DriverEntry(_In_ PDRIVER_OBJECT driverObject, _In_ PUNICODE_STRING registryPath)
{
    NTSTATUS status;

    UNREFERENCED_PARAMETER(registryPath);

    RtlZeroMemory(&g_AppTunnel, sizeof(g_AppTunnel));
    KeInitializeSpinLock(&g_AppTunnel.StateLock);
    g_AppTunnel.Diagnostics.Version = 1;

    driverObject->MajorFunction[IRP_MJ_CREATE] = AppTunnelDispatchCreateClose;
    driverObject->MajorFunction[IRP_MJ_CLOSE] = AppTunnelDispatchCreateClose;
    driverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = AppTunnelDispatchDeviceControl;
    driverObject->DriverUnload = AppTunnelDriverUnload;

    status = AppTunnelCreateDevice(driverObject);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_AUTH_CONNECT_V4, AppTunnelClassifyAuthConnect, NULL, &g_AppTunnel.CalloutIds[0]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_AUTH_CONNECT_V6, AppTunnelClassifyAuthConnect, NULL, &g_AppTunnel.CalloutIds[1]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_RECV_ACCEPT_V4, AppTunnelClassifyRecvAccept, NULL, &g_AppTunnel.CalloutIds[2]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_RECV_ACCEPT_V6, AppTunnelClassifyRecvAccept, NULL, &g_AppTunnel.CalloutIds[3]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_FLOW_ESTABLISHED_V4, AppTunnelClassifyFlowEstablished, AppTunnelFlowDelete, &g_AppTunnel.CalloutIds[4]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_FLOW_ESTABLISHED_V6, AppTunnelClassifyFlowEstablished, AppTunnelFlowDelete, &g_AppTunnel.CalloutIds[5]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_CONNECT_REDIRECT_V4, AppTunnelClassifyConnectRedirect, NULL, &g_AppTunnel.CalloutIds[6]);
    if (!NT_SUCCESS(status)) goto Exit;
    status = AppTunnelRegisterCallout(g_AppTunnel.DeviceObject, &APPTUNNEL_WFP_CALLOUT_CONNECT_REDIRECT_V6, AppTunnelClassifyConnectRedirect, NULL, &g_AppTunnel.CalloutIds[7]);

Exit:
    if (!NT_SUCCESS(status))
    {
        AppTunnelUnregisterCallouts();
        AppTunnelDeleteDevice();
    }

    return status;
}

VOID AppTunnelDriverUnload(_In_ PDRIVER_OBJECT driverObject)
{
    UNREFERENCED_PARAMETER(driverObject);
    AppTunnelUnregisterCallouts();
    AppTunnelDeleteDevice();
}

NTSTATUS AppTunnelDispatchCreateClose(_In_ PDEVICE_OBJECT deviceObject, _Inout_ PIRP irp)
{
    UNREFERENCED_PARAMETER(deviceObject);
    irp->IoStatus.Status = STATUS_SUCCESS;
    irp->IoStatus.Information = 0;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

NTSTATUS AppTunnelDispatchDeviceControl(_In_ PDEVICE_OBJECT deviceObject, _Inout_ PIRP irp)
{
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR information = 0;
    PIO_STACK_LOCATION stack;
    KIRQL oldIrql;

    UNREFERENCED_PARAMETER(deviceObject);
    stack = IoGetCurrentIrpStackLocation(irp);

    switch (stack->Parameters.DeviceIoControl.IoControlCode)
    {
    case IOCTL_APPTUNNEL_WFP_ADD_RULE:
        if (stack->Parameters.DeviceIoControl.InputBufferLength >= sizeof(APPTUNNEL_WFP_RULE))
        {
            const APPTUNNEL_WFP_RULE* rule;
            ULONG index;

            rule = (const APPTUNNEL_WFP_RULE*)irp->AssociatedIrp.SystemBuffer;
            status = STATUS_SUCCESS;

            KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);

            for (index = 0; index < g_AppTunnel.RuleCount; ++index)
            {
                if (RtlCompareMemory(&g_AppTunnel.Rules[index].RuleId, &rule->RuleId, sizeof(GUID)) == sizeof(GUID))
                {
                    g_AppTunnel.Rules[index] = *rule;
                    break;
                }
            }

            if (index == g_AppTunnel.RuleCount)
            {
                if (g_AppTunnel.RuleCount >= APPTUNNEL_WFP_MAX_RULES)
                {
                    status = STATUS_INSUFFICIENT_RESOURCES;
                }
                else
                {
                    g_AppTunnel.Rules[g_AppTunnel.RuleCount++] = *rule;
                }
            }

            g_AppTunnel.Diagnostics.InstalledRuleCount = g_AppTunnel.RuleCount;
            KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
        }
        else
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        break;

    case IOCTL_APPTUNNEL_WFP_REMOVE_RULE:
        if (stack->Parameters.DeviceIoControl.InputBufferLength >= sizeof(APPTUNNEL_WFP_RULE_KEY))
        {
            const APPTUNNEL_WFP_RULE_KEY* key;
            ULONG index;

            key = (const APPTUNNEL_WFP_RULE_KEY*)irp->AssociatedIrp.SystemBuffer;
            status = STATUS_NOT_FOUND;

            KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);

            for (index = 0; index < g_AppTunnel.RuleCount; ++index)
            {
                if (RtlCompareMemory(&g_AppTunnel.Rules[index].RuleId, &key->RuleId, sizeof(GUID)) == sizeof(GUID))
                {
                    if (index + 1 < g_AppTunnel.RuleCount)
                    {
                        RtlMoveMemory(
                            &g_AppTunnel.Rules[index],
                            &g_AppTunnel.Rules[index + 1],
                            sizeof(APPTUNNEL_WFP_RULE) * (g_AppTunnel.RuleCount - index - 1));
                    }

                    g_AppTunnel.RuleCount -= 1;
                    g_AppTunnel.Diagnostics.InstalledRuleCount = g_AppTunnel.RuleCount;
                    status = STATUS_SUCCESS;
                    break;
                }
            }

            KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
        }
        else
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        break;

    case IOCTL_APPTUNNEL_WFP_SET_RUNTIME_STATE:
        if (stack->Parameters.DeviceIoControl.InputBufferLength >= sizeof(APPTUNNEL_WFP_RUNTIME_STATE))
        {
            AppTunnelUpdateRuntimeState((const APPTUNNEL_WFP_RUNTIME_STATE*)irp->AssociatedIrp.SystemBuffer);
            status = STATUS_SUCCESS;
        }
        else
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        break;

    case IOCTL_APPTUNNEL_WFP_GET_DIAGNOSTICS:
        if (stack->Parameters.DeviceIoControl.OutputBufferLength >= sizeof(APPTUNNEL_WFP_DIAGNOSTICS))
        {
            KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);
            RtlCopyMemory(irp->AssociatedIrp.SystemBuffer, &g_AppTunnel.Diagnostics, sizeof(g_AppTunnel.Diagnostics));
            KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
            information = sizeof(g_AppTunnel.Diagnostics);
            status = STATUS_SUCCESS;
        }
        else
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        break;
    }

    irp->IoStatus.Status = status;
    irp->IoStatus.Information = information;
    IoCompleteRequest(irp, IO_NO_INCREMENT);
    return status;
}

VOID NTAPI AppTunnelClassifyAuthConnect(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut)
{
    APPTUNNEL_WFP_RULE rule;

    UNREFERENCED_PARAMETER(inMetaValues);
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;
    if (AppTunnelTryFindMatchingRule(inFixedValues, filter->layerId, &rule))
    {
        AppTunnelMaybeBlockOnTunnelDrop(&rule, classifyOut, TRUE);
    }
}

VOID NTAPI AppTunnelClassifyRecvAccept(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut)
{
    APPTUNNEL_WFP_RULE rule;

    UNREFERENCED_PARAMETER(inMetaValues);
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    classifyOut->actionType = FWP_ACTION_PERMIT;
    if (AppTunnelTryFindMatchingRule(inFixedValues, filter->layerId, &rule))
    {
        AppTunnelMaybeBlockOnTunnelDrop(&rule, classifyOut, FALSE);
    }
}

VOID NTAPI AppTunnelClassifyFlowEstablished(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut)
{
    APPTUNNEL_WFP_RULE rule;
    UINT64 flowHandle;
    UINT32 processId;

    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(flowContext);

    classifyOut->actionType = FWP_ACTION_PERMIT;
    if (!AppTunnelTryFindMatchingRule(inFixedValues, filter->layerId, &rule))
    {
        return;
    }

    if (!FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_FLOW_HANDLE))
    {
        return;
    }

    flowHandle = inMetaValues->flowHandle;
    processId = FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_PROCESS_ID)
        ? (UINT32)inMetaValues->processId
        : 0;

    FwpsFlowAssociateContext0(
        flowHandle,
        filter->layerId,
        filter->action.calloutId,
        flowHandle);

    AppTunnelTrackFlow(&rule, flowHandle, processId);
}

VOID NTAPI AppTunnelClassifyConnectRedirect(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut)
{
    APPTUNNEL_WFP_RULE rule;
    KIRQL oldIrql;

    UNREFERENCED_PARAMETER(inMetaValues);
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(flowContext);

    classifyOut->actionType = FWP_ACTION_PERMIT;
    if (!AppTunnelTryFindMatchingRule(inFixedValues, filter->layerId, &rule))
    {
        return;
    }

    KeAcquireSpinLock(&g_AppTunnel.StateLock, &oldIrql);
    if (g_AppTunnel.Diagnostics.FiltersEnabled && g_AppTunnel.Diagnostics.TunnelConnected)
    {
        g_AppTunnel.Diagnostics.TunnelRedirectCount += 1;
    }
    KeReleaseSpinLock(&g_AppTunnel.StateLock, oldIrql);
}

VOID NTAPI AppTunnelFlowDelete(
    _In_ UINT16 layerId,
    _In_ UINT32 calloutId,
    _In_ UINT64 flowContext)
{
    UNREFERENCED_PARAMETER(layerId);
    UNREFERENCED_PARAMETER(calloutId);
    AppTunnelRemoveFlow(flowContext);
}
