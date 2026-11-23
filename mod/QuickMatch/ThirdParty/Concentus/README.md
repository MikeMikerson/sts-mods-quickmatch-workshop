# Concentus (Opus codec, pure C#)

QuickMatch voice chat encodes and decodes Opus with Concentus, a C# port of the Opus reference
library by Logan Stromberg, under the Opus BSD license (`LICENSE`, which must stay with this code).

- Source: https://github.com/lostromb/concentus, commit `3885c4e46513ef0fc81fca100189e54f1714c6ca`
  (2025-09-27, package version 2.2.2), folder `CSharp/Concentus`.
- The source archive at `codeload.github.com/lostromb/concentus/tar.gz/<commit>` has
  SHA-256 `7c20f41c0da58ce16d24103006e7f5b48102762ae0c5f5bc7ac42782697b4a14`.
- The 132 files listed in `SOURCES.sha256` are byte-identical to that commit. Check with
  `shasum -a 256 -c SOURCES.sha256` in this folder.

QuickMatch compiles the managed codec into its own assembly. It omits the upstream project,
`AssemblyInfo.cs` and the native library loaders (`Native/`, `OpusCodecFactory.cs`,
`ResamplerFactory.cs`). `NativeNamespace.cs` supplies the namespace for one upstream
`using` directive. The local `.editorconfig` marks the vendor source as generated.
