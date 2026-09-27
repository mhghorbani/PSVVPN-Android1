# PSVVPN Android 0.2 Pilot

Android pilot for PSVVPN / Pishgaman Sepand Viera.

- Endpoint: `89.163.206.27:443`
- Imports `client.json` + `client.pfx` from the identity ZIP using Android Storage Access Framework.
- PFX password is entered at runtime.
- GitHub Actions builds an APK artifact automatically after upload/push.

> Important: this pilot assumes the server tunnel uses mutual TLS and a 4-byte big-endian packet-length framing. Verify against the actual PSVVPN server before production use.
