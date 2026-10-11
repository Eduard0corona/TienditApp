using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class SpecialDayConfiguration : IEntityTypeConfiguration<SpecialDay>
{
    public void Configure(EntityTypeBuilder<SpecialDay> builder)
    {
        // Mirror SpecialDayRule, so a stored row always reads as a valid rule: a closed day without hours, special
        // hours with one or two openings, a second one that starts once the first closes (only it may end the next
        // day or last 24 hours), and a reason that is null or not empty.
        builder.ToTable("SpecialDays", table =>
        {
            table.HasCheckConstraint("CK_SpecialDays_Kind", "[Kind] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_SpecialDays_Openings",
                "([Kind] = 0 AND [FirstOpens] IS NULL AND [FirstCloses] IS NULL AND [SecondOpens] IS NULL AND [SecondCloses] IS NULL) OR ([Kind] = 1 AND [FirstOpens] IS NOT NULL AND [FirstCloses] IS NOT NULL AND (([SecondOpens] IS NULL AND [SecondCloses] IS NULL) OR ([SecondOpens] IS NOT NULL AND [SecondCloses] IS NOT NULL)))");
            table.HasCheckConstraint(
                "CK_SpecialDays_SecondOpening",
                "[SecondOpens] IS NULL OR ([FirstCloses] > [FirstOpens] AND [SecondOpens] >= [FirstCloses] AND [SecondCloses] <> [SecondOpens])");
            // LEN, not <> '': the default collation gives emoji no weight, so a reason made only of emoji equals '' there
            // and the rule's own reasons would be refused. LEN counts every character but trailing spaces, which the
            // rule trims. The SQLite of the relational tests gets a LEN that does the same.
            table.HasCheckConstraint("CK_SpecialDays_Reason", "[Reason] IS NULL OR LEN([Reason]) > 0");
            table.HasCheckConstraint("CK_SpecialDays_Revision", "[Revision] >= 1");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PeriodId).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.FirstOpens).HasPrecision(0);
        builder.Property(x => x.FirstCloses).HasPrecision(0);
        builder.Property(x => x.SecondOpens).HasPrecision(0);
        builder.Property(x => x.SecondCloses).HasPrecision(0);
        builder.Property(x => x.Reason).HasMaxLength(SpecialDayRule.MaxReasonLength);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Ignore(x => x.Rule);

        // One rule per date and business, also when two people save at the same time.
        builder.HasIndex(x => new { x.TenantId, x.Date }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PeriodId });
    }
}
