# Documentation Index

The single index of design documentation. Documents under `.github/docs/` are for contributors and design; user-facing documentation (usage, migration, capacity tables) lives in [docs/](../../docs/).

Authoring rules, document types, and naming conventions: [docs_authoring_guidelines.md](docs_authoring_guidelines.md).

## Design principles

| Document | Covers |
|---|---|
| [DESIGN.md](DESIGN.md) | Library design principles (English + Japanese) |

## Specs (`specs/`)

Design records and spec-to-code maps for shipped behavior, organized symbology-first.

| Document | Type | Covers |
|---|---|---|
| [qrcode-symbologies.md](specs/qrcode-symbologies.md) | Architecture record | Symbology model, shared vs per-symbology components, API and data-model direction, scope decisions |
| [standardqr-spec-map.md](specs/standardqr-spec-map.md) | Spec-to-code map | Standard QR pipeline vs ISO/IEC 18004 |
| [standardqr-decoder.md](specs/standardqr-decoder.md) | Design record | Standard QR decoder scope, input tiers, lessons learned |
