#pragma once

#include <ntddk.h>
#include <ntstrsafe.h>
#include <fwpsk.h>

#include "..\..\Common\AppTunnelWfpShared.h"

typedef struct APPTUNNEL_FLOW_ENTRY_
{
    GUID RuleId;
    UINT64 FlowHandle;
    UINT32 ProcessId;
    WCHAR DisplayName[APPTUNNEL_WFP_MAX_DISPLAY_NAME];
    BOOLEAN Active;
} APPTUNNEL_FLOW_ENTRY;

typedef struct APPTUNNEL_DRIVER_GLOBALS_
{
    PDEVICE_OBJECT DeviceObject;
    UNICODE_STRING DosDeviceName;
    KSPIN_LOCK StateLock;
    APPTUNNEL_WFP_RULE Rules[APPTUNNEL_WFP_MAX_RULES];
    ULONG RuleCount;
    APPTUNNEL_FLOW_ENTRY Flows[APPTUNNEL_WFP_MAX_RULES];
    ULONG FlowCount;
    APPTUNNEL_WFP_DIAGNOSTICS Diagnostics;
    UINT32 CalloutIds[8];
} APPTUNNEL_DRIVER_GLOBALS;

DRIVER_INITIALIZE DriverEntry;
DRIVER_UNLOAD AppTunnelDriverUnload;
DRIVER_DISPATCH AppTunnelDispatchCreateClose;
DRIVER_DISPATCH AppTunnelDispatchDeviceControl;

VOID NTAPI AppTunnelClassifyAuthConnect(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut);

VOID NTAPI AppTunnelClassifyRecvAccept(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut);

VOID NTAPI AppTunnelClassifyFlowEstablished(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut);

VOID NTAPI AppTunnelClassifyConnectRedirect(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER3* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut);

VOID NTAPI AppTunnelFlowDelete(
    _In_ UINT16 layerId,
    _In_ UINT32 calloutId,
    _In_ UINT64 flowContext);
