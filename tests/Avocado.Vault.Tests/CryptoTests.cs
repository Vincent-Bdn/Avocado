using System.Security.Cryptography;
using System.Text;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Tests;

public class AeadTests
{
    private static readonly byte[] AssociatedData = "context"u8.ToArray();

    [Fact]
    public void Roundtrips()
    {
        using var key = SecretKey.Generate();
        var plaintext = "Dossier 2026-0042"u8.ToArray();

        var sealedData = Aead.Seal(key, plaintext, AssociatedData);
        var opened = Aead.Open(key, sealedData, AssociatedData);

        Assert.Equal(plaintext, opened);
    }

    [Fact]
    public void SealingTwiceProducesDifferentCiphertext()
    {
        using var key = SecretKey.Generate();
        var plaintext = "same input"u8.ToArray();

        Assert.NotEqual(
            Aead.Seal(key, plaintext, AssociatedData),
            Aead.Seal(key, plaintext, AssociatedData));
    }

    [Fact]
    public void RejectsWrongKey()
    {
        using var key = SecretKey.Generate();
        using var other = SecretKey.Generate();

        var sealedData = Aead.Seal(key, "secret"u8, AssociatedData);

        Assert.ThrowsAny<CryptographicException>(() => Aead.Open(other, sealedData, AssociatedData));
    }

    [Fact]
    public void RejectsTamperedCiphertext()
    {
        using var key = SecretKey.Generate();
        var sealedData = Aead.Seal(key, "secret"u8, AssociatedData);

        sealedData[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => Aead.Open(key, sealedData, AssociatedData));
    }

    [Fact]
    public void RejectsMismatchedAssociatedData()
    {
        using var key = SecretKey.Generate();
        var sealedData = Aead.Seal(key, "secret"u8, AssociatedData);

        Assert.ThrowsAny<CryptographicException>(() => Aead.Open(key, sealedData, "other context"u8));
    }
}

public class SecretKeyTests
{
    [Fact]
    public void RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => new SecretKey(new byte[16]));
    }

    [Fact]
    public void IsUnusableAfterDispose()
    {
        var key = SecretKey.Generate();
        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.Span.Length);
    }
}

public class KeyDerivationTests
{
    [Fact]
    public void HkdfIsDeterministicAndDomainSeparated()
    {
        var ikm = RandomNumberGenerator.GetBytes(32);
        var salt = RandomNumberGenerator.GetBytes(16);

        using var first = KeyDerivation.Hkdf(ikm, salt, "purpose-a");
        using var second = KeyDerivation.Hkdf(ikm, salt, "purpose-a");
        using var different = KeyDerivation.Hkdf(ikm, salt, "purpose-b");

        Assert.True(first.Span.SequenceEqual(second.Span));
        Assert.False(first.Span.SequenceEqual(different.Span));
    }

    [Fact]
    public void Argon2idIsDeterministicForTheSamePassphraseAndSalt()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var cheap = new Argon2Parameters { MemoryKib = 1024, Iterations = 1, Parallelism = 1 };

        using var first = KeyDerivation.Argon2id("correct horse", salt, cheap);
        using var second = KeyDerivation.Argon2id("correct horse", salt, cheap);
        using var wrong = KeyDerivation.Argon2id("incorrect horse", salt, cheap);

        Assert.True(first.Span.SequenceEqual(second.Span));
        Assert.False(first.Span.SequenceEqual(wrong.Span));
    }
}

public class RecoveryCodeTests
{
    [Fact]
    public void RoundtripsThroughItsPrintedForm()
    {
        using var key = SecretKey.Generate();

        var code = RecoveryCode.Format(key);

        Assert.True(RecoveryCode.TryParse(code, out var parsed));
        Assert.NotNull(parsed);
        Assert.True(key.Span.SequenceEqual(parsed.Span));
    }

    [Fact]
    public void IsGroupedForTranscription()
    {
        using var key = SecretKey.Generate();

        var code = RecoveryCode.Format(key);

        Assert.Equal(9, code.Split('-').Length);
        Assert.All(code.Split('-'), group => Assert.Equal(6, group.Length));
    }

    [Fact]
    public void ToleratesHowPeopleActuallyTypeItBack()
    {
        using var key = SecretKey.Generate();
        var code = RecoveryCode.Format(key);

        // Lower case, dashes stripped, spaces instead, and the I/L/O confusions from reading paper.
        var mangled = code
            .ToLowerInvariant()
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("0", "O", StringComparison.Ordinal)
            .Replace("1", "l", StringComparison.Ordinal);

        Assert.True(RecoveryCode.TryParse(mangled, out var parsed));
        Assert.NotNull(parsed);
        Assert.True(key.Span.SequenceEqual(parsed.Span));
    }

    [Fact]
    public void RejectsACorruptedChecksum()
    {
        using var key = SecretKey.Generate();
        var code = RecoveryCode.Format(key);

        // Last character is checksum; changing it can never agree with the untouched payload.
        var corrupted = code[..^1] + (code[^1] == 'Z' ? '0' : 'Z');

        Assert.False(RecoveryCode.TryParse(corrupted, out _));
    }

    [Fact]
    public void ChecksumCatchesNearlyEverySingleCharacterTypo()
    {
        using var key = SecretKey.Generate();
        var code = RecoveryCode.Format(key).Replace("-", "", StringComparison.Ordinal);

        var accepted = 0;
        for (var i = 0; i < code.Length; i++)
        {
            var typo = code[..i] + (code[i] == 'Z' ? '0' : 'Z') + code[(i + 1)..];
            if (RecoveryCode.TryParse(typo, out _))
            {
                accepted++;
            }
        }

        // A 10-bit checksum lets roughly one in a thousand through; anything beyond a couple means
        // it is not actually being verified.
        Assert.True(accepted <= 2, $"{accepted} of {code.Length} single-character typos went undetected.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-code")]
    [InlineData("!!!!!!-!!!!!!")]
    public void RejectsNonsense(string input)
    {
        Assert.False(RecoveryCode.TryParse(input, out _));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.False(RecoveryCode.TryParse(null, out _));
    }

    [Fact]
    public void UsesAnUnambiguousAlphabet()
    {
        using var key = SecretKey.Generate();

        var code = RecoveryCode.Format(key);

        Assert.DoesNotContain("I", code, StringComparison.Ordinal);
        Assert.DoesNotContain("L", code, StringComparison.Ordinal);
        Assert.DoesNotContain("O", code, StringComparison.Ordinal);
        Assert.DoesNotContain("U", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCodeIsDifferent()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 50; i++)
        {
            using var key = SecretKey.Generate();
            Assert.True(codes.Add(RecoveryCode.Format(key)));
        }
    }

    [Fact]
    public void EncodesFullEntropy()
    {
        // 52 payload characters + 2 checksum, at 5 bits each, must cover the whole 256-bit key.
        using var key = SecretKey.Generate();

        var code = RecoveryCode.Format(key).Replace("-", "", StringComparison.Ordinal);

        Assert.Equal(54, code.Length);
        Assert.True(Encoding.ASCII.GetByteCount(code) * 5 >= 256);
    }
}
