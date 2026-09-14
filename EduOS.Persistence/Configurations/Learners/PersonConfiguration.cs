using EduOS.Core.Entities.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Learners;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("Persons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.FullNameBangla).HasMaxLength(200);
        builder.Property(x => x.Gender).HasMaxLength(30);
        builder.HasIndex(x => x.PublicId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
