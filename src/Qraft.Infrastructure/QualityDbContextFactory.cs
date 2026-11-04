using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Qraft.Infrastructure;

public sealed class QualityDbContextFactory : IDesignTimeDbContextFactory<QualityDbContext>
{
    public QualityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseSqlite("Data Source=data/qraft.db;Foreign Keys=True").Options;
        return new QualityDbContext(options);
    }
}
