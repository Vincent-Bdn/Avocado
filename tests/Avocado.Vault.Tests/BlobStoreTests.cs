using System.Security.Cryptography;
using System.Text;
using Avocado.Vault.Blobs;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Tests;

public class BlobStoreTests
{
    [Fact]
    public async Task RoundtripsASmallDocument()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var content = "Conclusions récapitulatives"u8.ToArray();
        var reference = await store.PutAsync(new MemoryStream(content));

        Assert.Equal(content.Length, reference.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), reference.Sha256);
        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task RoundtripsAnEmptyFile()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var reference = await store.PutAsync(new MemoryStream([]));

        Assert.Equal(0, reference.SizeBytes);
        Assert.Empty(await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task RoundtripsAScanLargerThanOneChunk()
    {
        // Exercises the chunked framing, including a final chunk that is not a whole chunk.
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var content = RandomNumberGenerator.GetBytes((int)(2.5 * 1024 * 1024));
        var reference = await store.PutAsync(new MemoryStream(content));

        Assert.Equal(content.Length, reference.SizeBytes);
        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task RoundtripsContentThatIsExactlyOneChunk()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var content = RandomNumberGenerator.GetBytes(1024 * 1024);
        var reference = await store.PutAsync(new MemoryStream(content));

        Assert.Equal(content, await ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task StoresIdenticalContentOnlyOnce()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var content = "the same attachment, forwarded twice"u8.ToArray();
        var first = await store.PutAsync(new MemoryStream(content));
        var second = await store.PutAsync(new MemoryStream(content));

        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(directory.Path, "*.blob", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DoesNotWritePlaintextToDisk()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        await store.PutAsync(new MemoryStream("SECRET-CLIENT-NAME"u8.ToArray()));

        foreach (var file in Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(
                "SECRET-CLIENT-NAME",
                Encoding.Latin1.GetString(File.ReadAllBytes(file)),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FileNamesDoNotRevealThePlaintextHash()
    {
        // Otherwise anyone with the folder could confirm "this vault holds this exact document" by
        // hashing a candidate file, which would undo the encrypted database.
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var reference = await store.PutAsync(new MemoryStream("some evidence"u8.ToArray()));

        var names = Directory.GetFiles(directory.Path, "*.blob", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension);

        Assert.DoesNotContain(reference.Sha256, names);
    }

    [Fact]
    public async Task AnotherVaultsKeyCannotReadTheBlob()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        using var otherKey = SecretKey.Generate();

        var store = new EncryptedBlobStore(directory.Path, key);
        var reference = await store.PutAsync(new MemoryStream("privileged"u8.ToArray()));

        var intruder = new EncryptedBlobStore(directory.Path, otherKey);

        // The file name is itself keyed, so the wrong key cannot even locate it.
        Assert.False(intruder.Exists(reference));
        Assert.Throws<VaultCorruptedException>(() => intruder.OpenRead(reference));
    }

    [Fact]
    public async Task DetectsATruncatedBlob()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var reference = await store.PutAsync(
            new MemoryStream(RandomNumberGenerator.GetBytes(2 * 1024 * 1024)));

        var path = Directory.GetFiles(directory.Path, "*.blob", SearchOption.AllDirectories).Single();
        var truncated = File.ReadAllBytes(path)[..(1024 * 1024)];
        File.WriteAllBytes(path, truncated);

        await Assert.ThrowsAsync<VaultCorruptedException>(() => ReadAllAsync(store, reference));
    }

    [Fact]
    public async Task DetectsATamperedBlob()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var reference = await store.PutAsync(new MemoryStream("original evidence"u8.ToArray()));

        var path = Directory.GetFiles(directory.Path, "*.blob", SearchOption.AllDirectories).Single();
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        await Assert.ThrowsAsync<VaultCorruptedException>(() => ReadAllAsync(store, reference));
    }

    [Fact]
    public void ReportsAMissingBlobRatherThanCrashing()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var dangling = new BlobReference(Convert.ToHexString(SHA256.HashData("gone"u8)).ToLowerInvariant(), 4);

        Assert.False(store.Exists(dangling));
        var exception = Assert.Throws<VaultCorruptedException>(() => store.OpenRead(dangling));
        Assert.Contains("missing from the vault", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletesABlob()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        var reference = await store.PutAsync(new MemoryStream("disposable"u8.ToArray()));

        Assert.True(store.Delete(reference));
        Assert.False(store.Exists(reference));
        Assert.False(store.Delete(reference));
    }

    [Fact]
    public async Task LeavesNoTemporaryFilesBehind()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();
        var store = new EncryptedBlobStore(directory.Path, key);

        await store.PutAsync(new MemoryStream(RandomNumberGenerator.GetBytes(3 * 1024 * 1024)));

        var temporaryDirectory = Path.Combine(directory.Path, ".tmp");
        Assert.Empty(Directory.Exists(temporaryDirectory) ? Directory.GetFiles(temporaryDirectory) : []);
    }

    private static async Task<byte[]> ReadAllAsync(IBlobStore store, BlobReference reference)
    {
        await using var stream = store.OpenRead(reference);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
