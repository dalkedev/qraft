using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qraft.Contracts;

public static class QueueNames
{
    public const string Submissions = "qraft.inspections.v1";
    public const string Results = "simulator.results.v1";
    public const string DeadLetters = "qraft.dead-letters.v1";
}

public static class MessageJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public sealed record InspectionSubmittedV1
{
    public int SchemaVersion { get; init; } = 1;
    public Guid MessageId { get; init; }
    public Guid SimulationId { get; init; }
    public Guid BatchId { get; init; }
    public string BatchCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal MassKg { get; init; }
    public DateOnly ProductionDate { get; init; }
    public string Stage { get; init; } = "";
    public int Attempt { get; init; } = 1;
    public string Author { get; init; } = "";
    public DateTimeOffset OccurredAt { get; init; }
    public IReadOnlyList<SampleMeasurementV1>? Samples { get; init; }
}

public sealed record SampleMeasurementV1
{
    public string Code { get; init; } = "";
    public DateTimeOffset CollectedAt { get; init; }
    public decimal? MoisturePercent { get; init; }
    public decimal? DensityKgM3 { get; init; }
    public bool? ColorConformant { get; init; }
}

public sealed record ValidationIssue(string Code, string Field, string Message);

public sealed record CriterionEvaluationV1(
    string Criterion, string? Unit, decimal? NumericValue, bool? BooleanValue,
    decimal? Minimum, decimal? Maximum, bool IsConformant);

public sealed record SampleEvaluationV1(
    string Code, bool IsConformant, IReadOnlyList<CriterionEvaluationV1> Criteria);

public sealed record InspectionProcessedV1
{
    public int SchemaVersion { get; init; } = 1;
    public Guid ResultId { get; init; }
    public Guid MessageId { get; init; }
    public Guid SimulationId { get; init; }
    public Guid BatchId { get; init; }
    public string BatchCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Stage { get; init; } = "";
    public int Attempt { get; init; }
    public string Status { get; init; } = "";
    public string? Outcome { get; init; }
    public int NonconformityCount { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
    public IReadOnlyList<ValidationIssue> Errors { get; init; } = [];
    public IReadOnlyList<SampleEvaluationV1> Samples { get; init; } = [];
}
