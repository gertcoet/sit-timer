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
        });
    }
}
