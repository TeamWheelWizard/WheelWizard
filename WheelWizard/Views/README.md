# UI ownership

- `Components/`: reusable rendering controls and ordinary UI state. No feature services, navigation, or workflows.
- `Patterns/`: reusable interactions that coordinate controls and own a workflow. Composition alone does not make a pattern; create this folder when one is needed.
- `Shell/`: application layout, navigation, startup, and controls specific to the shell.
- `Dialogs/`: shared dialog workflows and their window infrastructure.
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
