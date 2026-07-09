using System.Text;
using FordConnectToAbrpSync.Security;
using Microsoft.AspNetCore.DataProtection;

namespace FordConnectToAbrpSync.Tests;

public class EncryptedFileTokenStoreTests
{
    private static (EncryptedFileTokenStore store, string path) NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fordsync-token-{Guid.NewGuid():N}.json");
        var provider = new EphemeralDataProtectionProvider();
        return (new EncryptedFileTokenStore(path, provider), path);
    }

    [Test]
    public async Task Load_WhenFileMissing_ReturnsNull()
    {
        var (store, _) = NewStore();

        await Assert.That(store.Load()).IsNull();
    }

    [Test]
    public async Task SaveThenLoad_RoundTripsToken()
    {
        var (store, path) = NewStore();
        try
        {
            var token = new StoredToken
            {
                RefreshToken = "refresh-abc-123",
                ObtainedUtc = new DateTimeOffset(2025, 8, 15, 21, 0, 0, TimeSpan.Zero),
            };

            store.Save(token);
            var loaded = store.Load();

            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.RefreshToken).IsEqualTo("refresh-abc-123");
            await Assert.That(loaded.ObtainedUtc).IsEqualTo(token.ObtainedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Save_Rotation_OverwritesPreviousToken()
    {
        var (store, path) = NewStore();
        try
        {
            store.Save(new StoredToken { RefreshToken = "old", ObtainedUtc = DateTimeOffset.UnixEpoch });
            store.Save(new StoredToken { RefreshToken = "new-rotated", ObtainedUtc = DateTimeOffset.UnixEpoch });

            await Assert.That(store.Load()!.RefreshToken).IsEqualTo("new-rotated");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Save_WritesEncryptedBytes_NotPlaintext()
    {
        var (store, path) = NewStore();
        try
        {
            store.Save(new StoredToken { RefreshToken = "super-secret-value", ObtainedUtc = DateTimeOffset.UnixEpoch });

            var raw = await File.ReadAllBytesAsync(path);
            var asText = Encoding.UTF8.GetString(raw);
            await Assert.That(asText).DoesNotContain("super-secret-value");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Save_CreatesMissingDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fordsync-{Guid.NewGuid():N}", "nested");
        var path = Path.Combine(dir, "token.json");
        var store = new EncryptedFileTokenStore(path, new EphemeralDataProtectionProvider());
        try
        {
            store.Save(new StoredToken { RefreshToken = "x", ObtainedUtc = DateTimeOffset.UnixEpoch });

            await Assert.That(File.Exists(path)).IsTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }
}
