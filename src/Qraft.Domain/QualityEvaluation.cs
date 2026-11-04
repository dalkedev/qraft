namespace Qraft.Domain;

public enum QualityCriterion
{
    Moisture,
    Density,
    Color
}

public enum InspectionOutcome
{
    Conformant,
    Nonconformant
}

public sealed record SampleMeasurement(
    string Code,
    decimal MoisturePercent,
    decimal DensityKgM3,
    bool ColorConformant);

public sealed record CriterionEvaluation(
    QualityCriterion Criterion,
    string? Unit,
    decimal? NumericValue,
    bool? BooleanValue,
    decimal? Minimum,
    decimal? Maximum,
    bool IsConformant);

public sealed record SampleEvaluation(string Code, IReadOnlyList<CriterionEvaluation> Criteria)
{
    public bool IsConformant => Criteria.All(criterion => criterion.IsConformant);
    public int NonconformityCount => Criteria.Count(criterion => !criterion.IsConformant);
}

public sealed record InspectionEvaluation(IReadOnlyList<SampleEvaluation> Samples)
{
    public InspectionOutcome Outcome => Samples.All(sample => sample.IsConformant)
        ? InspectionOutcome.Conformant
        : InspectionOutcome.Nonconformant;

    public int NonconformityCount => Samples.Sum(sample => sample.NonconformityCount);
}

/// <summary>Evaluates the three fixed, educational quality criteria.</summary>
public static class QualityEvaluator
{
    public const int RequiredSampleCount = 3;
    public const decimal MinimumMoisturePercent = 0m;
    public const decimal MaximumMoisturePercent = 0.200m;
    public const decimal MinimumDensityKgM3 = 880m;
    public const decimal MaximumDensityKgM3 = 950m;

    public static InspectionEvaluation Evaluate(IReadOnlyList<SampleMeasurement> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count != RequiredSampleCount)
        {
            throw new ArgumentException("Badanie wymaga dokładnie trzech próbek.", nameof(samples));
        }

        return new InspectionEvaluation(samples.Select(EvaluateSample).ToArray());
    }

    public static SampleEvaluation EvaluateSample(SampleMeasurement sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return new SampleEvaluation(sample.Code,
        [
            new(QualityCriterion.Moisture, "%", sample.MoisturePercent, null,
                MinimumMoisturePercent, MaximumMoisturePercent,
                sample.MoisturePercent is >= MinimumMoisturePercent and <= MaximumMoisturePercent),
            new(QualityCriterion.Density, "kg/m³", sample.DensityKgM3, null,
                MinimumDensityKgM3, MaximumDensityKgM3,
                sample.DensityKgM3 is >= MinimumDensityKgM3 and <= MaximumDensityKgM3),
            new(QualityCriterion.Color, null, null, sample.ColorConformant,
                null, null, sample.ColorConformant)
        ]);
    }
}
