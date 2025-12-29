"""
Validate a signed update payload consisting of a ZIP archive and a manifest.json.

The script performs three checks:
1. Validate the manifest structure (version, files map, signature field).
2. Verify the RSA signature using the provided public key.
3. Extract the ZIP in-memory and compare SHA-256 hashes for every file listed
   in the manifest (optionally erroring on extra files in the archive).
Example:
    python validate_payload.py --zip update.zip --manifest manifest.json \\
        --public-key public.pem --fail-on-extra
"""

import argparse
import base64
import hashlib
import json
import locale
import sys
from pathlib import Path
from typing import Dict, Optional
import zipfile

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding

def canonical_json(obj) -> bytes:
    """
    Produce canonical JSON bytes matching the format used by the launchers and
    manifest creation script (sorted keys, no whitespace, UTF-8).
    """
    return json.dumps(
        obj,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=False,
    ).encode("utf-8")

def enforce_german_locale() -> Optional[str]:
    """
    Force locale-sensitive validation steps to use a German locale, even on
    systems configured for a different language. Returns the locale string that
    could be activated or None if no German locale is available.
    """
    german_candidates = (
        "de_DE.UTF-8",  # Linux common
        "de_DE",        # Generic Unix fallback
        "deu_deu",      # Windows legacy
        "de-DE",
        "German_Germany.1252",  # Windows code page
    )

    for candidate in german_candidates:
        try:
            locale.setlocale(locale.LC_ALL, candidate)
            return candidate
        except locale.Error:
            continue

    return None

def normalize_relative_path(path: str) -> str:
    """
    Normalize a manifest relative path:
    - convert "\" to "/"
    - drop "." segments and empty parts
    - reject ".." segments
    """
    posix = path.replace("\\", "/")
    parts = []
    for part in posix.split("/"):
        if part in ("", "."):
            continue
        if part == "..":
            raise ValueError("Manifest enthält unzulässigen Pfadanteil '..'.")
        parts.append(part)
    return "/".join(parts)

def load_manifest(manifest_path: Path) -> tuple[str, Dict[str, str], bytes]:
    """Load and validate manifest.json fields."""
    data = json.loads(manifest_path.read_text(encoding="utf-8"))

    if "version" not in data or not isinstance(data["version"], str) or not data["version"].strip():
        raise ValueError("Manifest hat kein gültiges Versionsfeld.")

    if "files" not in data or not isinstance(data["files"], dict):
        raise ValueError("Manifest enthält keine gültige Dateiliste.")

    if "signature" not in data or not isinstance(data["signature"], str):
        raise ValueError("Manifest enthält keine Signatur.")

    try:
        signature = base64.b64decode(data["signature"], validate=True)
    except Exception as exc:
        raise ValueError(f"Signatur ist kein gültiges Base64: {exc}") from exc

    # Force all file keys/values to strings
    files: Dict[str, str] = {}
    for key, value in data["files"].items():
        if not isinstance(key, str) or not isinstance(value, str):
            raise ValueError("Dateiliste muss String-Schlüssel und -Werte enthalten.")
        files[key] = value

    return data["version"], files, signature

def load_public_key(public_key_path: Path):
    """Load an RSA public key from PEM."""
    with public_key_path.open("rb") as fh:
        return serialization.load_pem_public_key(fh.read())

def verify_signature(version: str, files: Dict[str, str], signature: bytes, public_key_path: Path) -> None:
    """Verify RSA PKCS#1 v1.5 signature for the unsigned manifest payload."""
    unsigned_manifest = {"version": version, "files": files}
    payload = canonical_json(unsigned_manifest)
    public_key = load_public_key(public_key_path)

    public_key.verify(
        signature,
        payload,
        padding.PKCS1v15(),
        hashes.SHA256(),
    )

def hash_zip_members(zip_path: Path) -> Dict[str, str]:
    """
    Return a map of normalized archive paths -> sha256 hex digests.
    Directory entries are ignored.
    """
    hashes: Dict[str, str] = {}

    with zipfile.ZipFile(zip_path, "r") as archive:
        for info in archive.infolist():
            if info.is_dir():
                continue

            normalized = normalize_relative_path(info.filename)
            if normalized in hashes:
                raise ValueError(f"Doppelte Datei im ZIP: {normalized}")

            digest = hashlib.sha256()
            with archive.open(info, "r") as file_handle:
                for chunk in iter(lambda: file_handle.read(8192), b""):
                    digest.update(chunk)
            hashes[normalized] = digest.hexdigest()

    return hashes

def validate_payload(zip_path: Path, manifest_path: Path, public_key_path: Path, fail_on_extra: bool) -> None:
    version, files, signature = load_manifest(manifest_path)

    try:
        verify_signature(version, files, signature, public_key_path)
    except InvalidSignature as exc:
        raise ValueError("Signaturprüfung fehlgeschlagen.") from exc

    archive_hashes = hash_zip_members(zip_path)

    normalized_manifest_files: Dict[str, str] = {}
    for rel_path, expected_hash in files.items():
        normalized = normalize_relative_path(rel_path)
        normalized_manifest_files[normalized] = expected_hash.lower()

    missing = []
    hash_mismatches = []
    for rel_path, expected_hash in normalized_manifest_files.items():
        if rel_path not in archive_hashes:
            missing.append(rel_path)
            continue

        if archive_hashes[rel_path].lower() != expected_hash:
            hash_mismatches.append(rel_path)

    extra_files = sorted(set(archive_hashes) - set(normalized_manifest_files))

    if missing:
        raise ValueError(f"Dateien fehlen im ZIP: {', '.join(missing)}")
    if hash_mismatches:
        raise ValueError(f"Hashes stimmen nicht: {', '.join(hash_mismatches)}")
    if fail_on_extra and extra_files:
        raise ValueError(f"ZIP enthält nicht im Manifest gelistete Dateien: {', '.join(extra_files)}")

    if extra_files:
        print(f"Warnung: Zusätzliche Dateien im ZIP (nicht im Manifest): {', '.join(extra_files)}")

    print(f"Manifest und ZIP sind gültig. Version: {version}")

def main() -> None:
    parser = argparse.ArgumentParser(description="Validiere ZIP-Payload und manifest.json gegen eine öffentliche Signatur.")
    parser.add_argument("--zip", required=True, type=Path, help="ZIP-Archiv mit der extrahierten Anwendung.")
    parser.add_argument("--manifest", required=True, type=Path, help="manifest.json, die die Payload beschreibt.")
    parser.add_argument(
        "--public-key",
        type=Path,
        default=Path(__file__).with_name("public.pem"),
        help="PEM-kodierter öffentlicher Schlüssel für die Signaturprüfung (Default: public.pem neben dem Skript).",
    )
    parser.add_argument(
        "--fail-on-extra",
        action="store_true",
        help="Fehlschlag, falls das ZIP zusätzliche Dateien enthält, die nicht im Manifest stehen.",
    )

    args = parser.parse_args()

    german_locale = enforce_german_locale()
    if german_locale:
        print(f"Locale für Validierung gesetzt auf: {german_locale}", file=sys.stderr)
    else:
        print("Warnung: Keine deutsche Locale verfügbar, es wird die System-Locale verwendet.", file=sys.stderr)

    try:
        validate_payload(args.zip, args.manifest, args.public_key, args.fail_on_extra)
    except Exception as exc:
        print(f"Validierung fehlgeschlagen: {exc}")
        raise SystemExit(1)

if __name__ == "__main__":
    main()
