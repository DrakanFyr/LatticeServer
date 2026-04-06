# LatticeServer
This is a [Visual Studio Code](https://code.visualstudio.com/) project. It implements the Anduril Lattice API (Entity Management and Task Management) as a local mock server for development and testing, exposing both gRPC and REST endpoints.

## Prerequisites

### Desktop

| Tool | Minimum Version | Notes |
|------|----------------|-------|
| [Visual Studio Code](https://code.visualstudio.com/download) | Latest | Primary IDE |
| [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) (VS Code extension) | Latest | Required for C# support, debugging, and test explorer |
| [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | 10.0 | Required to build and run the server |
| [OpenSSL](https://www.openssl.org/) | 1.1.1+ | Required to generate local development certificates (see below). On macOS install via `brew install openssl`; on Windows use [Win64 OpenSSL](https://slproweb.com/products/Win32OpenSSL.html) or the version bundled with Git for Windows |

### Android

| Tool | Notes |
|------|-------|
| .NET Android workload | `dotnet workload install android` |
| Android SDK platform 36.1 | Install via Android Studio SDK Manager or `sdkmanager "platforms;android-36"` |
| Android emulator or physical device (API 34+) | `FOREGROUND_SERVICE_SPECIAL_USE` requires API 34 minimum |

## Repository Structure

```
LatticeServer/
├── LatticeSDK/                        # Protobuf definitions and API documentation
│   ├── protos/
│   └── docs/
├── LatticeSDK.Templates/              # SDK for authoring templated entities (behavior DLLs)
├── LatticeTemplateSDK/                # Reference template — simulated UAV (desktop)
├── LatticePluginTemplate.Android/     # Reference template — Android APK plugin
├── src/
│   ├── LatticeServer/                 # Core ASP.NET Core server library
│   ├── LatticeServer.Desktop/         # Desktop host (thin exe, entry point for dotnet run)
│   ├── LatticeServer.Android/         # Android host (foreground service + APK plugin discovery)
│   ├── LatticeServer.Tests/           # xUnit integration tests
│   ├── LatticeClient/                 # Example gRPC client library
│   └── LatticeClientTest/             # Client test suite
└── LatticeServer.sln
```

### Templated Entities

LatticeServer supports **templated entities** — reusable entity blueprints that can be spawned from the UI, simulated with live C# behavior, and assigned tasks. See the [Template Authoring Guide](LatticeSDK.Templates/README.md) for full documentation on:

- The template folder structure (`entity.json`, `config.json`, `behavior.dll`, `task-configurations.json`)
- Entity JSON tokens (`<new_uuid>`, `<now+Xs>`, etc.)
- Writing a behavior DLL with `ITaskableEntity`
- Defining custom task types with protobuf and `ICustomTaskTypes`
- Customising the task form UI with `task-configurations.json`

The `LatticeTemplateSDK/` project is the reference desktop implementation (a simulated UAV that navigates to task objectives). The `LatticePluginTemplate.Android/` project is the equivalent starting point for Android APK plugins.

## Quick Start

### Desktop

#### 1. Build

```bash
dotnet build
```

#### 2. Generate and trust a local certificate

Follow the [Trusted Local Certificate Setup](#trusted-local-certificate-setup) section below before running for the first time. The HTTPS endpoint will not start without a valid `localhost.p12` at the repository root.

#### 3. Run

```bash
dotnet run --project src/LatticeServer.Desktop/LatticeServer.Desktop.csproj
```

The server starts two endpoints:

| Protocol | URL | Supported protocols |
|----------|-----|---------------------|
| HTTP | `http://localhost:5007` | HTTP/1.1 |
| HTTPS | `https://localhost:7087` | HTTP/1.1 + HTTP/2 (gRPC) |

A web dashboard is served at `http://localhost:5007` in your browser once the server is running.

#### 4. Run the tests

```bash
dotnet test
```

### Android

#### 1. Build the APK

```bash
dotnet build src/LatticeServer.Android/LatticeServer.Android.csproj
```

#### 2. Deploy to emulator or device

```bash
dotnet build src/LatticeServer.Android/LatticeServer.Android.csproj \
  -t:Install -p:AdbTarget="-e"
```

`-p:AdbTarget="-e"` targets the running emulator. Use `-p:AdbTarget="-d"` for a physical device, or `-p:AdbTarget="-s <serial>"` for a specific device.

#### 3. Launch

Open the **Lattice Server** app on the device and tap **Start Server**. The server starts a foreground service and begins listening on port 5007. The status line shows `running on port 5007` once ready.

#### 4. Connect clients

The Android server listens on `http://0.0.0.0:5007` using HTTP/2 cleartext (h2c). See [Connecting clients to the Android server](#connecting-clients-to-the-android-server) for client-specific setup.

#### 5. Install template plugins

Template plugins are distributed as APKs. Install a plugin APK on the same device and the server picks it up automatically — no restart required. See the [Android Plugin Authoring Guide](LatticeSDK.Templates/README.md#8-android-apk-plugins) for how to build a plugin.

## Connecting clients to the Android server

The Android server uses **h2c (HTTP/2 cleartext)** on port 5007. TLS is unnecessary for loopback traffic and adds certificate management complexity on Android. Clients connecting to the Android-hosted server need a one-time configuration change to allow unencrypted HTTP/2.

| Client | Required change |
|--------|----------------|
| **.NET** (`Grpc.Net.Client`) | `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` before creating any channel, then `GrpcChannel.ForAddress("http://<device-ip>:5007")` |
| **Go** | `grpc.Dial("device-ip:5007", grpc.WithTransportCredentials(insecure.NewCredentials()))` |
| **Python** | `grpc.insecure_channel("device-ip:5007")` |
| **REST** | No change — plain HTTP works as before |

> **Emulator note:** When connecting from the host machine to a running emulator, use `http://localhost:5007` (adb reverse maps device port 5007 to the host). When connecting from another device on the same network, use the device's LAN IP address.

To set up adb reverse port forwarding from the emulator to the host:

```bash
adb reverse tcp:5007 tcp:5007
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
