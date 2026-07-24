using Microsoft.EntityFrameworkCore;
using ReIdSample.Models;

namespace ReIdSample.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyMemberPhoto> FamilyMemberPhotos => Set<FamilyMemberPhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt);
        });

        modelBuilder.Entity<FamilyMemberPhoto>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FeatureVector).IsRequired();
            entity.Property(e => e.CreatedAt);

            entity.HasOne(e => e.FamilyMember)
                  .WithMany(m => m.Photos)
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
