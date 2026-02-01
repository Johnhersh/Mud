using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mud.Infrastructure.Data;

/// <summary>
/// Database context for the Mud application.
/// </summary>
public class MudDbContext : IdentityDbContext<ApplicationUser>
{
    public MudDbContext(DbContextOptions<MudDbContext> options)
        : base(options)
    {
    }

    public DbSet<CharacterEntity> Characters => Set<CharacterEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CharacterEntity>(entity =>
        {
            entity.Property(c => c.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.HasIndex(c => c.AccountId);
            entity.HasIndex(c => c.Name).IsUnique();

            entity.HasOne(c => c.Account)
                .WithMany(u => u.Characters)
                .HasForeignKey(c => c.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
