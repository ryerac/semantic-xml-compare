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

Drop two or three XML files onto the window, or choose them with the file buttons.

## Profiles

Profiles teach XML Compare how to interpret a document without putting domain-specific rules in the comparison engine. They are JSON files in [`profiles`](profiles) during development. For a published executable, custom profiles can be placed in a `Profiles` directory beside `XmlCompare.exe`.

For example, consider a product catalogue whose `<product>` elements may appear in a different order:

```xml
<catalogue>
  <product id="42">
    <sku>CHAIR-BLK</sku>
    <name>Black chair</name>
    <price>49.99</price>
    <generatedAt>2026-10-13T10:00:00Z</generatedAt>
  </product>
</catalogue>
```

A profile for it could be:

```json
{
  "name": "Product catalogue",
  "absoluteTolerance": 0.01,
  "relativeTolerance": 0.001,
  "trimWhitespace": true,
  "ignoreCase": false,
  "emptyEqualsMissing": false,
  "ignoredPaths": [
    "product/generatedAt"
  ],
  "matchRules": [
    {
      "path": "product",
      "keys": [
        ["sku"],
        ["@id"],
        ["name", "price"]
      ]
    }
  ],
  "labelRules": [
    {
      "path": "product",
      "fields": ["sku", "name"]
    }
  ]
}
```

The main properties are:

- `name`: name shown in the profile selector.
- `absoluteTolerance`: maximum numeric difference treated as equal. Here, `49.99` and `50.00` are equal.
- `relativeTolerance`: allowed numeric difference as a ratio of the larger value. `0.001` means 0.1%.
- `trimWhitespace`: ignores whitespace at the beginning and end of text values.
- `ignoreCase`: compares text without case sensitivity when enabled.
- `emptyEqualsMissing`: treats an absent field and an empty field as equivalent when enabled.
- `ignoredPaths`: elements or attributes excluded from comparison.
- `matchRules`: fields used to identify repeated elements independently of their order.
- `labelRules`: fields included in a block's tree label.

Paths are matched by suffix. `product` applies to any product element, while `catalogue/product` is more specific. Prefix an attribute name with `@`, such as `@id`.

Each entry in `keys` is an alternative identity. All fields in an alternative form one composite key. A key is usable only when its fields are present and unique within each collection; when several alternatives are usable, the engine favours the one that matches the most records across the files. This avoids choosing locally unique but regenerated identifiers.

The bundled [`Generic.json`](profiles/Generic.json) contains conservative defaults and no document-specific rules. Copy it under a new filename to create a custom profile.

The comparison never modifies source XML files. DTD processing and external XML resolution are disabled.
