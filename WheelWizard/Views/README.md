# UI ownership

- `Components/`: reusable rendering controls and ordinary UI state. No feature services, navigation, or workflows.
- `Patterns/`: reusable interactions that coordinate controls and own a workflow. Composition alone does not make a pattern; create this folder when one is needed.
- `Shell/`: application layout, navigation, startup, and controls specific to the shell.
- `Dialogs/`: shared dialog workflows and their in-window modal infrastructure.
- `Styles/`: shared resources and styles for native Avalonia controls.
- `DesignTime/`: previews, development tools, and the component gallery.
- `Features/<feature>/Views/`: feature pages and feature-specific rendering controls. Their presentation models belong alongside their feature, not in shared components.

## Control contracts

Prefer native Avalonia properties, events, commands, and flyouts. Add a custom property only for behavior or rendering the native control does not expose.

- `Button`: native `Click`, `Command`, `CommandParameter`, `Content`, and `Flyout`; `Text` and `IconData` provide the common label shortcut. `Variant` and `Size` select appearance.
- `LinkButton`, `TileButton`, and `ListActionButton`: button appearances with the same native activation contract. A list action does not know which page it opens; the caller owns that action.
- `TextField`: labeled text entry with a two-way `Text` binding, `PlaceholderText`, and optional `ErrorText`. A nonblank error message displays the error state; null, empty, or whitespace clears it. The caller owns validation and submission rules. There is no warning validation state or separate error flag to synchronize.
- `StatusBadge`: generic status rendering. Community roles and special community counts live in WheelWizardData's views.
- `HoverGlow`: pointer-following decoration shared by button templates. Render transforms keep its movement out of layout.

Use property selectors for appearance variants and pseudo-classes for interaction state. Preserve the caller's data context. Templates own presentation; callers own feature actions.

## Desktop and Couch presentation

`Layout` owns the single native application window. Its existing desktop composition and `Shell/Couch/CouchView` are alternative presentations, not separate application instances. `CouchMode.IsEnabled` is an inherited attached property: feature views use it to rearrange presentation while retaining their services and editing state.

Mode switching synchronously moves the same `ContentArea` host between presentations. Do not navigate, recreate the current page, or refresh a selected profile to switch views. Detach/attach can occur in the same UI turn: pair subscriptions on attachment and defer terminal page-model disposal until the page remains detached. Capture desktop layout values while attached rather than resolving inherited resources during detachment.

`NavigationService` and its unsaved-work guards remain the authority for route changes. `ICouchNavigationPage` supplies an actionable entry focus target and consumes Back only for a real inner transition; the shell handles outer navigation. Do not focus the page root or hidden panels as a recovery target. Rebuilt editor actions restore focus by semantic identity, with an enabled sibling as the fallback.

Couch Home uses `FOCUSED_USER` and the existing license service, not a separate selected-license state. Its native button opens `UserProfilePage`; Play remains the preferred controller entry target. `MiiAnimatedImage` reuses the full-body camera, `HomePlayer` idles, animation preference, and existing still/resource fallbacks. Keep the lobby height constrained by the scroll viewport without a fixed minimum height, including at increased Window Scale, so the model's desired image size cannot push its name and actions off-screen. Subscribe to license polling and relevant settings only while Home is attached in Couch View; release the preview and subscriptions when leaving. An empty primary slot must not borrow another license's Mii. Desktop Home keeps its original composition.

Generated editor and mod actions use stable `Tag` identities to restore focus after rebuilding. Never identify an action by its translated label, icon, or child index. At a mod reorder boundary, fall back to that mod's More Actions button.

The Mii editor shares `SegmentedControl`/`SegmentOption` selections between desktop and couch presentations. Controller confirmation selects the focused option; focus restoration uses option identity rather than rebuilding button-only variants. Optional “none” entries remain selectable but are outside the present-variant previous/next cycle.

`CouchInputController` maps SDL3 gamepads and keyboard actions to native control/focus semantics. Buttons use scoped directional focus search rather than arrow events that can change radio selection. Sliders adjust on their own axis and leave on the perpendicular axis; their synthetic keys use the native editing path rather than Avalonia's separate gamepad engagement state. Scroll-clipped controls remain reachable and are brought into view. Selectable labels are not couch focus targets.

Couch focus uses a window-scoped native `FocusAdorner`: a rounded teal outline with a translucent inner halo, using the shared brand colors and corner radius. Keep it noninteractive and out of layout; do not thicken control borders on focus or stack button-template rings over it. Desktop retains its existing focus styling.

`SdlGamepadBackend` uses one button-to-action mapping for held-state polling and queued button-down events. Keep those paths aligned so short taps and held presses cannot acquire different meanings.

`CouchInputHints` renders the shell and keyboard legends using Kenney Input Prompts (Steam Deck monochrome set) and localized action labels. `CouchInputGlyphs` shares decoded bitmaps across the footer, keyboard, and text-entry menu; its eight 128-pixel source images and original CC0 license are embedded from `Resources/Images/ControllerPrompts`. Render the original white artwork at reduced opacity for grey prompts, without modifying the upstream assets. Glyphs remain unmirrored in right-to-left layouts. These legends are noninteractive: keep fullscreen in the controller menu and Back in input routing, not in footer buttons.

The active popup child takes focus precedence over its owning modal, then the page/shell. `PopupNavigation` resolves framework popup ownership through placement targets, including nested menus. Back closes the innermost popup before dismissing a modal. Modal Escape handling bubbles so a dropdown can consume it first. Held-button tracking blocks carry-over actions without a timed neutral-input delay.

`ModalContent` always presents through the originating window's `ModalHost`. Call `SetModalOwner(originatingControl)` before asynchronous presentation; prompts owned by a closing modal close with it, without closing unrelated operations. Do not introduce native-window fallbacks for production dialogs. Framework popup/tooltip overlay hosting is configured in application styles, with platform overlay options where available. Native storage-provider portals and OS-managed menus are intentional external surfaces.

Couch mode hides the drawn-window minimize, maximize, and close buttons, as well as modal-header close buttons; controller Back/Escape and each dialog's own actions remain available. **Quit launcher** lives in the couch menu and uses `Window.Close()` so unsaved-work guards still run. Quit is unavailable while another modal owns an operation. Desktop caption controls and the fullscreen menu action are unchanged.

`COUCH_VIEW` persists the presentation preference. `--view=couch` / `--view=desktop` override startup selection without rewriting it; `--fullscreen` applies when starting in couch mode. Fullscreen and presentation are independent. Returning to desktop restores its window geometry and scaling.

Couch entry limits both its preferred 960×540 minimum and its remembered client size to the screen's logical working area (physical working area divided by screen scaling). Set the clamped minimums before assigning the client dimensions so high-DPI screens can fit the window. Returning to desktop resets both minimums to zero before restoring desktop geometry.

**Window Scale** changes couch UI size inside the existing window or fullscreen viewport, not the native window dimensions. `CouchScaleHost` scales the page, navigation, footer, and snackbars; `ModalScaleHost` scales dialogs and the keyboard while keeping native chrome outside the transform. Framework popups inherit their placement target's transform. Keep keyboard width constrained by the viewport rather than fixed. Couch scale previews use the normal Apply/Revert flow and are not clamped against desktop window dimensions; desktop layout and modal scaling remain unchanged.

The only user-facing presentation switch is **Settings → Appearance → Couch View**. Shell chrome and the controller menu link to settings rather than exposing another mode toggle.
