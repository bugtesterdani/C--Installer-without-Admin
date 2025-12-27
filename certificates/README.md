## MSIX Signing Certificate (development)

This directory contains a development self-signed code-signing certificate used by the CI workflow when no PFX secret is provided.

Files
- **PFX (Base64)**: `msix_signing.pfx.b64` (Base64-encoded PFX; no binary files are checked in; CN=hello, keyUsage=digitalSignature, EKU=codeSigning, basicConstraints=CA:false)
- **CER (Base64)**: `msix_signing.cer.b64` (Base64-encoded public certificate)
- **Password**: `msix-dev-password`
- The workflow decodes the Base64 PFX/CER at runtime and exports the CER for installation on client machines.

Usage
- CI prefers secrets `MSIX_SIGNING_CERTIFICATE_BASE64` and `MSIX_SIGNING_CERTIFICATE_PASSWORD`.
- If the secrets are not set, the workflow falls back to `msix_signing.pfx.b64` / `msix_signing.cer.b64` with the password above.

Production
- Replace this development certificate with your own trusted code-signing PFX via the secrets mentioned above.
