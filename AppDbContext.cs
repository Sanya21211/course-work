using Microsoft.EntityFrameworkCore;
using ServiceCenter.Web.Domain;

namespace ServiceCenter.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Equipment> EquipmentItems => Set<Equipment>();
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<RequestEvent> RequestEvents => Set<RequestEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Login).IsUnique();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Equipment>(e =>
        {
            e.HasIndex(x => new { x.OwnerId, x.SerialNumber }).IsUnique();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ServiceRequest>(e =>
        {
            e.Ignore(r => r.Number);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.ClientId);
            e.HasIndex(r => r.AssignedTechnicianId);
            e.HasIndex(r => r.UpdatedAt);

            // Інваріант: для одиниці обладнання не більше однієї відкритої заявки.
            e.HasIndex(r => r.EquipmentId).IsUnique()
                .HasFilter("\"Status\" NOT IN ('Completed', 'Cancelled')");

            e.HasOne(r => r.Client).WithMany().HasForeignKey(r => r.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Equipment).WithMany().HasForeignKey(r => r.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.AssignedTechnician).WithMany().HasForeignKey(r => r.AssignedTechnicianId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkItem>(e =>
        {
            e.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(w => w.Request).WithMany(r => r.WorkItems).HasForeignKey(w => w.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RequestEvent>(e =>
        {
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Request).WithMany(r => r.Events).HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
