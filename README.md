# Masonry

Seeded procedural levels built from modular pieces. You make the pieces as prefabs, mark where they connect,
and Masonry snaps them together. The same piece set and seed always give the same layout on every machine,
so a multiplayer game only has to share one integer.

## Install

```json
"com.aidancmays.masonry": "https://github.com/aidancmays/Masonry.git#v0.1.0"
```

No dependencies besides Unity 6.3.

## Concepts

| Thing | What it is |
|---|---|
| `Piece` | Component on the root of a piece prefab. Holds the piece's **bounds** (local box it occupies; pieces never overlap). |
| `Socket` | Component on a child of a piece: a connection point. Sits on a side of the bounds, **blue axis pointing out**, up axis up. |
| `SocketType` | Asset (Create > Masonry > Socket Type). Sockets join when their types match or one lists the other in *Connects To*. |
| `PieceSet` | Asset (Create > Masonry > Piece Set). The pieces of one kind of level, with role, weight, min/max count, min depth, plus layout size. |
| `LayoutBuilder` | Component in the scene. `Build(seed)` instantiates the layout under its transform. |
| `LayoutGenerator` | The pure algorithm behind it. Works on plain data, so it runs in tests or on a server without instantiating anything. |

## Making a piece

1. Model the piece upright. Pieces only ever turn about Y. A shared grid (for example 12 m cells) keeps pieces
   lining up and lets loops close, but it is not required.
2. Add `Piece` to the prefab root. Use **Fit To Renderers** or **Edit Bounds** to set the box. The box is what
   other pieces may not overlap, so leave out overhangs you are happy to have overlap.
3. Click **Add Socket** for each opening, move it to the middle of the opening on the edge of the bounds and turn
   it so its blue arrow points out. Give it a Socket Type.
4. Optional: in the socket's *When Joined* / *When Open* lists, add objects to switch on in each case. A gate or
   wall in *When Open* seals openings that ended up with nothing attached.
5. The Piece inspector warns about tilted sockets, sockets off the bounds, sockets facing inward and duplicates.

## Making a piece set

Add each prefab to a Piece Set and choose its role:

- **Start**: the first piece, placed at the builder's origin (for example the entrance with spawn points).
- **Normal**: grows the layout until the piece count (*Min/Max Pieces*) is reached.
- **Cap**: used only at the end, to close sockets still open (dead ends, alcoves).

*Weight* is the relative chance among the pieces that fit. *Min Count* forces a piece to appear (layouts that miss it
are retried with the next seeded try). *Max Count* limits it (0 = no limit). *Min Depth* keeps it at least that many
steps from the start piece, so an exit can be kept far from the entrance. A required piece may have weight 0: it is
then only placed when the budget runs low, which tends to put it near the end of a branch.

Select a `LayoutBuilder` to preview: set a seed and press **Build**, **Next Seed** or **Random Seed**. Previews are
never saved with the scene and are cleared before Play mode.

## Building at runtime

```csharp
var layout = builder.Build(seed);      // clears the previous build first
foreach (var piece in builder.Pieces)  // instantiated pieces, in placement order
    Debug.Log($"{piece.name} at depth {piece.Depth}");
```

`Built` fires once every piece is placed and every `Socket` knows whether it was joined (`Socket.IsJoined`,
`Socket.JoinedTo`). `Layout.Valid` is false when no try met the minimum counts; the closest layout is still built
and a warning is logged.

### Multiplayer

Build the layout locally on every machine from a seed the host picks and shares (for Netcode for GameObjects, a
`NetworkVariable<int>` on an in-scene NetworkObject next to the builder works well). Don't network the pieces.
The generator uses its own random number generator (`Rng`, SplitMix64), never `UnityEngine.Random`, so results
don't depend on the platform or on anything else drawing random numbers. Changing `Rng` or the generator changes
what an old seed builds.

## How it works

Start piece at the origin; then, until the piece budget is spent, pick a random open socket and try candidate
pieces (weighted, without repeats) through each of their compatible sockets (shuffled). A candidate fits if its
bounds, shrunk by *Overlap Tolerance*, overlap nothing. Other sockets of a new piece that land on an open socket,
facing it, are joined too, which closes loops. Sockets nothing fits are skipped. Then caps are tried on what is
left, and whatever remains open is reported in `Layout.OpenSockets`.
