# Masonry

Seeded procedural levels built from modular pieces. You make the pieces as prefabs, mark where they connect,
and Masonry snaps them together. The same piece set and seed always give the same layout on every machine,
so a multiplayer game only has to share one integer.

## Install

```json
"com.aidancmays.masonry": "https://github.com/aidancmays/Masonry.git#v0.3.0"
```

No dependencies besides Unity 6.3.

## Concepts

| Thing | What it is |
|---|---|
| `Piece` | Component on the root of a piece prefab. Holds the piece's **bounds** (local box it occupies; pieces never overlap). |
| `Socket` | Component on a child of a piece: a connection point. Sits on a side of the bounds, **blue axis pointing out**, up axis up. |
| `SocketType` | Asset (Create > Masonry > Socket Type). Sockets join when their types match or one lists the other in *Connects To*. |
| `PieceTag` | Asset (Create > Masonry > Piece Tag). A kind of piece (Objective, Puzzle, Combat...). A piece can carry several. |
| `ModuleType` | Asset (Create > Masonry > Module Type). What kind of space a piece is (Graveyard, Mausoleum...), with a priority. |
| `BoundaryStyle` | Asset (Create > Masonry > Boundary Style). A kind of edge: Fence, Wall, Hedge... |
| `Boundary` | Component on one stretch of a piece's edge holding the art for each style; the layout decides which one shows. |
| `PieceSet` | Asset (Create > Masonry > Piece Set). The rules for one kind of level: pieces with role, weight, min/max count and min depth; tag rules; layout size. |
| `LayoutBuilder` | Component in the scene. `Build(seed)` instantiates the layout under its transform. |
| `LayoutGenerator` | The pure algorithm behind it. Works on plain data, so it runs in tests or on a server without instantiating anything. |

## Workbench

**Window > Masonry > Workbench** (or *Open in Workbench* on a Piece Set). Rules on the left, a page of seeds drawn as
top-down maps on the right, redrawn whenever a rule changes. Colours: start, required pieces, each tag, caps, and red
dots for sockets left open. Hover a map for piece names; click to select (counts per piece and tag); double-click or
*Build in Scene* to build that seed in the scene (on the scene's builder for this set, or a temporary preview that is
never saved). *Run N Seeds* gives the share of seeds that meet every rule, piece and tag counts, and timing.

## Making a piece

1. Model the piece upright. Pieces only ever turn about Y. A shared grid (for example 12 m cells) keeps pieces of
   different sizes lining up and lets loops close, but it is not required.
2. Add `Piece` to the prefab root. Use **Fit To Renderers** or **Edit Bounds** to set the box. The box is what
   other pieces may not overlap, so leave out overhangs you are happy to have overlap.
3. On a grid, open *Grid Footprint*: set the cell size, the footprint in cells (1x1, 1x2, 2x2...) and press
   **Set Bounds**, then click the cells around the footprint to add or remove sockets (they face outward, with the
   *New Socket Type*). Off the grid, click **Add Socket** for each opening, move it to the middle of the opening on the edge of the bounds and turn
   it so its blue arrow points out. Give it a Socket Type.
4. Optional: in the socket's *When Joined* / *When Open* lists, add objects to switch on in each case. A gate or
   wall in *When Open* seals openings that ended up with nothing attached.
5. Give it *Tags* if a tag rule should count it (an objective, a shop, a boss room...).
6. The Piece inspector warns about tilted sockets, sockets off the bounds, sockets facing inward and duplicates.

## Making a piece set

Add each prefab to a Piece Set and choose its role:

- **Start**: the first piece, placed at the builder's origin (for example the entrance with spawn points).
- **Normal**: grows the layout until the piece count (*Min/Max Pieces*) is reached.
- **Cap**: used only at the end, to close sockets still open (dead ends, alcoves).

*Weight* is the relative chance among the pieces that fit. *Min Count* forces a piece to appear (layouts that miss it
are retried with the next seeded try). *Max Count* limits it (0 = no limit). *Min Depth* keeps it at least that many
steps from the start piece, so an exit can be kept far from the entrance. A required piece may have weight 0: it is
then only placed when the budget runs low, which tends to put it near the end of a branch.

### Tag rules

Tag rules count every piece with a tag, whichever piece it is. *Min* and *Max* equal give an exact number: "3
Objectives" can be any mix of puzzle rooms and combat arenas tagged Objective. *Depth* keeps them away from the
start; *Spacing* is the fewest steps between two of them (also checked when a loop closes). Owed pieces are placed
as the piece budget runs out, so give tagged pieces some weight if they should also appear earlier in a branch.
Tagged caps count too, as dead-end rooms.

Select a `LayoutBuilder` to preview: set a seed and press **Build**, **Next Seed** or **Random Seed**. Previews are
never saved with the scene and are cleared before Play mode.

## Boundaries between modules

Pieces that sit side by side would each draw their own fence, so walls doubled up. Instead, give each piece a
*Module Type* and put a `Boundary` on every edge cell (the Piece inspector's grid has **Add Boundaries**), with the
art for each style under it: e.g. a Fence child and a Wall child. On an edge with a socket, leave the opening clear
in the art and put the filler in *Closed Opening*; it shows only if the socket ends up unjoined.

The Piece Set's *Boundaries* table says which style goes where two module types meet (either way round), or where a
module meets the edge of the layout (*Meets* left empty). An empty style means nothing: the two run into each other.
Anything not listed uses *Default*. After every build, boundaries that face each other are paired and the style is
drawn by one side only: the side that has art for it, then the module with the higher priority, then the piece
placed first. Example: Graveyard + Graveyard = nothing, Mausoleum + Graveyard = Fence, Mausoleum + edge = Wall.

The Workbench maps draw the chosen boundaries in each style's colour. `Boundary.Style`, `Drawn` and `Neighbour`
tell game code what was picked; `BoundaryResolver.Resolve` runs it on pieces placed some other way.

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
