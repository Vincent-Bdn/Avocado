namespace Avocado.Server.Features.Documents.Folders;

/// <summary>
/// What no filesystem Avocado runs on will accept in a file name.
///
/// <para><b>Why not <see cref="Path.GetInvalidFileNameChars"/>.</b> It answers for the machine it is
/// running on, and the machines disagree: nine characters on Windows, two on macOS and Linux. So a
/// pièce versée on a Mac keeps the colon in « Contrat du 3 mars : avenants », and the file is
/// perfectly fine right up until it reaches Windows, where that name cannot be created at all. Eight
/// of the ten practices in the beta are on macOS, a restore is very often onto a different machine
/// from the one the files were made on, and dossiers get sent to confrères. A name has to be valid
/// everywhere or it is a bug waiting for a change of computer.</para>
///
/// <para>So the Windows set is used on every platform: it is the strictest, and being stricter than
/// the local filesystem costs nothing but a character she would not have wanted in a file name
/// anyway.</para>
/// </summary>
public static class PortableFileName
{
    /// <summary>
    /// Windows' list, verbatim, plus the control characters. <c>/</c> is here for Unix and <c>\</c>
    /// and <c>:</c> for Windows, and all three are refused on both.
    /// </summary>
    private static readonly char[] Invalid =
        [.. "\"<>|:*?\\/".Concat(Enumerable.Range(0, 32).Select(code => (char)code))];

    public static bool IsInvalid(char character) => Invalid.Contains(character);

    /// <summary>
    /// Replaces what a filesystem refuses, then trims what Windows accepts in a string and refuses at
    /// creation: a name may not end in a dot or a space.
    /// </summary>
    public static string Clean(string name, char replacement)
    {
        var cleaned = new string([.. name.Select(character => IsInvalid(character) ? replacement : character)]);

        return cleaned.TrimEnd('.', ' ');
    }
}
