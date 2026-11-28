# Lantern — Correct the Misinterpretation of ASP.NET Core Data Protection Key-Ring Files

## Confirmed Finding

An uploaded file named `key-<guid>.xml` was inspected.

Its structure contains:

- `Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption`
- `AES_256_CBC`
- `HMACSHA256`
- `Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor`
- A DPAPI-protected encrypted key descriptor.

**This file is an ASP.NET Core Data Protection key-ring entry, not a serialized Meilisearch API credential.**

Do not attempt to decode the Base64 value inside the XML as a Meilisearch API key.

Do not manually decrypt the Data Protection key ring to obtain application credentials.

## Required Investigation

1. Find where Lantern configures `AddDataProtection()`, `PersistKeysToFileSystem()`, `ProtectKeysWithDpapi()`, and any related Data Protection options.
2. Identify all calls to `IDataProtector.Protect()` and `IDataProtector.Unprotect()`.
3. Determine whether Meilisearch credentials are actually protected using ASP.NET Core Data Protection.
4. If so, locate the separately stored encrypted credential payload and identify the exact purpose string and application isolation configuration used during encryption.
5. Verify that the original key ring, application configuration, and appropriate Windows DPAPI identity are available during decryption.
6. Verify that the decrypted credential is correctly supplied to the Meilisearch SDK.
7. If Meilisearch credentials are stored using a different mechanism, identify and fix that mechanism instead.
8. Investigate the Linux implementation independently. Do not assume Windows DPAPI-protected key-ring XML can be transferred to Linux and decrypted there.
9. Ensure Data Protection key rings are persistent and usable across application restarts, including for authentication cookies.
10. Preserve existing key-ring files and encrypted credentials. Do not delete or regenerate them as a troubleshooting shortcut.

## Required Security Behavior

- Never interpret Data Protection key material as an application API key.
- Never log the contents of decrypted credentials or key-ring secrets.
- Do not silently replace an unreadable key ring.
- Do not change Data Protection purpose strings or application isolation settings without a compatibility plan.
- Do not overwrite existing Meilisearch credentials.
- Do not weaken Windows DPAPI protection to bypass an identity mismatch.

## Verification

Implement tests confirming:

- Existing Data Protection keys are loaded correctly.
- Data Protection `Protect()` and `Unprotect()` round-trip correctly.
- Persisted protected payloads can be decrypted after application restart.
- Existing encrypted payloads remain compatible when the required protection context is available.
- Windows Service identity and DPAPI scope are compatible.
- Linux Data Protection configuration is persistent and uses a supported key-protection mechanism.
- Meilisearch credentials load independently of key-ring XML parsing.
- Authenticated Meilisearch indexing and search succeed on both platforms.

## Completion Criteria

Report:

1. What the Data Protection key ring is used for.
2. Where the Meilisearch Master Key is actually stored.
3. Where the Meilisearch API Key is actually stored.
4. Whether those credentials are protected by Data Protection or another mechanism.
5. Why credential loading failed.
6. How the failure was corrected.
7. Whether existing credentials remain usable.
8. Actual Windows and Linux integration test results.

**Do not consider this issue fixed until Lantern can load the correct stored credential and successfully perform authenticated Meilisearch operations.**
