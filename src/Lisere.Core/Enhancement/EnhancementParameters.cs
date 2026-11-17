namespace Lisere.Core.Enhancement;

public enum EnhancementParameter
{
    FixCompression,
    ReduceNoise,
    Sharpen,
    RecoverDetails,
    Dehalo,
    AntialiasDeblur
}

public readonly record struct ModelParameters(
    double Compression,
    double Noise,
    double Blur,
    double Details,
    double Halo,
    double PreBlur);

public readonly record struct EnhancementParameters(
    double FixCompression,
    double ReduceNoise,
    double Sharpen,
    double RecoverDetails,
    double Dehalo,
    double AntialiasDeblur)
{
    public static EnhancementParameters Zero { get; } = new(0, 0, 0, 0, 0, 0);

    public EnhancementParameters Clamp() => new(
        ClampValue(FixCompression, EnhancementTuning.Parameters.Minimum, EnhancementTuning.Parameters.Maximum),
        ClampValue(ReduceNoise, EnhancementTuning.Parameters.Minimum, EnhancementTuning.Parameters.Maximum),
        ClampValue(Sharpen, EnhancementTuning.Parameters.Minimum, EnhancementTuning.Parameters.Maximum),
        ClampValue(RecoverDetails, EnhancementTuning.Parameters.Minimum, EnhancementTuning.Parameters.Maximum),
        ClampValue(Dehalo, EnhancementTuning.Parameters.Minimum, EnhancementTuning.Parameters.Maximum),
        ClampValue(AntialiasDeblur, EnhancementTuning.Parameters.AntialiasMinimum, EnhancementTuning.Parameters.AntialiasMaximum));

    public ModelParameters ToModelScale()
    {
        var clamped = Clamp();
        var divisor = EnhancementTuning.Parameters.ModelScaleDivisor;

        return new ModelParameters(
            clamped.FixCompression / divisor,
            clamped.ReduceNoise / divisor,
            clamped.Sharpen / divisor,
            clamped.RecoverDetails / divisor,
            clamped.Dehalo / divisor,
            clamped.AntialiasDeblur / divisor);
    }

    public EnhancementParameters Plus(EnhancementParameters offsets) => new EnhancementParameters(
        FixCompression + offsets.FixCompression,
        ReduceNoise + offsets.ReduceNoise,
        Sharpen + offsets.Sharpen,
        RecoverDetails + offsets.RecoverDetails,
        Dehalo + offsets.Dehalo,
        AntialiasDeblur + offsets.AntialiasDeblur).Clamp();

    public double this[EnhancementParameter parameter] => parameter switch
    {
        EnhancementParameter.FixCompression => FixCompression,
        EnhancementParameter.ReduceNoise => ReduceNoise,
        EnhancementParameter.Sharpen => Sharpen,
        EnhancementParameter.RecoverDetails => RecoverDetails,
        EnhancementParameter.Dehalo => Dehalo,
        EnhancementParameter.AntialiasDeblur => AntialiasDeblur,
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    public EnhancementParameters With(EnhancementParameter parameter, double value) => parameter switch
    {
        EnhancementParameter.FixCompression => this with { FixCompression = value },
        EnhancementParameter.ReduceNoise => this with { ReduceNoise = value },
        EnhancementParameter.Sharpen => this with { Sharpen = value },
        EnhancementParameter.RecoverDetails => this with { RecoverDetails = value },
        EnhancementParameter.Dehalo => this with { Dehalo = value },
        EnhancementParameter.AntialiasDeblur => this with { AntialiasDeblur = value },
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    private static double ClampValue(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;
}
