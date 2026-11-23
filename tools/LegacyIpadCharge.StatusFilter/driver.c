#include <ntddk.h>
#include <usb.h>
#include <usbdlib.h>
#include <usbioctl.h>
#include <wdf.h>
#include "statusprobe.h"

typedef struct _PROBE_CONTEXT {
    WDFQUEUE ParentQueue;
} PROBE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(PROBE_CONTEXT, ProbeContext)

static const GUID IpadStatusRawClass =
    {0xe41f03e4, 0x5dd2, 0x42cd, {0xab, 0x2c, 0x22, 0xef, 0xe5, 0x58, 0x54, 0x16}};

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD AddFilter;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL ForwardProbe;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL SendProbe;
static IO_COMPLETION_ROUTINE CompleteUsbIrp;

NTSTATUS DriverEntry(PDRIVER_OBJECT driver, PUNICODE_STRING registryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, AddFilter);
    return WdfDriverCreate(driver, registryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}

NTSTATUS AddFilter(WDFDRIVER driver, PWDFDEVICE_INIT init)
{
    WDFDEVICE device, child = NULL;
    WDFQUEUE parentQueue;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_OBJECT_ATTRIBUTES attributes;
    PWDFDEVICE_INIT childInit;
    NTSTATUS status;
    DECLARE_CONST_UNICODE_STRING(deviceId, L"LegacyIpadCharge\\GetStatus");
    DECLARE_CONST_UNICODE_STRING(instanceId, L"0");
    DECLARE_CONST_UNICODE_STRING(security, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");
    UNREFERENCED_PARAMETER(driver);

    WdfFdoInitSetFilter(init);
    status = WdfDeviceCreate(&init, WDF_NO_OBJECT_ATTRIBUTES, &device);
    if (!NT_SUCCESS(status)) return status;

    // A separate queue leaves the Apple stack's regular I/O on the default path.
    WDF_IO_QUEUE_CONFIG_INIT(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.PowerManaged = WdfTrue;
    queueConfig.EvtIoDeviceControl = SendProbe;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfIoQueueCreate(device, &queueConfig, &attributes, &parentQueue);
    if (!NT_SUCCESS(status)) goto unavailable;

    childInit = WdfPdoInitAllocate(device);
    if (childInit == NULL) {
        status = STATUS_INSUFFICIENT_RESOURCES;
        goto unavailable;
    }
    WdfPdoInitAllowForwardingRequestToParent(childInit);
    status = WdfPdoInitAssignRawDevice(childInit, &IpadStatusRawClass);
    if (NT_SUCCESS(status)) status = WdfPdoInitAssignDeviceID(childInit, &deviceId);
    if (NT_SUCCESS(status)) status = WdfPdoInitAddHardwareID(childInit, &deviceId);
    if (NT_SUCCESS(status)) status = WdfPdoInitAssignInstanceID(childInit, &instanceId);
    if (NT_SUCCESS(status)) status = WdfDeviceInitAssignSDDLString(childInit, &security);
    WdfDeviceInitSetCharacteristics(childInit, FILE_DEVICE_SECURE_OPEN, TRUE);
    if (NT_SUCCESS(status)) {
        WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, PROBE_CONTEXT);
        status = WdfDeviceCreate(&childInit, &attributes, &child);
    }
    if (!NT_SUCCESS(status)) {
        WdfDeviceInitFree(childInit);
        goto unavailable;
    }

    ProbeContext(child)->ParentQueue = parentQueue;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = ForwardProbe;
    status = WdfIoQueueCreate(child, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    if (NT_SUCCESS(status)) status = WdfDeviceCreateDeviceInterface(child, &IpadStatusInterface, NULL);
    if (NT_SUCCESS(status)) status = WdfFdoAddStaticChild(device, child);
    if (NT_SUCCESS(status)) return STATUS_SUCCESS;
    WdfObjectDelete(child);

unavailable:
    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
               "LegacyIpadStatus: probe interface unavailable (0x%08X)\n", status));
    return STATUS_SUCCESS;
}

VOID ForwardProbe(WDFQUEUE queue, WDFREQUEST request, size_t outputLength,
                  size_t inputLength, ULONG code)
{
    WDF_REQUEST_FORWARD_OPTIONS options;
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    if (inputLength == 0 &&
        ((code == IOCTL_IPAD_GET_STATUS && outputLength == sizeof(USHORT)) ||
         ((code == IOCTL_IPAD_CHARGE_ONCE || code == IOCTL_IPAD_CHARGE_PROBE) && outputLength == 0))) {
        WDF_REQUEST_FORWARD_OPTIONS_INIT(&options);
        status = WdfRequestForwardToParentDeviceIoQueue(
            request, ProbeContext(WdfIoQueueGetDevice(queue))->ParentQueue, &options);
        if (NT_SUCCESS(status)) return;
    }
    WdfRequestComplete(request, status);
}

static NTSTATUS CompleteUsbIrp(PDEVICE_OBJECT device, PIRP irp, PVOID context)
{
    UNREFERENCED_PARAMETER(device);
    UNREFERENCED_PARAMETER(irp);
    KeSetEvent((PKEVENT)context, IO_NO_INCREMENT, FALSE);
    return STATUS_MORE_PROCESSING_REQUIRED;
}

static NTSTATUS SubmitProbeUrb(PDEVICE_OBJECT device, PDEVICE_OBJECT lower,
                               USBD_HANDLE usb, PURB urb)
{
    PIRP irp = IoAllocateIrp(lower->StackSize, FALSE);
    if (irp == NULL) return STATUS_INSUFFICIENT_RESOURCES;

    PIO_STACK_LOCATION stack = IoGetNextIrpStackLocation(irp);
    stack->MajorFunction = IRP_MJ_INTERNAL_DEVICE_CONTROL;
    stack->Parameters.DeviceIoControl.IoControlCode = IOCTL_INTERNAL_USB_SUBMIT_URB;
    USBD_AssignUrbToIoStackLocation(usb, stack, urb);

    KEVENT completed;
    KeInitializeEvent(&completed, NotificationEvent, FALSE);
    NTSTATUS status = IoSetCompletionRoutineEx(device, irp, CompleteUsbIrp,
                                               &completed, TRUE, TRUE, TRUE);
    if (NT_SUCCESS(status)) {
        LARGE_INTEGER timeout;
        timeout.QuadPart = WDF_REL_TIMEOUT_IN_SEC(3);
        IoCallDriver(lower, irp);
        status = KeWaitForSingleObject(&completed, Executive, KernelMode, FALSE, &timeout);
        if (status == STATUS_TIMEOUT) {
            IoCancelIrp(irp);
            // The lower stack still owns the URB buffer until completion, even after cancellation.
            KeWaitForSingleObject(&completed, Executive, KernelMode, FALSE, NULL);
            status = STATUS_IO_TIMEOUT;
        } else {
            status = irp->IoStatus.Status;
        }
    }
    IoFreeIrp(irp);
    return status;
}

VOID SendProbe(WDFQUEUE queue, WDFREQUEST request, size_t outputLength,
               size_t inputLength, ULONG code)
{
    WDFDEVICE device = WdfIoQueueGetDevice(queue);
    PDEVICE_OBJECT wdmDevice = WdfDeviceWdmGetDeviceObject(device);
    PDEVICE_OBJECT lower = WdfIoTargetWdmGetTargetDeviceObject(WdfDeviceGetIoTarget(device));
    USBD_HANDLE usb;
    PURB urb;
    NTSTATUS status;
    PVOID output = NULL;
    ULONG returnedLength;
    const BOOLEAN vendorRequest = code == IOCTL_IPAD_CHARGE_ONCE || code == IOCTL_IPAD_CHARGE_PROBE;
    UNREFERENCED_PARAMETER(inputLength);

    if (!vendorRequest) {
        status = WdfRequestRetrieveOutputBuffer(request, sizeof(USHORT), &output, NULL);
        if (!NT_SUCCESS(status)) goto done;
    }
    if (lower == NULL) {
        status = STATUS_DEVICE_NOT_CONNECTED;
        goto done;
    }

    status = USBD_CreateHandle(wdmDevice, lower, USBD_CLIENT_CONTRACT_VERSION_602, 'SPIL', &usb);
    if (!NT_SUCCESS(status)) goto done;
    status = USBD_UrbAllocate(usb, &urb);
    if (NT_SUCCESS(status)) {
        RtlZeroMemory(urb, sizeof(*urb));
        if (vendorRequest) {
            struct _URB_CONTROL_TRANSFER_EX *transfer = &urb->UrbControlTransferEx;
            transfer->Hdr.Length = sizeof(*transfer);
            transfer->Hdr.Function = URB_FUNCTION_CONTROL_TRANSFER_EX;
            transfer->TransferFlags = USBD_DEFAULT_PIPE_TRANSFER | USBD_TRANSFER_DIRECTION_OUT;
            transfer->Timeout = 2000;
            const UCHAR *setup = code == IOCTL_IPAD_CHARGE_PROBE ? IpadChargeProbeSetup : IpadChargeSetup;
            RtlCopyMemory(transfer->SetupPacket, setup, sizeof(transfer->SetupPacket));
        } else {
            UsbBuildGetStatusRequest(urb, URB_FUNCTION_GET_STATUS_FROM_DEVICE, 0, output, NULL, NULL);
        }
        status = SubmitProbeUrb(wdmDevice, lower, usb, urb);
        returnedLength = vendorRequest ? urb->UrbControlTransferEx.TransferBufferLength
                                       : urb->UrbControlGetStatusRequest.TransferBufferLength;
        if (NT_SUCCESS(status) &&
            (!USBD_SUCCESS(urb->UrbHeader.Status) || returnedLength != outputLength)) {
            status = STATUS_DEVICE_DATA_ERROR;
        }
        USBD_UrbFree(usb, urb);
    }
    USBD_CloseHandle(usb);

done:
    WdfRequestCompleteWithInformation(request, status, NT_SUCCESS(status) ? outputLength : 0);
}
