using M365AcademyCrm.Models;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Data;

public class CrmDbContext : DbContext
{
    public CrmDbContext(DbContextOptions<CrmDbContext> options) : base(options)
    {
    }

    public DbSet<SalesUser> SalesUsers => Set<SalesUser>();

    public DbSet<Lead> Leads => Set<Lead>();

    public DbSet<Organisation> Organisations => Set<Organisation>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<Deal> Deals => Set<Deal>();

    public DbSet<Activity> Activities => Set<Activity>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as their names, not ints: readable in SQL, and safe to reorder.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesUser>().HasIndex(u => u.ObjectId).IsUnique();
        modelBuilder.Entity<SalesUser>().Property(u => u.ObjectId).HasMaxLength(64);
        modelBuilder.Entity<SalesUser>().Property(u => u.Email).HasMaxLength(254);
        modelBuilder.Entity<SalesUser>().Property(u => u.DisplayName).HasMaxLength(200);

        modelBuilder.Entity<Course>().HasIndex(c => c.Code).IsUnique();

        modelBuilder.Entity<Lead>().HasIndex(l => l.Email);
        modelBuilder.Entity<Lead>().HasIndex(l => l.Status);
        modelBuilder.Entity<Lead>().HasIndex(l => l.CreatedAtUtc);
        modelBuilder.Entity<Contact>().HasIndex(c => c.Email);
        modelBuilder.Entity<Organisation>().HasIndex(o => o.Name);
        modelBuilder.Entity<Deal>().HasIndex(d => d.Stage);
        modelBuilder.Entity<Activity>().HasIndex(a => new { a.OwnerId, a.CompletedAtUtc, a.DueAtUtc });
        modelBuilder.Entity<Quote>().HasIndex(q => q.Number).IsUnique();

        modelBuilder.Entity<Quote>().Ignore(q => q.Subtotal).Ignore(q => q.DiscountAmount).Ignore(q => q.NetTotal).Ignore(q => q.VatAmount).Ignore(q => q.GrossTotal);
        modelBuilder.Entity<QuoteLine>().Ignore(l => l.LineTotal);
        modelBuilder.Entity<Deal>().Ignore(d => d.WeightedValue).Ignore(d => d.IsOpen);
        modelBuilder.Entity<Lead>().Ignore(l => l.IsOpen);
        modelBuilder.Entity<Contact>().Ignore(c => c.FullName);
        modelBuilder.Entity<Course>().Ignore(c => c.DisplayName);
        modelBuilder.Entity<Activity>().Ignore(a => a.IsTask).Ignore(a => a.IsOpenTask);

        // Every relationship except quote lines is NoAction at the database:
        // SQL Server rejects multiple cascade/set-null paths into one table
        // (Activity is reachable from Organisation directly and via Contact
        // and Deal), so unlinking on delete is done explicitly in the
        // controllers instead, where it's also visible.
        modelBuilder.Entity<Contact>().HasOne(c => c.Organisation).WithMany(o => o.Contacts).HasForeignKey(c => c.OrganisationId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Deal>().HasOne(d => d.Organisation).WithMany(o => o.Deals).HasForeignKey(d => d.OrganisationId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Deal>().HasOne(d => d.Contact).WithMany().HasForeignKey(d => d.ContactId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Deal>().HasOne(d => d.Course).WithMany().HasForeignKey(d => d.CourseId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Lead>().HasOne(l => l.Course).WithMany().HasForeignKey(l => l.CourseId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Activity>().HasOne(a => a.Lead).WithMany().HasForeignKey(a => a.LeadId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Activity>().HasOne(a => a.Contact).WithMany().HasForeignKey(a => a.ContactId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Activity>().HasOne(a => a.Organisation).WithMany().HasForeignKey(a => a.OrganisationId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Activity>().HasOne(a => a.Deal).WithMany().HasForeignKey(a => a.DealId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Quote>().HasOne(q => q.Deal).WithMany().HasForeignKey(q => q.DealId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Quote>().HasOne(q => q.Organisation).WithMany().HasForeignKey(q => q.OrganisationId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Quote>().HasOne(q => q.Contact).WithMany().HasForeignKey(q => q.ContactId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<QuoteLine>().HasOne(l => l.Quote).WithMany(q => q.Lines).HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<QuoteLine>().HasOne(l => l.Course).WithMany().HasForeignKey(l => l.CourseId).OnDelete(DeleteBehavior.ClientSetNull);

        // Owners are never deleted (only deactivated), so Restrict is safe.
        modelBuilder.Entity<Lead>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Organisation>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Contact>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Deal>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Activity>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Quote>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);

        // The catalogue from m365academy.cloud/courses (and the contact form's
        // course codes). Prices deliberately zero - see Course.
        modelBuilder.Entity<Course>().HasData(
            new Course { Id = 1, Code = "MS-900", Title = "Microsoft 365 Fundamentals", Category = CourseCategory.Microsoft365, Duration = "1 day" },
            new Course { Id = 2, Code = "SC-900", Title = "Security, Compliance & Identity Fundamentals", Category = CourseCategory.Security, Duration = "1 day" },
            new Course { Id = 3, Code = "MS-102", Title = "Microsoft 365 Administrator", Category = CourseCategory.Microsoft365, Duration = "13 weeks, evenings" },
            new Course { Id = 4, Code = "MS-700", Title = "Managing Microsoft Teams", Category = CourseCategory.Microsoft365, Duration = "3 days" },
            new Course { Id = 5, Code = "AZ-900", Title = "Azure Fundamentals", Category = CourseCategory.Azure, Duration = "1 day" },
            new Course { Id = 6, Code = "AZ-104", Title = "Azure Administrator", Category = CourseCategory.Azure, Duration = "Multi-week, evenings" },
            new Course { Id = 7, Code = "AZ-500", Title = "Azure Security Engineer", Category = CourseCategory.Azure, Duration = "Multi-week, evenings" },
            new Course { Id = 8, Code = "AZ-305", Title = "Azure Solutions Architect Expert", Category = CourseCategory.Azure, Duration = "Multi-week, evenings" },
            new Course { Id = 9, Code = "SC-300", Title = "Identity and Access Administrator", Category = CourseCategory.Security, Duration = "Multi-week, evenings" },
            new Course { Id = 10, Code = "SC-200", Title = "Security Operations Analyst", Category = CourseCategory.Security, Duration = "Multi-week, evenings" },
            new Course { Id = 11, Code = "SC-100", Title = "Cybersecurity Architect", Category = CourseCategory.Security, Duration = "Multi-week, evenings" },
            new Course { Id = 12, Code = "MCT-Onboarding", Title = "MCT Onboarding Experience", Category = CourseCategory.MctProgramme, Duration = "3 days" });
    }
}
