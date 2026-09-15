# Host app integration

What the converter needs from the app it generates into, and what this repository provides.

Written 14 September 2026, after pointing the converter at this app and compiling its output.
`converter-decisions.md` explains why the tool is shaped the way it is; this is the other
side of that seam.

---

## The short version

The converter asks less of a host app than it looks. Its generated C# depends on
`CommunityToolkit.Mvvm`, `System.Text.Json` and `Microsoft.Extensions.DependencyInjection`
and nothing else. Everything specific to a host is three style keys, three plumbing types
and six controls — and four of the six are ordinary MAUI.

Measured rather than assumed: Occupancy Report V18, four pages and 177 fields, generates in
0.09 seconds, passes the generator's own verification against this app, and compiles.

## Destination folders

No adjustment was needed. The tool takes `--app <dir>` and writes to `Views/Generated/`,
`ViewModels/Generated/` and `Models/Generated/`, which is this project's layout already:

```
cd tools/FormWorks.Cli
dotnet run -- generate --template "Occupancy Report V18" --all-pages \
    --app ../.. --namespace TemplateGenerator.App
```

`--namespace` matters. It defaults to the original app's root namespace, and every generated
file, `x:Class` and `xmlns:controls` is built from it.

The one place our layout differed is controls. Generated XAML declares
`xmlns:controls="clr-namespace:{root}.Views.Controls"`, so house controls live in
`Views/Controls`, not the `Controls` folder the scaffold started with.

## What the app has to provide

### Style keys

Generated pages name exactly three, and commit to no appearance of their own. They are
defined in `Resources/Styles/FormStyles.xaml`, and changing what they mean needs no
regeneration.

| Key | Applies to | Used for |
|---|---|---|
| `FormFieldCaption` | `Label` | The label above a field |
| `FormFieldInstruction` | `Label` | Static instruction text the template carries |
| `FormDivider` | `BoxView` | A template `Line` element |

The generator refuses to write anything if a key it names is absent, because a missing
resource key is not a compile error — it crashes the page when it is shown.

### Plumbing types

| Type | Namespace | What it is |
|---|---|---|
| `IFormStore<T>` / `FormStore<T>` | `Services` | Holds the report. Singleton, because generated view models are transient and a multi-page form must keep its answers across navigation |
| `FormStep` | `Models.JobForms` | One page in the step strip: title, route, current, enabled, complete |
| `FieldProblem` / `FieldProblemKind` | `Models.Forms` | Why one incoming value could not be applied to a report |

`FormStep` was carried over from the original app unchanged apart from its namespace. The
other two are written here.

### Controls

Generated XAML names six controls in `Views/Controls`. The contract is narrow — one or two
bindable properties each — so the implementations are ours.

| Control | Contract | Notes |
|---|---|---|
| `SubSectionControl` | `Title`, implicit content | Needs `[ContentProperty]`: generated markup nests fields directly inside the element |
| `FormFieldError` | `Message`, `IsArmed` | Takes no space when silent. `IsArmed` is off until the form is submitted, so a half-filled page is not red |
| `FormStepSelectorControl` | `Steps`, `StepCommand` | Carried over from the original app, repointed at this palette |
| `NullableDatePicker` | `Date` (`DateTime?`) | MAUI's DatePicker has no empty state |
| `NullableTimePicker` | `Time` (`TimeSpan?`) | MAUI's TimePicker defaults to midnight |
| `PhotoCaptureControl` | `PhotoPath` (`string?`) | `MediaPicker` is part of MAUI; no package needed |

Everything else a generated page uses is stock: `Entry`, `Editor`, `Picker`, `CheckBox`,
`Label`, `BoxView`, `Button`, `Grid`, `VerticalStackLayout`, `ScrollView`.

#### `[ContentProperty]` also governs the control's own XAML

`SubSectionControl` points `[ContentProperty]` at `SubSectionContent` so that generated markup
can nest a section's fields directly inside the element. That attribute applies to *every*
implicit child of the type, including the one in the control's own XAML file — so a template
written as a bare `<Border>` child is assigned as the control's content rather than its
chrome, and the control then binds its content to a tree containing itself.

It does not fail. It spins: 100% of a core, forever, the first time a page holding one is
constructed. The chrome therefore sits inside an explicit `<ContentView.Content>`, and
anything else given a content property has to do the same.

#### Inputs get their edge from a platform handler, not the style sheet

The converter emits bare `Entry`, `Editor` and `Picker` controls — 294 of them across the
templates converted so far — and names no appearance of its own. MAUI exposes no stroke on
any of them, so out of the box a field is whatever the platform draws: faint on a text field,
nothing at all on a text view.

Worse, a `Picker`, `DatePicker` and `TimePicker` are all text fields underneath on UIKit, so
once they are outlined they become indistinguishable from a text box and from each other.

`Handlers/InputAppearance` appends to the Entry, Editor, Picker, DatePicker and TimePicker
handler mappers, giving each input a 1px rounded border in `InputBorder` and hanging an SF
Symbol in the right view of the three that are not text boxes: `chevron.down`, `calendar` and
`clock`. It also puts a 44pt floor under any input whose page sets no height, because
replacing the native border takes away the padding that came with it.

Two things worth knowing. What arrives differs by platform — a `DatePicker` is a text field on
iOS and a native `UIDatePicker` with its own chrome on Mac Catalyst — so the handler matches on
what it was actually given and leaves anything unrecognised alone. And the border colour is
resolved when the mapper runs, so a theme switch while the app is open will not repaint
existing fields until they update.

Because it is a handler rather than markup, it reaches generated and hand-written pages alike
and needs no regeneration. `NullableDatePicker` and `NullableTimePicker` therefore draw no
border of their own: they blank the inner picker's format while unanswered and lay a
placeholder over it, rather than covering the field with a panel, which would hide the glyph
that says what it is.

#### The two nullable pickers are not a style choice

They exist because an untouched date field that shows today's date is indistinguishable from
one a person answered today, and an untouched time reads as 00:00. That is harmless while
nothing leaves the device and wrong the moment answers are sent anywhere. The tool's own plan
lists this as the thing to fix before anything sends answers.

#### Wrapped controls must forward `Unfocused`

Where a template watches a field with an `OnBlur` handler, the emitter writes
`Unfocused="OnAnswerLeft"` onto the control element itself — including onto
`NullableDatePicker` and `NullableTimePicker`, twelve times in Occupancy Report V18 alone.

Focus does not bubble out of a wrapped control, so a `ContentView` wrapper never raises
`Unfocused` when the control inside it loses focus. Both pickers therefore declare
`new event EventHandler<FocusEventArgs> Unfocused` and raise it from the inner control, with
themselves as sender: the page's handler reads `AutomationId` off the sender to know which
answer the field holds, and that is set on the wrapper.

Without this the handler compiles, wires to an event that never fires, and the rule silently
never runs. Anything else wrapping a focusable control has to do the same.

## Converting

Conversion is a build step, not something the running app does: generated XAML is compiled,
so a converted template becomes an openable form at the next build. `scripts/convert` is the
whole loop.

```
scripts/convert "Some Survey V4"        # convert, then rebuild
scripts/convert --all-latest            # every latest-version template in the estate
scripts/convert --ios "Some Survey V4"  # rebuild for the simulator instead
```

It looks in this repository's `templates/` folder first, then in the estate, and passes
`--isolate` for you.

**The orchestration lives in the library.** `TemplateConverter.Convert` takes a
`ConversionRequest` and returns a `ConversionResult` — every count, file path and problem the
run produced, and no console output. The command line is a printer over that result.

**Generated routes classes state their own name.** `RoutesEmitter` emits a `TemplateName`
const beside `EntryRoute` and `Pages`. `GeneratedTemplates` finds every generated routes class
by reflection and reads those, so the menu lists what has been converted without a table
anybody maintains. `TrimmerRootAssembly` keeps them from being trimmed, since nothing
references them statically.

### One template per prefix

An app holding more than one converted template **must** pass a per-template class-name
prefix — `--isolate`, or `--prefix` with a value of your own. Page classes are otherwise named
after the page alone, and page names repeat across templates: Closure, Photographs, Next.
Without it one template's `ClosureAnswers` silently overwrites another's, and the app stops
compiling with errors in a file nobody edited.

### Generated output is client material

A converted template's pages, answers and rule tables carry the client's questions, option
lists and validation messages — the same material as the estate, transcribed into C#. The
estate is deliberately not in this repository, and neither is what the converter makes of it:
`Views/Generated`, `Models/Generated` and `ViewModels/Generated` are gitignored except for the
synthetic demo template's output, so a fresh clone still has a form to open.

## Changes made to the converter

**`Emit/GuardRenderer.cs` — an empty test against a checkbox.** FormWorks holds every answer
as text, so a template asks whether a field was answered by comparing it to `""`. The
renderer turned that into `x is null` for any non-text type, which is right for `DateTime?`
and `TimeSpan?` and does not compile for a `bool`. A checkbox cannot be unanswered, and Lua
agrees — comparing a boolean to a string is false there too — so the guard now renders as a
constant. Regression tests are in `GuardRendererTests`.

**`Emit/TemplateConverter.cs` — the conversion itself, moved out of the command line.** It was
`Commands.Generate`, which meant a second caller had to reimplement the order the emitters
depend on each other in. Behaviour is unchanged and the CLI's output is the same.

**`Emit/TemplateConverter.cs` — `PrefixFor`, and a required `RootNamespace`.** The first is the
class-name stem that keeps two templates in one app from colliding, exposed as `--isolate`. The
second removes a default that named one particular app: a plausible wrong namespace produces
output that will not compile, and there is no sensible generic value.

**`Emit/RoutesEmitter.cs` — a `TemplateName` const.** So a host app can list what it has
converted without inferring a template's name from a class name.

Worth noting how the checkbox fault surfaced: the generator's own verification passed. It checks bindings,
resource keys and setters, which are the failures a compiler cannot see; this was the
converse. Both checks are needed, which is what `generator-plan.md` says. It had not been
caught because only iOS had been built, and the fault is in a rule shape rather than a
platform.

## How the app is wired to it

**Generated forms are pages, not views.** Each carries its own step strip and Next/Back
routing, so the host does not wrap them in form chrome of its own. `AppShell` calls
`GeneratedTemplates.RegisterShellContent`, which gives every generated page an absolute route
and hides it from the flyout; the menu lists templates and navigates to each one's
`EntryRoute`. `MauiProgram` calls `AddGeneratedTemplates`, which registers each template's
pages, view models and report store.

Neither names an individual template. The phase-one form viewer — a host page hosting a
generated view, with a hand-written catalogue behind it — was retired when this landed.

## Still open

- **`MultiSelection` renders as a single-select `Picker`.** A fidelity gap in the control
  vocabulary, not a missing component.
- **Signature fields are skipped**, for want of a `DrawingView` wrapper.
  `CommunityToolkit.Maui` has one; adding it would be a one-line vocabulary change.
- **First navigation to a large page takes a few seconds** in a Debug build — about seven for
  a 127-field page, against 0.67s to construct it. That looks like the Mono interpreter rather
  than the layout; worth measuring on a Release build before treating it as a problem.
- **`dotnet build -t:Run` does not always rebuild first.** It has launched a stale binary on
  the simulator; build the target framework explicitly before running it.
