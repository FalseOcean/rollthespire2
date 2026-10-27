# Third-party sources and notices

RolltheSpire2 (RT2) uses game materials and references third-party algorithms. RT2's MPL-2.0 license covers the project's original contributions as described in `LICENSE`; it does not relicense game or third-party materials. Preserve applicable third-party notices with distributions containing those materials. This document records known sources and remaining attribution limits; it is not a claim that all rights have been cleared.

## Random-number algorithms

- `Core/Rng/Xoshiro256StarStar.cs` uses the **xoshiro256\*\*** algorithm by **David Blackman and Sebastiano Vigna**. [Original authors' C implementation and notice](https://prng.di.unimi.it/xoshiro256starstar.c).
- `Core/Rng/SplitMix64.cs` uses **SplitMix64**, with the reference implementation written in 2015 by **Sebastiano Vigna**. [Original author's C implementation and notice](https://prng.di.unimi.it/splitmix.c).

The original authors' notices dedicate the software to the public domain to the extent possible under law and explicitly permit use, copying, modification and distribution for any purpose, with or without a fee. These identify the upstream algorithms and their terms; they do not establish who authored or supplied RT2's particular C# implementations or its game-compatible sampling behavior.

The SplitMix64 source's original notice is reproduced here:

```text
Written in 2015 by Sebastiano Vigna (vigna@acm.org)

To the extent possible under law, the author has dedicated all copyright
and related and neighboring rights to this software to the public domain
worldwide.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted.
THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR
IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## GPU execution optimization ideas

RT2's October 2026 transfer and bounded-submission improvements independently
adapt ideas from **James Zhou (@JamesZhou233)** in
[PR #4](https://github.com/FalseOcean/rollthespire2/pull/4). This credits the
optimization ideas; the PR's implementation was not merged into RT2.

## Game art and development images

- **RT2 logo:** `Assets/Icons/Rt2Launcher.svg` and its PNG rendering are an RT2 adaptation based on Mega Crit's **Neow's Bones** bone-dice artwork in *Slay the Spire 2*. The game artwork and the project's design adjustments have distinct origins; the game artwork is not offered under RT2's MPL license. See [Mega Crit's Content Policy](https://megacrit.com/content-policy/) for the developer's published policy, including its Mods section.
- **Retired background reference:** the project owner generated `Ui/Art/blue-panel-background.png` using ChatGPT. It is a retired design reference with no current runtime use. This provenance does not establish the absence of third-party rights.
- **Prototype screenshots:** the eight images in `.artifacts/neow-ui-20260916/` are captures of RT2's prototype inside the game window, including cropped or normalized versions.
- **Map experiment images:** the two images in `docs/source_audits/map_viewer_spike_20260915/` were rendered in an isolated local host using the locally installed game's PCK resources and a synthetic graph. They are experimental layout comparisons, not captures of naturally generated in-game maps.

Development images retain game artwork and serve as mod demonstrations, technical illustrations or historical evidence. Capturing, cropping or rendering them does not transfer Mega Crit's rights to RT2 or grant a general license to reuse the underlying game assets. These source-tree paths identify provenance; not every distribution contains every image. RT2 is a community project and is not affiliated with Mega Crit.

## Attribution still being confirmed

The specific C# implementation ancestry for `Core/Seed/XxHash64.cs` and possible Redzen-related RNG code remains unconfirmed. [Yann Collet's xxHash source license](https://github.com/Cyan4973/xxHash/blob/v0.8.3/LICENSE), the [.NET XXH64 implementation](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Hashing/src/System/IO/Hashing/XxHash64.cs) and [Redzen's license](https://github.com/colgreen/Redzen/blob/main/LICENSE.txt) are references, not evidence that RT2 copied those particular implementations. No MIT or BSD grant for RT2's code is inferred from algorithm similarity alone.

The applicable permission for the old-game mirror in `Core/Seed/LegacyDeterministicHash32.cs` and for public redistribution of the game-map mirror also remains to be confirmed. The map evidence's source and derivation are recorded in `Search/Selectivity/MapEvidence/README.md` and `provenance.json`. These open provenance questions remain scoped follow-up items; they do not change the algorithms, prediction behavior or project license.
