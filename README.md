# ETCS

ETCS is a C# Autodesk Revit add-in that collects several electrical and model-coordination utilities in an **ETCS Tools** ribbon tab. The current project is an early-stage implementation: it contains three commands, but placement and circuit-naming workflows still have important limitations described below.

## Features

The add-in creates an **ETCS Tools → Commands** ribbon panel with these commands:

### List Family Instances

Lists `FamilyInstance` elements visible in the active view, grouped by category in expandable sections. Selecting an item also selects the corresponding element in Revit.

Implementation: [StartupCommand.cs](./ETCS/Commands/StartupCommand.cs)

### Place Family Instances

Prompts for rooms found in loaded Revit links, then lets the user choose a family category/type, a level, spacing values, an elevation value, and whether to place the instance as ceiling-hosted. Placement is performed in a Revit external event and transaction.

Despite the spacing controls and ribbon tooltip, the current algorithm places **at most one instance per selected room**. It finds candidate points on a grid and retains only the point closest to the average of the room boundary vertices (or falls back to that average).

Implementation: [PlaceFamilyInstancesCommand.cs](./ETCS/Commands/PlaceFamilyInstancesCommand.cs)

### Update Panel Schedule Circuit Naming

Uses the elements currently selected in Revit as panels and asks for a starting integer. It scans electrical systems for a panel reference and writes successive values to a writable `Custom Circuit Number` parameter.

Implementation: [UpdatePanelScheduleCircuitNamingCommand.cs](./ETCS/Commands/UpdatePanelScheduleCircuitNamingCommand.cs)

The separate panel picker and settings model in [PanelCircuitNamingDialog.xaml](./ETCS/Commands/Classes/PanelCircuitNamingDialog.xaml), [PanelCircuitNamingDialog.xaml.cs](./ETCS/Commands/Classes/PanelCircuitNamingDialog.xaml.cs), and [PanelCircuitSettings.cs](./ETCS/Commands/Classes/PanelCircuitSettings.cs) are not currently connected to this command.

## Known issues and review findings

These are findings from reviewing the current source, not a claim that they have been filed in an issue tracker.

| Priority | Area | Finding |
|---|---|---|
| High | Room placement | Spacing values are parsed but not checked for positivity or finiteness. A spacing of zero or a negative value can make the placement grid loop run indefinitely and hang Revit. |
| High | Revit API context | The modeless family-instance list calls `UIDocument.Selection.SetElementIds` directly from a WPF selection handler. Modeless UI should marshal Revit API work through an `ExternalEvent` (or another supported Revit callback). |
| High | Placement result | The external event is asynchronous, but the UI displays `PlacedInstanceCount` immediately after raising it. The displayed count can therefore be stale (often zero) and the message can appear before placement succeeds or fails. |
| Medium | Placement behavior | Grid candidates are reduced to one point per room, so spacing does not produce a grid. If no grid point is found, the arithmetic mean of boundary vertices is used without checking that it lies inside the room. Only the first boundary loop is considered. |
| Medium | Placement coordinates and hosts | The elevation field is labelled as an offset, but its value is used as an absolute Z elevation for unhosted instances; the selected level's elevation is not added. The hosted option searches ceilings using only level and bounding-box XY checks, which may select an unsuitable host. |
| Medium | Circuit naming | Circuit discovery depends on a parameter looked up by the literal name `PANEL_ID_PARAM`; naming writes depend on a writable `Custom Circuit Number` parameter. Neither dependency is validated up front. Missing parameters can produce repeated warning dialogs, after which the command can still show a success message. Circuit ordering is not made explicit. |
| Medium | Circuit naming UI | The existing panel-selection dialog is unused. Users must select elements in Revit before running the command, and the command does not verify that each selected element is an electrical panel. |
| Medium | Quality gates | There is no test project or CI workflow in the repository. Revit-dependent behavior has not been validated in a running Revit installation. |
| Low | Startup and release | Startup assumes the `ETCS Tools` ribbon tab can be created; an existing tab with the same name can prevent startup. No license or end-user release/install guide is included. |

## Recommended next steps

1. **Prevent invalid placement input:** require finite, strictly positive X/Y spacing, validate elevation and numeric parsing, and bound the number of grid iterations/placements.
2. **Make placement completion reliable:** report results only after the external event finishes, including failure details and the actual number placed.
3. **Choose and implement the intended placement behavior:** either implement a real multi-instance grid or remove the grid implication from the UI; handle all room boundary loops and ensure fallback points are inside the room.
4. **Harden placement against real models:** confirm linked-model transforms and host selection against representative Revit models, clarify whether elevation is absolute or relative to the selected level, and handle inactive family symbols.
5. **Make circuit naming deterministic and safer:** use Revit's supported panel/circuit relationships, validate panel selections and required parameters before starting a transaction, define circuit sort order, and report a single actionable summary instead of repeated dialogs.
6. **Resolve the modeless selection API issue:** route selection changes through a supported Revit API callback.
7. **Add quality and release checks:** create tests for isolated geometry/validation logic, add Windows CI builds for the supported Revit configurations, and document licensing and installation/release procedures.

## Technology and supported Revit configurations

- C# with the .NET SDK-style project system
- WPF for command windows
- Autodesk Revit API, using Nice3point Revit packages and build tasks
- x64 build target
- Configurations are defined for Revit 2021–2026:
  - Revit 2021–2024: .NET Framework 4.8 (`net48`)
  - Revit 2025–2026: .NET 8 for Windows (`net8.0-windows`)

Use the configuration matching the Revit version installed on the Windows development machine. Revit API assemblies and the Revit application itself are required to run and validate the add-in.

## Repository layout

```text
ETCS.sln
ETCS/
  Application.cs                         Revit application and ribbon registration
  ETCS.addin                              Revit add-in manifest
  ETCS.csproj                             Frameworks, configurations, and dependencies
  Commands/
    StartupCommand.cs                     List family instances
    PlaceFamilyInstancesCommand.cs        Place instances in linked rooms
    UpdatePanelScheduleCircuitNamingCommand.cs
    Classes/
      PanelCircuitNamingDialog.xaml       Unused panel picker UI
      PanelCircuitNamingDialog.xaml.cs
      PanelCircuitSettings.cs             Unused dialog result model
  Resources/Icons/                        Ribbon icons
```

## Build

Build on Windows with a .NET SDK capable of targeting the project's frameworks and with the required Revit API NuGet packages available:

```powershell
dotnet restore ETCS.sln
dotnet build ETCS.sln --configuration "Debug R26"
```

Replace `R26` with the desired configured Revit version (`R21` through `R26`). The project enables Revit add-in deployment through Nice3point build tasks, so a build may also attempt to copy the add-in to the corresponding local Revit add-ins directory. Building on a machine without Revit or without permission to write to that directory may fail during this deployment step.

The add-in manifest is [ETCS.addin](./ETCS/ETCS.addin); the project also configures the Revit executable as its Visual Studio start program.

## Running and using the commands

1. Open a Revit version matching the build configuration.
2. Build/deploy the add-in using the project build tasks, or install the generated assembly and manifest into that Revit version's add-ins folder.
3. Start Revit and open **ETCS Tools → Commands**.
4. For **Place Family Instances**, ensure linked models are loaded and select rooms in the dialog. For **Update Panel Schedule Circuit Naming**, select panel elements in Revit before running the command.

Work on a copy of a project when evaluating the placement or circuit-naming tools. These commands modify the active Revit document, and the current implementation has not been validated for production use.

## Testing and verification status

No automated test project is present. In this environment, dependency restore succeeded and the Revit 2026 target produced the assembly and publish output, but the overall build failed during the automatic deployment step: the build task could not write the manifest to `/Autodesk/Revit/Addins/2026` in the Linux container. A nullable-reference warning was also reported in `UpdatePanelScheduleCircuitNamingCommand.cs`. A successful build/deployment and behavioral smoke test should be run on Windows with the matching Revit version installed.

## License

No license file is currently included. Until a license is added, do not assume the project grants permission to redistribute or reuse the code.
