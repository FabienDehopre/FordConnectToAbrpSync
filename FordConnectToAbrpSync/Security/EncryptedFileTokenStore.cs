using System.Text;
using System.Text.Json;
using FordConnectToAbrpSync.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace FordConnectToAbrpSync.Security;

/// <summary>
/// A <see cref="ITokenStore"/> that keeps the refresh credential in a single
/// JSON file, encrypted at rest with ASP.NET Data Protection and written
/// atomically (temp file + move) so a crash mid-write can't corrupt it.
/// </summary>
internal sealed class EncryptedFileTokenStore : ITokenStore
{
    private const string Purpose = "FordConnectToAbrpSync.FordRefreshToken.v1";

    private readonly string _filePath;
    private readonly IDataProtector _protector;

    public EncryptedFileTokenStore(string filePath, IDataProtectionProvider protectionProvider)
    {
        _filePath = filePath;
        _protector = protectionProvider.CreateProtector(Purpose);
    }

    public StoredToken? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_filePath);
        var json = Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
        return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.StoredToken);
    }

    public void Save(StoredToken token)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(token, AppJsonSerializerContext.Default.StoredToken);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));

        var tempPath = _filePath + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
