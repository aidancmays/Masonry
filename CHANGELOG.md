# Changelog

## [0.1.0] - 2026-11-22

First release, built for Non Plus Ultra.

- `Piece`, `Socket` and `SocketType` for authoring modular pieces, with Scene view gizmos, a bounds handle and
  inspector checks for badly placed sockets.
- `PieceSet`: start / normal / cap roles, weights, min and max counts, min depth from the start, layout size.
- `LayoutGenerator`: seeded, deterministic growth from a start piece with overlap tests, loop closing, capping and
  seeded retries until minimum counts are met.
- `LayoutBuilder`: instantiates a layout, tells each socket whether it was joined (with objects to switch on for
  joined / open), and previews layouts in edit mode without saving them.
