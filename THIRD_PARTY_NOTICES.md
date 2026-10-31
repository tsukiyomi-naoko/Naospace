# Third-party notices

NOVA Workspace v0.1.0 contains no third-party source code. It uses only:

* .NET 10 runtime, WPF and Windows Forms (MIT License, © .NET Foundation and Contributors) — not redistributed in
  framework-dependent builds; included in self-contained builds under their license.
* Documented Windows APIs (Shell COM interfaces, Win32), called through interop declarations written for this project.

Test-only dependencies (not shipped): xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT).

The specification allows selectively reusing appropriately licensed code (for example from Microsoft PowerToys, MIT).
None has been reused so far. If code is reused in the future, its license text and attribution will be added here and
in the affected source files.
