# Getting started

Choose one of the existing samples first. The terminal sample needs only the
.NET SDK and an interactive terminal. The MAUI sample additionally needs a
Windows development environment.

NuGet packages are not available yet. The project is evolving rapidly, which
makes maintaining published packages difficult. For now, clone the repository
and use project references or run the included samples.

## Get the repository and SDK

```sh
git clone https://github.com/ruLegen/DotNetCompose.git
cd DotNetCompose
dotnet --version
```

Run all commands on this page from the repository root. [global.json](../global.json)
selects SDK **10.0.102** with `rollForward: latestPatch`: install that version or
a newer patch in the same `10.0.1xx` feature band. A different .NET 10 feature
band alone does not satisfy this setting. See Microsoft's
[SDK download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and
[SDK selection rules](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).

NuGet access is required for third-party dependencies when they are not cached.
The samples reference the DotNetCompose projects in this repository.

## TUI in an interactive terminal

Open a terminal with interactive input and output, such as Windows Terminal,
and run:

```sh
dotnet run --project src/DotNetCompose.Tui.Sample/DotNetCompose.Tui.Sample.csproj -c Release
```

The task editor displays text fields, a task list, and action buttons. Use **Tab**
or **Shift+Tab** to move focus and **Enter** to activate buttons. Type a task,
choose **Add**, and filter the list. Press **Esc** to exit.

Do not pipe or redirect this command's input/output. The adapter rejects a
non-interactive terminal; automated tests use `FakeTerminalDriver` instead.

Continue with the [sample guide](../src/DotNetCompose.Tui.Sample/README.md) or
[create and run your own counter](tui.md#project-setup).

## MAUI on Windows

The current sample targets `net10.0-windows10.0.19041.0` and `win-x64`. Use a
Windows x64 machine with the SDK above, the MAUI Windows workload, and the
Windows App SDK runtime. The project specifies Windows 10 build 17763 as its
minimum platform version; the framework name identifies the target SDK.

Follow Microsoft's [.NET MAUI installation guide](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0)
to prepare your environment. Check installed workloads:

```powershell
dotnet workload list
```

For a Windows-only CLI setup, install the workload if it is missing:

```powershell
dotnet workload install maui-windows
```

If MAUI is already installed through your IDE, use its installer to maintain
the workload. For an SDK/workload mismatch, Microsoft's
[workload restore](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-workload-restore)
can resolve the workloads required by this project:

```powershell
dotnet workload restore src/DotNetCompose.Maui.Sample/DotNetCompose.Maui.Sample.csproj
```

Launch the app:

```powershell
dotnet run --project src/DotNetCompose.Maui.Sample/DotNetCompose.Maui.Sample.csproj -c Release
```

The window includes **Reload host** and **Light / Dark** controls. After the home
screen initializes, open the editor to try text input, switches, a counter,
and preview navigation. The preview includes canvas drawing.

Continue with the [sample guide](../src/DotNetCompose.Maui.Sample/README.md) or
[MAUI guide](maui.md). This sample does not provide Android, iOS, or macOS launch
instructions.

## Runtime and generator

Read [Core concepts](concepts.md), then [Source Generator basics](source-generator.md#basic-reading-path)
for compiler integration and entry points. Continue with [Runtime](runtime.md)
for the composition engine and custom hosts, then return to
[advanced generator topics](source-generator.md#advanced-reading-path) as needed.
The runtime targets `netstandard2.1` and `net9.0`; the generator targets
`netstandard2.0`. These library targets are separate from the SDK required to
build this checkout and the samples' .NET 10 targets.

Build individual projects while getting started. Building the entire solution
also brings in the MAUI Windows application and its platform requirements.

If a step fails, see [Troubleshooting](troubleshooting.md).

[Documentation index](README.md)
