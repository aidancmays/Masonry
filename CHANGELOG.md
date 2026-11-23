# Changelog

## [0.8.0] - 2026-11-23

- `LayoutSettings.depthBias`: how often growth continues from the deepest open socket. Deeper layouts fit many
  depth-stepped objectives (e.g. 8 objectives two steps apart build reliably at 0.8).
- `BuildOptions.depthBias` and `BuildOptions.attempts` overrides for the first level.

## [0.7.0] - 2026-11-23

- `BuildOptions`: build flags, `maxLinkDepth`, and first-level overrides for piece count and tag counts.
  `LayoutBuilder.Build(seed, options)`, `BuildPreview(seed, options)`, `LevelPlan.Generate(set, seed, options)`,
  `PieceSet.CreateGenerator(..., flags, overrides)`.
- `links` flag on levels that may still link further, so link pieces only appear when there is a level below.
- `WorkbenchWindow` is public, with `Open(set, options, label)` to preview a game's level settings.

## [0.6.0] - 2026-11-23

- Spacing rules: pieces with any of a set of tags (e.g. Entrance, Objective, Exit) keep a minimum number of steps
  from each other, including across loops. Tag rules' spacing uses the same machinery.
- Tag rule *Step* (`TagRule.depthStep`): tagged pieces sit at least that many steps deeper or shallower than each
  other, so objectives lead further in.
- Dead-end rule: long dead ends must end in a piece with one of the given tags. Caps with those tags are preferred
  at the end of long corridors; layouts that still break the rule are retried. `Layout.DeadEnds`,
  `Layout.DeadEndViolations`, `PieceDefinition.HasAnyTag`.
- Workbench: empty long dead ends circled in red; Piece Set inspector shows spacing and dead-end rules.

## [0.5.0] - 2026-11-23

- Build flags and entry conditions: `PieceSet.Entry.condition` ("exit-gate", "!night", "main, floor-3"...) picks
  which entries take part; `LayoutBuilder.Build(seed, flags)`, `LevelPlan.Generate(set, seed, flags)`,
  `PieceSet.CreateGenerator(..., flags)`. Levels get `main` or `linked` automatically. `Flags` helper.
- Piece Set table: *Only If* column. Workbench: *Flags* box in the toolbar.

## [0.4.0] - 2026-11-23

- Levels: `LevelLink` on a piece leads into a level built from another Piece Set (e.g. crypt stairs down to the
  crypts). `LevelPlan` builds a set and every level its links reach; `LayoutBuilder` builds them all, exposes
  `Levels`, and tags pieces with `Piece.Level`. Boundaries are resolved per level.
- `LayoutGenerator.Generate(seed, starts)`: grow one layout from several start points (`LayoutStart`).
- New role `Arrival`: placed only where a link enters the level, never as its standalone start.
- Workbench: level picker and *Edit Its Rules* for linked levels; captions and *Build in Scene* use the main seed.

## [0.3.0] - 2026-11-23

- Module types (`ModuleType`), boundary styles (`BoundaryStyle`) and per-edge `Boundary` components. A Piece Set's
  boundary rules pick the style between two module types or at the edge of the layout, and each shared edge is drawn
  by one side only, so walls are no longer doubled. `BoundaryResolver` runs after every build.
- Boundaries close an unjoined socket's opening with an optional *Closed Opening* object per style.
- Piece inspector: module type, and **Add Boundaries** on every edge cell of the grid footprint.
- Piece Set inspector and Workbench: boundary rules table; maps show module colours and boundary lines.

## [0.2.0] - 2026-11-22

- Piece tags (`PieceTag`) and tag rules on a Piece Set: min/max count (exact numbers), min depth and min spacing
  between pieces with the tag, for things like "exactly 3 objectives, 3 steps apart". Loops that would break the
  spacing are left unjoined.
- Workbench window (Window > Masonry > Workbench): edit a set's rules and see a page of seeds as live top-down maps,
  hover for piece names, build any seed in the scene, and run batch statistics.
- Piece Set inspector as compact tables; optional grid size on the set.
- Piece inspector: grid footprint (cell size, width x depth) and click-to-add sockets around the footprint, for
  pieces of different sizes on one grid.
- `Layout.Pieces`, `Layout.CountWithTag`, `LayoutGenerator.Deficit`. Seeds may build differently from 0.1.0.

## [0.1.0] - 2026-11-22

First release, built for Non Plus Ultra.

- `Piece`, `Socket` and `SocketType` for authoring modular pieces, with Scene view gizmos, a bounds handle and
  inspector checks for badly placed sockets.
- `PieceSet`: start / normal / cap roles, weights, min and max counts, min depth from the start, layout size.
- `LayoutGenerator`: seeded, deterministic growth from a start piece with overlap tests, loop closing, capping and
  seeded retries until minimum counts are met.
- `LayoutBuilder`: instantiates a layout, tells each socket whether it was joined (with objects to switch on for
  joined / open), and previews layouts in edit mode without saving them.
