# Mii editor idle loops

Twenty separate `.miianim` loops for the Mii editor: ten standing motions and ten close-camera motions. All are 60 fps and last 10–16 seconds. They keep the Mii's Normal expression throughout, so the selected eyes, eyebrows and mouth remain visible. There are no winks, blinks, particles or expression changes.

Every clip starts and ends in the same neutral standing pose, with a half-second still hold at both ends. Enable looping in the player. You can choose another idle at the loop boundary or return from an editor action's resting pose. The files contain no `done` event.

## Standing choices

These use small body or hand movements for the whole-Mii view. Both feet stay planted, there is no turning or walking, and hands stay below the face.

| File | Length | Motion |
| --- | --- | --- |
| [Editor_idle_calm_breathing.miianim](Editor_idle_calm_breathing.miianim) | 12 s | Two soft breaths, with a tiny body settle and counteracting neck tilt. |
| [Editor_idle_weight_left.miianim](Editor_idle_weight_left.miianim) | 14 s | Ease weight onto the left leg, pause, glance slightly aside and return. |
| [Editor_idle_weight_right.miianim](Editor_idle_weight_right.miianim) | 13 s | Rest weight on the right leg, give a small chin dip and recenter. |
| [Editor_idle_slow_sway.miianim](Editor_idle_slow_sway.miianim) | 16 s | Slowly shift from one side to the other with the head balancing the torso. |
| [Editor_idle_relaxed_shoulders.miianim](Editor_idle_relaxed_shoulders.miianim) | 12 s | Loosen both low arms, make a soft shoulder reset and settle. |
| [Editor_idle_hands_settle.miianim](Editor_idle_hands_settle.miianim) | 11 s | Slight wrist turn and low hand adjustment, followed by a quiet head glance. |
| [Editor_idle_sleeve_glance.miianim](Editor_idle_sleeve_glance.miianim) | 13 s | Briefly raise one sleeve, look down at it, linger and lower the arm. |
| [Editor_idle_quiet_palm.miianim](Editor_idle_quiet_palm.miianim) | 12 s | Relax one hand into a low open palm while tilting the head slightly. |
| [Editor_idle_loose_arm.miianim](Editor_idle_loose_arm.miianim) | 15 s | Leisurely extend one forearm away from the waist, hold and release it. |
| [Editor_idle_thoughtful_pause.miianim](Editor_idle_thoughtful_pause.miianim) | 14 s | Look slightly up and aside with a small torso turn, then face forward. |

## Close-camera choices

Use these for eyes/eyebrows, face, facial hair, glasses, hair, mole, mouth and nose editing. Files ending in `_upper` animate only the neck rotation. The root, torso, arms, legs and head origin stay fixed, keeping a chest-up or head-and-shoulders crop stable. Turns are at most eight degrees and tilts are restrained. Keep the entire hair silhouette in the crop.

| File | Length | Motion |
| --- | --- | --- |
| [Editor_idle_attentive_upper.miianim](Editor_idle_attentive_upper.miianim) | 11 s | Tiny chin lift and an attentive near-front look. |
| [Editor_idle_glance_left_upper.miianim](Editor_idle_glance_left_upper.miianim) | 13 s | Slow left glance, short hold and unhurried return. |
| [Editor_idle_glance_right_upper.miianim](Editor_idle_glance_right_upper.miianim) | 12 s | Slightly raised chin during a right glance and hold. |
| [Editor_idle_curious_tilt_upper.miianim](Editor_idle_curious_tilt_upper.miianim) | 14 s | Small curious head tilt with a relaxed pause. |
| [Editor_idle_quiet_nod_upper.miianim](Editor_idle_quiet_nod_upper.miianim) | 10 s | One soft nod with a tiny recovery lift. |
| [Editor_idle_upward_thought_upper.miianim](Editor_idle_upward_thought_upper.miianim) | 15 s | Look slightly up and aside, hold as if thinking, then return. |
| [Editor_idle_downward_glance_upper.miianim](Editor_idle_downward_glance_upper.miianim) | 12 s | Small downward look followed by a slow return to the camera. |
| [Editor_idle_look_between_upper.miianim](Editor_idle_look_between_upper.miianim) | 16 s | Look to one side, pause at the camera, then glance to the other. |
| [Editor_idle_relaxed_listen_upper.miianim](Editor_idle_relaxed_listen_upper.miianim) | 14 s | Listening tilt, mild nod and long gradual release. |
| [Editor_idle_neck_settle_upper.miianim](Editor_idle_neck_settle_upper.miianim) | 13 s | Two modest diagonal neck adjustments before facing forward again. |

## Authoring

Source: `tests/MiiAnim.Core.Tests/Authoring/EditorIdleAnimations.cs`. Run `EditorIdleAnimationWriter.WriteEditorIdleAnimations` to export only these twenty filenames. This writer does not recreate the editor's action reactions.

`EditorIdleAnimationTests` checks both body rigs, stationary feet, hand/body and head clearance including compact Miis, quiet neutral loop seams, slow movement, Normal-only facial previews, fixed close-camera body poses and file round trips. Collision checks approximate a typical FFL head with medium hair; unusually large hair should be checked in WheelWizard's actual crop.
