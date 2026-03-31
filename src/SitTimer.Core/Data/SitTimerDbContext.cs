using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SitTimer.Core.Models;

namespace SitTimer.Core.Data;

public class SitTimerDbContext : DbContext
{
    public DbSet<Session> Sessions => Set<Session>();

    public SitTimerDbContext(DbContextOptions<SitTimerDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.StartTime).IsRequired();
            e.Property(s => s.LastHeartbeat).IsRequired();
            e.HasIndex(s => s.StartTime);
            e.Property(s => s.MergeGroupId).IsRequired(false);
        });
    }

    public async Task EnsureSchemaUpToDateAsync()
    {
        try
        {
            await Database.ExecuteSqlRawAsync(
                "ALTER TABLE Sessions ADD COLUMN BreakTime INTEGER NULL");
        }
        catch (SqliteException ex)
            when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists — no-op.
        }

        try
        {
            await Database.ExecuteSqlRawAsync(
                "ALTER TABLE Sessions ADD COLUMN MergeGroupId TEXT NULL");
        }
        catch (SqliteException ex)
            when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists — no-op.
        }
    }
}
