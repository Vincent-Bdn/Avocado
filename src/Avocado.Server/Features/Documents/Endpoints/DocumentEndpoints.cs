using Avocado.Server.Features.Documents.Workspace;

namespace Avocado.Server.Features.Documents.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/matters/{matterId:guid}/documents", ListDocuments.HandleAsync)
            .WithTags("Documents");

        routes.MapPost("/api/matters/{matterId:guid}/documents", UploadDocument.HandleAsync)
            .WithTags("Documents")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute());

        routes.MapGet("/api/documents/workspace", EditDocument.Status).WithTags("Documents");

        var group = routes.MapGroup("/api/documents").WithTags("Documents");

        group.MapGet("/{id:guid}/content", DownloadDocument.HandleAsync);
        group.MapPut("/{id:guid}", UpdateDocument.HandleAsync);
        group.MapPut("/{id:guid}/exhibit", PromoteToExhibit.HandleAsync);
        group.MapDelete("/{id:guid}/exhibit", WithdrawExhibit.HandleAsync);
        group.MapPost("/{id:guid}/open", EditDocument.OpenAsync);
        group.MapPost("/{id:guid}/close", EditDocument.CloseAsync);
        group.MapPost("/{id:guid}/resolve", EditDocument.ResolveAsync);
        group.MapDelete("/{id:guid}", DeleteDocument.HandleAsync);

        return routes;
    }
}

/// <summary>
/// Raises the multipart body limit past the framework's 128 MB default so a batch drop of large scans
/// is bounded by the per-file 50 Mo rule rather than by the request size.
/// </summary>
internal sealed class RequestSizeLimitAttribute : Attribute, Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => 512L * 1024 * 1024;
}
