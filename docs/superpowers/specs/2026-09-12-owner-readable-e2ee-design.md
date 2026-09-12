# Owner-Readable E2EE Design

**Goal:** Keep message content end-to-end encrypted for normal users while allowing the authorized NovaChat Owner client to decrypt message content locally.

## Architecture

Each text message uses one random AES-256-GCM content key. That key is RSA-OAEP-SHA256 wrapped once for every active device participating in the chat and once for every active Owner device. The server stores only the ciphertext envelope and wrapped keys; it never receives plaintext.

The Owner-only conversation endpoint will return the original encrypted message envelope instead of replacing content with an administrator placeholder. The Owner WPF client will use the existing local Owner device private key to decrypt the envelope before rendering it. Media remains outside E2EE exactly as currently implemented.

## Security Boundary

This is recipient-expanded E2EE: the Owner is an intentional recipient of every new text message. The Owner client must be authenticated by the existing OwnerOnly policy for global conversation inspection. The server must never add plaintext message content to an API response or database field.

## Backward Compatibility

Messages created before Owner recipient keys existed cannot be retroactively decrypted by the Owner unless the Owner device was already one of the original encrypted recipients. They remain intact and continue to decrypt for devices that already have their wrapped key. New messages will always include the active Owner device keys.

## Required Changes

1. Extend `E2eeDeviceService` to include active Owner devices when building the recipient device list for a chat.
2. Change `OwnerChatController` message responses to return normal encrypted `MessageDto` data rather than a plaintext-blocking placeholder.
3. Extend `AllChatsView` to initialize E2EE and decrypt Owner-visible message history locally before binding it to the UI.
4. Keep media upload/download flows unchanged and outside E2EE.
5. Add focused tests for envelope recipient inclusion, Owner decryption, and preservation of media behavior.

## Acceptance Criteria

- A normal user sends a text message and the stored message content is still JSON ciphertext, never plaintext.
- The message envelope contains the active Owner device id in `keys` when the Owner is not a member of the chat.
- Owner opens All Chats, selects a conversation, and sees readable text for every newly encrypted message that contains an Owner key.
- Normal participants can still decrypt their own messages.
- Images, voice messages, and regular files continue using the existing plain authenticated media upload path.
- No changes are made to `main`; all work stays on `feature/e2ee-messaging`.
