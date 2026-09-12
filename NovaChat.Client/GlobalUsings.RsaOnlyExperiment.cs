// RSA-only experiment: redirect existing E2EE service references to the prototype
// without modifying the working hybrid AES+RSA implementation on the original branch.
global using E2eeCryptoService = NovaChat.Client.Services.RsaOnlyCryptoService;
