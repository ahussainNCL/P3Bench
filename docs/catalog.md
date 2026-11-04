# Catalogue format

A catalogue maps bytes of a config block to named settings. The bridge and the UI
never hard-code parameter names or offsets, so a researched catalogue can replace
the demo file without touching code.

Loaded from `catalog/p3.demo.json` at start-up, or pasted into the UI
(**Paste JSON** → validated before use). Reload from disk with **Reload file**.

## Top level

```json
{
  "id": "p3.demo.synthetic",
  "label": "P3 demo catalog (synthetic)",
  "description": "What this is and where the offsets came from.",
  "synthetic": true,
  "platform": "P3",
  "configLength": 254,
  "checksum": { "enabled": true, "byteIndex": 1, "from": 2, "to": 254 },
  "groups": [ ... ]
}
```

| Field | Meaning |
| --- | --- |
| `id` | stable identifier, shown in the UI and in exports |
| `label`, `description` | display text |
| `synthetic` | `true` when the data is invented. The UI shows a permanent warning and never claims it came from a car |
| `platform` | free text, `P3` |
| `configLength` | block size in bytes (1–4096). Imports of other lengths are rejected |
| `checksum` | optional. `byteIndex` holds the sum of bytes `[from, to)` modulo 256 |

Rules enforced at load:

* every parameter needs a unique `id` (case-insensitive);
* `type` must be one of `bool`, `enum`, `u8`, `u16le`, `u16be`, `ascii`;
* `offset + length` must stay inside the block;
* `ascii` needs a `length` of at least 1;
* `scale` must not be negative, `min` must not be negative, and `max` must be
  greater than `min` when set;
* `min`/`max` must still fit the raw storage width after dividing by `scale`
  (a `u8` with `max: 300, scale: 1` is rejected rather than truncated);
* two parameters may share a byte only if both are `bool` **and** use different `bit`s;
* `enum` values must be unique, labelled, and within 0–255;
* the checksum byte must sit outside the summed range and not belong to a parameter.

## Groups and parameters

```json
{
  "id": "lighting",
  "label": "Lighting",
  "description": "Exterior lights and assistance.",
  "params": [
    {
      "id": "drlMode",
      "label": "Daytime running lights",
      "description": "Region-specific DRL behaviour.",
      "type": "enum",
      "offset": 20,
      "values": [
        { "value": 0, "label": "Off" },
        { "value": 1, "label": "Nordic" }
      ],
      "default": 1
    },
    {
      "id": "autoLock",
      "label": "Automatic door locking",
      "type": "bool",
      "offset": 12,
      "bit": 0,
      "default": true
    }
  ]
}
```

| Field | Notes |
| --- | --- |
| `type` | `bool`, `enum`, `u8`, `u16le`, `u16be`, `ascii` |
| `offset` | byte offset in the block |
| `bit` | 0–7, `bool` only |
| `length` | `ascii` only; other types derive it from `type` |
| `min`, `max` | numeric limits, compared against the **scaled** value. `max` 0/absent means the type maximum |
| `scale` | display multiplier; stored value is `value / scale` |
| `unit` | shown next to numeric inputs |
| `values` | required for `enum`; the UI renders ≤4 options as buttons, more as a select |
| `default` | used by the simulator to build a plausible block. Ignored when reading a real block |
| `notes` | shown as a warning icon on the row |

Numeric values are validated before a plan is staged, so the UI cannot produce a
byte that the catalogue says is out of range.

## Checksums

If `checksum.enabled` is true, `Plan` recomputes the byte after applying edits and
adds a `Block checksum` change to the plan. `apply` refuses to run when the
recomputed value does not match the byte in the candidate block. The byte is drawn
with a green outline in the **Bytes** view.

## Provenance

Add the source of every offset in `description`/`notes` when you move beyond the
demo file: dump id, module, part number, software number, session, date. A
catalogue without provenance is how a hobby tool turns into a bricked CEM.

## Demo data warning

`catalog/p3.demo.json` is generated for the simulator. Offsets, encodings, names
and defaults were chosen to exercise every control type. Do not use them on a car.
