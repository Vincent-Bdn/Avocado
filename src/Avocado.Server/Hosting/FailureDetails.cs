using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Avocado.Server.Hosting;

/// <summary>
/// Turns an unhandled exception into something the user can act on.
/// <para>
/// The framework's default is « An error occurred while processing your request. » — in English, with
/// no indication of what failed, on a screen that is otherwise entirely in French. Every message here
/// says what could not be done and, where it is knowable, what to do about it. The exception itself
/// still goes to the log; what changes is what reaches the window.
/// </para>
/// </summary>
public sealed class FailureDetails(ILogger<FailureDetails> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled failure on {Method} {Path}.", context.Request.Method, context.Request.Path);

        var (status, title, detail) = Describe(exception);

        context.Response.StatusCode = status;

        return new ValueTask<bool>(
            context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = detail,
                },
                cancellationToken).ContinueWith(_ => true, cancellationToken));
    }

    private static (int Status, string Title, string Detail) Describe(Exception exception) => exception switch
    {
        // The usual cause by far: the document is open in Word or in a PDF reader, which holds it
        // exclusively. Saying so is the whole difference between a bug and an instruction.
        IOException io when IsSharingViolation(io) => (
            StatusCodes.Status409Conflict,
            "Le fichier est ouvert dans une autre application",
            "Fermez-le, puis réessayez. Tant qu’une application le tient ouvert, Avocado ne peut ni le " +
            "lire ni le remplacer."),

        UnauthorizedAccessException => (
            StatusCodes.Status409Conflict,
            "Accès refusé à un fichier du coffre",
            "Vérifiez que le dossier du coffre n’est pas en lecture seule et qu’aucune autre " +
            "application ne le verrouille."),

        IOException io => (
            StatusCodes.Status409Conflict,
            "Opération impossible sur un fichier du coffre",
            io.Message),

        Avocado.Vault.VaultException vault => (
            StatusCodes.Status500InternalServerError,
            "Le coffre a refusé l’opération",
            vault.Message),

        _ => (
            StatusCodes.Status500InternalServerError,
            "L’opération n’a pas abouti",
            "Le détail est dans le journal technique de l’application. Rien n’a été enregistré à moitié : " +
            "l’écriture en base est transactionnelle."),
    };

    /// <summary>Windows reports a locked file as HRESULT 0x80070020 (ERROR_SHARING_VIOLATION).</summary>
    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xFFFF) is 32 or 33;
}
