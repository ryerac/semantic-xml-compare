# Creating comparison profiles

Profiles teach XML Compare how to interpret a document without putting domain-specific rules in the comparison engine. A profile can define how repeated elements are matched, how blocks are labelled, which values are ignored, and how numeric values are compared.

Start by copying [`profiles/Generic.json`](../profiles/Generic.json) under a new filename. During development, profile JSON files belong in the repository's `profiles` directory and are copied to the build output. For a published executable, place custom profiles in a `Profiles` directory beside `XmlCompare.exe`.

## Example XML

Consider a product catalogue whose `<product>` elements may appear in a different order:

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

The following profile matches products by SKU, ignores their generated timestamp, and gives each product a useful tree label:

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

## Profile properties

- `name`: name shown in the profile selector.
- `absoluteTolerance`: maximum fixed numeric difference treated as equal. With `0.01`, values `49.99` and `50.00` are equal.
- `relativeTolerance`: allowed numeric difference as a ratio of the larger value. `0.001` means 0.1%.
- `trimWhitespace`: ignores whitespace at the beginning and end of text values.
- `ignoreCase`: compares text without case sensitivity when enabled.
- `emptyEqualsMissing`: treats an absent field and an empty field as equivalent when enabled.
- `ignoredPaths`: elements or attributes excluded from comparison.
- `matchRules`: fields used to identify repeated elements independently of their order.
- `labelRules`: fields included in a block's tree label.

A numeric value is considered equal when it passes either the fixed or relative tolerance.

## Paths and attributes

Paths are matched by suffix. `product` applies to any product element, while `catalogue/product` is more specific.

Prefix an attribute name with `@`. For example, `@id` refers to the `id` attribute in `<product id="42">`.

The same path rules apply to `ignoredPaths`, `matchRules`, and `labelRules`.

## Matching repeated elements

Each entry in `keys` is an alternative identity. All fields inside one entry form a composite key:

```json
"keys": [
  ["sku"],
  ["@id"],
  ["name", "price"]
]
```

This means:

1. Prefer `sku` when it reliably connects records across the files.
2. Otherwise try the `id` attribute.
3. Otherwise use the combination of `name` and `price`.

A key is usable only when all its fields are present and the resulting values are unique within each collection. When several alternatives are usable, the engine favours the one matching the most records across the files. This prevents a locally unique but regenerated identifier from being selected over a stable business key.

Without an explicit rule, XML Compare attempts to discover common identity fields and falls back to structural matching.

## Display labels

Label rules make repeated blocks easier to recognise in the tree:

```json
{
  "path": "product",
  "fields": ["sku", "name"]
}
```

This produces a label similar to:

```text
product: CHAIR-BLK | Black chair
```

Labels affect presentation only; they do not control matching.

## Ignoring volatile values

Use `ignoredPaths` for values that should not contribute to the comparison, such as generated timestamps:

```json
"ignoredPaths": [
  "product/generatedAt",
  "response/calculationDuration"
]
```

Keep ignore rules as specific as practical. A rule containing only `generatedAt` ignores every element with that suffix, regardless of where it appears.
