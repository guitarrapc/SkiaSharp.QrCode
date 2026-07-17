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
| [qrcode-test-fixtures.md](specs/qrcode-test-fixtures.md) | Design record | Committed fixture corpus, manifest schema, external-oracle capability matrix |
| [microqr-spec-map.md](specs/microqr-spec-map.md) | Spec-to-code map | Micro QR encoding pipeline vs ISO/IEC 18004 |
| `rmqr-spec-map.md` | Spec-to-code map | Planned (implementation plan Phase 5) |

## Plans (`plans/`)

Forward-looking strategy; durable decisions graduate into `specs/` after implementation.

| Document | Covers |
|---|---|
| [skiasharp-qrcode-microqr-rmqr-implementation-plan.md](plans/skiasharp-qrcode-microqr-rmqr-implementation-plan.md) | Micro QR / rMQR implementation order (Phase 0-8) |
| [skiasharp-qrcode-microqr-rmqr-test-strategy.md](plans/skiasharp-qrcode-microqr-rmqr-test-strategy.md) | Micro QR / rMQR test strategy (oracles, fixtures, CI design) |
