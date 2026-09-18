# Stage 1 Original Layout and Layer Execution Sheet

> **Version**: 0.2.0-stage1-design
> **Status**: Original design execution sheet. Character-fidelity and rights conclusions remain blocked by `REF-03`; it has not passed human visual review.
> **Related assets**: [neutral layout wireframe](../artwork-source/fuxuan-neutral-layout-v01.svg), [thumbnail check](../acceptance/thumbnail-check-v01.svg), [silhouette check](../acceptance/silhouette-check-v01.svg)
> **Boundary**: These diagrams express newly created proportions, part boxes, anchors, and occlusion only. They contain no pixels, textures, meshes, curves, physics data, or exports from existing or external models.

## 1. Neutral Front Composition

The canvas is fixed at 4096 x 4096 RGBA with a transparent background. The center line `x=2048` runs through the forehead, nose bridge, sternum, waist, and midpoint between the feet. The visible character height targets 3480 px (about 85 percent), with at least 250 px of top clearance, 260 px of side dynamic clearance on each side, and 180 px below the hem.

| Area | Design intent | Canvas bounds (x, y, w, h) | Movement and paint-over allowance |
|---|---|---:|---|
| Crown ornament | Geometric central ornament and two hanging side elements create a top silhouette anchor | 1350, 250, 1400, 700 | 120 px on every side; hanging elements stay separate from hair |
| Back hair | Large rear volume split into left and right descending sections | 760, 580, 2580, 2140 | 220 px outward and downward at ends |
| Face and front hair | Slightly wider-than-jaw oval face with front hair framing but not hiding the eyes | 1260, 720, 1570, 1330 | Complete paint behind face edge, side hair, and bangs |
| Torso and collar | Narrow shoulders, layered collar, central chest ornament anchor | 1190, 1690, 1720, 1080 | 80 px vertical room for breathing |
| Sleeves and hands | Neutral arms-down pose; cuffs visually larger than hands | 620, 1740, 2850, 1540 | 140 px at underarm, sleeve interior, and wrist |
| Waist and skirt | Upper narrow and lower stable multi-layer silhouette without a placeholder costume | 930, 2440, 2240, 1280 | 200 px per front/back hem motion zone |
| Legs and shoes | Visible only at skirt openings and lower edge, with a stable grounded stance | 1420, 3370, 1260, 510 | Fully paint regions hidden behind the skirt |

## 2. Original Visual Language

This sheet fixes an executable visual language only. A claim of Fu Xuan fidelity remains pending official-reference evidence and human review.

| Category | First-version design rule | Unacceptable result |
|---|---|---|
| Silhouette | Top ornament anchor, tapered waist, stable skirt spread; left/right visual weight approximately balanced | Head-only design or clothing color blocks without a complete body and shoes |
| Hair | Separate front hair, side hair, rear hair, and hair-end groups; prioritize large controllable locks over fragmented strands | Merge all hair into one non-movable layer |
| Ornaments | Crown ornament, side ornaments, chest pendant, and waist decoration are separate layers with traceable connection points | Bake ornaments into hair or clothing so they cannot occlude correctly |
| Costume | Collar, top body, outer/inner sleeves, waist band, front/back skirt, hem decoration, and shoes exist as complete structure | A generic one-piece dress or temporary flat-color clothing |
| Palette | Build five levels from deep violet, light violet, gray-white, charcoal, and restrained metallic light accents; final color values need authorized-reference review | Single high-saturation color across most of the body, or color picking from external images |
| Face | Eye white, iris, highlight, upper/lower lids, brow, and mouth are separate; expressions come from controllable layers | Merge facial features into the face base so blinking or opening the mouth creates holes |

## 3. Layer Naming and Draw Order

The naming convention is `part_side_depth_role`. `side` is one of `C`, `L`, `R`; `depth` uses two digits; `role` is one of `base`, `shadow`, `highlight`, `mask`, `motion`, `physics`, `expression`, or `support`.

| Order | Layer group | Minimum layers | Cubism reservation |
|---:|---|---|---|
| 01 | Canvas support | `canvas_C_00_support` | Alignment-only; not exported as a background |
| 02 | Back hair and rear ornaments | `hair_back_L_10_base`, `hair_back_R_10_base`, `ornament_back_C_11_base` | Separate physics candidates for hair and ornaments |
| 03 | Back skirt, legs, shoes | `skirt_back_C_20_base`, `leg_L_21_base`, `leg_R_21_base`, `shoe_L_22_base`, `shoe_R_22_base` | Back-skirt and leg occlusion paint-over |
| 04 | Body foundation | `body_C_30_base`, `neck_C_31_base`, `shoulder_C_32_base` | Body angles and breath |
| 05 | Top and waist | `top_C_40_base`, `collar_C_41_base`, `waist_C_42_base` | Torso deformation and breath |
| 06 | Front skirt | `skirt_front_C_50_base`, `hem_L_51_physics`, `hem_R_51_physics` | Front/back separation and skirt physics |
| 07 | Sleeves, arms, hands | `sleeve_L_60_base`, `arm_L_61_base`, `hand_L_62_base`, mirrored on right | Future shoulder, arm, and hand channels |
| 08 | Face and front hair | `face_C_70_base`, `hair_front_L_71_motion`, `hair_front_R_71_motion` | Head angle and low-amplitude hair following |
| 09 | Facial features | `eye_white_L_80_base`, `iris_L_81_motion`, `lid_L_82_expression`, `brow_L_83_expression`, `mouth_C_84_expression`, mirrored where applicable | Blink, eye movement, brow, mouth |
| 10 | Front ornaments and pendants | `ornament_front_C_90_base`, `pendant_C_91_physics`, `ribbon_L_92_physics`, mirrored where applicable | Internal-only gentle motion; no LLM direct control |
| 11 | Finishing layers | `face_C_95_shadow`, `top_C_96_highlight`, `effect_C_97_support` | Effect slot is not a first-version capability claim |

## 4. Occlusion, Paint-Over, and Mesh Preconditions

| Boundary | Required paint-over | Intended occlusion | Failure condition |
|---|---|---|---|
| Bangs / face | Bang underside, complete forehead base, and region above brows and eyes | Bangs sit before face; features are separate from bangs | Turn or blink shows holes, black edges, or feature protrusion |
| Side hair / shoulder | Shoulder line, neck, side-hair interior | Independent mask decides whether the lock is before or behind shoulder | Hair enters neck or shoulder line breaks |
| Sleeve / top / hand | Underarm, sleeve interior, wrist, waist side | Arm lies inside sleeve; cuff covers part of wrist | Arm raise exposes holes or sleeve cuts the top incorrectly |
| Front/back skirt / legs | Below-waist area, upper legs, interiors of both skirt layers | Back skirt behind legs; front skirt covers upper legs | Motion exposes legs, waist, or background incorrectly |
| Pendant / ornaments | Attachment strip and the hair/clothing base behind it | Pendant is independent with protected attachment mask | Gentle motion intersects face/shoulder or floats |

Before Cubism begins, every `motion`, `physics`, or `expression` layer needs one visible target and one adjacent layer that can paint over exposed regions. A layer failing either condition cannot receive a mesh.

## 5. First-Version Expressions and Motion Intent

| Target | Layers | Design intention | Current status |
|---|---|---|---|
| Neutral | All base layers | Symmetric, stable, no exaggerated lean | Design candidate |
| Slight smile | Brow, mouth, lids | Small mouth-corner and brow movement; do not change face shape | Design candidate |
| Alert | Lids, brow, iris | Slightly wider eye opening and raised brow, still restrained | Design candidate |
| Sleepy | Upper/lower lids, brow, mouth | Lowered lids without full closure | Design candidate |
| Aggrieved | Brow, mouth, eye highlight | Inner brow rises, mouth narrows | Design candidate |
| Open mouth / closed eyes | Mouth interior, lips, left/right lids | Closure never reveals transparent regions | Design candidate |
| Breath and micro motion | Torso, shoulder, collar, rear hair | Small vertical motion; accessories follow only through internal physics | Design candidate |

This document creates no parameter IDs, min/max values, or runtime mappings. Parameter facts, exports, and Probe evidence may only be produced in Stages 3 through 7.
