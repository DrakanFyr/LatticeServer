# LatticeServer
This is a [Visual Studio Code](https://code.visualstudio.com/) project. It implements the Anduril Lattice API (Entity Management and Task Management) as a local mock server for development and testing, exposing both gRPC and REST endpoints.

## Prerequisites

| Tool | Minimum Version | Notes |
|------|----------------|-------|
| [Visual Studio Code](https://code.visualstudio.com/download) | Latest | Primary IDE |
| [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) (VS Code extension) | Latest | Required for C# support, debugging, and test explorer |
| [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | 10.0 | Required to build and run the server |
| [OpenSSL](https://www.openssl.org/) | 1.1.1+ | Required to generate local development certificates (see below). On macOS install via `brew install openssl`; on Windows use [Win64 OpenSSL](https://slproweb.com/products/Win32OpenSSL.html) or the version bundled with Git for Windows |

## Repository Structure

```
LatticeServer/
├── LatticeSDK/                    # Protobuf definitions and API documentation
│   ├── protos/
│   └── docs/
├── src/
│   ├── LatticeServer/             # Main ASP.NET Core server
│   ├── LatticeServer.Tests/       # xUnit integration tests
│   ├── LatticeClient/             # Example gRPC client library
│   └── LatticeClientTest/         # Client test suite
└── LatticeServer.sln
```

## Quick Start

### 1. Build

```bash
dotnet build
```

### 2. Generate and trust a local certificate

Follow the [Trusted Local Certificate Setup](#trusted-local-certificate-setup) section below before running for the first time. The HTTPS endpoint will not start without a valid `localhost.p12` at the repository root.

### 3. Run

```bash
dotnet run --project src/LatticeServer/LatticeServer.csproj
```

The server starts two endpoints:

| Protocol | URL | Supported protocols |
|----------|-----|---------------------|
| HTTP | `http://localhost:5007` | HTTP/1.1 |
| HTTPS | `https://localhost:7087` | HTTP/1.1 + HTTP/2 (gRPC) |

A web dashboard is served at `http://localhost:5007` in your browser once the server is running.

### 4. Run the tests

```bash
dotnet test
```

## Configuration

### `appsettings.json`

| Key | Default | Description |
|-----|---------|-------------|
| `ScenarioConfigPath` | `scenarios.json` | Path to the scenario file (relative to the working directory) |
| `Kestrel.Endpoints.Http.Url` | `http://localhost:5007` | HTTP endpoint |
| `Kestrel.Endpoints.Https.Url` | `https://localhost:7087` | HTTPS/gRPC endpoint |
| `Kestrel.Endpoints.Https.Certificate.Path` | `../../localhost.p12` | Path to the PKCS#12 certificate (relative to the project directory) |
| `Kestrel.Endpoints.Https.Certificate.Password` | `changeit` | Certificate password — matches the `-passout pass:changeit` used when generating the cert |

### `scenarios.json`

Scenarios define entities that the server automatically spawns and keeps alive at startup. The active scenario is selected by the `activeScenario` key.

```jsonc
{
  "activeScenario": "default",   // which scenario to load on startup
  "scenarios": {
    "my-scenario": {
      "name": "...",
      "description": "...",
      "entities": [
        {
          "entityId": "...",            // stable UUID for this entity
          "template": "Track|Asset",   // entity template type
          "name": "...",
          "latitude": 0.0,
          "longitude": 0.0,
          "expirySeconds": 15,         // how long before the entity expires if not refreshed
          "refreshIntervalSeconds": 5, // how often the server re-publishes the entity
          // asset-only fields:
          "taskSpecificationUrls": []  // task types this asset can accept
        }
      ]
    }
  }
}
```

The default scenario ships with a simulated ground track and a simulated UAS asset, both positioned near Seattle.

## API Overview

The server exposes two API styles over the same endpoints:

| Style | Transport | Use case |
|-------|-----------|----------|
| **gRPC** | HTTP/2 (HTTPS only) | Primary — matches the production Lattice API contract |
| **REST** | HTTP/1.1 or HTTPS | Convenience — JSON wrappers around the same underlying stores |

**gRPC services:**
- `anduril.entitymanager.v1.EntityManagerAPI` — publish, get, override, and stream entities
- `anduril.taskmanager.v1.TaskManagerAPI` — create, query, update, cancel, and stream tasks

**REST controllers** mirror the same operations and use Server-Sent Events (SSE) for streaming responses.

Full API documentation and OpenAPI specs are in `LatticeSDK/docs/`.

## Trusted Local Certificate Setup

Steps 1 and 2 are the same on all platforms.

### 1. Generate a self-signed cert + private key

```
openssl req -x509 -newkey rsa:2048 -days 365 -nodes -keyout localhost.key -out localhost.crt -subj "/CN=localhost" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
```

### 2. Package into a .p12 (PKCS#12) file with the password from appsettings.json

```
openssl pkcs12 -export -out localhost.p12 -inkey localhost.key -in localhost.crt -passout pass:changeit
```

### 3. Trust the certificate

#### Windows

Run in PowerShell:

```powershell
Import-Certificate -FilePath localhost.crt -CertStoreLocation Cert:\CurrentUser\Root
```

#### macOS

Add to the system keychain and mark as trusted:

```bash
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain localhost.crt
```

> **Note for Python:** Python on macOS (especially when installed via Homebrew or pyenv) does not always read from the system keychain. The most reliable approach is to append the cert to Python's CA bundle via `certifi`:
>
> ```bash
> cat localhost.crt >> $(python -c "import certifi; print(certifi.where())")
> ```
>
> You may need to re-run this after upgrading `certifi`. Alternatively, set the `SSL_CERT_FILE` environment variable (see below).

#### Linux

Copy the cert to the system CA store and update it. The exact path depends on your distro:

**Debian / Ubuntu:**
```bash
sudo cp localhost.crt /usr/local/share/ca-certificates/localhost.crt
sudo update-ca-certificates
```

**RHEL / CentOS / Fedora:**
```bash
sudo cp localhost.crt /etc/pki/ca-trust/source/anchors/localhost.crt
sudo update-ca-trust
```

> **Note for Python:** Python reads the system CA bundle, so the above steps should be sufficient. If it still doesn't trust the cert, verify Python is using the updated bundle:
>
> ```bash
> python -c "import ssl; print(ssl.get_default_verify_paths())"
> ```

#### All platforms — Python fallback via environment variable

If the system-level steps above don't work for your Python environment, you can point Python's ssl module directly at the cert by setting this environment variable before running your script:

```bash
export SSL_CERT_FILE=/path/to/localhost.crt
```

Or to merge it with the existing system bundle:

```bash
export SSL_CERT_FILE=$(python -c "import ssl; print(ssl.get_default_verify_paths().cafile or ssl.get_default_verify_paths().capath)")
cat localhost.crt >> "$SSL_CERT_FILE"
```
