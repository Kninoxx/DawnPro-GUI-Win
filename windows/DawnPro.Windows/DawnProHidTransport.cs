using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DawnPro.Core;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace DawnPro.Windows;

internal sealed class DawnProHidTransport : IDawnProReports, IDisposable
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int HidpStatusSuccess = 0x00110000;
    private const ushort ConsumerUsagePage = 0x0C;
    private const ushort ConsumerControlUsage = 0x01;

    private readonly SafeFileHandle _deviceHandle;

    private DawnProHidTransport(SafeFileHandle deviceHandle, string devicePath)
    {
        _deviceHandle = deviceHandle;
        DevicePath = devicePath;
    }

    public string DevicePath { get; }

    public static DawnProHidTransport Open(string? expectedPath = null)
    {
        string hardwareId = $"vid_{DawnProController.VendorId:X4}&pid_{DawnProController.ProductId:X4}&mi_02";
        string[] paths = EnumeratePaths()
            .Where(path => path.Contains(hardwareId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (expectedPath is not null)
            paths = paths.Where(path => string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (paths.Length == 0)
            throw new IOException(expectedPath is null
                ? "Connect an original Moondrop Dawn Pro and select Refresh."
                : "The original connection is gone. Select Refresh to connect again.");
        if (paths.Length > 1)
            throw new IOException("More than one Dawn Pro HID connection was found. Connect one DAC and select Refresh.");

        string path = paths[0];
        const uint readWriteAccess = 0xC0000000;
        const uint shareReadWrite = 3;
        const uint openExisting = 3;
        SafeFileHandle handle = CreateFile(path, readWriteAccess, shareReadWrite, IntPtr.Zero, openExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            IOException error = NativeError("Opening the Dawn Pro HID interface");
            handle.Dispose();
            throw error;
        }

        try
        {
            ValidateReports(handle);
            return new DawnProHidTransport(handle, path);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Write(byte[] report)
    {
        if (report.Length != DawnProController.OutputReportLength)
            throw new ArgumentException("The Dawn Pro requires an eight-byte output report.", nameof(report));
        if (!WriteFile(_deviceHandle, report, (uint)report.Length, out uint written, IntPtr.Zero))
            throw NativeError("Writing a Dawn Pro report");
        if (written != report.Length)
            throw new IOException("Windows wrote an incomplete Dawn Pro report.");
    }

    public byte[] Read()
    {
        // The unnumbered report uses a leading zero. The DAC returns the reply through GET_REPORT.
        byte[] report = new byte[DawnProController.InputReportLength];
        if (!HidD_GetInputReport(_deviceHandle, report, report.Length))
            throw NativeError("Reading a Dawn Pro input report");
        return report;
    }

    public void Dispose() => _deviceHandle.Dispose();

    private static List<string> EnumeratePaths()
    {
        HidD_GetHidGuid(out Guid hidClass);
        IntPtr devices = SetupDiGetClassDevs(ref hidClass, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (devices == new IntPtr(-1))
            throw NativeError("Finding Windows HID interfaces");

        var paths = new List<string>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var item = new DeviceInterfaceData { Size = (uint)Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref hidClass, index, ref item))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == ErrorNoMoreItems)
                        return paths;
                    throw NativeError("Enumerating Windows HID interfaces", error);
                }

                paths.Add(GetDevicePath(devices, ref item));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devices);
        }
    }

    private static string GetDevicePath(IntPtr devices, ref DeviceInterfaceData item)
    {
        if (!SetupDiGetDeviceInterfaceDetail(devices, ref item, IntPtr.Zero, 0, out uint size, IntPtr.Zero) &&
            Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer)
        {
            throw NativeError("Checking a Windows HID interface path");
        }

        IntPtr detail = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            // SetupAPI expects cbSize to account for native alignment. DevicePath follows the 4-byte DWORD.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(devices, ref item, detail, size, out _, IntPtr.Zero))
                throw NativeError("Getting a Windows HID interface path");
            return Marshal.PtrToStringUni(detail + sizeof(uint))
                ?? throw new IOException("Windows returned an empty HID interface path.");
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static void ValidateReports(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out IntPtr data))
            throw NativeError("Opening the Dawn Pro HID descriptor");

        try
        {
            int status = HidP_GetCaps(data, out HidCaps caps);
            if (status != HidpStatusSuccess)
                throw new IOException($"Windows could not read the Dawn Pro HID capabilities (0x{status:X8}).");
            if (caps.UsagePage != ConsumerUsagePage || caps.Usage != ConsumerControlUsage ||
                caps.InputReportByteLength != DawnProController.InputReportLength ||
                caps.OutputReportByteLength != DawnProController.OutputReportLength)
            {
                throw new IOException("The connected DAC has an unsupported HID report layout.");
            }
        }
        finally
        {
            HidD_FreePreparsedData(data);
        }
    }

    private static IOException NativeError(string operation) => NativeError(operation, Marshal.GetLastPInvokeError());

    private static IOException NativeError(string operation, int error)
    {
        var cause = new Win32Exception(error);
        return new IOException($"{operation} failed ({error}). {cause.Message}", cause);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    // The remaining fields in HIDP_CAPS do not affect report framing.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct HidCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportByteLength;
        [FieldOffset(6)] public ushort OutputReportByteLength;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, out HidCaps capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetInputReport(SafeFileHandle device, [In, Out] byte[] report, int length);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle device, byte[] report, uint length,
        out uint written, IntPtr overlapped);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid interfaceClass, IntPtr enumerator,
        IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devices, IntPtr deviceInfo,
        ref Guid interfaceClass, uint index, ref DeviceInterfaceData item);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devices, ref DeviceInterfaceData item,
        IntPtr detail, uint detailSize, out uint requiredSize, IntPtr deviceInfo);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
}
