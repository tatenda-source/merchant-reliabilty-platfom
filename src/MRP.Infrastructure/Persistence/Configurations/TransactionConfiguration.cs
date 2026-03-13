using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MRP.Domain.Entities;

namespace MRP.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Amount).HasPrecision(18, 4);
        builder.Property(t => t.Currency).HasMaxLength(3);
        builder.Property(t => t.PaynowReference).HasMaxLength(128);
        builder.Property(t => t.MerchantReference).HasMaxLength(128);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.PaymentMethod).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(t => t.PaynowReference);
        builder.HasIndex(t => t.MerchantReference);
        builder.HasIndex(t => new { t.MerchantId, t.CreatedAt });
        builder.HasIndex(t => new { t.Source, t.Status });

        builder.HasOne(t => t.Merchant)
            .WithMany(m => m.Transactions)
            .HasForeignKey(t => t.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
