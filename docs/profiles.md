# Profiles

A profile describes one way of talking to a P3 car: CAN IDs, bit rate, block
length and — most importantly — whether the read path has ever been verified.

Two profiles ship with the bridge:

| id | Read | Notes |
| --- | --- | --- |
| `p3.sim` | in-process simulator | synthetic, always writable |
| `p3.cem-726` | **none** | `0x726`/`0x72E` @ 500 kbit/s, VIN + tester present only |

`p3.cem-726` exists so hardware mode can open a channel and read identity. It has
`read.method = "none"`, so **Read config** explains why it is blocked instead of
guessing an address.

## Adding your own

Create `profiles.local.json` next to `start.cmd` (the bridge loads it at start-up).
It is a JSON array and overrides same-id built-ins. This file is git-ignored.

```json
[
  {
    "id": "p3.my-car",
    "label": "P3 CEM, verified DID",
    "description": "Read-only config block for my own car.",
    "platform": "P3",
    "synthetic": false,
    "validated": false,
    "requestId": "0x726",
    "responseId": "0x72E",
    "baud": 500000,
    "configLength": 254,
    "read": {
      "method": "did",
      "did": "0x0100",
      "length": 254,
      "note": "Read-only. Verified on a bench module on 2026-01-01; see notes."
    },
    "notes": [
      "Identity probes only. No session change, no security access, no writes."
    ]
  }
]
```

| Field | Meaning |
| --- | --- |
| `requestId` / `responseId` | 11-bit CAN IDs, hex string or decimal |
| `baud` | ISO 15765 bit rate |
| `configLength` | expected block length; the read must return at least this many bytes |
| `read.method` | `simulator`, `did` (UDS `22`), or `none` |
| `read.did` | identifier for `22`, hex string |
| `validated` | `true` only after you verified the path on real hardware |

The `0x0100` above is a **placeholder**. Do not copy it onto a car.

## What `validated` changes

* `false` → the header shows the profile is unverified, and the roadmap stays
  read-only.
* `true` → the bridge still refuses config writes, because writes are not
  implemented at all. That is intentional: `App.Apply` only accepts simulator
  mode.

## Read path behaviour

* `did`: sends `22 <did>`, expects `62 <did> <data...>`, requires at least
  `read.length` bytes, trims to the catalogue length, and records the raw response
  in the dump provenance.
* Nothing is retried automatically, no session is changed and no security access
  is attempted.
* Hardware writes need `10 82`/`27`/`2E`/`31` sequencing, a key routine and a
  rollback story. None of that is in this repository, and `Apply` is hard-blocked
  outside simulator mode.
