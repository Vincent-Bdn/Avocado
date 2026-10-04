using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Agenda.Infrastructure;

public sealed class AgendaEventConfiguration : IEntityTypeConfiguration<AgendaEvent>
{
    public void Configure(EntityTypeBuilder<AgendaEvent> builder)
    {
        builder.ToTable("agenda_events");

        // The échéance is the key: one event per échéance, and a second row for the same one would
        // mean a duplicate in her agenda that nothing would ever clean up.
        builder.HasKey(evt => evt.DeadlineId);

        builder.Property(evt => evt.GoogleEventId).IsRequired();
        builder.Property(evt => evt.Fingerprint).IsRequired();

        // No foreign key to Deadline, deliberately. A deleted échéance has to leave this row behind
        // for one more pass: it is the only record of which event in her agenda to go and remove.
        // Cascading the delete would silently abandon the event in her calendar for ever.
    }
}
