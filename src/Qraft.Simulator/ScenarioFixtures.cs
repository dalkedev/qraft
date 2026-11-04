using Microsoft.EntityFrameworkCore;
using Qraft.Contracts;
using Qraft.Simulator.Data;

namespace Qraft.Simulator;

public static class ScenarioFixtures
{
    public static async Task SeedAsync(SimulatorDbContext database, CancellationToken cancellationToken)
    {
        var existing = await database.Fixtures.Select(item => item.Code).ToListAsync(cancellationToken);
        foreach (var fixture in CreateFixtures().Where(item => !existing.Contains(item.Code)))
            database.Fixtures.Add(fixture);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<ScenarioFixture> CreateFixtures()
    {
        var samples = Enumerable.Range(1, 3).Select(index => new SampleMeasurementV1
        {
            Code = $"P{index}", CollectedAt = DateTimeOffset.UnixEpoch,
            MoisturePercent = 0.100m, DensityKgM3 = 910.000m, ColorConformant = true
        }).ToArray();
        var baseline = new InspectionSubmittedV1
        {
            BatchCode = "PP-DEMO", ProductName = "Regranulat PP Demo", MassKg = 1000m,
            ProductionDate = new DateOnly(2026, 1, 1), Stage = "E1", Attempt = 1,
            Author = "Symulator Qraft", OccurredAt = DateTimeOffset.UnixEpoch, Samples = samples
        };
        yield return Fixture("good", "Zgodna partia", "Trzy próbki spełniają wszystkie kryteria.",
            baseline, "Processed", "Conformant");
        yield return Fixture("nonconformant", "Niezgodna partia", "Pierwsza próbka przekracza limit wilgotności.",
            baseline with { Samples = [samples[0] with { MoisturePercent = 0.280m }, samples[1], samples[2]] },
            "Processed", "Nonconformant");
        yield return Fixture("invalid-payload", "Niepoprawne dane", "Poprawny JSON z ujemną masą i brakującym pomiarem.",
            baseline with { MassKg = -10m, Samples = [samples[0] with { MoisturePercent = null }, samples[1], samples[2]] },
            "Rejected", null);
        yield return new ScenarioFixture
        {
            Code = "malformed-json", Name = "Uszkodzony JSON", Description = "Niedomknięty JSON; identyfikatory transportowe pozostają poprawne.",
            TemplateJson = "{\"schemaVersion\":1,\"messageId\":\"{{messageId}}\",\"simulationId\":\"{{simulationId}}\",\"samples\":[",
            ExpectedStatus = "Rejected"
        };
        yield return Fixture("boundary", "Wartości graniczne", "Próbki zawierają dokładne dolne i górne granice zgodności.",
            baseline with
            {
                Samples = [
                    samples[0] with { MoisturePercent = 0m, DensityKgM3 = 880m },
                    samples[1] with { MoisturePercent = 0.200m, DensityKgM3 = 950m },
                    samples[2] with { MoisturePercent = 0.200m, DensityKgM3 = 880m }]
            }, "Processed", "Conformant");
    }

    private static ScenarioFixture Fixture(string code, string name, string description,
        InspectionSubmittedV1 template, string status, string? outcome) => new()
    {
        Code = code, Name = name, Description = description, TemplateJson = MessageJson.Serialize(template),
        ExpectedStatus = status, ExpectedOutcome = outcome
    };

    public static string CreatePayload(ScenarioFixture fixture, Guid messageId, Guid runId, Guid batchId,
        string batchCode, DateTimeOffset now)
    {
        if (fixture.Code == "malformed-json")
            return fixture.TemplateJson.Replace("{{messageId}}", messageId.ToString(), StringComparison.Ordinal)
                .Replace("{{simulationId}}", runId.ToString(), StringComparison.Ordinal);
        var template = MessageJson.Deserialize<InspectionSubmittedV1>(fixture.TemplateJson)
            ?? throw new InvalidDataException($"Scenario fixture {fixture.Code} has no payload.");
        return MessageJson.Serialize(template with
        {
            MessageId = messageId, SimulationId = runId, BatchId = batchId, BatchCode = batchCode,
            ProductionDate = DateOnly.FromDateTime(now.UtcDateTime), OccurredAt = now,
            Samples = template.Samples?.Select(sample => sample with { CollectedAt = now.AddMinutes(-1) }).ToArray()
        });
    }
}
