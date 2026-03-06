# WildwoodComponents Tools Directory

This directory contains development tools and utilities for the WildwoodComponents library.

## Directory Structure

### `/testing/`
Component testing and build utilities:
- `build.bat` - Component build automation script
- Future component testing tools
- Library verification scripts

### `/debug/`
Debug utilities and build artifacts:
- `BuildTest.cs` - Build testing utility
- Component debugging tools
- Library compilation artifacts

## Usage

Tools can be run from the WildwoodComponents project directory:
```powershell
# From WildwoodComponents project root
.\tools\testing\build.bat
dotnet run --project tools\debug\BuildTest.cs
```

This is a component library, so tools focus on build verification and component testing.
