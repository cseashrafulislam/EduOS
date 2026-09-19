using EduOS.Core.Entities.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Learners;

public class PersonIdentifierConfiguration : IEntityTypeConfiguration<PersonIdentifier>
{
    public void Configure(EntityTypeBuilder<PersonIdentifier> builder)
    {
        builder.ToTable("PersonIdentifiers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.ProtectedValue).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.LookupDigest).IsRequired().HasMaxLength(64);
        builder.Property(x => x.VerificationStatus).HasConversion<int>();
        builder.Property(x => x.VerificationProvider).HasMaxLength(100);

        builder.HasIndex(x => new { x.Type, x.LookupDigest })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => x.PersonId);

        builder.HasOne(x => x.Person)
            .WithMany(x => x.Identifiers)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
