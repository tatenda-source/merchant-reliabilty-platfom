using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MRP.Domain.Entities;

namespace MRP.Infrastructure.Persistence.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("merchants");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(256).IsRequired();
        builder.Property(m => m.TradingName).HasMaxLength(256);
        builder.Property(m => m.ContactEmail).HasMaxLength(256);
        builder.Property(m => m.ContactPhone).HasMaxLength(32);
        builder.Property(m => m.Tier).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.ReliabilityScore).HasPrecision(5, 2);

        builder.HasOne(m => m.Integration)
            .WithOne(i => i.Merchant)
            .HasForeignKey<MerchantIntegration>(i => i.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.Name);
        builder.HasIndex(m => m.IsActive);
    }
}
