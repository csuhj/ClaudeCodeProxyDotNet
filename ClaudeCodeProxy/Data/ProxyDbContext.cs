using ClaudeCodeProxy.Models;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeProxy.Data;

public class ProxyDbContext : DbContext
{
    public ProxyDbContext(DbContextOptions<ProxyDbContext> options) : base(options) { }

    public DbSet<ProxyRequest> ProxyRequests => Set<ProxyRequest>();
    public DbSet<LlmUsage> LlmUsages => Set<LlmUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ensure LlmUsage.ProxyRequestId has a unique index so the relationship
        // is one-to-one (each proxied request has at most one token usage record).
        modelBuilder.Entity<LlmUsage>()
            .HasIndex(u => u.ProxyRequestId)
            .IsUnique();

        modelBuilder.Entity<LlmUsage>()
            .HasOne(u => u.ProxyRequest)
            .WithOne(r => r.LlmUsage)
            .HasForeignKey<LlmUsage>(u => u.ProxyRequestId);
    }
}
