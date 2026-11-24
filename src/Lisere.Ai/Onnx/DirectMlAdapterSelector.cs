using System.Globalization;
using System.Runtime.InteropServices;

namespace Lisere.Ai.Onnx;

/// <summary>One DXGI adapter, with the identity DirectML uses for its device id.</summary>
public readonly record struct DirectMlAdapterInfo(
    int Index,
    string Name,
    long DedicatedVideoMemory,
    long SharedSystemMemory,
    bool IsSoftware);

/// <summary>
/// Chooses the DXGI adapter handed to the DirectML execution provider.
/// DirectML's device id is an IDXGIFactory enumeration index, and adapter 0 is
/// the primary display adapter: on a hybrid laptop that is usually the
/// integrated GPU, whose memory is shared with the system. Picking the adapter
/// with the most dedicated video memory keeps inference on the discrete GPU
/// instead of consuming RAM through the iGPU.
/// </summary>
public static class DirectMlAdapterSelector
{
    /// <summary>
    /// Optional override: a DXGI index ("0", "1") or a case-insensitive
    /// fragment of the adapter name ("RTX 4070"). An unknown value falls back
    /// to the automatic choice instead of failing the render.
    /// </summary>
    public const string PreferenceVariable = "LISERE_DML_ADAPTER";

    private const uint AdapterFlagSoftware = 0x2;

    public static DirectMlAdapterInfo? Resolve(string? preference = null) =>
        Choose(Enumerate(), preference);

    /// <summary>
    /// Pure selection rule, separated from DXGI so it can be tested without a
    /// particular graphics card.
    /// </summary>
    public static DirectMlAdapterInfo? Choose(
        IReadOnlyList<DirectMlAdapterInfo> adapters,
        string? preference = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var hardware = adapters
            .Where(adapter => adapter.Index >= 0 && !adapter.IsSoftware)
            .OrderBy(adapter => adapter.Index)
            .ToArray();

        if (hardware.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preference))
        {
            var requested = preference.Trim();

            if (int.TryParse(
                requested,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var index))
            {
                var byIndex = hardware.FirstOrDefault(adapter => adapter.Index == index);
                if (byIndex.Name is not null)
                {
                    return byIndex;
                }
            }
            else
            {
                var byName = hardware.FirstOrDefault(adapter =>
                    adapter.Name.Contains(requested, StringComparison.OrdinalIgnoreCase));
                if (byName.Name is not null)
                {
                    return byName;
                }
            }
        }

        // Dedicated memory, not shared memory: an iGPU reports several gigabytes
        // of shared system RAM, which must not make it look like the fast card.
        return hardware
            .OrderByDescending(adapter => adapter.DedicatedVideoMemory)
            .ThenBy(adapter => adapter.Index)
            .First();
    }

    /// <summary>
    /// Enumerates the DXGI adapters in the same order DirectML uses for its
    /// device id. Returns an empty list when DXGI is unavailable, so callers
    /// keep the adapter-0 behaviour and the normal CPU fallback.
    /// </summary>
    public static IReadOnlyList<DirectMlAdapterInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<DirectMlAdapterInfo>();
        }

        var adapters = new List<DirectMlAdapterInfo>();
        object? factoryObject = null;
        var factoryPointer = IntPtr.Zero;

        try
        {
            var factoryId = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref factoryId, out factoryPointer) < 0 ||
                factoryPointer == IntPtr.Zero)
            {
                return adapters;
            }

            factoryObject = Marshal.GetObjectForIUnknown(factoryPointer);
            if (factoryObject is not IDXGIFactory1 factory)
            {
                return adapters;
            }

            for (var index = 0u; ; index++)
            {
                IDXGIAdapter1? adapter = null;
                try
                {
                    if (factory.EnumAdapters1(index, out adapter) < 0 || adapter is null)
                    {
                        break;
                    }

                    if (adapter.GetDesc1(out var description) >= 0)
                    {
                        adapters.Add(new DirectMlAdapterInfo(
                            (int)index,
                            (description.Description ?? string.Empty).TrimEnd('\0'),
                            unchecked((long)description.DedicatedVideoMemory),
                            unchecked((long)description.SharedSystemMemory),
                            (description.Flags & AdapterFlagSoftware) != 0));
                    }
                }
                finally
                {
                    if (adapter is not null && Marshal.IsComObject(adapter))
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException
                or COMException or InvalidCastException or NotSupportedException)
        {
            // DXGI is not usable here: let OnnxSessionFactory try adapter 0 and
            // then fall back to the CPU with its own reason.
            adapters.Clear();
        }
        finally
        {
            if (factoryObject is not null && Marshal.IsComObject(factoryObject))
            {
                Marshal.ReleaseComObject(factoryObject);
            }

            if (factoryPointer != IntPtr.Zero)
            {
                Marshal.Release(factoryPointer);
            }
        }

        return adapters;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid name, IntPtr unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);

        [PreserveSig]
        int GetParent(ref Guid riid, out IntPtr parent);

        [PreserveSig]
        int EnumAdapters(uint adapter, out IntPtr adapterOut);

        [PreserveSig]
        int MakeWindowAssociation(IntPtr windowHandle, uint flags);

        [PreserveSig]
        int GetWindowAssociation(out IntPtr windowHandle);

        [PreserveSig]
        int CreateSwapChain(IntPtr device, IntPtr description, out IntPtr swapChain);

        [PreserveSig]
        int CreateSoftwareAdapter(IntPtr module, out IntPtr adapterOut);

        [PreserveSig]
        int EnumAdapters1(uint adapter, out IDXGIAdapter1? adapterOut);

        [PreserveSig]
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid name, IntPtr unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);

        [PreserveSig]
        int GetParent(ref Guid riid, out IntPtr parent);

        [PreserveSig]
        int EnumOutputs(uint output, out IntPtr outputOut);

        [PreserveSig]
        int GetDesc(out DxgiAdapterDesc description);

        [PreserveSig]
        int CheckInterfaceSupport(ref Guid interfaceName, out long version);

        [PreserveSig]
        int GetDesc1(out DxgiAdapterDesc1 description);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }
}
