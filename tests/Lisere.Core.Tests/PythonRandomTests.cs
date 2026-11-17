using Lisere.Core.Imaging;

namespace Lisere.Core.Tests;

public class PythonRandomTests
{
    [Fact]
    public void RandIntMatchesCpythonForSeed1234()
    {
        var random = new PythonRandom(1234);
        int[] expected = [48, 5, -37, -51, -40, 23, -47, 34, 37, -41, -39, 47];
        var actual = new int[expected.Length];

        for (var i = 0; i < actual.Length; i++)
        {
            actual[i] = random.RandInt(-51, 51);
        }

        Assert.Equal(expected, actual);
    }
}
