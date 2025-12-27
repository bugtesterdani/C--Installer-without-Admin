from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes, serialization


PRIVATE_KEY_PATH = "private.pem"
PUBLIC_KEY_PATH = "public.pem"


def load_private_key():
    with open(PRIVATE_KEY_PATH, "rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


def load_public_key():
    with open(PUBLIC_KEY_PATH, "rb") as f:
        return serialization.load_pem_public_key(f.read())


def main():
    message = b"test-message-123"

    print("Lade Schlüssel...")
    priv = load_private_key()
    pub = load_public_key()

    print("Signiere Test-Nachricht...")
    sig = priv.sign(
        message,
        padding.PKCS1v15(),
        hashes.SHA256()
    )

    print("Verifiziere Signatur mit Public Key...")
    try:
        pub.verify(
            sig,
            message,
            padding.PKCS1v15(),
            hashes.SHA256()
        )
        print("✔ Schlüssel passen zueinander (Public/Private korrekt).")
    except Exception as e:
        print("❌ Schlüssel passen NICHT zueinander!")
        print("Fehler:", e)


if __name__ == "__main__":
    main()
