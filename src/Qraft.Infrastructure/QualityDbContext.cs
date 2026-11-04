using Microsoft.EntityFrameworkCore;

namespace Qraft.Infrastructure;

public sealed class ReceivedInspection
{
    public Guid MessageId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public Guid SimulationId { get; set; }
    public Guid BatchId { get; set; }
    public string BatchCode { get; set; } = "";
    public string Stage { get; set; } = "";
    public int Attempt { get; set; }
    public string Status { get; set; } = "";
    public string? Outcome { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public List<StoredSampleAssessment> Samples { get; set; } = [];
}

public sealed class StoredSampleAssessment
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string Code { get; set; } = "";
    public bool IsConformant { get; set; }
    public string CriteriaJson { get; set; } = "";
}

public sealed class ResultOutboxMessage
{
    public Guid Id { get; set; }
    public Guid SourceMessageId { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class QualityDbContext(DbContextOptions<QualityDbContext> options) : DbContext(options)
{
    public DbSet<ReceivedInspection> ReceivedInspections => Set<ReceivedInspection>();
    public DbSet<StoredSampleAssessment> SampleAssessments => Set<StoredSampleAssessment>();
    public DbSet<ResultOutboxMessage> ResultOutbox => Set<ResultOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var incoming = modelBuilder.Entity<ReceivedInspection>();
        incoming.HasKey(x => x.MessageId);
        incoming.Property(x => x.PayloadHash).HasMaxLength(64);
        incoming.HasIndex(x => x.ReceivedAtUtc);
        incoming.HasMany(x => x.Samples).WithOne().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        var sample = modelBuilder.Entity<StoredSampleAssessment>();
        sample.HasKey(x => x.Id);
        sample.HasIndex(x => new { x.MessageId, x.Code }).IsUnique();
        var outbox = modelBuilder.Entity<ResultOutboxMessage>();
        outbox.HasKey(x => x.Id);
        outbox.HasIndex(x => x.SourceMessageId).IsUnique();
        outbox.HasIndex(x => new { x.PublishedAtUtc, x.NextAttemptAtUtc });
    }
}
