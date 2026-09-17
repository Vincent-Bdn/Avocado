using System.Reflection;
using PdfSharp.Fonts;

namespace Avocado.Server.Features.Documents.Folders;

/// <summary>
/// The one font Avocado draws with, carried inside the binary.
///
/// <para><b>Why this has to exist.</b> PDFsharp resolves fonts through the host, and on a machine
/// with no font resolver registered it throws « No appropriate font found for family name » for every
/// name including Helvetica. That failed on Windows as readily as on Linux, so it was never a
/// platform question: without this, stamping a pièce throws on every machine.</para>
///
/// <para><b>Why not the system's fonts.</b> Asking the operating system means Arial on Windows, and
/// something else on a Mac, and a stamped pièce that looks different depending on whose computer
/// prepared it. It also means shipping a font this application does not have the right to embed.
/// IBM Plex Sans is under the SIL Open Font License, is already the typeface of the interface, and
/// travels in the binary, so the number on the stamp is the same everywhere.</para>
///
/// <para>Half a megabyte, against a self-contained binary of seventy. The licence sits beside it in
/// <c>Resources/</c>, as the OFL requires.</para>
/// </summary>
internal sealed class StampFont : IFontResolver
{
    private const string Resource = "Avocado.Server.Resources.IBMPlexSans.ttf";

    /// <summary>The name this resolver answers to. Any request resolves to it: there is only one.</summary>
    public const string FaceName = "AvocadoPlex";

    private static readonly Lazy<byte[]> Bytes = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"The stamp font is missing from the build. Expected the embedded resource '{Resource}'.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    });

    /// <summary>
    /// Registered once, whoever asks first.
    ///
    /// <para><see cref="GlobalFontSettings"/> is exactly what its name says, so this is idempotent and
    /// guarded: two dossiers stamped at the same moment must not race to install it, and installing it
    /// twice throws.</para>
    /// </summary>
    public static void Install()
    {
        if (GlobalFontSettings.FontResolver is not null)
        {
            return;
        }

        lock (Bytes)
        {
            GlobalFontSettings.FontResolver ??= new StampFont();
        }
    }

    public byte[]? GetFont(string faceName) => Bytes.Value;

    /// <summary>
    /// Every request resolves to the same face, bold or not.
    ///
    /// <para>The file is a variable font, so PDFsharp embeds its default instance and a request for
    /// bold gets the same outlines rather than a synthesised slant. For a two-digit number on a stamp
    /// that is the right trade: one predictable glyph set, no fallback, no surprise on a machine that
    /// happens to have something installed.</para>
    /// </summary>
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(FaceName);
}
