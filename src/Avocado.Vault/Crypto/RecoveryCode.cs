using System.Security.Cryptography;
using System.Text;

namespace Avocado.Vault.Crypto;

/// <summary>
/// Renders a 256-bit recovery key as something a person can write down, read back over the phone, or
/// type off a printed sheet.
/// <para>
/// Crockford Base32: no I, L, O or U, so there is no 1/I, 0/O ambiguity and no accidental profanity.
/// Decoding is case-insensitive, ignores separators, and folds the confusable letters back, so a code
/// transcribed by hand still works. A two-character checksum catches the typos that remain.
/// </para>
/// </summary>
public static class RecoveryCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int PayloadChars = 52;  // ceil(256 bits / 5)
    private const int ChecksumChars = 2;
    private const int GroupSize = 6;

    /// <summary>Formatted as nine dash-separated groups, e.g. <c>K7M2QX-4WPB9T-…</c>.</summary>
    public static string Format(SecretKey recoveryKey)
    {
        var body = Encode(recoveryKey.Span);
        var full = body + Checksum(body);

        var formatted = new StringBuilder(full.Length + (full.Length / GroupSize));
        for (var i = 0; i < full.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                formatted.Append('-');
            }

            formatted.Append(full[i]);
        }

        return formatted.ToString();
    }

    /// <summary>
    /// Parses a code the user typed. Tolerates lower case, missing or extra dashes, spaces, and the
    /// I/L/O substitutions people make when copying from paper.
    /// </summary>
    public static bool TryParse(string? input, out SecretKey? recoveryKey)
    {
        recoveryKey = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = new StringBuilder(PayloadChars + ChecksumChars);
        foreach (var raw in input)
        {
            var c = char.ToUpperInvariant(raw);
            c = c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            };

            if (Alphabet.Contains(c, StringComparison.Ordinal))
            {
                normalized.Append(c);
            }
            else if (c is '-' or ' ' or '\t' or '\r' or '\n')
            {
                // Separators are decorative.
            }
            else
            {
                return false;
            }
        }

        if (normalized.Length != PayloadChars + ChecksumChars)
        {
            return false;
        }

        var full = normalized.ToString();
        var body = full[..PayloadChars];
        var checksum = full[PayloadChars..];

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(checksum),
                Encoding.ASCII.GetBytes(Checksum(body))))
        {
            return false;
        }

        var decoded = Decode(body);
        try
        {
            recoveryKey = new SecretKey(decoded);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static string Checksum(string body)
    {
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(body));
        return Encode(digest)[..ChecksumChars];
    }

    private static string Encode(ReadOnlySpan<byte> data)
    {
        var result = new StringBuilder((data.Length * 8 / 5) + 1);
        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
        {
            result.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return result.ToString();
    }

    private static byte[] Decode(string encoded)
    {
        var result = new byte[encoded.Length * 5 / 8];
        var written = 0;
        var buffer = 0;
        var bits = 0;

        foreach (var c in encoded)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                result[written++] = (byte)((buffer >> bits) & 0xFF);
            }
        }

        return result;
    }
}
