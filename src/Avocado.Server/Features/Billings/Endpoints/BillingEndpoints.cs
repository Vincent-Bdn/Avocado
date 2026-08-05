namespace Avocado.Server.Features.Billings.Endpoints;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBilling(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/matters/{matterId:guid}/billing", GetBilling.HandleAsync).WithTags("Billing");
        routes.MapPost("/api/matters/{matterId:guid}/invoices", CreateInvoice.HandleAsync).WithTags("Billing");
        routes.MapPost("/api/matters/{matterId:guid}/ledger-entries", CreateLedgerEntry.HandleAsync)
            .WithTags("Billing");

        routes.MapPut("/api/invoices/{id:guid}", UpdateInvoice.HandleAsync).WithTags("Billing");
        routes.MapDelete("/api/invoices/{id:guid}", DeleteBillingRecord.InvoiceAsync).WithTags("Billing");
        routes.MapDelete("/api/ledger-entries/{id:guid}", DeleteBillingRecord.LedgerEntryAsync)
            .WithTags("Billing");

        return routes;
    }
}
