using Microsoft.EntityFrameworkCore;

namespace Qraft.Simulator.Data;

public sealed class SimulatorDbContext(DbContextOptions<SimulatorDbContext> options) : DbContext(options)
{
    public DbSet<ScenarioFixture> Fixtures => Set<ScenarioFixture>();
    public DbSet<SimulationRun> Runs => Set<SimulationRun>();
    public DbSet<SourceMessage> Messages => Set<SourceMessage>();
    public DbSet<SubmissionOutbox> Outbox => Set<SubmissionOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    public static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScenarioFixture>(entity =>
        {
            entity.ToTable("ScenarioFixtures");
            entity.HasKey(item => item.Code);
            entity.Property(item => item.Code).HasMaxLength(64);
            entity.Property(item => item.Name).IsRequired();
            entity.Property(item => item.Description).IsRequired();
            entity.Property(item => item.TemplateJson).IsRequired();
            entity.Property(item => item.ExpectedStatus).IsRequired();
        });
        modelBuilder.Entity<SimulationRun>(entity =>
        {
            entity.ToTable("SimulationRuns");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ScenarioCode).IsRequired();
            entity.HasOne<ScenarioFixture>().WithMany().HasForeignKey(item => item.ScenarioCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.CreatedAt);
        });
        modelBuilder.Entity<SourceMessage>(entity =>
        {
            entity.ToTable("SourceMessages");
            entity.HasKey(item => item.MessageId);
            entity.Property(item => item.BatchCode).IsRequired();
            entity.Property(item => item.Stage).IsRequired();
            entity.Property(item => item.PayloadJson).IsRequired();
            entity.HasOne<SimulationRun>().WithMany().HasForeignKey(item => item.RunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.RunId);
            entity.HasIndex(item => item.ResultId).IsUnique();
        });
        modelBuilder.Entity<SubmissionOutbox>(entity =>
        {
            entity.ToTable("SubmissionOutbox");
            entity.HasKey(item => item.Id);
            entity.HasOne<SourceMessage>().WithMany().HasForeignKey(item => item.SourceMessageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.PublishedAt, item.NextAttemptAt });
            entity.HasIndex(item => item.SourceMessageId);
        });
    }
}

public sealed class ScenarioFixture
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TemplateJson { get; set; } = "";
    public string ExpectedStatus { get; set; } = "";
    public string? ExpectedOutcome { get; set; }
}

public sealed class SimulationRun
{
    public Guid Id { get; set; }
    public string ScenarioCode { get; set; } = "";
    public int Count { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SourceMessage
{
    public Guid MessageId { get; set; }
    public Guid RunId { get; set; }
    public Guid BatchId { get; set; }
    public string BatchCode { get; set; } = "";
    public string Stage { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid? ResultId { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? Status { get; set; }
    public string? Outcome { get; set; }
    public string? ResultJson { get; set; }
}

public sealed class SubmissionOutbox
{
    public Guid Id { get; set; }
    public Guid SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? LastError { get; set; }
}
