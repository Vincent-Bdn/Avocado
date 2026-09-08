using System.Security.Cryptography;
using System.Text;
using Avocado.Vault.Blobs;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Tests;

/// <summary>
/// Compression lives inside the blob, which means it is invisible to everything above it and must
/// stay that way. These tests hold it to that: same reference, same bytes back, and blobs written
/// before it existed still open.
/// </summary>
public class BlobCompressionTests
{
    /// <summary>A .msg or a .txt: the half of a dossier that is prose.</summary>
    private static byte[] Prose(int repetitions) =>
        Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
            "Monsieur le Président, par conclusions signifiées le 14 mars, la demanderesse expose que. ",
            repetitions)));

    [Fact]
    public async Task StoresProseInLessSpaceThanItTakesUp()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var content = Prose(2_000);
        var reference = await store.PutAsync(new MemoryStream(content));

        var onDisk = Directory.EnumerateFiles(directory.Path, "*.blob", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

        // The point of the whole exercise. Prose deflates to a fraction of itself, and a nightly
        // capture of a dossier full of correspondence is what pays for it.
        Assert.True(
            onDisk < content.Length / 4,
            $"{content.Length} bytes of prose took {onDisk} bytes on disk.");

        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task LeavesAScanAloneRatherThanGrowingIt()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        // Stands in for the PDFs and JPEGs that are already compressed inside: deflate cannot help,
        // and storing its slightly larger output would make the vault bigger than the originals.
        var content = RandomNumberGenerator.GetBytes(400_000);
        var reference = await store.PutAsync(new MemoryStream(content));

        var onDisk = Directory.EnumerateFiles(directory.Path, "*.blob", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

        Assert.True(onDisk < content.Length + 1024, $"{content.Length} bytes grew to {onDisk}.");
        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task RoundtripsProseAcrossSeveralChunks()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        // Every chunk compresses, including the short final one, and each is framed separately.
        var content = Prose(40_000);
        Assert.True(content.Length > 3 * 1024 * 1024);

        var reference = await store.PutAsync(new MemoryStream(content));

        Assert.Equal(content.Length, reference.SizeBytes);
        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task NamesABlobAfterItsPlaintextWhateverTheCompressorDid()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var content = Prose(50);
        var reference = await store.PutAsync(new MemoryStream(content));

        // Identity is the plaintext's, not the stored bytes'. This is what lets a document keep
        // deduplicating against the copy stored last year even if deflate's output changes under us
        // in a future .NET, and what lets the capture compare a file to its blob without reading it.
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), reference.Sha256);
        Assert.Equal(content.Length, reference.SizeBytes);
    }

    [Fact]
    public async Task RefusesABlobWhoseCompressionFlagWasFlipped()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var reference = await store.PutAsync(new MemoryStream(Prose(50)));
        var path = Directory.EnumerateFiles(directory.Path, "*.blob", SearchOption.AllDirectories).Single();

        // The flags byte opens the first record, right after the 28-byte header. Turning « deflated »
        // off would hand the caller a deflate stream as though it were the document; turning it on
        // where it was off would hand the inflater arbitrary bytes. Neither gets that far, because
        // the byte is authenticated rather than merely written down.
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[4 + 16 + 8] ^= 0b10;
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAsync<VaultCorruptedException>(() => ReadAllAsync(store, reference));
    }

    /// <summary>
    /// A blob written by the version the beta users are running today, byte for byte, read back by
    /// the version that compresses.
    ///
    /// <para>Their vaults hold modèles in this format. The compatibility is not incidental, it is the
    /// reason the compression flag went into the byte that already carried « final » rather than into
    /// a new header: 0 and 1 mean exactly what they meant, and the associated data they were sealed
    /// with is reproduced exactly. A fixture is the only way to keep that true, since the code that
    /// wrote it no longer exists to be run.</para>
    /// </summary>
    [Fact]
    public async Task ReadsABlobWrittenBeforeCompressionExisted()
    {
        const string Base64 =
            "QVZCMXsEQWqOSvOAnqWJLswWwAKdnawUztxgrgEAAAE4QCA9XykfINmiSdSDHpTQBp1d5+8zakb/djZMfq0aPjW7hx" +
            "9hxX5Qq7ZTu7NRXo/UDzxWj9XVSMyhKY4WKicPFvkY7udtFPoWJZkY4pyjSjPW3ShvGydQo18s9lCyXdMdm+mcLhpj" +
            "MyPM5k23RZVs81vKP0bvXtedGYBEeHa5OeLCrknCSovEtACKy8foA+LShLbw3jmsw6+9j9MdUwsceU764JJfmDEMYa" +
            "7TgZOjpH405ShrxQB8fvJr8P9czpiGoY94BsaFGe2bh/hE46DdL6M2unpUseQCUy97rue9nZCT5W4O/ZLXiPUY22wZ" +
            "qF8k4c4Ibq/POtTMsFTYhTdGxoAEk4Vtyzj49MyCSB3TCdsVGmeDJU+u82XmCi0Eb6UkfHDynITMg086YUAhIvw1rc" +
            "jbI13QPhkO";

        var content = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("Conclusions récapitulatives dappel. ", 8)));

        var keyBytes = new byte[SecretKey.SizeInBytes];
        for (var index = 0; index < keyBytes.Length; index++)
        {
            keyBytes[index] = (byte)((index * 7) + 3);
        }

        using var directory = new TempDirectory();
        using var key = new SecretKey(keyBytes);
        var store = new EncryptedBlobStore(directory.Path, key);

        // The store names a blob from the key and the plaintext hash, both of which are fixed here,
        // so storing the content puts the file exactly where the fixture belongs. Overwriting it with
        // the fixture is then the same vault holding the same document, written by the old code.
        var reference = await store.PutAsync(new MemoryStream(content));
        var path = Directory.EnumerateFiles(directory.Path, "*.blob", SearchOption.AllDirectories).Single();
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(Base64));

        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    private static async Task<byte[]> ReadAllAsync(IBlobStore store, BlobReference reference)
    {
        await using var stream = store.OpenRead(reference);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
