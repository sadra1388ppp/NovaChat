using NovaChat.Client.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaChat.Client.Services;

/// <summary>
/// Experimental RSA-only text encryption.
/// RSA-OAEP can only encrypt small payloads, so plaintext UTF-8 bytes are split into chunks.
/// Media remains outside E2EE and uses the normal authenticated media flow.
/// </summary>
public sealed class RsaOnlyCryptoService
{
    private const int RsaKeySize = 3072;
    private const int OaepHashBytes = 32; // SHA-256
    private const int RsaModulusBytes = RsaKeySize / 8;
    private const int RsaOaepMaxPlaintextBytes = RsaModulusBytes - (2 * OaepHashBytes) - 2;
    private const int ChunkSize = 190;
    private const int MaxPlaintextBytes = 60_000;
    private const string Algorithm = "RSA-3072-OAEP-SHA256-CHUNKED";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private RSA? _privateKey;
    private string _deviceId = string.Empty;
    private string _initializedUserId = string.Empty;

    private string KeyFilePath
    {
        get
        {
            var userId = string.IsNullOrWhiteSpace(AuthState.UserId) ? "unknown" : AuthState.UserId.Trim();
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaChat",
                "e2ee",
                $"device-{userId}.json");
        }
    }

    public async Task InitializeAsync(ApiService api, CancellationToken cancellationToken = default)
    {
        var currentUserId = AuthState.UserId.Trim();
        if (string.IsNullOrWhiteSpace(currentUserId))
            throw new InvalidOperationException("RSA encryption requires an authenticated user.");

        if (_privateKey != null && !string.IsNullOrWhiteSpace(_deviceId) && _initializedUserId == currentUserId)
            return;

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_privateKey != null && !string.IsNullOrWhiteSpace(_deviceId) && _initializedUserId == currentUserId)
                return;

            _privateKey?.Dispose();
            _privateKey = null;
            _deviceId = string.Empty;
            _initializedUserId = currentUserId;

            var directory = System.IO.Path.GetDirectoryName(KeyFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

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

                var stored = new StoredDevice
                {
                    DeviceId = _deviceId,
                    PrivateKeyPem = _privateKey.ExportPkcs8PrivateKeyPem()
                };

                var plainBytes = JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions);
                var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                await File.WriteAllBytesAsync(KeyFilePath, protectedBytes, cancellationToken);
            }
        }
        finally
        {
            _initializeLock.Release();
        }

        var publicKeyPem = _privateKey!.ExportSubjectPublicKeyInfoPem();
        await api.PostAsync<RegisterDeviceRequest, RegisterDeviceResponse>(
            "api/e2ee/devices",
            new RegisterDeviceRequest { DeviceId = _deviceId, PublicKeyPem = publicKeyPem });
    }

    public async Task<string> EncryptForChatAsync(
        int chatId,
        string plaintext,
        ApiService api,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Message cannot be empty.", nameof(plaintext));

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            if (plainBytes.Length > MaxPlaintextBytes)
                throw new InvalidOperationException($"RSA-only messages are limited to {MaxPlaintextBytes:N0} UTF-8 bytes in this experiment.");

            var devices = await api.GetAsync<List<E2eeDeviceDto>>(
                $"api/e2ee/chats/{chatId}/devices", cancellationToken);

            if (devices == null || devices.Count == 0)
                throw new InvalidOperationException("No trusted encryption devices are registered for this conversation.");

            if (ChunkSize > RsaOaepMaxPlaintextBytes)
                throw new InvalidOperationException("RSA chunk size configuration is invalid.");

            var encryptedForDevices = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var device in devices.DistinctBy(x => x.DeviceId))
            {
                if (string.IsNullOrWhiteSpace(device.DeviceId) || string.IsNullOrWhiteSpace(device.PublicKeyPem))
                    continue;

                using var deviceRsa = RSA.Create();
                deviceRsa.ImportFromPem(device.PublicKeyPem);
                var encryptedChunks = new List<string>();

                foreach (var chunk in Chunk(plainBytes, ChunkSize))
                {
                    var encrypted = deviceRsa.Encrypt(chunk, RSAEncryptionPadding.OaepSHA256);
                    encryptedChunks.Add(Convert.ToBase64String(encrypted));
                    CryptographicOperations.ZeroMemory(chunk);
                }

                encryptedForDevices[device.DeviceId] = encryptedChunks;
            }

            if (encryptedForDevices.Count == 0)
                throw new InvalidOperationException("No valid trusted encryption devices are registered for this conversation.");

            var envelope = new RsaEnvelope
            {
                Version = 1,
                Algorithm = Algorithm,
                ChunkSize = ChunkSize,
                Chunks = encryptedForDevices
            };

            return JsonSerializer.Serialize(envelope, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public async Task<MessageModel> DecryptMessageAsync(
        MessageModel message,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (message.MessageType is "image" or "file" or "voice")
            return message;

        if (!RsaEnvelope.TryParse(message.Content, out var envelope) || envelope == null)
        {
            message.Content = "[Legacy message — not RSA-only]";
            return message;
        }

        if (!envelope.Chunks.TryGetValue(_deviceId, out var encryptedChunks) || encryptedChunks.Count == 0)
        {
            message.Content = "[RSA-encrypted message — this device has no key]";
            return message;
        }

        try
        {
            using var buffer = new MemoryStream();
            foreach (var encryptedChunk in encryptedChunks)
            {
                if (string.IsNullOrWhiteSpace(encryptedChunk))
                    throw new CryptographicException("Encrypted chunk is empty.");

                var cipher = Convert.FromBase64String(encryptedChunk);
                var plain = _privateKey!.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);
                if (plain.Length == 0 || plain.Length > envelope.ChunkSize)
                {
                    CryptographicOperations.ZeroMemory(plain);
                    throw new CryptographicException("RSA chunk size is invalid.");
                }

                await buffer.WriteAsync(plain, cancellationToken);
                CryptographicOperations.ZeroMemory(plain);
            }

            var plainBytes = buffer.ToArray();
            try
            {
                if (plainBytes.Length > MaxPlaintextBytes)
                    throw new CryptographicException("RSA plaintext is too large.");

                message.Content = Encoding.UTF8.GetString(plainBytes);
                return message;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        catch (CryptographicException)
        {
            message.Content = "[RSA-encrypted message — unable to decrypt]";
            return message;
        }
        catch (FormatException)
        {
            message.Content = "[RSA-encrypted message — invalid envelope]";
            return message;
        }
        catch (JsonException)
        {
            message.Content = "[RSA-encrypted message — invalid envelope]";
            return message;
        }
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentUserId = AuthState.UserId.Trim();
        if (_privateKey == null || string.IsNullOrWhiteSpace(_deviceId) || _initializedUserId != currentUserId)
            throw new InvalidOperationException("RSA encryption is not initialized for the current user.");
        return Task.CompletedTask;
    }

    private static IEnumerable<byte[]> Chunk(byte[] source, int chunkSize)
    {
        for (var offset = 0; offset < source.Length; offset += chunkSize)
        {
            var size = Math.Min(chunkSize, source.Length - offset);
            var chunk = new byte[size];
            Buffer.BlockCopy(source, offset, chunk, 0, size);
            yield return chunk;
        }
    }

    private sealed class StoredDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PrivateKeyPem { get; set; } = string.Empty;
    }

    private sealed class RegisterDeviceRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }

    private sealed class RegisterDeviceResponse
    {
        public string DeviceId { get; set; } = string.Empty;
    }

    private sealed class E2eeDeviceDto
    {
        public string DeviceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }

    private sealed class RsaEnvelope
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("alg")] public string Algorithm { get; set; } = string.Empty;
        [JsonPropertyName("chunkSize")] public int ChunkSize { get; set; }
        [JsonPropertyName("chunks")] public Dictionary<string, List<string>> Chunks { get; set; } = new(StringComparer.Ordinal);

        public static bool TryParse(string? content, out RsaEnvelope? envelope)
        {
            envelope = null;
            if (string.IsNullOrWhiteSpace(content))
                return false;

            try
            {
                var value = JsonSerializer.Deserialize<RsaEnvelope>(content, JsonOptions);
                if (value == null || value.Version != 1 || value.Algorithm != RsaOnlyCryptoService.Algorithm ||
                    value.ChunkSize <= 0 || value.ChunkSize > RsaOaepMaxPlaintextBytes || value.Chunks.Count == 0)
                    return false;

                if (value.Chunks.Values.Any(x => x == null || x.Count == 0))
                    return false;

                envelope = value;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
