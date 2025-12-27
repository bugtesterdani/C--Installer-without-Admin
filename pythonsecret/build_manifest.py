import json
import hashlib
import tempfile
import zipfile
import os
import requests
import base64

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding


UPDATE_JSON_URL = "https://github.com/bugtesterdani/C--Installer-without-Admin/releases/latest/download/update.json"
PRIVATE_KEY_PATH = "private.pem"
OUTPUT_MANIFEST = "manifest.json"


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def download_file(url, target_path):
    print(f"Lade ZIP herunter: {url}")
    r = requests.get(url)
    r.raise_for_status()
    with open(target_path, "wb") as f:
        f.write(r.content)


def load_private_key():
    with open(PRIVATE_KEY_PATH, "rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


# 🔥 WICHTIG: Python & C# müssen EXAKT dieselben Bytes erzeugen
def canonical_json(obj):
    return json.dumps(
        obj,
        sort_keys=True,
        separators=(",", ":"),   # keine Leerzeichen
        ensure_ascii=False       # gleiche Escapes wie C#
    ).encode("utf-8")


def main():
    print("Lade update.json...")
    update_info = requests.get(UPDATE_JSON_URL).json()

    version = update_info["Version"]
    zip_url = update_info["Url"]

    # ZIP herunterladen
    temp_zip = tempfile.NamedTemporaryFile(delete=False).name
    download_file(zip_url, temp_zip)

    # ZIP entpacken
    temp_dir = tempfile.mkdtemp()
    print(f"Entpacke ZIP nach: {temp_dir}")

    with zipfile.ZipFile(temp_zip, "r") as zip_ref:
        zip_ref.extractall(temp_dir)

    # Hashes erzeugen
    print("Erzeuge Datei-Hashes...")
    files = {}

    for root, dirs, filenames in os.walk(temp_dir):
        for name in filenames:
            full = os.path.join(root, name)
            rel = os.path.relpath(full, temp_dir)
            files[rel.replace("\\", "/")] = sha256_file(full)

    # Manifest ohne Signatur
    manifest_unsigned = {
        "version": version,
        "files": files
    }

    # 🔥 KANONISCHE BYTES erzeugen
    manifest_bytes = canonical_json(manifest_unsigned)

    # Signieren
    print("Signiere Manifest...")
    private_key = load_private_key()
    signature = private_key.sign(
        manifest_bytes,
        padding.PKCS1v15(),
        hashes.SHA256()
    )

    manifest = manifest_unsigned.copy()
    manifest["signature"] = base64.b64encode(signature).decode()

    # Speichern
    with open(OUTPUT_MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=4, ensure_ascii=False)

    print(f"manifest.json erzeugt: {OUTPUT_MANIFEST}")


if __name__ == "__main__":
    main()
