# LatticeSDK.Templates — Template Authoring Guide

This document explains how to create a **templated entity** for LatticeServer: a reusable blueprint that the server can spawn on demand, simulate with live behavior, and accept tasks from a connected client.

The built-in `simulated-uav` template (in `LatticeTemplateSDK/`) is the reference example for everything described here.

---

## Table of Contents

1. [Template folder structure](#1-template-folder-structure)
2. [entity.json — the entity definition](#2-entityjson--the-entity-definition)
3. [config.json — template configuration](#3-configjson--template-configuration)
4. [behavior.dll — simulated behavior](#4-behaviordll--simulated-behavior)
5. [Custom task protobufs](#5-custom-task-protobufs)
6. [task-configurations.json — UI display configuration](#6-task-configurationsjson--ui-display-configuration)
7. [Building and deploying (desktop)](#7-building-and-deploying-desktop)
8. [Android APK plugins](#8-android-apk-plugins)

---

## 1. Template folder structure

Each template is a **named subfolder** inside the server's template watch directory (`src/LatticeServer/data/templates/` by default). The folder name becomes the template ID.

```
templates/
└── my-template/          ← template ID = "my-template"
    ├── entity.json        ← required
    ├── config.json        ← optional
    ├── behavior.dll       ← optional; compiled from your C# behavior project
    └── task-configurations.json   ← optional; controls how custom tasks appear in the UI
```

The server scans this directory on startup and watches for changes, hot-reloading templates whenever any file changes.

---

## 2. entity.json — the entity definition

`entity.json` is the **entity template**. Every time a new instance is spawned, the server substitutes tokens and parses the result as a Lattice `Entity` protobuf (JSON format).

### Tokens

The following tokens can appear anywhere inside a JSON string value and are replaced at spawn time:

| Token | Replaced with |
|---|---|
| `<new_uuid>` | A new random UUID (e.g. `"3f1a2b4c-…"`) |
| `<unique_number>` | A zero-padded six-digit incrementing integer (e.g. `000001`, `000002`) |
| `<now>` | The current UTC time in RFC 3339 format |
| `<now+Xs>` | The current UTC time plus `X` seconds (e.g. `<now+300s>` = now + 5 minutes) |

Each occurrence of `<new_uuid>` or `<unique_number>` in a single entity.json gets its own independent value, even if the same token appears multiple times.

### Minimal example

```jsonc
{
  "entityId": "<new_uuid>",
  "isLive": true,
  "expiryTime": "<now+300s>",
  "aliases": { "name": "My Entity <unique_number>" },
  "location": {
    "position": {
      "latitudeDegrees": 47.6,
      "longitudeDegrees": -122.2,
      "altitudeHaeMeters": 100.0
    }
  }
}
```

### Declaring accepted task types

To make an entity taskable from the UI, add a `taskCatalog` with the task specification URLs you want to advertise:

```jsonc
"taskCatalog": {
  "taskDefinitions": [
    { "taskSpecificationUrl": "type.googleapis.com/anduril.tasks.v2.Investigate" },
    { "taskSpecificationUrl": "type.googleapis.com/lattice.tasks.v1.FlyTo" }
  ]
}
```

Built-in Lattice task types (e.g. `anduril.tasks.v2.*`) are always available. Custom task types (e.g. `lattice.tasks.v1.*`) must also be registered via `ICustomTaskTypes` in `behavior.dll` (see [section 5](#5-custom-task-protobufs)).

### Common entity fields

The entity JSON follows the Lattice Entity protobuf schema. Some useful fields:

```jsonc
{
  "milView": {
    "disposition": "DISPOSITION_FRIENDLY",   // FRIENDLY, HOSTILE, SUSPECT, NEUTRAL, UNKNOWN
    "environment": "ENVIRONMENT_AIR"          // AIR, LAND, SURFACE, SUBSURFACE, SPACE
  },
  "ontology": {
    "platformType": "PLATFORM_TYPE_UAS"       // UAS, FIXED_WING, ROTARY_WING, GROUND_VEHICLE, …
  },
  "provenance": {
    "integrationName": "LatticeServer-Template",
    "dataType": "Simulation",
    "sourceUpdateTime": "<now>"
  }
}
```

---

## 3. config.json — template configuration

`config.json` controls how the server manages the template instance and passes configuration into the behavior. All fields are optional.

```jsonc
{
  "id": "my-template",           // must match the folder name; used only as a sanity check
  "displayName": "My Template",  // label shown in the Add Entity menu and task wizard UI
  "category": "UAV",             // groups templates together in the Add Entity menu
  "defaultLocation": {           // spawn position when not placed explicitly on the map
    "latitudeDegrees": 47.6147980,
    "longitudeDegrees": -122.1949701,
    "altitudeHaeMeters": 150.0
  },
  "tickIntervalMs": 100,         // how often OnUpdate is called (milliseconds); default 1000
  "custom": {                    // arbitrary key/value pairs passed to OnSpawn as TemplateConfig.Custom
    "speedMps": 15.0,
    "headingDegrees": 90.0
  }
}
```

### Fields

| Field | Type | Default | Description |
|---|---|---|---|
| `id` | string | — | Informational; should match the folder name |
| `displayName` | string | (template ID) | Human-readable name shown in the UI |
| `category` | string | — | Groups entries in the Add Entity dropdown |
| `defaultLocation` | object | — | Lat/lon/alt used when the entity is spawned without a map click |
| `tickIntervalMs` | integer | 1000 | Milliseconds between `OnUpdate` calls |
| `custom` | object | `{}` | Free-form key/value pairs forwarded to `OnSpawn` via `TemplateConfig.Custom` |

The `custom` object is passed to your behavior as `IReadOnlyDictionary<string, JsonElement>`, so values can be any JSON type.

---

## 4. behavior.dll — simulated behavior

`behavior.dll` is a .NET assembly that the server loads at startup (and hot-reloads on change). It defines how entity instances respond to ticks and tasks.

### Project setup

Create a `.csproj` that:
- Sets `<AssemblyName>behavior</AssemblyName>`
- References `LatticeSDK.Templates`
- Optionally references `Google.Protobuf` and `Grpc.Tools` for custom task types

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>behavior</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../LatticeSDK.Templates/LatticeSDK.Templates.csproj" />
  </ItemGroup>
</Project>
```

### Implementing ITaskableEntity

Your behavior class must implement `ITaskableEntity`. The server discovers it via reflection — there must be exactly one non-abstract implementation per DLL.

```csharp
using LatticeSDK.Templates;

public class MyBehavior : ITaskableEntity
{
    private IEntityPublisher _publisher = null!;
    private ITaskReporter _reporter = null!;
    private double _lat, _lon, _altM;

    public void OnSpawn(string entityId, IEntityPublisher publisher,
                        ITaskReporter reporter, IEntityQuerier querier,
                        TemplateConfig config)
    {
        _publisher = publisher;
        _reporter  = reporter;

        // Read spawn position
        var pos = querier.TryGetPosition(entityId);
        _lat  = pos?.LatitudeDegrees  ?? config.DefaultLocation?.LatitudeDegrees  ?? 0;
        _lon  = pos?.LongitudeDegrees ?? config.DefaultLocation?.LongitudeDegrees ?? 0;
        _altM = pos?.AltitudeHaeMeters ?? config.DefaultLocation?.AltitudeHaeMeters ?? 0;

        // Read custom config values
        var speed = config.Custom.TryGetValue("speedMps", out var s) ? s.GetDouble() : 10.0;
    }

    public void OnUpdate(float deltaTimeSecs)
    {
        // Update position…
        _publisher.PublishEntityUpdate(new EntityUpdate(
            Latitude: _lat,
            Longitude: _lon,
            AltitudeHaeMeters: _altM,
            Disposition: null,
            ExtraFieldsJson: null));
    }

    public void OnTaskReceived(TaskPayload task)
    {
        // Parse task.SpecificationJson and begin execution…
        _reporter.ReportStatus(task.TaskId, new TaskStatusUpdate(TaskStatusCode.WillComply));
        _reporter.ReportStatus(task.TaskId, new TaskStatusUpdate(TaskStatusCode.Executing));
    }

    public void OnTaskCancelRequest(string taskId)
    {
        _reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.Cancelled));
    }

    public void OnTaskCompleteRequest(string taskId)
    {
        _reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.DoneOk));
    }

    public void OnDespawn() { }
}
```

### ITaskableEntity lifecycle

| Method | When called |
|---|---|
| `OnSpawn` | Once per instance, immediately after spawn. Inject dependencies and read initial state here. |
| `OnUpdate(deltaTimeSecs)` | Every `tickIntervalMs` milliseconds. Advance simulation and call `PublishEntityUpdate`. |
| `OnTaskReceived(task)` | When a task is delivered (status SENT). Parse `task.SpecificationJson` and begin executing. |
| `OnTaskCancelRequest(taskId)` | When the task manager requests cancellation. Stop execution and report `Cancelled`. |
| `OnTaskCompleteRequest(taskId)` | When the task manager requests explicit completion. Stop and report `DoneOk`. |
| `OnDespawn` | Just before the instance is removed. Clean up any external resources. |

### IEntityPublisher — publishing state

Call `PublishEntityUpdate` from `OnUpdate` to push position and state to all connected clients:

```csharp
publisher.PublishEntityUpdate(new EntityUpdate(
    Latitude: 47.6,
    Longitude: -122.2,
    AltitudeHaeMeters: 150.0,
    Disposition: "DISPOSITION_FRIENDLY",   // null = leave unchanged
    ExtraFieldsJson: null));               // see below
```

`EntityUpdate` fields:

| Field | Type | Description |
|---|---|---|
| `Latitude` | `double?` | Latitude in degrees. `null` = leave unchanged. |
| `Longitude` | `double?` | Longitude in degrees. `null` = leave unchanged. |
| `AltitudeHaeMeters` | `double?` | Altitude (HAE) in metres. `null` = leave unchanged. |
| `Disposition` | `string?` | Protobuf enum string, e.g. `"DISPOSITION_FRIENDLY"`. `null` = leave unchanged. |
| `ExtraFieldsJson` | `string?` | JSON object merged into the entity at the top level for fields not covered above (e.g. signal strength, fuel level). `null` = no extra fields. |

### ITaskReporter — reporting task status

```csharp
reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.Executing));
reporter.ReportStatus(taskId, new TaskStatusUpdate(TaskStatusCode.DoneOk));
reporter.ReportStatus(taskId, new TaskStatusUpdate(
    TaskStatusCode.DoneNotOk,
    ErrorMessage: "Navigation failure"));
```

`TaskStatusCode` values map 1:1 to the Lattice `Status` proto enum:

| Code | Meaning |
|---|---|
| `Ack` | Task received and acknowledged |
| `WillComply` | Entity has accepted the task |
| `Executing` | Task is actively being executed |
| `WaitingForUpdate` | Execution paused, awaiting input |
| `DoneOk` | Task completed successfully |
| `DoneNotOk` | Task failed; set `ErrorMessage` for details |
| `Rejected` | Entity refused the task |
| `Cancelled` | Task was cancelled |

### IEntityQuerier — querying other entities

```csharp
var pos = querier.TryGetPosition(otherEntityId);
if (pos != null)
{
    double lat = pos.LatitudeDegrees;
    double lon = pos.LongitudeDegrees;
    double? alt = pos.AltitudeHaeMeters;
}
```

Returns `null` if the entity does not exist or has no position data.

### Parsing task specification JSON

Tasks arrive as raw JSON in `task.SpecificationJson`. The JSON structure follows the protobuf message for the task type, serialised without the `@type` wrapper field. Example for a standard Investigate task:

```json
{
  "objective": {
    "entityId": "abc123"
  }
}
```

Example for a custom `FlyTo` task (see [section 5](#5-custom-task-protobufs)):

```json
{
  "latitudeDegrees": 47.62,
  "longitudeDegrees": -122.19,
  "altitudeHaeMeters": 200.0
}
```

---

## 5. Custom task protobufs

If your entity accepts task types that are not part of the built-in Lattice task catalog, you must define them as protobuf messages and register them with the server.

### 1. Define a .proto file

Place proto files under a `protos/` subdirectory in your behavior project. Follow the standard proto path convention:

```
protos/
└── my_org/tasks/v1/my_task.proto
```

```proto
syntax = "proto3";

package my_org.tasks.v1;

option csharp_namespace = "MyOrg.Tasks.V1";

message MyTask {
  double latitude_degrees  = 1;
  double longitude_degrees = 2;
  string mode              = 3;
}
```

### 2. Add to your .csproj

```xml
<ItemGroup>
  <PackageReference Include="Google.Protobuf" Version="3.27.2" />
  <PackageReference Include="Grpc.Tools"      Version="2.64.0" PrivateAssets="All" />
</ItemGroup>

<ItemGroup>
  <Protobuf Include="protos/**/*.proto" GrpcServices="None" ProtoRoot="protos" />
</ItemGroup>
```

### 3. Implement ICustomTaskTypes

Your behavior class (or a separate class in the same DLL) must implement `ICustomTaskTypes`. It must have a **public parameterless constructor**.

```csharp
using Google.Protobuf.Reflection;
using LatticeSDK.Templates;

public class MyBehavior : ITaskableEntity, ICustomTaskTypes
{
    public IEnumerable<MessageDescriptor> GetTaskDescriptors()
    {
        yield return MyOrg.Tasks.V1.MyTask.Descriptor;
    }

    // … ITaskableEntity implementation …
}
```

The server calls `GetTaskDescriptors()` once at load time and registers the returned types so that `google.protobuf.Any` JSON serialisation works correctly when tasks are sent or stored.

### 4. Declare the type URL in entity.json

Add the full type URL to `taskCatalog.taskDefinitions`:

```jsonc
{ "taskSpecificationUrl": "type.googleapis.com/my_org.tasks.v1.MyTask" }
```

The type URL is always `type.googleapis.com/` followed by the proto package + message name.

---

## 6. task-configurations.json — UI display configuration

`task-configurations.json` controls how custom task types appear in the LatticeServer web UI. Without this file, the UI auto-derives a basic form from the proto field definitions. Use field overrides to customise labels, hide raw fields, and add map-pick UI for geographic inputs.

The file must be a JSON **array**, with one entry per task type you want to configure.

### Top-level entry fields

```jsonc
[
  {
    "typeUrl": "type.googleapis.com/my_org.tasks.v1.MyTask",  // required; must match a registered type
    "displayName": "My Task",          // label shown in the task type picker
    "description": "Does a thing.",    // subtitle shown below the display name
    "executionTag": "Executing Task",  // short label shown on the entity card while task is running
    "fieldOverrides": { … }            // field-level customisation (see below)
  }
]
```

### Field overrides

`fieldOverrides` is an object whose keys are **proto field names** (snake_case as defined in your `.proto` file) or **virtual field keys** (prefixed with `_` for composite UI widgets not backed by a single proto field).

Each value is an object with any combination of these properties:

| Property | Type | Description |
|---|---|---|
| `label` | string | Human-readable label shown above the field in the form |
| `type` | string | UI field type (see [Field types](#field-types) below) |
| `required` | boolean | Shows a `*` indicator and prevents submission if unset |
| `group` | string | Groups fields under a collapsible section heading |
| `hidden` | boolean | Hides the field from the UI entirely (useful for fields exposed via a composite widget) |
| `latKey` | string | For `objective_point` fields: the proto field name to write the latitude into |
| `lonKey` | string | For `objective_point` fields: the proto field name to write the longitude into |
| `altKey` | string | For `objective_point` fields: the proto field name to write the altitude into |

### Field types

| Type | Description |
|---|---|
| `float_opt` | Optional floating-point number input. Auto-derived for `float` and `double` proto fields. |
| `int32_opt` | Optional 32-bit signed integer input. Auto-derived for `int32`, `sint32`, `sfixed32`, `fixed32`. |
| `uint32_opt` | Optional 32-bit unsigned integer input. Auto-derived for `uint32`. |
| `int64_opt` | Optional 64-bit signed integer input. Auto-derived for `int64`, `sint64`, `sfixed64`, `fixed64`. |
| `uint64_opt` | Optional 64-bit unsigned integer input. Auto-derived for `uint64`. |
| `string` | Text input. Auto-derived for `string` proto fields. |
| `objective` | Entity or map-point picker. Lets the user select an entity from a dropdown or click the map. Writes a `google.protobuf.Any` objective payload. |
| `objective_point` | Map-point-only picker (no entity selection). The user clicks the map or an entity marker; the lat/lon/alt are written into the proto fields named by `latKey`/`lonKey`/`altKey`. |

### Auto-derivation

Fields not mentioned in `fieldOverrides` (and not hidden) are auto-derived from the proto descriptor:

- Scalar numeric and string fields get a matching `_opt` or `string` input.
- Message fields and repeated fields are not surfaced automatically; use overrides if you need them.

### Example: hiding raw lat/lon fields and replacing with a map picker

This is the pattern used by the built-in `FlyTo` task:

```jsonc
[
  {
    "typeUrl": "type.googleapis.com/lattice.tasks.v1.FlyTo",
    "displayName": "Fly To",
    "description": "Fly the UAV to a specific geographic position.",
    "executionTag": "Flying",
    "fieldOverrides": {
      "latitude_degrees":  { "hidden": true },
      "longitude_degrees": { "hidden": true },
      "altitude_hae_meters": { "hidden": true },
      "_location": {
        "label":    "Destination",
        "type":     "objective_point",
        "required": true,
        "latKey":   "latitude_degrees",
        "lonKey":   "longitude_degrees",
        "altKey":   "altitude_hae_meters"
      }
    }
  }
]
```

Here, the three raw proto fields are hidden from the form and a single `_location` virtual field of type `objective_point` is added. When the user clicks the map or an entity marker, the server writes the resolved coordinates directly into `latitude_degrees`, `longitude_degrees`, and `altitude_hae_meters` on the task spec.

---

## 7. Building and deploying (desktop)

The recommended approach is to build your behavior project with the same `DeployTemplate` MSBuild target used by `LatticeTemplateSDK`:

```xml
<Target Name="DeployTemplate" AfterTargets="Build">
  <PropertyGroup>
    <_TemplateDir>$(TemplatesDir)\$(TemplateId)</_TemplateDir>
  </PropertyGroup>
  <MakeDir Directories="$(_TemplateDir)" />
  <Copy SourceFiles="$(OutputPath)behavior.dll"        DestinationFolder="$(_TemplateDir)" SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(OutputPath)behavior.pdb"        DestinationFolder="$(_TemplateDir)" SkipUnchangedFiles="true"
        Condition="Exists('$(OutputPath)behavior.pdb')" />
  <Copy SourceFiles="$(MSBuildThisFileDirectory)entity.json" DestinationFolder="$(_TemplateDir)" SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(MSBuildThisFileDirectory)config.json" DestinationFolder="$(_TemplateDir)" SkipUnchangedFiles="true" />
  <Copy SourceFiles="$(MSBuildThisFileDirectory)task-configurations.json"
        DestinationFolder="$(_TemplateDir)"
        SkipUnchangedFiles="true"
        Condition="Exists('$(MSBuildThisFileDirectory)task-configurations.json')" />
</Target>
```

Set `TemplateId` and `TemplatesDir` as properties (either in the project or via the command line):

```bash
dotnet build -p:TemplateId=my-template -p:TemplatesDir=../src/LatticeServer/data/templates
```

The server hot-reloads the template immediately after the files are copied. If the server is already running, you can simply rebuild to see changes without restarting.

---

## 8. Android APK plugins

On Android, templates are distributed as APKs rather than folder-based packages. The same four files (`entity.json`, `config.json`, `behavior.dll`, `task-configurations.json`) are bundled into the APK's `assets/` directory. LatticeServer's `ApkTemplateSource` discovers plugins by scanning installed packages for a manifest metadata flag and loads them using the same `ZipTemplateReader` used for desktop ZIPs.

> **Starting point:** The `LatticePluginTemplate.Android/` project in this repository is a ready-to-use template. Copy it and follow the steps below to create your own plugin.

### Project setup

Your plugin is a standard `.NET Android` project. The key differences from the desktop template:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-android</TargetFramework>
    <OutputType>Exe</OutputType>
    <ApplicationId>com.example.my_plugin</ApplicationId>
    <AndroidManifest>Platforms/Android/AndroidManifest.xml</AndroidManifest>
    <SupportedOSPlatformVersion>34</SupportedOSPlatformVersion>

    <!-- DLL is loaded at runtime by LatticeServer — AOT must be off -->
    <PublishAot>false</PublishAot>
    <RunAOTCompilation>false</RunAOTCompilation>

    <!-- Must be "behavior" so ZipTemplateReader finds it by name -->
    <AssemblyName>behavior</AssemblyName>

    <TemplateId>my-plugin</TemplateId>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../LatticeSDK.Templates/LatticeSDK.Templates.csproj" />
  </ItemGroup>

  <!-- Bundle template files as APK assets -->
  <ItemGroup>
    <AndroidAsset Include="entity.json" />
    <AndroidAsset Include="config.json" Condition="Exists('config.json')" />
    <AndroidAsset Include="task-configurations.json" Condition="Exists('task-configurations.json')" />
  </ItemGroup>
</Project>
```

> **Note:** `behavior.dll` does not need to be listed as an `AndroidAsset` — the .NET Android SDK automatically bundles all compiled assemblies into the APK. `ZipTemplateReader` locates it at `assets/behavior.dll` inside the APK ZIP.

### AndroidManifest.xml

Declare two `<meta-data>` entries so LatticeServer can discover your plugin:

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
  <application android:label="My Plugin" android:hasCode="false">
    <!-- Discovery flag: LatticeServer scans for this key -->
    <meta-data
        android:name="com.lattice.template"
        android:value="true" />
    <!-- Template ID: must be unique across all installed plugins -->
    <meta-data
        android:name="com.lattice.template.id"
        android:value="my-plugin" />
  </application>
</manifest>
```

`android:hasCode="false"` is correct for a plugin APK — there is no Java/Kotlin code. The `behavior.dll` is loaded by LatticeServer's process, not the plugin APK's own runtime.

The template ID declared here must match the `TemplateId` property in your `.csproj` and the `templateId` fields in your `entity.json` / `config.json`.

### Template files

The same `entity.json`, `config.json`, `behavior.dll`, and `task-configurations.json` files described in sections 2–6 apply unchanged. Place them at the root of your project directory (alongside the `.csproj`) and they will be bundled into `assets/` automatically.

### Behavior DLL

Your behavior class is identical to the desktop version — implement `ITaskableEntity` (and optionally `ICustomTaskTypes`) exactly as described in sections 4 and 5. No Android-specific code is needed in the behavior class itself.

### Building and deploying

```bash
# Build the APK
dotnet build -p:TemplateId=my-plugin -p:ApplicationId=com.example.my_plugin

# Install on the running emulator
dotnet build -t:Install -p:AdbTarget="-e"

# Install on a physical device
dotnet build -t:Install -p:AdbTarget="-d"
```

Once the APK is installed, LatticeServer detects it automatically via `PackageManager` and loads the template without a server restart. Reinstalling or updating the APK triggers a hot-reload: the old template is unloaded and the new one is loaded in place.

### Hot-reload behavior

| Event | Server response |
|-------|----------------|
| APK installed (new plugin) | Template loaded, available for spawn |
| APK updated (reinstall) | Old template unloaded, new template loaded |
| APK uninstalled | Template unloaded, all running instances continue until despawned |

### Troubleshooting

- **Template not discovered:** Check that both `com.lattice.template` and `com.lattice.template.id` are present in `AndroidManifest.xml` and that the APK is fully installed (`adb shell pm list packages | grep your.package.name`).
- **`entity.json` missing:** LatticeServer logs a warning and skips the APK. Verify the file is listed as `<AndroidAsset>` and that the asset name matches exactly (`entity.json`, lowercase).
- **DLL load failure:** Ensure `PublishAot=false` and `RunAOTCompilation=false` in your `.csproj`. Dynamic assembly loading is incompatible with AOT.
