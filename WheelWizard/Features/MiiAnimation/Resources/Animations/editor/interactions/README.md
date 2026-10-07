# Editor click interactions

Seven one-shot reactions for clicking a front-facing Mii: three playful general reactions and four flinches selected by hit area. All use the existing standing pose, preserve the Mii's Normal expression throughout and finish with a twelve-frame neutral hold. No blink or wink replaces the selected facial features.

## Playful front clicks

Choose among these for a general front click, for example by cycling or picking randomly without repeating the previous choice.

| File | Length | Reaction |
| --- | --- | --- |
| [Front_click_ticklish_shrug.miianim](front_click/Front_click_ticklish_shrug.miianim) | 3 s | Pull back a little, give two ticklish shoulder pulses, then open the hands in a playful shrug. |
| [Front_click_playful_dodge.miianim](front_click/Front_click_playful_dodge.miianim) | 3.2 s | Dodge sideways, peek around, and tease with a smaller lean to the opposite side. Both feet remain planted. |
| [Front_click_mock_scold.miianim](front_click/Front_click_mock_scold.miianim) | 3.6 s | Recoil, raise a “hey, you” palm, wag it twice and give a small mock-serious head shake. |

## Click-area flinches

Use the Mii's anatomical left and right when choosing the leg reaction. From a direct front view, its left leg appears on the viewer's right.

| Hit area | File | Length | Reaction |
| --- | --- | --- | --- |
| Head | [Flinch_head.miianim](flinch/Flinch_head.miianim) | 2.1 s | Snap the neck backward, retreat slightly and recover with a small rebound. |
| Body | [Flinch_body.miianim](flinch/Flinch_body.miianim) | 2.4 s | Recoil through the chest and hips, spread the hands to regain balance, then settle. |
| Left leg | [Flinch_left_leg.miianim](flinch/Flinch_left_leg.miianim) | 2.5 s | Shift onto the right leg, pull the clicked left foot up and backward, look down at it and put it back. |
| Right leg | [Flinch_right_leg.miianim](flinch/Flinch_right_leg.miianim) | 2.5 s | Mirror the left-leg reaction: keep the left foot planted and withdraw the clicked right leg. |

## Playback and framing

Play once, then return to an editor idle at `done`. These files provide animation assets; the application's hit testing decides which file to play. Each starts with `click` at frame 0. The flinches reach `recoil` at frame 6 for the head and frame 8 for the body and legs. The leg reactions have `foot_down` at frame 36. The playful clips include gesture markers such as `shrug`, `dodge`, `peek` and `palm_up`.

Use the full-body editor camera with room around the shoulders and feet. The files face +Z toward the front camera; backward recoil is negative Z. Head and body flinches keep both feet planted. The leg flinches keep only the supporting foot planted until the clicked foot returns. They move the whole body and are distinct from the fixed-body `_upper` idle loops.

Blend briefly from the current idle pose into the reaction if a click arrives mid-loop. Avoid stacking simultaneous reactions; return to an idle or replace the active reaction with a short pose blend when handling another click.

Source: `tests/MiiAnim.Core.Tests/Authoring/EditorInteractionAnimations.cs`. `EditorInteractionAnimationWriter.WriteEditorInteractionAnimations` exports only these seven paths. Headless validation checks both body rigs, default and compact head clearance, foot planting, backward recoil, correct clicked-leg withdrawal, unchanged facial features, final rest poses and file round trips.
