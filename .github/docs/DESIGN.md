# Library Design

SkiaSharp.QrCode aims to be the best QR code library for C#.
We pursue encoding and decoding implemented in pure C#, first-class support for NativeAOT and WebAssembly, performance backed by measurement, and an API that feels good from the very first line of code.

This document defines the design principles that make SkiaSharp.QrCode what it is. Design details for individual features and implementations are documented in [specs/](specs/).

## Principles

### A Pure C# Core with No External Dependencies

The core QR code encoding and decoding algorithms depend only on the BCL, while SkiaSharp is responsible solely for rendering and image I/O.
The core remains pure C#: rather than adding external dependencies, we implement the algorithms we need ourselves.

### Zero Allocation

We avoid unnecessary allocations made solely for processing and provide APIs that write results directly into caller-provided buffers.
We pursue zero allocation on hot paths while balancing performance with minimal memory allocation.

### Performance Is Measured

We optimize based on measurements of real usage paths, not assumptions.
Beyond techniques such as `Span<T>`, `Memory<T>`, `stackalloc`, and SIMD, we inspect the generated CPU instructions and branches with BenchmarkDotNet to pursue the best possible performance.

### API-Driven Development

We design an API that feels natural and pleasant to use, then determine how to implement it without compromising that experience or performance.
The API should let users start with a single line of code and progressively move down to lower-level control when needed.

### Multiplatform by Design

We treat NativeAOT, trimming, and WebAssembly as first-class execution environments.
We avoid reflection and dynamic code generation, and continuously verify builds and behavior on NativeAOT and WebAssembly.

## Playground

The Playground serves as a working demonstration of the library's design principles.
Its AOT-compiled WebAssembly performs encoding and decoding entirely in the browser, without relying on a server.

---

# ライブラリデザイン

SkiaSharp.QrCodeは、C#における最高のQRコードライブラリを目指します。
純粋なC#によるエンコードとデコード、NativeAOTとWebAssemblyへの対応、計測に裏付けられた性能、そして最初の1行から気持ちよく使えるAPIを追求します。

この文書は、SkiaSharp.QrCodeがこのライブラリであり続けるためのデザイン原則を記録するものです。個々の機能や実装の設計については[specs/](specs/)に記録します。

## 原則

### 純粋なC#、依存のないコア

QRコードのエンコードとデコードのコアはBCLだけで完結し、SkiaSharpは描画と画像の入出力だけを担います。
コアは純粋なC#であり続け、外部依存を増やすのではなく、必要なアルゴリズムは自ら実装します。

### ゼロアロケーション

処理のためだけの不要なメモリアロケーションを許容せず、呼び出し側が用意した領域へ直接結果を書き込めるAPIを提供します。
ホットパスのゼロアロケーションを追求し、パフォーマンスと最小アロケーションの両立を目指します。

### 性能は計測する

最適化は推測ではなく、実際に利用される経路を測定した結果に基づいて行います。
`Span<T>`、`Memory<T>`、`stackalloc`、SIMDといった高速化手法だけでなく、ベンチマークによる逆アセンブル結果から、生成されたCPU命令や分岐まで確認して性能を追求します。

### API駆動開発

実装に先立って、使う人にとって自然で気持ちのよいAPIを設計し、その手触りと性能を両立する方法を考えます。
1行で使い始められ、必要に応じて低レベルな制御へ段階的に降りられるAPIを提供します。

### マルチプラットフォーム

NativeAOT、トリミング、WebAssemblyを第一級の実行環境として扱います。
リフレクションや動的コード生成を避け、NativeAOTとWebAssemblyでのビルドと動作を継続的に検証します。

## Playground

Playgroundは、このライブラリの動く証明として提供しています。
AOTコンパイルされたWebAssemblyが、サーバーに依存せずブラウザ上でエンコードとデコードを実行します。
