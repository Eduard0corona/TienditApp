using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The reviews customers write about a business (Fase 24H, ADR 0030). They are the business's (filtered by it, for its
/// team) and their author's: they go with the account when it is deleted, and a business is never deleted (it is
/// suspended or cancelled). The database keeps the rules the screens rely on: the stars, a report only on a published
/// review, a reason for every hidden one, and an answer with its date.
/// </summary>
public class BusinessReviewConfiguration : IEntityTypeConfiguration<BusinessReview>
{
    public void Configure(EntityTypeBuilder<BusinessReview> builder)
    {
        builder.ToTable("BusinessReviews", table =>
        {
            table.HasCheckConstraint("CK_BusinessReviews_Rating", "[Rating] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_BusinessReviews_Status", "[Status] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_BusinessReviews_Report",
                "([ReportedAt] IS NULL AND [ReportReason] IS NULL)"
                + " OR ([ReportedAt] IS NOT NULL AND [ReportReason] IS NOT NULL AND [ReportReason] BETWEEN 1 AND 5 AND [Status] = 0)");
            table.HasCheckConstraint(
                "CK_BusinessReviews_Hidden",
                "([Status] = 0 AND [HiddenReason] IS NULL) OR ([Status] = 1 AND [HiddenReason] IS NOT NULL AND [HiddenReason] BETWEEN 1 AND 5)");
            table.HasCheckConstraint(
                "CK_BusinessReviews_Reply",
                "([Reply] IS NULL AND [RepliedAt] IS NULL) OR ([Reply] IS NOT NULL AND [RepliedAt] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.AuthorName).HasMaxLength(BusinessReview.MaxAuthorNameLength).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(BusinessReview.MaxCommentLength);
        builder.Property(x => x.Reply).HasMaxLength(BusinessReview.MaxReplyLength);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ReportReason).HasConversion<int?>();
        builder.Property(x => x.HiddenReason).HasConversion<int?>();
        builder.Ignore(x => x.IsPublished);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // One review per account and business; it is also the account's list.
        builder.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique();
        // The store's list, newest first, and its average.
        builder.HasIndex(x => new { x.TenantId, x.Status, x.WrittenAt }).IncludeProperties(x => x.Rating);
        // What the platform has to look at.
        builder.HasIndex(x => x.ReportedAt).HasFilter("[ReportedAt] IS NOT NULL");
    }
}
