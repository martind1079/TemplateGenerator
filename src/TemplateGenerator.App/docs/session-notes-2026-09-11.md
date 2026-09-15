# Session notes — 11 September 2026

Handover for the next session. What was proven, what it cost, and what comes next.

---

## Where we got to

Four POCs built, running on an iPad simulator against the real codebase, on branch `poc/local-db-field-population`.

| POC | Proves | Page |
|---|---|---|
| Lookup | Options loaded from SQLite, cached, scoped per client, cascading | `Views/Diagnostics/LookupPocPage.xaml` |
| Visibility | Field, section and nested conditions; button-set state | `Views/Diagnostics/VisibilityPocPage.xaml` |
| Enable / disable | Cross-field gating, value discard, read-only vs disabled, `CanExecute` | `Views/Diagnostics/EnablementPocPage.xaml` |
| Hand-built page | A real template page, start to finish, by hand | `Views/JobForms/ExampleAudioPage.xaml` |
| Multi-page | State across pages, branching routes, save and resume | `Views/JobForms/Example*Page.xaml` |
| Step selector | Direct movement between pages, reachability rules | `Views/Controls/FormStepSelectorControl.xaml` |

Reference data now seeds from the real exported CSVs in `mauimobileapp/Data/`.
Techniques are written up in `docs/form-patterns-guide.md`.

---

## The number that matters

Page 1 of the example template took roughly **two hours** by hand — and it is one of
the simplest pages in the estate: mostly static labels, four checkboxes, one
conditional section.

Set against the target:

| | Template 1 | Template 2 |
|---|---|---|
| Pages | 2 | **16** |
| Elements | 102 | **496** |
| Input fields | 60 | **310** |
| Scripts | 42 | **252** |

One example template is **5× the elements and 6× the scripts** of the template just
hand-built. And that is one of 33 families; the whole estate is 27,801 fields and
7,244 script handlers.

**This is the case for the generator, stated in hours.** Hand-conversion does not
scale to 33 templates, and the arithmetic is now defensible rather than asserted.
Worth putting in front of whoever is planning the work.

---

## Finding: navigation is a graph, not a sequence

The biggest thing learned today, and it shapes the multi-page POC.

One example template has **41 `changePage` calls across 39 distinct edges**. The shape:

```
DPA → Job Sheet
         ├─ Case Resolved ─────────┐
         ├─ Contact Made (Tenant)  │
         ├─ Contact Made (3rd)     │
         ├─ Refused ───────────────┤
         ├─ Deceased               ├─→ Building Information ─┐
         ├─ Abandoned / Empty      │   Vehicle Information ──┴─→ Completion
         ├─ Access Not Gained      │
         ├─ No Contact Made        │
         └─ Gone Away ─────────────┘
```

Job Sheet fans out to **nine outcome pages** depending on what happened at the
door. They converge on Building and Vehicle Information, then Completion.

So the multi-page POC is not "Next / Back". It is a **decision tree driven by an
answer**, and the design needs to handle:

- branching to one of nine pages from a single answer
- convergence back onto a shared tail
- returning to a page already visited (`Vulnerable → CaseResolved`,
  `Completion → CaseResolved`)
- state shared across pages — one form, many screens, one ViewModel or several?

### Worse: navigation is entangled with visibility

`changePage` is not called from a Next button. It sits **inside `OnValueChange`
handlers**, mixed in with visibility assignments:

```lua
if Checklist1.value == "Yes" or CaseVulnerable.value == "Yes" then
    CaseNumber.visible = false;
    CaseNumberVulnerable.visible = true;

    if string.sub(Reference.value, 1, 4) == "ASLB"
       or string.sub(Reference.value, 1, 6) == "ASBHM1" then
        --
    else
        Vulnerable.visible = true;
        form.changePage("Vulnerable");
    end
end
```

Two things to notice:

1. **Answering a question can navigate you.** Visibility and routing are decided in
   the same handler, so they cannot be translated independently.
2. **`string.sub(Reference.value, 1, 4) == "ASLB"`** — the form routes differently
   based on a **case reference prefix**. That is client-specific business logic
   hidden in a string comparison, with no comment explaining it. There will be more
   of these, and they are exactly the rules that get silently lost in conversion.

**Action:** grep the estate for `string.sub(Reference` and catalogue every prefix
rule before any conversion work starts. These are unrecoverable once missed.

---

## Multi-page POC — what it settled

Committed as `b011d30` on `poc/local-db-field-population`.

- **Answers live on a model, not a ViewModel.** ViewModels are `AddTransient`, so
  one is created on every navigation and anything held on it is lost. The report
  is held by a singleton service and serialised into `ActivityDiaryEntry.LogData`,
  the same mechanism the activity diary already uses. This is the single thing
  that makes a 16-page form possible.
- **Routing is a lookup table, not control flow.** `FormNavigator` turns the
  template's chain of elseifs into a dictionary. It can be unit tested without a
  UI, and a generator can emit it as data.
- **Reachability is derived from the same table.** `FormSteps` decides which
  pages the selector may jump to, so the selector and the navigator cannot
  disagree. Choosing "Instruction Cancelled" skips the outcome page, and the
  selector disables it rather than letting the agent walk into a skipped page.

### Two failures that the compiler cannot see

Both cost real time, and both matter more at scale than they did here.

| Symptom | Cause |
|---|---|
| Page crashes on navigation, builds fine | A `StaticResource` key that does not exist. The house palette spells it **`Grey`**; the `Gray` ramp in `Colors.xaml` sits inside a commented-out block of MAUI defaults and shadows the real names. |
| Saves fine, resumes empty, no error | `System.Text.Json` skips read-only properties of complex type on deserialise. Answer sections need setters. |

The second is the worse of the two. Save and resume is how a visit survives being
interrupted on a doorstep, so it fails exactly when an agent has already lost the
conversation. Every section of every converted template needs a setter.

### Also fixed

The flyout was a plain stack with no scroller, so menu items past the bottom of
the screen could not be reached. That is a real bug in the shell rather than POC
scaffolding, and worth taking to the real repo on its own.

### Left open

Every page calls `Refresh` in `OnAppearing` to re-point bindings at the shared
report. It works, but it is bookkeeping the framework should be doing. The
alternative is moving change notification onto the model so pages observe it
directly. Decide before the generator hardens.

---

## Next sessions

See **`docs/generator-plan.md`**. In short: close the last three unknowns
(reference-prefix rules, validation, repeated blocks), then build the generator
as structure and bindings in one pass, leaving logic to a human.

---

## Open questions for the team

- **Who wraps `DrawingView` and `MediaPicker`** as framework controls? Both are
  proven in the prototypes but absent from the framework. Signature appears in 28 of
  33 templates, Photo in 27.
- **SDK version.** `global.json` pins 10.0.301; this machine has 10.0.400. Everything
  currently builds from outside the repo to dodge it. Which are the team on?
- **`Descriptions.csv` and `Descriptions2.csv` are byte-identical**, and the `Action`
  column is empty throughout — so the FormWorks query
  `select Action from Descriptions2` returns nothing. Export artefact, or is that
  lookup genuinely dead?
- **Binding-failure diagnostics.** A wrong binding path should be a build warning
  (`XC0022`) with `x:DataType` set, but `{Binding recordingUnderway}` passed the
  build today. Worth turning those warnings into errors.

---

## Repo housekeeping

- `MAUI_APP` is its own git repo, baseline 
- `route-planner/.gitignore` excludes `MAUI_APP/` so it cannot reach a personal
  GitHub account.
- `mauimobileapp/Data/Vehicles.csv` holds real staff names, emails and vehicle
  registrations. Currently gitignored. **Decide whether it should be committed** —
  git history is permanent and travels to every clone.
- `./scripts/run-sim.sh` builds, installs and launches. Use `--clean` sparingly: it
  clears the keychain as well as app data, and must, because on iOS the keychain
  outlives an uninstall.

---

## Gotchas learned the hard way

Full detail in `docs/form-patterns-guide.md`; these are the ones that cost time.

| Symptom | Cause |
|---|---|
| Change doesn't appear in the app | **Build failed** — the script reinstalls the previous `.app`. Read the output. |
| Empty flyout, no way to log out | Keychain says signed in, database has no user. Never `simctl uninstall` to clear data. |
| Bordered box with nothing in it | `CollectionView` inside a `ScrollView` has no height. Use `BindableLayout`. |
| Crash on opening a page | XAML casing — `spacing` not `Spacing`, `Control:` not `controls:`. Property errors surface at **runtime**, not build. |
| `InitializeComponent` does not exist | `x:Class` and the code-behind `namespace` disagree. |
| Field never appears despite correct logic | Computed property not notified. `SetProperty` only raises for the property it sets. |
