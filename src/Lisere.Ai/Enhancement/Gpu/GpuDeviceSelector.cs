using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Choisit l'adaptateur D3D12 du pipeline classique. Le GPU dédié passe avant
/// l'iGPU : sur un portable, l'adaptateur 0 de DXGI est souvent l'iGPU, dont la
/// mémoire est partagée avec le système. <c>LISERE_CLASSIC_GPU</c> accepte
/// <c>0</c>/<c>cpu</c> pour désactiver, un index, ou un fragment de nom.
/// </summary>
internal static class GpuDeviceSelector
{
    public static GraphicsDevice? TryCreate(out string? reason)
    {
        reason = null;

        if (!OperatingSystem.IsWindows())
        {
            reason = "Windows requis pour le calcul D3D12";
            return null;
        }

        var preference = Environment.GetEnvironmentVariable("LISERE_CLASSIC_GPU")?.Trim();

        if (string.Equals(preference, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            reason = "désactivé par LISERE_CLASSIC_GPU";
            return null;
        }

        List<GraphicsDevice> devices;
        try
        {
            devices = GraphicsDevice.QueryDevices(_ => true).ToList();
        }
        catch (Exception exception)
        {
            reason = $"D3D12 indisponible : {exception.Message}";
            return null;
        }

        var hardware = devices.Where(device => device.IsHardwareAccelerated).ToList();
        GraphicsDevice? selected = null;

        if (preference is { Length: > 0 } &&
            !string.Equals(preference, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(preference, "on", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(preference, "force", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(preference, out var index))
            {
                selected = index >= 0 && index < hardware.Count ? hardware[index] : null;
            }
            else
            {
                selected = hardware.FirstOrDefault(device =>
                    device.Name.Contains(preference, StringComparison.OrdinalIgnoreCase));
            }

            if (selected is null)
            {
                reason = $"adaptateur « {preference} » introuvable";
                DisposeAll(devices);
                return null;
            }
        }

        selected ??= hardware
            .OrderByDescending(device => device.DedicatedMemorySize)
            .ThenByDescending(device => device.SharedMemorySize)
            .FirstOrDefault();

        if (selected is null)
        {
            reason = "aucun GPU matériel D3D12 détecté";
            DisposeAll(devices);
            return null;
        }

        foreach (var device in devices)
        {
            if (!ReferenceEquals(device, selected))
            {
                device.Dispose();
            }
        }

        return selected;
    }

    private static void DisposeAll(IEnumerable<GraphicsDevice> devices)
    {
        foreach (var device in devices)
        {
            device.Dispose();
        }
    }
}
