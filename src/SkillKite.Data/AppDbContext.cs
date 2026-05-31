using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Models;

namespace SkillKite.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<CareerPath> CareerPaths => Set<CareerPath>();
    public DbSet<LearningResource> LearningResources => Set<LearningResource>();
    public DbSet<Roadmap> Roadmaps => Set<Roadmap>();
    public DbSet<ProgressEntry> ProgressEntries => Set<ProgressEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Student>(e =>
        {
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(15).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.State).HasMaxLength(50);
            e.Property(x => x.EducationLevel).HasMaxLength(50);
            e.Property(x => x.CollegeName).HasMaxLength(200);
            e.Property(x => x.PreferredLanguage).HasConversion<string>();
        });

        b.Entity<ChatSession>(e =>
        {
            e.HasOne(x => x.Student).WithMany(s => s.ChatSessions)
                .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.AssessmentDataJson).HasColumnType("jsonb");
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasOne(x => x.Session).WithMany(s => s.Messages)
                .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Role).HasConversion<string>();
            e.Property(x => x.Content).IsRequired();
        });

        b.Entity<CareerPath>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.TitleHi).HasMaxLength(200);
            e.Property(x => x.Category).HasConversion<string>();
            e.Property(x => x.DemandLevel).HasConversion<string>();
            e.Property(x => x.RequirementsJson).HasColumnType("jsonb");
            e.Property(x => x.TimeToJobReady).HasMaxLength(50);
        });

        b.Entity<LearningResource>(e =>
        {
            e.HasOne(x => x.CareerPath).WithMany(c => c.Resources)
                .HasForeignKey(x => x.CareerPathId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.Property(x => x.Platform).HasMaxLength(50);
            e.Property(x => x.Language).HasMaxLength(10);
            e.Property(x => x.SkillTagsJson).HasColumnType("jsonb");
        });

        b.Entity<Roadmap>(e =>
        {
            e.HasOne(x => x.Student).WithMany(s => s.Roadmaps)
                .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CareerPath).WithMany()
                .HasForeignKey(x => x.CareerPathId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.WeeksPlanJson).HasColumnType("jsonb");
            e.Property(x => x.PdfUrl).HasMaxLength(500);
        });

        b.Entity<ProgressEntry>(e =>
        {
            e.HasOne(x => x.Roadmap).WithMany(r => r.ProgressEntries)
                .HasForeignKey(x => x.RoadmapId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>();
        });
    }
}
