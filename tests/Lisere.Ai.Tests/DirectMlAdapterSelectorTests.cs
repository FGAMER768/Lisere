using Lisere.Ai.Models;
using Lisere.Ai.Onnx;
using Xunit.Abstractions;

namespace Lisere.Ai.Tests;

public class DirectMlAdapterSelectorTests
{
    private const long Megabyte = 1024 * 1024;

    private readonly ITestOutputHelper _output;

    public DirectMlAdapterSelectorTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheAdapterWithTheMostDedicatedMemoryWinsEvenWhenTheIntegratedOneIsFirst()
    {
        var integrated = new DirectMlAdapterInfo(
            0,
            "Intel(R) Iris(R) Xe Graphics",
            128 * Megabyte,
            8 * 1024 * Megabyte,
            IsSoftware: false);
        var discrete = new DirectMlAdapterInfo(
            1,
            "NVIDIA GeForce RTX 4070 Laptop GPU",
            8 * 1024 * Megabyte,
            0,
            IsSoftware: false);

        var chosen = DirectMlAdapterSelector.Choose([integrated, discrete]);

        Assert.NotNull(chosen);
        Assert.Equal(1, chosen.Value.Index);
        Assert.Equal("NVIDIA GeForce RTX 4070 Laptop GPU", chosen.Value.Name);
    }

    [Fact]
    public void SharedMemoryDoesNotOutrankDedicatedMemory()
    {
        // A large shared pool is what an iGPU reports; it must never win.
        var integrated = new DirectMlAdapterInfo(
            0,
            "Intel(R) Graphics",
            0,
            32 * 1024 * Megabyte,
            IsSoftware: false);
        var discrete = new DirectMlAdapterInfo(
            1,
            "NVIDIA GeForce GTX 1650",
            4 * 1024 * Megabyte,
            0,
            IsSoftware: false);

        var chosen = DirectMlAdapterSelector.Choose([integrated, discrete]);

        Assert.NotNull(chosen);
        Assert.Equal(1, chosen.Value.Index);
    }

    [Fact]
    public void SoftwareAdaptersAreNeverChosen()
    {
        var software = new DirectMlAdapterInfo(
            0,
            "Microsoft Basic Render Driver",
            0,
            0,
            IsSoftware: true);
        var hardware = new DirectMlAdapterInfo(
            1,
            "Intel(R) Iris(R) Xe Graphics",
            128 * Megabyte,
            8 * 1024 * Megabyte,
            IsSoftware: false);

        Assert.Equal(1, DirectMlAdapterSelector.Choose([software, hardware])?.Index);
        Assert.Null(DirectMlAdapterSelector.Choose([software]));
    }

    [Fact]
    public void AnExplicitIndexWinsOverTheAutomaticChoice()
    {
        var integrated = new DirectMlAdapterInfo(0, "Intel(R) Graphics", 128 * Megabyte, 8 * 1024 * Megabyte, false);
        var discrete = new DirectMlAdapterInfo(1, "NVIDIA GeForce RTX 4070", 8 * 1024 * Megabyte, 0, false);

        Assert.Equal(0, DirectMlAdapterSelector.Choose([integrated, discrete], "0")?.Index);
    }

    [Fact]
    public void AnExplicitNameMatchesPartiallyAndCaseInsensitively()
    {
        var integrated = new DirectMlAdapterInfo(0, "Intel(R) Graphics", 128 * Megabyte, 8 * 1024 * Megabyte, false);
        var discrete = new DirectMlAdapterInfo(1, "NVIDIA GeForce RTX 4070", 8 * 1024 * Megabyte, 0, false);

        Assert.Equal(1, DirectMlAdapterSelector.Choose([integrated, discrete], "rtx 4070")?.Index);
    }

    [Fact]
    public void AnUnknownPreferenceFallsBackToTheAutomaticChoice()
    {
        var integrated = new DirectMlAdapterInfo(0, "Intel(R) Graphics", 128 * Megabyte, 8 * 1024 * Megabyte, false);
        var discrete = new DirectMlAdapterInfo(1, "NVIDIA GeForce RTX 4070", 8 * 1024 * Megabyte, 0, false);

        Assert.Equal(1, DirectMlAdapterSelector.Choose([integrated, discrete], "9")?.Index);
        Assert.Equal(1, DirectMlAdapterSelector.Choose([integrated, discrete], "not a gpu")?.Index);
    }

    [Fact]
    public void TheMachineAdaptersAreEnumeratedInDirectMlOrder()
    {
        var adapters = DirectMlAdapterSelector.Enumerate();
        _output.WriteLine(string.Join(
            Environment.NewLine,
            adapters.Select(adapter =>
                $"adapter {adapter.Index}: {adapter.Name} " +
                $"(dedicated {adapter.DedicatedVideoMemory / Megabyte} MB, " +
                $"shared {adapter.SharedSystemMemory / Megabyte} MB, " +
                $"software: {adapter.IsSoftware})")));

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.NotEmpty(adapters);

        for (var index = 0; index < adapters.Count; index++)
        {
            Assert.Equal(index, adapters[index].Index);
            Assert.False(string.IsNullOrWhiteSpace(adapters[index].Name));
        }
    }

    [Fact]
    public void TheDirectMlSessionNamesTheAdapterItChose()
    {
        var directory = ModelCatalog.DiscoverDirectory();
        Assert.NotNull(directory);
        var model = ModelCatalog.Load(directory).Find("realesr-general-x4v3");
        Assert.NotNull(model);

        var selected = DirectMlAdapterSelector.Resolve();
        var result = OnnxSessionFactory.Create(model.FullPath);
        using var session = result.Session;
        _output.WriteLine(
            $"provider: {result.ProviderName}, device: {result.DeviceName ?? "<none>"}, " +
            $"selected: {selected?.Name ?? "<none>"}, fallback: {result.FallbackReason ?? "<none>"}");

        if (result.ProviderName == "DirectML")
        {
            Assert.False(string.IsNullOrWhiteSpace(result.DeviceName));
            Assert.Equal(selected?.Name, result.DeviceName);
        }
        else
        {
            Assert.Equal("CPU", result.ProviderName);
            Assert.False(string.IsNullOrWhiteSpace(result.FallbackReason));
        }
    }
}
