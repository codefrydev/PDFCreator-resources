---
name: frypdf-plugin-authoring
description: Expert workflow and guidance for authoring, developing, debugging, packaging, and cataloging FryPDF plugins with standalone Avalonia runners, Material Design 3 Expressive UI, and .fryplugin distribution archives. Use when creating new plugins, modifying existing plugins, adding standalone runners, or publishing plugins to the marketplace catalog.
version: 1.0
---

# FryPDF Plugin Authoring & Marketplace Integration Skill

This skill provides step-by-step instructions, templates, and best practices for creating, maintaining, testing, and distributing plugins for **FryPDF**.

---

## 1. Plugin Architecture Overview

FryPDF plugins are self-contained modular extensions mounted onto the host application's microkernel via `IFryPluginContext`.

### Workspace Layout
- **`examples/<PluginName>Plugin/`**: The complete plugin source code, ViewModel, XAML view, manifest (`plugin.json`), and standalone `Runner/` subproject.
- **`plugins/<id>/`**: The distribution directory holding `<Name>.fryplugin`, `plugin.json`, and `README.md`.
- **`plugins/catalog.json`**: Central marketplace registry consumed by the host application's Plugins Studio dialog.

---

## 2. Step-by-Step Workflow: Creating a New Plugin

### Step 1: Initialize Plugin Directory Structure
```bash
mkdir -p examples/MyPlugin/Runner
```

### Step 2: Create Modern XML Solution (`MyPlugin.slnx`)
Create `examples/MyPlugin/MyPlugin.slnx`:
```xml
<Solution>
  <Project Path="MyPlugin.csproj" />
  <Project Path="Runner/MyPlugin.Runner.csproj" />
</Solution>
```

### Step 3: Configure Dual-Mode Project File (`MyPlugin.csproj`)
Create `examples/MyPlugin/MyPlugin.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>13</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AssemblyName>MyPlugin</AssemblyName>
    <RootNamespace>PdfEditorApp.Plugins.MyFeature</RootNamespace>
    <Version>1.0.0</Version>
    <Company>FryPDF Team</Company>
    <Description>Feature description</Description>
    <DefaultItemExcludes>$(DefaultItemExcludes);Runner/**</DefaultItemExcludes>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <!-- Dual-mode build: in-tree repo references or standalone installed app references -->
  <Choose>
    <When Condition="Exists('..\..\..\PDFCreator\src\PdfEditorApp.Core\PdfEditorApp.Core.csproj')">
      <ItemGroup>
        <ProjectReference Include="..\..\..\PDFCreator\src\PdfEditorApp.Core\PdfEditorApp.Core.csproj">
          <Private>false</Private>
          <ExcludeAssets>runtime</ExcludeAssets>
        </ProjectReference>
        <ProjectReference Include="..\..\..\PDFCreator\src\PdfEditorApp\PdfEditorApp.csproj">
          <Private>false</Private>
          <ExcludeAssets>runtime</ExcludeAssets>
        </ProjectReference>
      </ItemGroup>
    </When>
    <Otherwise>
      <PropertyGroup>
        <FryPdfInstallDir Condition="'$(FryPdfInstallDir)' == '' and '$(FRYPDF_HOME)' != ''">$(FRYPDF_HOME)</FryPdfInstallDir>
        <FryPdfInstallDir Condition="'$(FryPdfInstallDir)' == '' and Exists('/Applications/FryPDF.app/Contents/MacOS')">/Applications/FryPDF.app/Contents/MacOS</FryPdfInstallDir>
        <FryPdfInstallDir Condition="'$(FryPdfInstallDir)' == '' and Exists('C:\Program Files\FryPDF')">C:\Program Files\FryPDF</FryPdfInstallDir>
        <FryPdfInstallDir Condition="'$(FryPdfInstallDir)' == '' and Exists('/opt/FryPDF')">/opt/FryPDF</FryPdfInstallDir>
      </PropertyGroup>
      <ItemGroup>
        <Reference Include="PdfEditorApp.Core">
          <HintPath>$(FryPdfInstallDir)\PdfEditorApp.Core.dll</HintPath>
          <Private>false</Private>
        </Reference>
        <Reference Include="PdfEditorApp">
          <HintPath>$(FryPdfInstallDir)\PdfEditorApp.dll</HintPath>
          <Private>false</Private>
        </Reference>
      </ItemGroup>
    </Otherwise>
  </Choose>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.1" PrivateAssets="all" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" PrivateAssets="all" />
    <PackageReference Include="Material.Icons.Avalonia" Version="3.0.2" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <!-- Packaging into .fryplugin distribution archive -->
  <Target Name="PackageFryPlugin" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
    <PropertyGroup>
      <PluginStagingDir>$(TargetDir)staging\</PluginStagingDir>
      <OutputFryPlugin>$(TargetDir)MyPlugin.fryplugin</OutputFryPlugin>
    </PropertyGroup>
    <RemoveDir Directories="$(PluginStagingDir)" />
    <MakeDir Directories="$(PluginStagingDir)" />
    <ItemGroup>
      <PluginFiles Include="$(TargetDir)MyPlugin.dll" />
      <PluginFiles Include="$(ProjectDir)plugin.json" />
    </ItemGroup>
    <Copy SourceFiles="@(PluginFiles)" DestinationFolder="$(PluginStagingDir)" />
    <Delete Files="$(OutputFryPlugin)" Condition="Exists('$(OutputFryPlugin)')" />
    <ZipDirectory SourceDirectory="$(PluginStagingDir)" DestinationFile="$(OutputFryPlugin)" />
    <RemoveDir Directories="$(PluginStagingDir)" />
    <Message Importance="High" Text="✨ Successfully packaged FryPDF plugin: $(OutputFryPlugin)" />
  </Target>
</Project>
```

### Step 4: Configure Standalone Runner
Create `examples/MyPlugin/Runner/MyPlugin.Runner.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>13</LangVersion>
    <AssemblyName>MyPlugin.Runner</AssemblyName>
    <RootNamespace>PdfEditorApp.Plugins.MyFeature.Runner</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyPlugin.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.1" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.1" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.1" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.1" />
    <PackageReference Include="Material.Icons.Avalonia" Version="3.0.2" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.11" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.11" />
  </ItemGroup>
</Project>
```

Add `Program.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow.axaml`, and `StandaloneSettingsStore.cs` in `Runner/` modeled on `examples/SnakePlugin/Runner/`.

### Step 5: Author Manifest (`plugin.json`)
```json
{
  "id": "frypdf.overlay.myplugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "author": "FryPDF Team",
  "description": "Interactive floating overlay feature for FryPDF.",
  "entryPoint": "MyPlugin.dll",
  "icon": "Extension",
  "dependencies": [],
  "settingsSchema": {}
}
```

### Step 6: Implement Plugin Class (`MyPlugin.cs`)
```csharp
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using PdfEditorApp.Core.Plugins;
using PdfEditorApp.Core.Plugins.Context;

namespace PdfEditorApp.Plugins.MyFeature;

public class MyPlugin : IFryPlugin
{
    public string Id => "frypdf.overlay.myplugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    public IReadOnlyList<string> RequiredServices => [];

    public Task ApplyAsync(IFryPluginContext ctx, CancellationToken ct = default)
    {
        var vm = new MyPluginViewModel(ctx.Services);
        var view = new MyPluginView { DataContext = vm };

        // Mount to shell.overlay slot
        ctx.RegisterShellOverlaySlot("myplugin", view);

        // Register LIFO cleanup
        ctx.RegisterEffect(() =>
        {
            vm.Dispose();
        });

        return Task.CompletedTask;
    }
}
```

---

## 3. Packaging & Marketplace Publishing

Use the automation tool to compile, package, stage, and update catalog metadata in one step:

```bash
# Package a specific plugin
python3 tools/package_plugin.py MyPlugin

# Or package all plugins
python3 tools/package_plugin.py --all
```

### Manual Catalog Registration (`plugins/catalog.json`)
Ensure an entry exists in `plugins/catalog.json` with:
```json
{
  "id": "frypdf.overlay.myplugin",
  "name": "My Plugin",
  "publisher": "FryPDF Team",
  "version": "1.0.0",
  "category": "UI & Extensions",
  "description": "Short description",
  "longDescription": "Extended markdown description with key features...",
  "rating": 5.0,
  "ratingCount": 10,
  "installCount": 100,
  "formattedSize": "20 KB",
  "iconKind": "Extension",
  "iconColorHex": "#3B82F6",
  "license": "MIT",
  "isVerified": true,
  "isOfficial": true,
  "downloadUrl": "https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main/plugins/frypdf.overlay.myplugin/MyPlugin.fryplugin",
  "tags": ["overlay", "widget"],
  "highlights": ["Key highlight 1", "Key highlight 2"],
  "contributedFeatures": ["Shell Overlay: My Plugin Floating Card"],
  "dependencies": ["PdfEditorApp.Core >= 1.0.0"]
}
```

---

## 4. Verification Commands

Always run the full validation suite:

```bash
# 1. Build plugin solution (Plugin + Runner)
dotnet build examples/MyPlugin/MyPlugin.slnx

# 2. Package and stage
python3 tools/package_plugin.py MyPlugin

# 3. Validate entire ecosystem integrity
python3 tools/validate_plugins.py
```
