using Microsoft.EntityFrameworkCore;
using PzServerManager.Domain.Servers;

namespace PzServerManager.Infrastructure.Persistence;

internal sealed class ManagerDbContext(DbContextOptions<ManagerDbContext> options)
    : DbContext(options)
{
    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var instance = modelBuilder.Entity<ServerInstance>();
        instance.ToTable("ServerInstances");
        instance.HasKey(item => item.Id);
        instance.Property(item => item.Name).HasMaxLength(80).IsRequired();
        instance.Property(item => item.Slug).HasMaxLength(100).IsRequired();
        instance.HasIndex(item => item.Name).IsUnique();
        instance.HasIndex(item => item.Slug).IsUnique();
        instance.Property(item => item.InstallationPath).HasMaxLength(1024).IsRequired();
        instance.Property(item => item.DataPath).HasMaxLength(1024).IsRequired();
        instance.Property(item => item.Branch).HasConversion<string>().HasMaxLength(32);
        instance.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
    }
}
