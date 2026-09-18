# Artwork Source Delivery Rules

> **Status**: Stage 2 prerequisite. This directory contains no formal artwork, external images, or Cubism project yet.

## File Boundary

- The editable source must be a layered PSD or equivalent editable format; provide a full-size transparent PNG preview with each delivery.
- Use the file name `fuxuan_derivative_vNN_<purpose>`, for example `fuxuan_derivative_v01_neutral.psd`.
- Each delivery includes `layer-manifest-vNN.md`, listing layer name, dimensions, motion category, paint-over status, and author/source.
- Formal source files may only use original project artwork or assets whose URL, original license text, and usage conclusion have been registered.
- Do not import, trace, or reconstruct existing model textures, rendered `.moc3` screenshots, official illustration fragments, or AI output of unknown provenance.

## Layer Requirements

- Fixed canvas: 4096 x 4096 RGBA with transparent background; no clipped character boundary.
- Follow `design/layer-layout.md` naming: `part_side_depth_role`.
- Every planned deformation edge retains complete hidden pixels behind adjacent layers; never fill an exposure gap with a background color.
- Separate base color, line, shadow, and highlight layers. Do not cross left/right movable parts.
- Eye white, iris, upper/lower lids, brows, mouth, mouth interior, front/rear hair, ornaments, cuffs, front/back skirt, and pendants must be independently locatable.
- Any external font, brush, texture, or AI-generation input needs a verifiable source and license conclusion appended under `sources/`; unknown provenance is a blocker.

## Pre-Delivery Checks

1. Check for white fringe, semi-transparent contamination, and color leaks on transparent, dark, and light backgrounds.
2. Hide each front layer and verify that the behind-layer is fully painted for a small deformation.
3. Check silhouette, costume completeness, and excess detail at 256 px, grayscale, and 1:1 scale.
4. Confirm the source is editable, unflattened, and free of untraceable embedded layers.
5. Record the result in the Stage 2 acceptance report. Do not import into Cubism before passing.
