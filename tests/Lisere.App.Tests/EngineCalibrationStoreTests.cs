using System.IO;
using Lisere.Ai.Enhancement;
using Lisere.Services;

namespace Lisere.App.Tests;

public class EngineCalibrationStoreTests
{
    private static readonly EngineCalibrationKey Key = new(
        "realplksr-denoise-1x",
        "fa7a8541",
        "DirectML",
        256,
        "1.23.0");

    [Fact]
    public void AMeasurementIsRememberedAsSecondsPerMegapixel()
    {
        var store = new EngineCalibrationStore();

        Assert.Null(store.SecondsPerMegapixel(Key));

        store.Record(new EngineMeasurement(Key, 0.5, TimeSpan.FromSeconds(26)));

        Assert.Equal(52.0, store.SecondsPerMegapixel(Key)!.Value, 3);
    }

    [Fact]
    public void SuccessiveMeasurementsAreAveraged()
    {
        var store = new EngineCalibrationStore();

        store.Record(new EngineMeasurement(Key, 1.0, TimeSpan.FromSeconds(40)));
        store.Record(new EngineMeasurement(Key, 1.0, TimeSpan.FromSeconds(60)));

        Assert.Equal(50.0, store.SecondsPerMegapixel(Key)!.Value, 3);
    }

    [Fact]
    public void AnotherProviderIsAnotherCost()
    {
        var store = new EngineCalibrationStore();
        store.Record(new EngineMeasurement(Key, 1.0, TimeSpan.FromSeconds(52)));

        var onCpu = Key with { Provider = "CPU" };

        Assert.Null(store.SecondsPerMegapixel(onCpu));
    }

    [Fact]
    public void TheMostRecentProviderIsTheOneShown()
    {
        var store = new EngineCalibrationStore();
        Assert.Null(store.Latest(Key.ModelId));

        store.Record(new EngineMeasurement(Key with { Provider = "CPU" }, 1.0, TimeSpan.FromSeconds(70)));
        store.Record(new EngineMeasurement(Key with { Provider = "DirectML" }, 1.0, TimeSpan.FromSeconds(52)));

        // La dernière mesure fait foi, pas la première entrée du dictionnaire.
        Assert.Equal("DirectML", store.Latest(Key.ModelId)?.Provider);

        store.Record(new EngineMeasurement(Key with { Provider = "CPU" }, 1.0, TimeSpan.FromSeconds(68)));

        Assert.Equal("CPU", store.Latest(Key.ModelId)?.Provider);
        Assert.Null(store.Latest("autre-modele"));
    }

    [Fact]
    public void TheOrderSurvivesARestart()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-calibration-{Guid.NewGuid():N}.json");

        try
        {
            var first = new EngineCalibrationStore(path);
            first.Record(new EngineMeasurement(Key with { Provider = "CPU" }, 1.0, TimeSpan.FromSeconds(70)));
            first.Record(new EngineMeasurement(Key with { Provider = "DirectML" }, 1.0, TimeSpan.FromSeconds(52)));

            Assert.Equal("DirectML", new EngineCalibrationStore(path).Latest(Key.ModelId)?.Provider);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheCacheSurvivesARestart()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-calibration-{Guid.NewGuid():N}.json");

        try
        {
            new EngineCalibrationStore(path)
                .Record(new EngineMeasurement(Key, 0.81, TimeSpan.FromSeconds(51.8)));

            var reopened = new EngineCalibrationStore(path);

            Assert.Equal(63.95, reopened.SecondsPerMegapixel(Key)!.Value, 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ABrokenCacheFileIsRebuiltInsteadOfThrowing()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"lisere-calibration-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ ceci n'est pas du json");

        try
        {
            var store = new EngineCalibrationStore(path);

            Assert.Null(store.SecondsPerMegapixel(Key));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
