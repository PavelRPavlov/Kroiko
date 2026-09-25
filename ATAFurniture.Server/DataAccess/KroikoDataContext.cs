using Microsoft.EntityFrameworkCore;

namespace ATAFurniture.Server.DataAccess;

public class KroikoDataContext : DbContext
{
    public DbSet<User> Users { get; set; }

    public KroikoDataContext(DbContextOptions<KroikoDataContext> options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasKey(u => u.Id);
        modelBuilder.Entity<User>()
            .Property(u => u.Id)
            .ValueGeneratedOnAdd();
        modelBuilder.Entity<User>()
            .Property(u => u.AadId)
            .IsRequired();
        modelBuilder.Entity<User>()
            .Property(u => u.Email)
            .IsRequired();
        modelBuilder.Entity<User>().OwnsOne(u => u.LastSelectedCompany);
    }
}