using Microsoft.EntityFrameworkCore;
using Federated.AuthService.Models;

namespace Federated.AuthService.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Api> Apis => Set<Api>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ApiRoute> ApiRoutes => Set<ApiRoute>();
    public DbSet<Grant> Grants => Set<Grant>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId).IsUnique();
            entity.Property(e => e.ClientId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ClientName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SecretHash).IsRequired();
        });

        modelBuilder.Entity<Api>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Audience).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Audience).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ApiId, e.Scope }).IsUnique();
            entity.Property(e => e.Scope).IsRequired().HasMaxLength(100);

            entity.HasOne(e => e.Api)
                  .WithMany(a => a.Permissions)
                  .HasForeignKey(e => e.ApiId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiRoute>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ApiId, e.Method, e.PathPattern }).IsUnique();
            entity.Property(e => e.Method).IsRequired().HasMaxLength(10);
            entity.Property(e => e.PathPattern).IsRequired().HasMaxLength(300);

            entity.HasOne(e => e.Api)
                  .WithMany(a => a.Routes)
                  .HasForeignKey(e => e.ApiId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Permission)
                  .WithMany(p => p.Routes)
                  .HasForeignKey(e => e.PermissionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Grant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClientId, e.PermissionId }).IsUnique();

            entity.HasOne(e => e.Client)
                  .WithMany(c => c.Grants)
                  .HasForeignKey(e => e.ClientId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Permission)
                  .WithMany(p => p.Grants)
                  .HasForeignKey(e => e.PermissionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Actor).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
        });
    }
}
