# XML Compare

A read-only Windows desktop application for semantic XML comparison. It compares two or three documents as logical trees, matches reordered collection items, and highlights changed or missing values.

## Run

```powershell
dotnet run --project .\src\XmlCompare\XmlCompare.csproj
```

In VS Code, open the repository folder and press **F5**. The included `XML Compare (WPF)` launch configuration builds and starts the app with the debugger attached.

## Single executable

Create a self-contained Windows x64 executable with:

```powershell
dotnet publish .\src\XmlCompare\XmlCompare.csproj -p:PublishProfile=SingleFile
```

The result is `artifacts\publish\win-x64\XmlCompare.exe`. It does not require a separate .NET installation. Published builds use built-in Generic defaults; place an optional `Profiles` directory beside the executable to distribute additional comparison profiles.

Drop two or three XML files onto the window, or choose them with the file buttons. The bundled **Imago** profile is selected by default.

## Profiles

Comparison profiles are JSON files in [`profiles`](profiles). They configure matching keys, display labels, ignored paths, numeric tolerances, whitespace/case handling, and missing-value behaviour. Files are copied to the application's output `Profiles` directory when built.

Rules use suffix paths, so `chargeevent` applies wherever that element occurs. A path beginning with `/` is treated as an exact/full suffix path. Matching key alternatives are tried in order; every item in a key must be present and unique within each file for that key to be used.

The comparison never modifies source XML files. DTD processing and external XML resolution are disabled.
