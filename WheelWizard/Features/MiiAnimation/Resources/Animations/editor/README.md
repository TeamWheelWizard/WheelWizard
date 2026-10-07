# Editor animations

The editor contains one action folder per current editor control. Each newly covered action has two different reactions; favorite removal also has a foot-stomp alternative. The existing `favoritecolor/Shirt_color_surprise.miianim`, `gender/Become_boy.miianim`, and `gender/Become_girl.miianim` are retained as authored.

The [idle folder](idle/README.md) contains twenty ambient loops: ten standing motions and ten fixed-body close-camera choices. They keep the selected face unchanged throughout and share a neutral loop pose. Idle authoring and its writer are separate from the action reactions below.

The [interaction suite](interactions/README.md) adds three playful front-click reactions and separate backward flinches for the head, body, left leg and right leg. They return to the idle pose and keep the selected facial features unchanged.

The new clips are authored in `tests/MiiAnim.Core.Tests/Authoring/EditorAnimations.cs`. `EditorAnimationWriter.WriteEditorAnimations` writes their exact relative paths through `WheelWizardSampleWriter.WriteRelative`. The full catalog now has 31 generated filenames in `EditorAnimations.All`.

For the requested revisions, run only `EditorAnimationWriter.WriteRevisedEditorAnimations`. It uses `EditorAnimations.RevisedPaths` to write the 11 revised existing filenames plus `Favorite_removed_stomp.miianim`, preserving unrelated clips that may be undergoing manual edits. Hair and mouth filenames retain their original paths so existing open-file references remain useful, while their displayed animation names describe the new inspection motions.

## Camera and playback

- Canonical rig coordinates use +Y up and +Z forward toward a front camera. The Mii's left is +X. Place the floor at root Y = 0.
- Names ending in `_upper` are the close-camera versions for facial controls. Use a chest-up or head-and-shoulders crop. They keep the root, hips, chest, legs, and arms fixed at rest. The head origin stays fixed, with small neck rotations and short expression changes. No hand enters the face area.
- The other facial versions use larger three-quarter views and low gestures for a full-body or medium shot. Keep the full head and hair silhouette in the frame; none relies on touching the edited feature.
- Every new clip is 60 fps, returns to the Mii's Normal expression and rest pose, and holds that pose for the last twelve frames. The upper variants also start at rest, so their loop boundaries match. Play action reactions once and switch back to the shared idle afterward.
- `Editor_enter_wave` starts at X = 86 facing screen left and walks to the origin. It needs room at the right edge of a full-body shot. `Editor_enter_ready` is a stationary alternative.
- Expressions are FFL expression IDs, not edits to the Mii's stored eyes, eyebrows, or mouth. Normal is used for most facial-preview frames so the chosen feature remains visible. Both mouth clips stay Normal for every frame, including their transitions and rest holds.

## Clip catalog

| Folder | Clip | Motion and intended use |
| --- | --- | --- |
| enter | `Editor_enter_wave` | Walk in from the right, step to face the camera, and wave with both eyes unchanged. No blink or wink. 2.9 s. |
| enter | `Editor_enter_ready` | Low open hands, small welcome bow, then an attentive look. 2.4 s. |
| body_shape | `Body_shape_stretch` | Plant both feet, gather low, and stretch the arms outward while looking up. 2.77 s. |
| body_shape | `Body_shape_width_check` | Look down at the waist, frame the sides, then open the arms to compare width. 2.9 s. |
| eyes_eyebrows | `Eyes_eyebrows_blink_test` | Curious alternating tilt, one slow blink, then a double blink. 2.4 s. |
| eyes_eyebrows | `Eyes_eyebrows_wink_upper` | Close-up left wink, right wink, and blink with tiny tilts. 2.2 s. |
| face | `Face_angle_check` | Low presenting palms and two deliberate three-quarter silhouette holds. 2.9 s. |
| face | `Face_portrait_upper` | Slow small-angle portrait turns with a quiet blink between sides. 2.6 s. |
| facial_hair | `Facial_hair_proud_chin` | Proud chin lift and side inspection, hands below and behind the beard. 2.9 s. |
| facial_hair | `Facial_hair_inspect_upper` | Modest chin lift and two restrained inspection holds. 2.4 s. |
| favorite | `Favorite_selected` | Excited gather, low open-armed thank-you, and a friendly two-eye smile. 2.8 s. |
| favorite | `Favorite_removed` | Frustrated droop with three stationary purple overhead squiggles, then an accepting low shrug. 2.5 s. |
| favorite | `Favorite_removed_stomp` | Shift onto the left foot, hold the right foot raised, stomp sharply with a small dust impact, then settle. Purple frustration lines mark the impact. 2.7 s. |
| favoritecolor | `Shirt_color_present` | Shirt-tinted poof at frame 1, then inspect the new shirt, open the arms to expose the color, and present it. 2.8 s. |
| favoritecolor | `Shirt_color_compare` | Raise and hold each sleeve for 36 frames before lowering it, ending with a satisfied nod. 3.7 s. |
| favoritecolor | `Shirt_color_surprise` | Preserved existing authored shirt-change surprise. |
| gender | `Become_boy` | Preserved existing stomp, strength pose, and transformation timing. |
| gender | `Become_girl` | Preserved existing hop twirl, transformation, and finishing wave. |
| glasses | `Glasses_look_over` | Studious side glances, chin down then up, with a deliberate blink. Both arms stay at rest. 2.73 s. |
| glasses | `Glasses_focus_upper` | Slower focusing turns and nods, one brief eight-frame blink, and hands fixed down. 3.53 s. |
| hair | `Hair_swish` | Displayed as “Haircut inspection”: slow controlled turns with two 36-frame side holds and no swishing or hair physics. 3.6 s. |
| hair | `Hair_inspect_upper` | Quiet side holds and small chin dips for a close hairstyle preview. 2.6 s. |
| mole | `Mole_cheek_reveal` | Offer both cheeks, then hold a small front-facing tilt. 3 s. |
| mole | `Mole_find_upper` | Searching micro-tilts and a front-on hold; the expression stays Normal. 2.4 s. |
| mouth | `Mouth_say_hello` | Displayed as “Mouth angle inspection”: understated head angles and a held low palm-up gesture. Expression stays Normal throughout. 2.4 s. |
| mouth | `Mouth_smile_test_upper` | Displayed as “Mouth close inspection”: small neck angles with fixed arms and body. Expression stays Normal throughout. 2.2 s. |
| name | `Name_introduction` | Palm-up introduction with two speech beats and a greeting nod. 2.5 s. |
| name | `Name_thinking` | Upward thinking glance, consideration of both sides, then a deciding nod. 3 s. |
| nose | `Nose_profile_check` | Hold each profile to expose depth, with the hands low. 3 s. |
| nose | `Nose_sniff_upper` | Two small sniff pulses and a tiny confirming nod. 2.2 s. |
| randomize | `Randomize_shuffle` | Short side steps, a closed-eye swap beat, then a surprised reveal. 3.2 s. |
| randomize | `Randomize_reveal` | Compress into a huddle, swap during a blink and a large body/head poof, then open out confidently with no wink. Particles are still alive at reveal and clear before done. 2.7 s. |
| save | `Save_proud_bow` | Small finished-result bow and a low palm-up presentation. 2.9 s. |
| save | `Save_relief` | Relieved exhale, relaxing shoulders, then a quick confirmation nod. 2.4 s. |

## Markers and checks

All new clips end with `done`. Facial clips also include `feature_visible` at a Normal-expression inspection beat. Other optional markers are `arrived`, `ready`, `body_visible`, `favorite_selected`, `favorite_removed`, `color_visible`, `name_visible`, `name_decided`, and `saved`.

The two randomize reactions include `randomize` during a closed-eye beat and `reveal` after the swap. These markers are timing suggestions; the file does not change Mii data. The preserved gender clips keep their original `swap_gender` markers.

`Shirt_color_compare` includes `left_sleeve` / `left_sleeve_end` and `right_sleeve` / `right_sleeve_end` for its equal inspection holds. The stomp variation includes `anticipate`, `stomp_lift`, and `stomp`. Its supporting foot stays planted throughout; the lifted foot lands six frames after the held lift marker.

Purple frustration lines use the procedural `Squiggle` particle shape (ID 6), one particle per fixed offset around and above the head. They have no shirt tint, spawn spread, velocity, or gravity. All new particle effects fade and expire before the final twelve-frame rest hold.

`EditorAnimationTests` checks both body models frame by frame for hand/head and hand/torso clearance, ground penetration, finite interpolation, final rest poses, close-up root/leg/head-origin stability, matching close-up loop boundaries, feature visibility, action coverage, and serialized motion. Revision checks cover Normal-only mouth previews, requested absence of winks/blinks, fixed glasses arms, slower close-up timing with a brief blink, held sleeves and haircut angles, particle size/color/lifetime, and the stomp's actual lift, sharp impact, and planted supporting foot. Close-up geometry also checks a compact height/build profile. The head collision approximation represents a typical FFL head and medium hair; custom extreme hair silhouettes can need additional clearance when integrated with the actual renderer.
