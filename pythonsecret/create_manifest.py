"""
Create a signed manifest.json for an extracted update payload.

The generated manifest matches the structure expected by the launchers:
{
    "version": "<semantic version>",
    "files": { "<relative path>": "<sha256 hex>" },
    "signature": "<base64 RSA signature>"
}
"""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding


def canonical_json(obj) -> bytes:
    """Return canonical JSON bytes compatible with the C# verifier."""
    return json.dumps(
        obj,
        sort_keys=True,
        separators=(",", ":"),  # no whitespace
        ensure_ascii=False,
    ).encode("utf-8")


def sha256_file(path: Path) -> str:
    """Calculate a SHA-256 hex digest for a single file."""
    hasher = hashlib.sha256()
    with path.open("rb") as f:
        while chunk := f.read(8192):
            hasher.update(chunk)
    return hasher.hexdigest()


def collect_hashes(payload_dir: Path) -> dict[str, str]:
    """Recursively hash all files under the payload directory and return a relative-path map."""
    files: dict[str, str] = {}
    for file_path in payload_dir.rglob("*"):
        if file_path.is_file():
            rel = file_path.relative_to(payload_dir).as_posix()
            files[rel] = sha256_file(file_path)
    return files


def load_private_key(private_key: Path):
    """Load a PEM-encoded RSA private key for signing."""
    with private_key.open("rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


def write_manifest(output: Path, manifest: dict):
    """Write the manifest JSON to disk with UTF-8 encoding."""
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=4, ensure_ascii=False)


def main():
    """Entrypoint for generating a signed manifest from a payload folder."""
    parser = argparse.ArgumentParser(description="Generate signed manifest.json")
    parser.add_argument(
        "--payload-dir",
        required=True,
        type=Path,
        help="Folder containing the update payload (will be hashed recursively)",
    )
    parser.add_argument(
        "--version",
        required=True,
        help="Version string to embed in the manifest",
    )
    parser.add_argument(
        "--private-key",
        type=Path,
        default=Path(__file__).with_name("private.pem"),
        help="PEM-encoded RSA private key used for signing",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("manifest.json"),
        help="Where to write manifest.json (default: working directory)",
    )

    args = parser.parse_args()

    files = collect_hashes(args.payload_dir)

    unsigned_manifest = {"version": args.version, "files": files}
    manifest_bytes = canonical_json(unsigned_manifest)

    private_key = load_private_key(args.private_key)
    signature = private_key.sign(
        manifest_bytes,
        padding.PKCS1v15(),
        hashes.SHA256(),
    )

    manifest = unsigned_manifest | {"signature": base64.b64encode(signature).decode()}
    write_manifest(args.output, manifest)

    print(f"Manifest created at: {args.output}")


if __name__ == "__main__":
    main()
