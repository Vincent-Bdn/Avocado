using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// « Détail de facturation »: the annexe that goes to the client with the facture.
/// <para>
/// A client who receives « honoraires : 6 000 € » and nothing else asks what it covers, and the
/// answer arrives three weeks later by email. Sending the detail with the invoice is what stops that
/// conversation happening at all, so this is a real .xlsx, column widths, a total row, dates as
/// dates, rather than a CSV an accountant has to reformat.
/// </para>
/// </summary>
public static class ExportBillingDetail
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var invoice = await database.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (invoice is null)
        {
            return Results.NotFound();
        }

        var matter = await database.Matters
            .AsNoTracking()
            .Include(candidate => candidate.Parties)
            .ThenInclude(party => party.Contact)
            .FirstAsync(candidate => candidate.Id == invoice.MatterId, cancellationToken);

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.InvoiceId == id)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.StartedAt)
            .ToListAsync(cancellationToken);

        var client = matter.Parties.FirstOrDefault(party => party.IsClient)?.Contact;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Détail de facturation");

        sheet.Cell(1, 1).Value = "Détail de facturation";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Dossier {matter.Reference} · {matter.Name}";
        sheet.Cell(3, 1).Value = client is null
            ? string.Empty
            : $"Client : {(client.Type == ContactType.Organisation ? client.LegalName : client.DisplayName)}";
        sheet.Cell(4, 1).Value = invoice.ExternalReference is null
            ? $"Facture du {invoice.Date:dd/MM/yyyy}"
            : $"Facture {invoice.ExternalReference} du {invoice.Date:dd/MM/yyyy}";

        var header = 6;
        string[] columns = ["Date", "Prestation", "Durée", "Taux horaire", "Montant HT"];

        for (var column = 0; column < columns.Length; column++)
        {
            var cell = sheet.Cell(header, column + 1);
            cell.Value = columns[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECE4");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var row = header + 1;

        foreach (var entry in entries)
        {
            var rate = entry.HourlyRateCentsOverride ?? matter.HourlyRateCents;

            sheet.Cell(row, 1).Value = entry.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(row, 2).Value = entry.Task;
            // As she reads it, « 2 h 30 », not 2.5: this page is read by a client, not by a machine.
            sheet.Cell(row, 3).Value = Duration(entry.DurationMinutes);
            sheet.Cell(row, 4).Value = rate / 100m;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00 €";
            sheet.Cell(row, 5).Value = entry.AmountCents(matter.HourlyRateCents) / 100m;
            sheet.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 €";

            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 2).Value = "Total";
        sheet.Cell(totalRow, 3).Value = Duration(entries.Sum(entry => entry.DurationMinutes));
        sheet.Cell(totalRow, 5).Value = invoice.BilledTimeCents / 100m;
        sheet.Cell(totalRow, 5).Style.NumberFormat.Format = "#,##0.00 €";
        sheet.Range(totalRow, 1, totalRow, 5).Style.Font.Bold = true;
        sheet.Range(totalRow, 1, totalRow, 5).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        // The arbitrage is stated rather than hidden in a rounded total: a client who sees the geste
        // written down reads it as a geste.
        if (invoice.VarianceCents != 0)
        {
            var granted = totalRow + 1;
            sheet.Cell(granted, 2).Value = invoice.VarianceCents < 0 ? "Remise accordée" : "Complément";
            sheet.Cell(granted, 5).Value = invoice.VarianceCents / 100m;
            sheet.Cell(granted, 5).Style.NumberFormat.Format = "#,##0.00 €";

            var billed = granted + 1;
            sheet.Cell(billed, 2).Value = "Montant facturé HT";
            sheet.Cell(billed, 5).Value = invoice.AmountExclVatCents / 100m;
            sheet.Cell(billed, 5).Style.NumberFormat.Format = "#,##0.00 €";
            sheet.Range(billed, 1, billed, 5).Style.Font.Bold = true;
        }

        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 62;
        sheet.Column(3).Width = 10;
        sheet.Column(4).Width = 14;
        sheet.Column(5).Width = 14;
        sheet.Column(2).Style.Alignment.WrapText = true;

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        var name = $"detail-facturation-{matter.Reference}-{invoice.Date:yyyyMMdd}.xlsx";

        return Results.File(
            buffer.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            name);
    }

    private static string Duration(int minutes) =>
        minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60:00}";
}
