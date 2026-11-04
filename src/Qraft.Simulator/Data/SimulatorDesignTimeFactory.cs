using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Qraft.Simulator.Data;

public sealed class SimulatorDesignTimeFactory : IDesignTimeDbContextFactory<SimulatorDbContext>
{
    public SimulatorDbContext CreateDbContext(string[] args)
    {
        var path = Environment.GetEnvironmentVariable("Data__DatabasePath")
            ?? Path.Combine(Environment.GetEnvironmentVariable("QRAFT_DATA_DIR")
                ?? Path.Combine(Directory.GetCurrentDirectory(), "data"), "simulator.db");
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path), ForeignKeys = true, DefaultTimeout = 10
        };
        return new SimulatorDbContext(new DbContextOptionsBuilder<SimulatorDbContext>()
            .UseSqlite(connection.ToString()).Options);
    }
}
