using NovaChat.Client.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaChat.Client.Services;

public sealed class E2eeCryptoService
{
    private const int RsaKeySize = 3072;
    private const int AesKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxPlaintextLength = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private RSA? _privateKey;
    private string _deviceId = string.Empty;

    private string KeyFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaChat", "e2ee-device.json");

    public async Task InitializeAsync(ApiService api, CancellationToken cancellationToken = default)
    {
        if (_privateKey != null && !string.IsNullOrWhiteSpace(_deviceId)) return;
        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_privateKey != null && !string.IsNullOrWhiteSpace(_deviceId)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);
            if (File.Exists(KeyFilePath))
            {
                try
                {
                    var protectedBytes = await File.ReadAllBytesAsync(KeyFilePath, cancellationToken);
                    var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    var stored = JsonSerializer.Deserialize<StoredDevice>(plainBytes, JsonOptions);
                    if (stored != null && !string.IsNullOrWhiteSpace(stored.DeviceId) && !string.IsNullOrWhiteSpace(stored.PrivateKeyPem))
                    {
                        _deviceId = stored.DeviceId;
                        _privateKey = RSA.Create();
                        _privateKey.ImportFromPem(stored.PrivateKeyPem);
                    }
                }
                catch
                {
                    _privateKey?.Dispose();
                    _privateKey = null;
                    _deviceId = string.Empty;
                }
            }

            if (_privateKey == null)
            {
                _privateKey = RSA.Create(RsaKeySize);
                _deviceId = Guid.NewGuid().ToString("N");
                var stored = new StoredDevice { DeviceId = _deviceId, PrivateKeyPem = _privateKey.ExportPkcs8PrivateKeyPem() };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                await File.WriteAllBytesAsync(KeyFilePath, protectedBytes, cancellationToken);
            }
        }
        finally { _initializeLock.Release(); }

        var publicKeyPem = _privateKey!.ExportSubjectPublicKeyInfoPem();
        await api.PostAsync<RegisterDeviceRequest, RegisterDeviceResponse>("api/e2ee/devices", new RegisterDeviceRequest { DeviceId = _deviceId, PublicKeyPem = publicKeyPem });
    }

    public async Task<MessageModel> DecryptMessageAsync(MessageModel message, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!E2eeEnvelope.TryParse(message.Content, out var envelope) || envelope == null)
        {
            message.Content = "[Legacy message — not E2EE]";
            return message;
        }

        if (!envelope.Keys.TryGetValue(_deviceId, out var wrappedKey))
        {
            message.Content = "[Encrypted message — this device has no key]";
            return message;
        }

        try
        {
            var key = _privateKey!.Decrypt(Convert.FromBase64String(wrappedKey), RSAEncryptionPadding.OaepSHA256);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var cipher = Convert.FromBase64String(envelope.Ciphertext);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            CryptographicOperations.ZeroMemory(key);
            message.Content = Encoding.UTF8.GetString(plain);
            return message;
        }
        catch (Exception) when (true)
        {
            message.Content = "[Encrypted message — unable to decrypt]";
            return message;
        }
    }

    public async Task<string> EncryptForChatAsync(int chatId, string plaintext, ApiService api, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(plaintext)) throw new ArgumentException("Message cannot be empty.", nameof(plaintext));
        if (plaintext.Length > MaxPlaintextLength) throw new InvalidOperationException("Message is too large.");

        var devices = await api.GetAsync<List<E2eeDeviceDto>>($"api/e2ee/chats/{chatId}/devices");
        if (devices == null || devices.Count == 0) throw new InvalidOperationException("No trusted encryption devices are registered for this conversation. Ask every participant to open NovaChat once.");

        var aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(aesKey, TagSize)) aes.Encrypt(nonce, plain, cipher, tag);

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var device in devices.DistinctBy(x => x.DeviceId))
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(device.PublicKeyPem);
            var wrapped = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
            keys[device.DeviceId] = Convert.ToBase64String(wrapped);
        }
        CryptographicOperations.ZeroMemory(aesKey);

        var envelope = new E2eeEnvelope
        {
            Version = 1,
            Algorithm = "AES-256-GCM+RSA-OAEP-SHA256",
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(cipher),
            Keys = keys
        };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_privateKey == null || string.IsNullOrWhiteSpace(_deviceId))
            throw new InvalidOperationException("E2EE is not initialized.");
        await Task.CompletedTask;
    }

    private sealed class StoredDevice { public string DeviceId { get; set; } = string.Empty; public string PrivateKeyPem { get; set; } = string.Empty; }
    private sealed class RegisterDeviceRequest { public string DeviceId { get; set; } = string.Empty; public string PublicKeyPem { get; set; } = string.Empty; }
    private sealed class RegisterDeviceResponse { public string DeviceId { get; set; } = string.Empty; }
    private sealed class E2eeDeviceDto { public string DeviceId { get; set; } = string.Empty; public string UserId { get; set; } = string.Empty; public string PublicKeyPem { get; set; } = string.Empty; }

    private sealed class E2eeEnvelope
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("alg")] public string Algorithm { get; set; } = string.Empty;
        [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;
        [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
        [JsonPropertyName("ciphertext")] public string Ciphertext { get; set; } = string.Empty;
        [JsonPropertyName("keys")] public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

        public static bool TryParse(string? content, out E2eeEnvelope? envelope)
        {
            envelope = null;
            if (string.IsNullOrWhiteSpace(content)) return false;
            try
            {
                var candidate = JsonSerializer.Deserialize<E2eeEnvelope>(content, JsonOptions);
                if (candidate == null || candidate.Version != 1 || candidate.Algorithm != "AES-256-GCM+RSA-OAEP-SHA256" || candidate.Keys.Count == 0) return false;
                if (string.IsNullOrWhiteSpace(candidate.Nonce) || string.IsNullOrWhiteSpace(candidate.Tag) || string.IsNullOrWhiteSpace(candidate.Ciphertext)) return false;
                envelope = candidate;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
