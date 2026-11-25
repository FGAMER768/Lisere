using System.Diagnostics;
using ComputeSharp;

namespace Lisere.Ai.Enhancement.Gpu;

/// <summary>
/// Choisit l'adaptateur D3D12 du pipeline classique. Sur une machine hybride,
/// DXGI énumère l'iGPU en premier et la mémoire dédiée seule ne suffit pas à
/// désigner le bon GPU : l'ordre est donc explicite, NVIDIA puis AMD, avant la
/// mémoire dédiée. <c>LISERE_CLASSIC_GPU</c> accepte <c>0</c>/<c>cpu</c> pour
/// désactiver, un index, ou un fragment de nom.
/// </summary>
internal static class GpuDeviceSelector
{
    private static readonly Lazy<string?> HiddenNvidiaAdapter = new(FindHiddenNvidia);

    public static GraphicsDevice? TryCreate(out string? reason)
    {
        var device = TryCreate(out reason, out _);
        return device;
    }

    /// <summary>
    /// <paramref name="note"/> porte un diagnostic qui n'empêche pas le rendu,
    /// par exemple une NVIDIA présente mais non exposée à D3D12.
    /// </summary>
    public static GraphicsDevice? TryCreate(out string? reason, out string? note)
    {
        reason = null;
        note = null;

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

        var hardware = devices
            .Where(device => device.IsHardwareAccelerated)
            .OrderBy(Rank)
            .ThenByDescending(device => device.DedicatedMemorySize)
            .ToList();

        if (hardware.Count == 0)
        {
            reason = "aucun GPU matériel D3D12 détecté" + Describe(devices);
            return null;
        }

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
                reason = $"adaptateur « {preference} » introuvable" + Describe(devices);
                return null;
            }
        }

        selected ??= hardware[0];

        // Une GeForce absente de l'énumération signifie que le pilote hybride
        // n'expose pas le GPU dédié au processus : aucun choix d'adaptateur ne
        // peut le corriger, mais la raison doit le dire au lieu de laisser
        // croire que la machine n'a pas de NVIDIA.
        if (Rank(selected) > 0 && HiddenNvidiaAdapter.Value is { } nvidia)
        {
            note = $"« {nvidia} » est présente mais absente de D3D12 ; " +
                "définissez Lisere.exe sur « Processeur NVIDIA haute performance » " +
                "(Paramètres Windows → Affichage → Graphiques, ou Panneau NVIDIA → " +
                "Gérer les paramètres 3D → Paramètres de programme).";
        }

        // Les adaptateurs non retenus ne sont pas libérés : ComputeSharp rend
        // la même instance pour un adaptateur donné, et un autre moteur (le
        // modèle ONNX et son étage colorimétrique, par exemple) peut déjà la
        // tenir. La libérer ici invaliderait son device en pleine session.
        return selected;
    }

    /// <summary>
    /// 0 : NVIDIA, 1 : AMD/Intel Arc, 2 : autre, 3 : iGPU Intel. Le tri suivant,
    /// la mémoire dédiée, départage deux GPU de même rang.
    /// </summary>
    private static int Rank(GraphicsDevice device)
    {
        var name = device.Name;

        if (Contains(name, "nvidia") ||
            Contains(name, "geforce") ||
            Contains(name, "quadro") ||
            Contains(name, "rtx"))
        {
            return 0;
        }

        if (Contains(name, "radeon") || Contains(name, "amd") || Contains(name, "arc"))
        {
            return 1;
        }

        return Contains(name, "intel") ? 3 : 2;
    }

    private static bool Contains(string name, string fragment) =>
        name.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>Tous les adaptateurs vus, pour qu'un rapport distant soit exploitable.</summary>
    public static string Describe(IEnumerable<GraphicsDevice> devices)
    {
        var listed = devices
            .Select(device =>
                $"{device.Name} ({FormatMemory(device.DedicatedMemorySize)}" +
                (device.IsHardwareAccelerated ? ")" : ", logiciel)"))
            .ToList();

        return listed.Count == 0
            ? " (aucun adaptateur énuméré)"
            : " : " + string.Join(" · ", listed);
    }

    private static string FormatMemory(nuint dedicated)
    {
        var gigabytes = (ulong)dedicated / (1024UL * 1024UL * 1024UL);
        return gigabytes > 0 ? $"{gigabytes} Go" : "mémoire partagée";
    }

    /// <summary>
    /// Interroge le pilote NVIDIA : nvidia-smi nomme la carte même quand DXGI
    /// ne l'énumère pas pour ce processus. Le résultat est mis en cache et
    /// n'est demandé qu'une fois, et seulement si aucun adaptateur NVIDIA n'a
    /// été vu.
    /// </summary>
    private static string? FindHiddenNvidia()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi")
            {
                Arguments = "--query-gpu=name --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(2000) || process.ExitCode != 0)
            {
                return null;
            }

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // nvidia-smi absent : la machine n'a pas de pilote NVIDIA, ou il est
            // hors du PATH. Ce n'est pas une panne du rendu.
            return null;
        }
    }

}
