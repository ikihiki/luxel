# UI customization contract

Status: accepted for new UI and Editor controls.

## Layers and precedence

Customization is lowered into the existing `Bindable<T>`/attached-property runtime rather than a second cascade engine. Effective values are resolved in this order, from lowest to highest priority:

1. control built-in fallback;
2. primitive and semantic theme tokens;
3. theme component defaults;
4. `Appearance`, `Variant`, `Intent`, `Size`, and `Density`;
5. reusable immutable sparse appearance patch;
6. the `utilities: [...]` collection;
7. explicit named `[UiParam]` arguments;
8. active explicit widget-state overrides;
9. DevTools overrides.

Replacement utilities use last-write-wins order inside one collection. Additive behaviors such as transitions append or merge. The source-code ordering of named arguments and `utilities` never changes their precedence.

## Utility descriptors

`U` is a public readonly descriptor applied without reflection, assembly scanning, runtime class strings, or `Activator`. Generated `[UiComponent]` factories accept a trailing optional `Utilities utilities = default`, apply utilities first, and then apply named parameters.

Categories are:

- `Property`: paint/value properties such as background, foreground, and opacity;
- `Layout`: width, height, margin, and padding;
- `Attached`: typed parent-layout metadata such as `U.Grid.Column`;
- `State`: nested utilities lowered to a `WidgetState` layer;
- `Transition`: additive state-transition behavior;
- `ControlSpecific`: descriptors whose target type is a specific control;
- `Custom`: an explicitly typed, reflection-free applier for extension packages.

Utilities must not contain child widgets, slots, arbitrary event handlers, services, commands, business state, or reflection-based processing. A target mismatch is an error, not a silently ignored style.

`U.Grid.*`, `U.TreeView.*`, and future scopes are public readonly marker values exposed through C# static extension members. This preserves dependency direction: `Luxel.UI` does not reference controls, while Controls, Editor UI, and third-party packages can add scopes or members.

## Runtime value and state channel

`Bindable<T>`, `SetBase`, `SetState`, and generated `.When(...)` remain the canonical value channel. Appearance patches and utilities lower into that channel or typed attached/behavior metadata. Existing state priority is retained for `Disabled`, `Pressed`, `Hover`, `Focused`, `Checked`, and `Selected`; descriptor shapes must not prevent future compound selectors.

## Invalidation

Changes are classified as:

- **Paint**: update retained effects/nodes without rebuilding structure;
- **Layout**: invalidate measurement and placement;
- **Structure**: rebuild/realize a changed subtree.

A property may advertise reactive `Signal` support only when its invalidation path is implemented. Layout utilities remain fixed values or theme-derived values until layout-signal invalidation is explicit. `NGUI003` rejects direct `Signal<T>` and `Bind.From(...)` values passed to the built-in `Width`, `Height`, `Margin`, and `Padding` utilities at compile time. `NGUI004` rejects those layout utilities inside `When`, `Hover`, `Pressed`, `Focused`, or `Disabled` state utilities until state-driven layout invalidation exists. Each `U` descriptor exposes its stable name, `UtilityKind`, target widget type, and assigned value type (when applicable) for tooling without executing the descriptor. `UtilityTargetAttribute` lets extension packs expose the same target metadata at compile time without a registry; `NGUI005` rejects annotated control-specific utilities when a generated factory's component type is incompatible. Runtime descriptor checks remain as defense in depth. Structural changes belong to slots or child APIs, not ordinary utilities.

## Theme contract

The runtime source is the host-local `UiBuildContext.Theme`. New APIs must not capture or extend static global theme state. Theme evolution uses immutable snapshots or replace-only semantics:

```text
primitive tokens
  -> semantic tokens
  -> component defaults/recipes
  -> Appearance
  -> sparse patch / utilities / named override
```

`Luxel.UI.Tailwind.Tw` is a primitive palette/spacing/radius source. Production controls should consume semantic tokens rather than hard-code raw `Tw.*` values.

Typed attached properties expose a stable ID, owner type, value type, default value, and validation function. The owner identifies the layout/control that consumes child metadata; it does not restrict which child widget may carry the property.

## Appearance patches

Aggregate appearance objects are immutable sparse patches: an omitted field means "inherit the lower layer", not a completed default value. Whole-object signals are appropriate for infrequent recipe/theme changes; field-level signals are appropriate for frequent paint updates. APIs must not hide paint, layout, and structure invalidation differences.

## Slot contract

A slot changes content or a part subtree and is intentionally separate from `utilities`.

- The owner defines when the template is instantiated and which constraints it receives.
- Each produced widget instance has one owner and is disposed with that owner unless ownership is explicitly transferred.
- Runtime replacement requires structure invalidation and disposes the replaced owned subtree.
- Slot content receives the owner theme and state context unless the slot contract explicitly isolates them.
- The slot contract identifies whether the owner or inserted subtree owns hit targets, focus, and accessibility semantics.
- Color, padding, radius, and other scalar appearance changes are not slots.

## Typed attached properties

Parent-layout metadata uses `AttachedProperty<T>` with a stable ID, default, type, and validation. `GridProperties.Column`, `Row`, `ColumnSpan`, and `RowSpan` are the canonical keys. `U.Grid.*` and compatibility methods such as `.GridColumn(...)` write those same keys; `Grid` reads them before layout.

## Gallery documentation

Control documentation uses component-first canonical paths:

```text
Controls/{Component}/Docs
Controls/{Component}/Basic
Controls/{Component}/Playground
Controls/{Component}/Examples/{UseCase}
Controls/{Component}/States/{State}
Controls/{Component}/Accessibility/{Scenario}
Controls/{Component}/Test/{Scenario}
```

`Overview` aliases are not registered. Generated component metadata provides the minimum `Docs` and `Basic` entries; authored stories may replace an exact generated fallback while retaining production identity and generated schema metadata.
