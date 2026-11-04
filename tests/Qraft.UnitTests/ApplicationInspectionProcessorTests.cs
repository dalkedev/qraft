using Qraft.Application;
using Qraft.Contracts;

namespace Qraft.UnitTests;

public sealed class ApplicationInspectionProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly InspectionProcessor _processor = new();

    [Fact]
    public void Valid_message_is_evaluated_and_keeps_correlation_metadata()
    {
        var message = ValidMessage() with { BatchCode = " PP-001 ", ProductName = " PP Demo " };

        var result = _processor.Process(message, Now);

        Assert.Equal("Processed", result.Status);
        Assert.Equal("Conformant", result.Outcome);
        Assert.Empty(result.Errors);
        Assert.NotEqual(Guid.Empty, result.ResultId);
        Assert.Equal(message.MessageId, result.MessageId);
        Assert.Equal(message.SimulationId, result.SimulationId);
        Assert.Equal(message.BatchId, result.BatchId);
        Assert.Equal("PP-001", result.BatchCode);
        Assert.Equal("PP Demo", result.ProductName);
        Assert.Equal(message.Stage, result.Stage);
        Assert.Equal(message.Attempt, result.Attempt);
        Assert.Equal(Now, result.ProcessedAt);
        Assert.Equal(3, result.Samples.Count);
        Assert.All(result.Samples, sample => Assert.Equal(3, sample.Criteria.Count));
    }

    [Fact]
    public void Valid_out_of_specification_measurements_are_processed_instead_of_rejected()
    {
        var message = WithFirstSample(sample => sample with { MoisturePercent = 0.280m, ColorConformant = false });

        var result = _processor.Process(message, Now);

        Assert.Equal("Processed", result.Status);
        Assert.Equal("Nonconformant", result.Outcome);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.NonconformityCount);
        var failedSample = Assert.Single(result.Samples, sample => !sample.IsConformant);
        Assert.Equal(2, failedSample.Criteria.Count(criterion => !criterion.IsConformant));
    }

    [Theory]
    [InlineData("moisturePercent")]
    [InlineData("densityKgM3")]
    [InlineData("colorConformant")]
    public void Missing_measurements_reject_the_contract_without_domain_evaluation(string field)
    {
        var message = WithFirstSample(sample => field switch
        {
            "moisturePercent" => sample with { MoisturePercent = null },
            "densityKgM3" => sample with { DensityKgM3 = null },
            _ => sample with { ColorConformant = null }
        });

        AssertRejection(_processor.Process(message, Now), "Required", $"samples[0].{field}");
    }

    [Theory]
    [InlineData("E3")]
    [InlineData("e1")]
    [InlineData(" E1 ")]
    public void Stage_must_match_the_contract_exactly(string stage)
    {
        AssertRejection(_processor.Process(ValidMessage() with { Stage = stage }, Now), "InvalidStage", "stage");
    }

    [Fact]
    public void Unsupported_schema_is_rejected()
    {
        AssertRejection(_processor.Process(ValidMessage() with { SchemaVersion = 2 }, Now),
            "UnsupportedSchemaVersion", "schemaVersion");
    }

    [Fact]
    public void Duplicate_sample_codes_are_detected_after_trimming_and_ignoring_case()
    {
        var message = ValidMessage();
        message = message with { Samples = [message.Samples![0], message.Samples[1] with { Code = " s1 " }, message.Samples[2]] };

        AssertRejection(_processor.Process(message, Now), "DuplicateSampleCode", "samples[1].code");
    }

    [Fact]
    public void Precision_is_mathematical_and_trailing_zeroes_are_accepted()
    {
        var accepted = WithFirstSample(sample => sample with { MoisturePercent = 0.2000m, DensityKgM3 = 880.0000m });
        var rejected = WithFirstSample(sample => sample with { MoisturePercent = 0.2001m });

        var result = _processor.Process(accepted, Now);
        Assert.Equal("Processed", result.Status);
        Assert.Equal("Conformant", result.Outcome);
        AssertRejection(_processor.Process(rejected, Now), "PrecisionExceeded", "samples[0].moisturePercent");
    }

    [Fact]
    public void Null_samples_and_null_elements_produce_validation_errors()
    {
        AssertRejection(_processor.Process(ValidMessage() with { Samples = null }, Now), "InvalidSampleCount", "samples");
        AssertRejection(_processor.Process(ValidMessage() with { Samples = [null!, Sample("S2"), Sample("S3")] }, Now),
            "Required", "samples[0]");
    }

    [Fact]
    public void Null_message_is_rejected_without_an_unhandled_exception()
    {
        AssertRejection(_processor.Process(null!, Now), "Required", "$");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void Exactly_three_samples_are_required(int count)
    {
        var samples = Enumerable.Range(1, count).Select(index => Sample($"S{index}")).ToArray();

        AssertRejection(_processor.Process(ValidMessage() with { Samples = samples }, Now), "InvalidSampleCount", "samples");
    }

    [Fact]
    public void Empty_identifiers_and_blank_metadata_are_reported_together()
    {
        var message = ValidMessage() with
        {
            MessageId = Guid.Empty, SimulationId = Guid.Empty, BatchId = Guid.Empty,
            BatchCode = " ", ProductName = null!, Author = "\t"
        };

        var result = _processor.Process(message, Now);

        foreach (var field in new[] { "messageId", "simulationId", "batchId", "batchCode", "productName", "author" })
        {
            AssertRejection(result, "Required", field);
        }
    }

    [Fact]
    public void Text_limits_apply_after_trimming()
    {
        var accepted = ValidMessage() with
        {
            BatchCode = $" {new string('B', 64)} ", ProductName = new string('P', 120), Author = new string('A', 120)
        };
        Assert.Equal("Processed", _processor.Process(accepted, Now).Status);

        var result = _processor.Process(accepted with
        {
            BatchCode = new string('B', 65), ProductName = new string('P', 121), Author = new string('A', 121)
        }, Now);
        foreach (var field in new[] { "batchCode", "productName", "author" })
        {
            AssertRejection(result, "MaxLength", field);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000000.001")]
    public void Mass_outside_the_supported_range_is_rejected(string mass)
    {
        var value = decimal.Parse(mass, System.Globalization.CultureInfo.InvariantCulture);
        AssertRejection(_processor.Process(ValidMessage() with { MassKg = value }, Now), "OutOfRange", "massKg");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Attempt_outside_the_supported_range_is_rejected(int attempt)
    {
        AssertRejection(_processor.Process(ValidMessage() with { Attempt = attempt }, Now), "OutOfRange", "attempt");
    }

    [Fact]
    public void Future_dates_and_missing_timestamps_are_rejected()
    {
        var message = WithFirstSample(sample => sample with { CollectedAt = Now.AddMinutes(5).AddTicks(1) }) with
        {
            ProductionDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1), OccurredAt = default
        };

        var result = _processor.Process(message, Now);

        AssertRejection(result, "InvalidDate", "productionDate");
        AssertRejection(result, "Required", "occurredAt");
        AssertRejection(result, "FutureTimestamp", "samples[0].collectedAt");
    }

    [Fact]
    public void Future_tolerance_and_UTC_production_date_use_the_supplied_clock()
    {
        var processedAt = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.FromHours(2));
        var message = WithFirstSample(sample => sample with { CollectedAt = processedAt.AddMinutes(5) }) with
        {
            ProductionDate = new DateOnly(2026, 10, 24), OccurredAt = processedAt.AddMinutes(5),
            Stage = "E2", Attempt = 1000, MassKg = 1_000_000m
        };

        Assert.Equal("Processed", _processor.Process(message, processedAt).Status);
        AssertRejection(_processor.Process(message with { ProductionDate = new DateOnly(2026, 10, 25) }, processedAt),
            "InvalidDate", "productionDate");
    }

    [Fact]
    public void Negative_measurements_are_contract_errors()
    {
        var message = WithFirstSample(sample => sample with { MoisturePercent = -0.001m, DensityKgM3 = -1m });

        var result = _processor.Process(message, Now);

        AssertRejection(result, "OutOfRange", "samples[0].moisturePercent");
        AssertRejection(result, "OutOfRange", "samples[0].densityKgM3");
    }

    [Fact]
    public void Parse_rejection_is_correlated_and_contains_a_structured_issue()
    {
        var messageId = Guid.NewGuid();
        var result = _processor.Reject(messageId, Now, "InvalidJson", "$", "Nieprawidłowy format komunikatu.");

        AssertRejection(result, "InvalidJson", "$");
        Assert.Equal(messageId, result.MessageId);
        Assert.NotEqual(Guid.Empty, result.ResultId);
        Assert.Equal(Now, result.ProcessedAt);
    }

    private static void AssertRejection(InspectionProcessedV1 result, string code, string field)
    {
        Assert.Equal("Rejected", result.Status);
        Assert.Null(result.Outcome);
        Assert.Empty(result.Samples);
        Assert.Equal(0, result.NonconformityCount);
        Assert.Contains(result.Errors, error => error.Code == code && error.Field == field && !string.IsNullOrWhiteSpace(error.Message));
    }

    private static InspectionSubmittedV1 WithFirstSample(Func<SampleMeasurementV1, SampleMeasurementV1> update)
    {
        var message = ValidMessage();
        return message with { Samples = [update(message.Samples![0]), message.Samples[1], message.Samples[2]] };
    }

    private static SampleMeasurementV1 Sample(string code) => new()
    {
        Code = code, CollectedAt = Now.AddMinutes(-10),
        MoisturePercent = 0.100m, DensityKgM3 = 900m, ColorConformant = true
    };

    private static InspectionSubmittedV1 ValidMessage() => new()
    {
        MessageId = Guid.NewGuid(), SimulationId = Guid.NewGuid(), BatchId = Guid.NewGuid(),
        BatchCode = "PP-001", ProductName = "PP Demo", MassKg = 1000m,
        ProductionDate = new DateOnly(2026, 10, 24), Stage = "E1", Attempt = 1,
        Author = "Jan Kowalski", OccurredAt = Now.AddMinutes(-1),
        Samples = [Sample("S1"), Sample("S2"), Sample("S3")]
    };
}
