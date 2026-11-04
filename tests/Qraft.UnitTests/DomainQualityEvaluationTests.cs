using Qraft.Domain;

namespace Qraft.UnitTests;

public sealed class DomainQualityEvaluationTests
{
    [Theory]
    [InlineData("0", "880")]
    [InlineData("0.200", "950")]
    public void Inclusive_numeric_boundaries_are_conformant(string moisture, string density)
    {
        var sample = QualityEvaluator.EvaluateSample(new SampleMeasurement(
            "S1", Parse(moisture), Parse(density), true));

        Assert.True(sample.IsConformant);
        Assert.Equal(0, sample.NonconformityCount);
    }

    [Theory]
    [InlineData("0.201", "900", QualityCriterion.Moisture)]
    [InlineData("0.100", "879.999", QualityCriterion.Density)]
    [InlineData("0.100", "950.001", QualityCriterion.Density)]
    public void Values_outside_limits_identify_the_failed_criterion(
        string moisture, string density, QualityCriterion criterion)
    {
        var sample = QualityEvaluator.EvaluateSample(new SampleMeasurement(
            "S1", Parse(moisture), Parse(density), true));

        Assert.False(sample.IsConformant);
        var failure = Assert.Single(sample.Criteria, item => !item.IsConformant);
        Assert.Equal(criterion, failure.Criterion);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Color_uses_the_actual_boolean_answer(bool answer, bool expectedConformity)
    {
        var sample = QualityEvaluator.EvaluateSample(new SampleMeasurement("S1", 0.1m, 900m, answer));

        var color = Assert.Single(sample.Criteria, criterion => criterion.Criterion == QualityCriterion.Color);
        Assert.Equal(expectedConformity, sample.IsConformant);
        Assert.Equal(answer, color.BooleanValue);
        Assert.Null(color.NumericValue);
        Assert.Null(color.Minimum);
        Assert.Null(color.Maximum);
    }

    [Fact]
    public void One_sample_failing_two_criteria_produces_two_nonconformities()
    {
        var evaluation = QualityEvaluator.Evaluate(
        [
            new("S1", 0.280m, 900m, false),
            new("S2", 0.100m, 900m, true),
            new("S3", 0.100m, 900m, true)
        ]);

        Assert.Equal(InspectionOutcome.Nonconformant, evaluation.Outcome);
        Assert.Single(evaluation.Samples, sample => !sample.IsConformant);
        Assert.Equal(2, evaluation.NonconformityCount);
    }

    [Fact]
    public void Incomplete_sample_sets_cannot_be_evaluated_as_a_positive_inspection()
    {
        Assert.Throws<ArgumentException>(() => QualityEvaluator.Evaluate([]));
        Assert.Throws<ArgumentException>(() => QualityEvaluator.Evaluate([new("S1", 0.1m, 900m, true)]));
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
