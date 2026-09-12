# Owner-Readable E2EE Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow the authenticated NovaChat Owner client to decrypt and inspect all newly encrypted text messages without storing plaintext on the server.

**Architecture:** Every text message keeps one AES-256-GCM content key and wraps that key for every active chat device plus every active Owner device. `OwnerChatController` returns the encrypted envelope to the Owner client, and `AllChatsView` decrypts it with the Owner device private key before display. Existing media upload/download remains unchanged and unencrypted.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/Pomelo, MariaDB, SignalR, WPF, RSA-OAEP-SHA256, AES-256-GCM.

**Spec:** `docs/superpowers/specs/2026-09-12-owner-readable-e2ee-design.md`

## Global Constraints

- All work stays on `feature/e2ee-messaging`.
- Never store plaintext text messages on the server.
- Owner access is limited by the existing `OwnerOnly` authorization policy.
- Images, voice messages, and regular files remain on the existing authenticated media path outside E2EE.
- Existing participant device decryption must continue to work.
- Messages created before Owner-recipient expansion cannot be retroactively decrypted unless the Owner device was already an encrypted recipient.

---

### Task 1: Add Owner devices to text-message recipients

**Files:**
- Modify: `NovaChat.Server/Services/E2eeDeviceService.cs`
- Test: existing build plus an API-level/manual verification because the repository currently has no focused E2EE test project

**Interfaces:**
- `GetChatDevicesAsync(int chatId, long userId, CancellationToken)` continues to return active chat devices and additionally active devices for the configured Owner user.

- [ ] **Step 1: Read current Owner configuration and device lookup logic**
  Confirm `Owner:Username` is the configured Owner identity and that `EncryptionDevices.UserId` maps to `Users.Id`.

- [ ] **Step 2: Inject configuration into `E2eeDeviceService`**
  Change the primary constructor from `E2eeDeviceService(AppDbContext db)` to `E2eeDeviceService(AppDbContext db, IConfiguration configuration)` and keep the existing DI registration unchanged.

- [ ] **Step 3: Resolve the Owner user id**
  Read `Owner:Username`, normalize it, query the `Users` table for the matching owner id, and include that id in the recipient user-id set only when it exists.

- [ ] **Step 4: Preserve chat authorization**
  Keep the existing requirement that the caller must belong to the chat. Adding Owner recipient devices must not grant arbitrary users access to another chat's device list.

- [ ] **Step 5: Return a distinct active-device set**
  Query active `EncryptionDevices` rows for chat members plus Owner. Use distinct device ids so an Owner who is also a chat member is included only once.

- [ ] **Step 6: Build and inspect the changed server**
  Run `dotnet build NovaChat.Server/NovaChat.Server.csproj -c Debug` and verify no compile errors.

### Task 2: Restore encrypted payload access for Owner-only conversation inspection

**Files:**
- Modify: `NovaChat.Server/Controllers/OwnerChatController.cs`

**Interfaces:**
- `GET /api/OwnerChat/{chatId}/messages?pageSize=N` returns normal `MessageDto` objects containing encrypted content, not a plaintext-blocking placeholder.

- [ ] **Step 1: Keep OwnerOnly policy**
  Leave `[Authorize(Policy = "OwnerOnly")]` unchanged.

- [ ] **Step 2: Map messages through the normal DTO mapper**
  Replace the local anonymous result that forces `Content = "[Encrypted message...]"` with `messages.Select(MessageDtoMapper.Map)` so the Owner receives the exact encrypted `Content` and all existing message metadata.

- [ ] **Step 3: Do not decrypt on the server**
  Ensure there is no server-side RSA private key handling and no plaintext returned from the database.

- [ ] **Step 4: Increase inspection capacity safely**
  Accept `pageSize`, clamp it to `1..1000`, and keep deterministic ordering by `SentAt`, then `Id`.

- [ ] **Step 5: Build the server**
  Run `dotnet build NovaChat.Server/NovaChat.Server.csproj -c Debug` and verify no compile errors.

### Task 3: Decrypt Owner history in All Chats UI

**Files:**
- Modify: `NovaChat.Client/Views/AllChatsView.xaml.cs`

**Interfaces:**
- `AllChatsView` owns an `E2eeCryptoService` instance and initializes it for the authenticated Owner account.
- `ShowChatDetailsAsync` decrypts returned encrypted messages locally before creating `AdminMessageItem` rows.

- [ ] **Step 1: Add the E2EE service field**
  Add `private readonly E2eeCryptoService _e2ee = new();` next to `_apiService`.

- [ ] **Step 2: Initialize Owner E2EE keys once on load**
  In `AllChatsView_Loaded`, initialize `_e2ee` with `_apiService` before loading chat details. Preserve existing loading guard and error handling.

- [ ] **Step 3: Request encrypted Owner history**
  Keep the existing `api/OwnerChat/{id}/messages` request, changing the page size to `1000`.

- [ ] **Step 4: Decrypt each text message locally**
  For each returned message, call `await _e2ee.DecryptMessageAsync(message)`. The existing crypto service leaves plain image/file/voice messages untouched and decrypts text envelopes with the local device id.

- [ ] **Step 5: Render decrypted content**
  Bind the decrypted message list to `MessagesList`. Keep sender and time formatting unchanged.

- [ ] **Step 6: Handle unavailable historical Owner keys honestly**
  Preserve the crypto service's `[Encrypted message — this device has no key]` result instead of inventing plaintext or bypassing encryption.

- [ ] **Step 7: Build the client**
  Run `dotnet build NovaChat.Client/NovaChat.Client.csproj -c Debug` after ensuring no running `NovaChat.Client` process is locking the output binary.

### Task 4: End-to-end verification

**Files:**
- No source changes unless a verification failure identifies a required fix.

- [ ] **Step 1: Build the full solution**
  Run `dotnet clean` and `dotnet build -c Debug` with the client process stopped.

- [ ] **Step 2: Verify Owner registration**
  Start the server and Owner client once. Confirm the Owner device is present in `EncryptionDevices`.

- [ ] **Step 3: Verify a non-Owner private chat**
  With User A and User B in a private chat, send a new text. Confirm both normal clients render plaintext and the database `Messages.Content` remains an E2EE JSON envelope.

- [ ] **Step 4: Verify Owner decryption without membership**
  From Owner's All Chats screen, open that same conversation and verify the new text is readable. This proves the Owner key was included even when Owner is not a member.

- [ ] **Step 5: Verify group chat**
  Send a new encrypted text in a group and confirm all participants plus Owner can decrypt it.

- [ ] **Step 6: Verify media regression safety**
  Send one image, one WAV voice message, and one regular file. Confirm they still use the existing media upload path and render/play normally.

- [ ] **Step 7: Verify older-message limitation**
  Open a message created before the Owner recipient change. Confirm it remains readable for its original recipients and is marked unavailable to Owner when Owner lacked a historical wrapped key.

- [ ] **Step 8: Confirm branch isolation**
  Verify `main` was not modified and all commits belong to `feature/e2ee-messaging`.
