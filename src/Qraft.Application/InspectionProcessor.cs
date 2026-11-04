using Qraft.Contracts;
using Qraft.Domain;

namespace Qraft.Application;

/// <summary>
/// Validates one incoming inspection and maps a domain evaluation to a result message.
/// Persistence, messaging and decisions about releasing a batch belong to other components.
/// </summary>
public sealed class InspectionProcessor
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public InspectionProcessedV1 Process(InspectionSubmittedV1 message, DateTimeOffset processedAt)
    {
        if (message is null)
        {
            return Reject(Guid.Empty, processedAt, "Required", "$", "Komunikat badania nie może mieć wartości null.");
        }
        var errors = Validate(message, processedAt);
        var result = new InspectionProcessedV1
        {
            ResultId = Guid.NewGuid(),
            MessageId = message.MessageId,
            SimulationId = message.SimulationId,
            BatchId = message.BatchId,
            BatchCode = message.BatchCode?.Trim() ?? "",
            ProductName = message.ProductName?.Trim() ?? "",
            Stage = message.Stage ?? "",
            Attempt = message.Attempt,
            ProcessedAt = processedAt,
            Status = "Rejected",
            Errors = errors
        };

        if (errors.Count > 0)
        {
            return result;
        }

        var measurements = message.Samples!.Select(sample => new SampleMeasurement(
            sample.Code.Trim(), sample.MoisturePercent!.Value,
            sample.DensityKgM3!.Value, sample.ColorConformant!.Value)).ToArray();
        var evaluation = QualityEvaluator.Evaluate(measurements);

        return result with
        {
            Status = "Processed",
            Outcome = evaluation.Outcome.ToString(),
            NonconformityCount = evaluation.NonconformityCount,
            Samples = evaluation.Samples.Select(MapSample).ToArray()
        };
    }

    /// <summary>Creates a correlated rejection when the incoming JSON cannot be deserialized.</summary>
    public InspectionProcessedV1 Reject(
        Guid messageId, DateTimeOffset processedAt, string code, string field, string description)
    {
        return new InspectionProcessedV1
        {
            ResultId = Guid.NewGuid(),
            MessageId = messageId,
            Status = "Rejected",
            ProcessedAt = processedAt,
            Errors = [new ValidationIssue(code, field, description)]
        };
    }

    private static SampleEvaluationV1 MapSample(SampleEvaluation sample)
    {
        return new SampleEvaluationV1(sample.Code, sample.IsConformant,
            sample.Criteria.Select(criterion => new CriterionEvaluationV1(
                criterion.Criterion switch
                {
                    QualityCriterion.Moisture => "MoisturePercent",
                    QualityCriterion.Density => "DensityKgM3",
                    QualityCriterion.Color => "ColorConformant",
                    _ => throw new InvalidOperationException("Nieznane kryterium jakości.")
                },
                criterion.Unit, criterion.NumericValue, criterion.BooleanValue,
                criterion.Minimum, criterion.Maximum, criterion.IsConformant)).ToArray());
    }

    private static IReadOnlyList<ValidationIssue> Validate(
        InspectionSubmittedV1 message, DateTimeOffset processedAt)
    {
        var errors = new List<ValidationIssue>();

        if (message.SchemaVersion != 1)
        {
            Add(errors, "UnsupportedSchemaVersion", "schemaVersion", "Obsługiwana wersja komunikatu to 1.");
        }

        ValidateId(message.MessageId, "messageId", errors);
        ValidateId(message.SimulationId, "simulationId", errors);
        ValidateId(message.BatchId, "batchId", errors);
        ValidateText(message.BatchCode, "batchCode", 64, errors);
        ValidateText(message.ProductName, "productName", 120, errors);
        ValidateText(message.Author, "author", 120, errors);

        if (message.MassKg <= 0m || message.MassKg > 1_000_000m)
        {
            Add(errors, "OutOfRange", "massKg", "Masa musi być większa od 0 i nie większa niż 1 000 000 kg.");
        }

        var todayUtc = DateOnly.FromDateTime(processedAt.UtcDateTime);
        if (message.ProductionDate == default || message.ProductionDate > todayUtc)
        {
            Add(errors, "InvalidDate", "productionDate", "Podaj datę produkcji nie późniejszą niż dzisiejsza data UTC.");
        }

        if (message.Stage is not ("E1" or "E2"))
        {
            Add(errors, "InvalidStage", "stage", "Etap musi mieć dokładnie wartość E1 albo E2.");
        }

        if (message.Attempt is < 1 or > 1000)
        {
            Add(errors, "OutOfRange", "attempt", "Numer podejścia musi należeć do zakresu od 1 do 1000.");
        }

        ValidateTimestamp(message.OccurredAt, "occurredAt", processedAt, errors);

        if (message.Samples is null || message.Samples.Count != QualityEvaluator.RequiredSampleCount)
        {
            Add(errors, "InvalidSampleCount", "samples", "Badanie musi zawierać dokładnie trzy próbki.");
        }

        if (message.Samples is null)
        {
            return errors;
        }

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < message.Samples.Count; index++)
        {
            var sample = message.Samples[index];
            var field = $"samples[{index}]";
            if (sample is null)
            {
                Add(errors, "Required", field, "Próbka nie może mieć wartości null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(sample.Code))
            {
                Add(errors, "Required", $"{field}.code", "Podaj kod próbki.");
            }
            else if (!codes.Add(sample.Code.Trim()))
            {
                Add(errors, "DuplicateSampleCode", $"{field}.code", "Kod próbki musi być unikalny w badaniu.");
            }

            ValidateTimestamp(sample.CollectedAt, $"{field}.collectedAt", processedAt, errors);
            ValidateMeasurement(sample.MoisturePercent, $"{field}.moisturePercent", errors);
            ValidateMeasurement(sample.DensityKgM3, $"{field}.densityKgM3", errors);
            if (sample.ColorConformant is null)
            {
                Add(errors, "Required", $"{field}.colorConformant", "Podaj odpowiedź Tak albo Nie dla zgodności barwy.");
            }
        }

        return errors;
    }

    private static void ValidateId(Guid value, string field, List<ValidationIssue> errors)
    {
        if (value == Guid.Empty)
        {
            Add(errors, "Required", field, "Identyfikator nie może być pusty.");
        }
    }

    private static void ValidateText(string? value, string field, int maximumLength, List<ValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, "Required", field, "Pole tekstowe nie może być puste.");
        }
        else if (value.Trim().Length > maximumLength)
        {
            Add(errors, "MaxLength", field, $"Pole może zawierać najwyżej {maximumLength} znaków po usunięciu skrajnych spacji.");
        }
    }

    private static void ValidateTimestamp(
        DateTimeOffset value, string field, DateTimeOffset processedAt, List<ValidationIssue> errors)
    {
        if (value == default)
        {
            Add(errors, "Required", field, "Podaj prawidłową datę i czas.");
        }
        else if (value - processedAt > FutureTolerance)
        {
            Add(errors, "FutureTimestamp", field, "Data i czas nie mogą wyprzedzać czasu przetwarzania o więcej niż 5 minut.");
        }
    }

    private static void ValidateMeasurement(decimal? value, string field, List<ValidationIssue> errors)
    {
        if (value is null)
        {
            Add(errors, "Required", field, "Podaj wynik pomiaru.");
            return;
        }

        // Decimal cannot contain NaN or infinity. Unsupported JSON numbers are rejected by the deserializer.
        if (value < 0m)
        {
            Add(errors, "OutOfRange", field, "Wynik pomiaru nie może być ujemny.");
        }

        // Compare values, not the stored scale: 0.2000 is equivalent to 0.200.
        if (decimal.Round(value.Value, 3) != value.Value)
        {
            Add(errors, "PrecisionExceeded", field, "Wynik pomiaru może mieć najwyżej trzy miejsca po przecinku, z pominięciem końcowych zer.");
        }
    }

    private static void Add(List<ValidationIssue> errors, string code, string field, string description)
        => errors.Add(new ValidationIssue(code, field, description));
}
