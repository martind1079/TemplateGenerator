# Converter progress

Running log for the FormWorks to MAUI converter. Newest phase at the top. Trimmed as
things land: this records where the work is, not everything it has ever been.

Decisions live in `converter-decisions.md`. The original plan is `generator-plan.md`,
and its sequencing has been revised once, recorded as D2. Converting real templates from
the estate one at a time, with per-template status and issues found along the way, is
tracked separately in `estate-conversion-log.md`.

---

## Phase 2 — emit structure and bindings (in progress)

**Goal.** Template JSON in, a buildable set of pages plus the matching model out, with
no logic at all.

### Landed

All 16 pages of An example template are generated and compile.

| | |
|---|---|
| Pages generated | 16 |
| Fields bound | 298 |
| Generation time, whole template | 0.02s |
| Build result | succeeded, no new warnings |

The 298 bound fields reconcile exactly against the 310 input fields counted in the
session notes: the 12 remaining are Photo controls, deferred because the framework has
no MediaPicker wrapper yet. That is one of the open questions for the team, and it is
now a concrete blocker on 12 fields rather than a general concern.

The plan's target was minutes per template. It is twenty milliseconds, so generation
cost is not a constraint on anything and the emitter can be re-run freely while layout
is iterated.

Four files per page, all under `Generated` folders (D7): the page, its code-behind, the
answers class, and the generated half of the view model. Nothing is written at all if
verification fails (D8).

```
cd tools/FormWorks.Cli
dotnet run -- generate --template "Example Formworks Template.json" --all-pages --app ../../mauimobileapp
```

The MAUI project cannot be built from inside the repository, because the root SDK pin
requires a version that is not installed. Run `dotnet build` from outside the
repository and it resolves normally. That is the same workaround the POC phase used and
D3 explains why it has not been changed.

### What the verifier caught, and what it did not

Verification refused nothing on this template: bindings resolved, resource keys existed,
every answer had a setter. What it did not catch was a backing field emitted without its
semicolon, which passed every semantic check and failed at compile. The checks are
semantic and the compiler is the syntax check. Both are needed and neither substitutes
for the other, which is now written into D8 and covered by a regression test.

### Every page is reachable

Routes, dependency registration and an index page are generated per template, so all 16
pages can be opened and checked. Previously only the one page anybody had wired by hand
could be shown, and a page that compiles but cannot be shown is a page nobody has checked.

The hand-written part is now one flyout entry pointing at the generated index, plus two
calls in `MauiProgram.cs`. That does not grow when a second template is converted.

Reviewing a converted template: open the flyout, choose the template, then any page from
the index.

### It runs

The generated Job Sheet page is wired into the shell and renders on the iPad simulator.
Date and time pickers show the emitted defaults, checkboxes and pickers are live, and
the runtime log carries no binding or XAML complaint. The page reads as a real form:
titled cards, fields in their template rows, instruction text in place. That matters because both POC
failures were runtime-only: a wrong resource key crashes a page when it is shown, not
when it is built.

Wiring is three hand edits, in `MauiProgram.cs` and `AppShell.xaml`: register the page
and its view model, declare a route, add a flyout entry. Sixteen pages would be sixteen
sets of that, which is glue a generator should write rather than a person. Only the Job
Sheet is wired.

### Row packing

FormWorks stores no row marker. Fields carry a width in points against a design surface
of 980 and flow left to right, wrapping when the next one would not fit, so rows are
recovered by accumulating widths and margins against the container. The contact
summary's first row comes to 954 across five fields and the sixth overflows, which is
the whole of the rule. That geometry was already in the parser's raw property bag.

The emitted widths are ratios, not points. Reproducing 980-point columns literally would
put a desktop form on a tablet and cut it off on anything smaller, so each row becomes a
grid with proportional star columns. The contact summary is now five rows of five
columns rather than 25 stacked rows.

Two things were fixed alongside it, both found by reading the output rather than by the
build.

**Static labels were being dropped entirely.** The template carries 30 of them on this
template alone, and they are instructions to the agent: which identity documents count,
whether to reference a document in the audio recording. Silently losing them changes
what the form asks. They are now rendered and take part in row packing.

**Sections and groups were being reordered.** Fields were emitted first and nested
groups after, regardless of template order. A form asks its questions in a sequence and
reordering it changes the form. Items now keep their position.

**Paragraph fields were sizing themselves.** Every one of the 3,197 paragraph fields in
the estate declares a `textLines` count, from 2 to 14, and an editor left to size itself
expands to fill whatever it is given. Heights now come from that count times a line
height held in the control vocabulary, so it is a house-style dial rather than a
constant in the emitter. On this template that produces 38 sized editors between 82 and
302 points. The two largest are the notes fields, which the template declares as 13
lines, so a third of a landscape screen is what they are meant to be.

**Groups were being stacked instead of laid out side by side.** A FormWorks group is a
layout container with its own width, and a section uses two of them to put questions in
columns. Customer Details holds two 490-point groups; Financials holds a 330 beside a
650. Breaking the row on every container stacked those, which is a different form from
the one the template describes. Containers now take part in row packing exactly as
fields do, a titled group renders as a card inside its section, and a group's children
pack against the group's width rather than the page's. Verified on the simulator:
nesting the card control does not collapse its height.

**Fields with no title showed their element name.** Seven captions on the job sheet read
"ContactType2" or "Time3", because a missing title fell back to the element name. Those
are the repeated contact rows, where the template deliberately gives no caption because
row one already carries the column headers. A field the template gives no title has no
caption.

**Untitled sections leaked internal names.** An empty title fell back to the element
name, so the agent saw a heading reading "WhyNoPhotoEvidence". An untitled section is now
a plain grouping with no heading bar.

### Content width and margins

Two template properties the emitter was ignoring.

`contentWidth` is how wide the control itself may draw, as against `width`, which is the
space the field occupies in its row. A field 230 wide whose content width is 140 keeps
its place in the row and its label at full width while the input box is narrower. 4,369
fields across the estate set the two differently, so ignoring it stretched every one of
them. The control also has to be told not to stretch back out once narrowed.

The four margin properties are the template's own spacing between components, so the
emitter now emits them and sets no spacing of its own. A hardcoded gap on top of a
declared margin is two gaps. The dominant margin in the estate is 10 top and bottom, 20
left and right.

Fixing those exposed a third fault. A row that did not fill its container stretched its
only field across the whole width, so a date of birth declared 160 wide filled a 490
group. Rows now carry a filler column for whatever they do not use, unless the shortfall
is under two per cent, which means the field was meant to fill the row.

**Reviewing screenshots.** `simctl` captures the iPad in the frame buffer's orientation,
so a landscape screenshot arrives rotated and is genuinely hard to judge. `sips --rotate
270` turns it upright. Worth doing before reading a layout: two layout faults above were
invisible until the image was the right way up.

### Rows must all divide the same total

A star column takes its share of the width as its own value over the row's total, so two
rows whose fields add up differently do not line up, however close the totals are. The
contact summary has exactly that: four rows totalling 954 points and a fifth totalling
964, because one checkbox in the last row is declared ten points wider than the four
above it. That is a discrepancy in the source template, not in the conversion.

The filler column is what normalises them, and it originally had a two per cent threshold
so a field a couple of points short of the container would not gain a sliver column
beside it. The fifth row's sixteen-point remainder fell under that threshold, so it got no
filler, divided 964 instead of 980, and every field in it sat a little to the right of the
rows above.

There is now no threshold: any remainder at all becomes a column. A field 970 wide in a
980 container gets a ten-point filler and still reads as full width, which costs nothing,
where the threshold cost alignment.

### Reserved title space

`assignSpaceForTitle` is how a row stays aligned when its fields disagree about captions.
A checkbox wears its caption on the right, so without reserved space the tick floats to
the top of a row whose other fields sit under their headings. Estate-wide 809 checkboxes
ask for the space and 1,700 do not, which is the difference between a tick in a table
column and a tick against a statement. The same applies to rows two to five of the
contact summary, whose headings are on row one.

The spacer is a blank caption in the same stack and the same style as a real one, so it
cannot drift out of step with one. A checkbox is also shorter than a text input, so the
row it sits in carries a minimum height matching the input height, and the control
centres in that band. It is a minimum rather than a fixed height, so a caption long
enough to wrap still grows the row.

### Caption placement is per control type

A checkbox reads as a tick against a statement, not as a box with a heading over it, so
the vocabulary now carries where a caption sits: above the control, or beside it. Only
checkboxes use the second, and the identity-evidence block went from six stacked
label-over-box pairs to a single row of ticks with their text alongside.

The caption sits in a star column of a two-column grid rather than in a horizontal stack.
A stack lets its children take whatever width they ask for, so a long caption would run
off the row instead of wrapping and growing the row taller.

### Appearance is not in the generated pages

The generated XAML now carries no colour, font, thickness or size of its own. It names
three style keys and nothing else: a field caption, an instruction line, and a divider.
Those live in Styles.xaml, so restyling a converted form is an edit to the style sheet
with no regeneration and no generator change. The key names themselves are vocabulary
configuration, so a repository with its own naming can point at different ones.

That gives three tiers for any visual change:

| Change | Where | Regenerate? |
|---|---|---|
| Colour, font, border, spacing of a control | Styles.xaml, or a handler mapping | No |
| Which control renders a field type | the control vocabulary, one line | Yes, 0.03s |
| How fields are arranged | the emitter | Yes, 0.03s |

Only the third is really a generator change. The second is a one-line table edit and a
regeneration that costs less than reading this paragraph.

### Buttons

Buttons are emitted for their layout. A button carries a caption and a width like any
other element, so it packs into its row the same way, and it renders in house style.

What it does when tapped is not wired. The generated view model exposes a command per
button whose body is a partial method nobody implements, which compiles away to nothing
rather than throwing. So the button is inert rather than broken, and the seam is named and
waiting for the routing table.

Routing cannot be wired until answers are shared across pages. Every outcome page's Next
button routes on the case reference, which is captured on the Job Sheet, and each page
currently owns its answers with nothing between them. That is the report model the
multi-page proof of concept concluded was necessary, and it belongs to the logic phase
rather than to this one.

### Photo fields

The 12 photo fields are emitted, so all 310 input fields of the template are now bound,
which is the figure the original session notes counted.

A FormWorks photo field holds one image and declares its own height, so the control is a
single tappable box rather than a gallery. `Views/Controls/PhotoCaptureControl.xaml` is a
house control, not a framework one: the framework still has no MediaPicker wrapper and
that is still an open question for the team. It is named in the generator's control
vocabulary, so swapping in the team's own is a one-line change.

The picker hands back a temporary file, so the control copies it into app data. Otherwise
the photograph is gone by the time the report is resumed. Camera and photo-library usage
descriptions were added to the iOS Info.plist, without which the app is terminated rather
than refused.

### Input appearance

A regression worth recording, because the cause is a trap rather than a typo.

Replacing the native iOS rounded border also removes the padding that came with it, so an
input with no height of its own collapses to the height of its text. Generated pages were
fine, because the app's implicit Entry style sets a height of 48. The login page was not:
it declares its own Entry style for text alignment, and an implicit style shadows the
app-level one rather than extending it, so the height went with it. Its fields ended up
about ten points tall.

Fixed in the handler rather than on that page: it now puts a floor of 44 under any input
whose page sets no height. A handler that quietly depends on a style being present would
break the next page somebody writes without one, and only one page in the app has that
shape today.



Borders and the select chevron, both in `Handlers/InputAppearance.cs`.

The house styles never set a border on a text input at all. What shows on an Entry is the
native iOS rounded text field, which is faint and not ours, and an Editor maps to a text
view with no border, so a paragraph field was an invisible box on a white card.

Giving every input the same frame then created a second problem: Picker, DatePicker and
TimePicker are all text fields underneath too, so every one of them looked like a text
box and like each other. Each now carries a glyph in its right view: a chevron, a
calendar, a clock.

Neither can be done in the style sheet, because MAUI exposes no stroke and no accessory on
these controls. Both are still appearance rather than structure, so they apply to
hand-written pages as well as generated ones and changing them needs no regeneration. The
chevron change touched no generated file at all, which is the tiering working as intended.

While reading Colors.xaml for this: `BorderColor` is referenced by `InputBorderStyle` and
`CardStyle` as a dynamic resource and is defined nowhere. A dynamic resource that does not
resolve fails silently, which is why nobody has noticed. Worth a look, separately.

### Open

- What happens to answers on a page the agent routes away from. Fill in Refused, change
  the outcome to Deceased, and the Refused answers are still on the report. The template
  does not say what should happen, so it is a decision rather than a translation.
- Every step in the strip is enabled. Reachability beyond being on the current path, such
  as refusing to jump ahead of unanswered questions, is not implemented.
- The input borders are iOS only. Android and Mac Catalyst need their own mapping, and
  the code is guarded so it does nothing rather than breaking there.
- The border colour is resolved when a control is created, so toggling dark mode does not
  restyle pages that are already built. Pages are transient and recreated on navigation,
  so it corrects itself on the next visit.
- Only the iOS target has been built. Android and Mac Catalyst are untried.
- Tapping a button in the simulator cannot be driven from here, so navigation was proved
  by calling the route at startup rather than by pressing the index button. The button
  handler is three generated lines around that same call.
- Dates use a non-nullable type defaulting to today, so an unanswered date reads as
  answered. Correct for a structure-only pass, wrong for a real form.
- Hidden sections are emitted visible, which the plan calls for at this stage, so the
  page shows internal plumbing such as the contact-rules block.

---

## Phase 1 — read the estate (complete)

**Goal.** Parse every template into a typed tree, and answer the questions the
emitter's design depends on with estate-wide numbers rather than one template's
sample.

### Landed

A command line tool at `tools/`, three projects, no package dependencies.

```
tools/FormWorks.Templates/   parser and analysers
tools/FormWorks.Cli/         the `formworks` command
tools/global.json            SDK pin for the tools only (D3)
```

Run it from `tools/FormWorks.Cli`. The estate path comes from `--estate`, then
the nearest `templates/` folder, so no machine-specific path is
committed. `formworks help` lists the commands.

**Parsing.** All 57 template folders parse with no failures. `--latest` reduces them
to 33 families, 19,032 nodes and 7,244 script handlers, which matches the figures in
`session-notes-2026-09-11.md` exactly and independently.

Templates nest every child in a single-key wrapper keyed by its type, so the tree is
typed by the wrapper rather than by a property. Files carry a UTF-8 byte order mark
that `JsonDocument.Parse` rejects outright. Both are handled in the loader.

**Control vocabulary.** Seventeen node types. The first run flagged four the reader
did not know about, which is the check earning its place: `Image`, `Line`,
`MultiSelection` and `TableCell`. All four are now in the vocabulary and the check
passes clean.

### What the analysis found

**Handler shapes are the headline.** Grouping handlers by their normalised body shows
how much of the estate is one rule repeated:

| Top shapes | Handlers covered | Share of 7,244 |
|---|---|---|
| 5 | 1,846 | 25.5% |
| 25 | 4,028 | 55.6% |
| 100 | 5,401 | 74.6% |
| 500 | 6,637 | 91.6% |

There are 1,092 distinct shapes in total and 577 occur exactly once. So the human
reading task is closer to eleven hundred distinct things than to seven thousand
handlers, and roughly six hundred of those are genuinely one-offs.

Validation, called the largest untouched category at 2,721 handlers, is 277 shapes.
The single most common is a required-field check conditioned on visibility, which
appears in three variants covering over 900 handlers between them.

**Routing is more mechanical than the plan assumed.** In one example template the 41
`changePage` calls sit in 17 handlers, and only 2 of those also set visibility or
enablement. Nine handlers are the identical two-way split on a reference prefix and
four are a single unconditional line. That is 13 of 17 handlers that are data rather
than judgement, which is the basis for the open recommendation below.

**The prefix catalogue is generated, not written.** Across the latest 33 families
there are 146 tests on the case reference and 32 distinct prefix literals. Two traps
the tool now detects on its own:

- `ASLB` and `MSUK` are each tested at both four and six characters, so evaluation
  order decides which branch wins.
- Three rules use the Lua not-equals form. Inverting one of these by eye is silent.

Two other fields are also sliced this way, `OurReference` and `jsSpecial`, so the
rule family is not limited to the case reference.

**Branch conditions do not match option lists.** Checking branches against what a
control actually offers found defects in the source templates, not in any conversion.
In one template alone: 11 branches test values that cannot be selected, 8 values
are tested twice in one chain so the second can never fire, and 14 selectable options
have no branch at all. These are invisible to someone converting by eye.

**Tests.** Package restore was confirmed working, which met the condition D4 set for
revisiting, so `tools/FormWorks.Templates.Tests` now exists. 37 tests, all fixtures
written inline rather than read from the estate, so the suite cannot quietly stop
running on a machine that does not have it. The guard walker is covered first, because
pass two is built on it.

**Routing comes apart completely.** Recovering the guard on each routing call, rather
than just its destination, needs the if/elseif/else structure walked. Across the latest
33 families all 415 routing calls were recovered:

| | Calls |
|---|---|
| Unconditional | 251 |
| Else branches | 30 |
| Guarded by a real condition | 136 |

Every one of the 136 conditional routes decomposed into comparisons a generator can
emit, and no handler failed to parse. This is the evidence behind D5, which moves
routing into pass two as ordered data.

The table is an ordered chain: the first matching entry wins, as the source does. The
compact view shows each branch's own condition and relies on that order. `--full`
expands every guard with its negated siblings, which is correct in any order and
unreadable by design.

### Open

- Sibling fields named with the index last, such as five contact rows named Date1 to
  Date5 alongside Time1 to Time5, are classified as incidental numbering when they are
  really a flattened repeat with the index and the field name transposed. This does not
  change what is emitted, because D6 flattens either way, but the report is wrong and
  should be fixed.
- Reachability is computed from `changePage` targets without regard for whether the
  branch condition can ever be true. The Gone Away page in one example template looks
  reachable to the graph and is dead according to coverage. The two analysers should
  be joined up.
- Routes recovered from form-level scripts have no originating page, so they appear
  with a blank source in the table. Harmless in a report, but pass two will need a
  rule for where such a route belongs.
- Whether a repeated block becomes a collection or a flat set of numbered properties
  is still open. The tool now reports indexed name groups, so this is ready to be
  answered rather than guessed.

---

## Phase 3 — routing (in progress)

### The report

Answers now live on a generated report object, one property per page, held by a singleton
store. Answers cannot live on the pages that collect them: view models are transient, so
anything held on one is gone the moment the agent navigates, and routing reads answers
across pages anyway. This is the multi-page POC's conclusion, generated rather than
hand-written.

`Services/FormStore.cs` is generic and hand-written once. Only the report classes are
generated, so converting a second template adds no plumbing.

### The navigator

The routing table is emitted as typed C#: 41 methods' worth for this template, in template
order, first match wins. An interpreter reading guards by name was the alternative, but
the whole estate references only 22 distinct fields, so it would have bought generality
nobody needs and traded compiler checking for reflection.

Checked on the simulator against a real report:

| Answers | Where Next goes |
|---|---|
| Reference ASBHM1…, outcome Instruction Cancelled | Vehicle Information |
| Reference ASLBBB…, outcome Instruction Cancelled | Building Information |
| Outcome Refused | Refused |
| Outcome unanswered | nowhere |

The first two are the client-specific prefix rule the session notes called the easiest
thing in the conversion to lose. Going nowhere on an unanswered form is the template's own
behaviour; inventing a destination would not be.

A guard naming a field that does not exist is reported and emitted as an explicitly false
condition, never guessed.

## Guards naming Lua's `this`

`this` is the self-reference, meaning the field whose handler the script is. It is not a
name to look up, so the field index never found it and 45 of the estate's 415 routes were
refused as unroutable. Resolving it to the node owning the script clears 43 of them.

Two remain, and they are a defect in two templates rather than a limit of the generator.
Two templates each carry a handler reading
`ProceedToJobSheet.value`, and in both the alias `ProceedToJobSheet` sits on the button
itself rather than on the question. A button holds no value, so the condition can never be
true and the branch is dead. The same handler in Mortgage Arrears Special V39 has the alias
on a selection field, where it works, so it was copied between templates and landed on the
wrong element twice.

Harmless here, because both branches of that handler route to the same page and only a
`focus()` call differs. Reported rather than guessed, which is the point.

The message distinguishes the two cases now: a name that does not exist is a different
fault from a name that exists and answers nothing, and only one of them is the template's
mistake.

## Validation, emitted

Rules run continuously; messages are shown only once the form has been checked, and
nothing checks it yet.

`ShowValidationErrors` on the report is off and stays off: no generated code sets it. The
first cut had Next arm it, which was wrong twice over, because moving between pages is not
a submission and because the flag is form-level, so one Next left every later page red for
the rest of the visit.

For submission, the validator exposes `ValidateAll(report)` to run every page's rules and
`Invalid(report)` to list what is wrong, each field by its fully qualified FormWorks name
with its message. Submission then runs the rules, stops if anything is invalid, and sets
the flag so the agent can see why. Splitting "run the rules" from "is anything wrong"
leaves that decision with the caller.

FormWorks ran every `OnValidate` at a submission trigger rather than as the agent typed,
and the first cut of this ignored that: an untouched page opened covered in red. The
report now carries `ShowValidationErrors`, a submit or next button sets it, and a message
appears only when there is one and the form has been checked.

The rules themselves still run on every change, so a field clears the moment it is
answered rather than waiting for the next submission. That is better than FormWorks did
and costs nothing.

**Messages: mostly one per field, but not always.** Of 871 fields carrying a rule, 401
have more than one, and 39 say something different depending on which branch fires. So a
message per rule earns its place, even though a message per field would be right for the
large majority.

None of the differences come from different events. Where a field's message varies it is
between branches of one handler, which is the ordering the emitter already preserves.

**A bug this turned up.** 429 of the estate's messages are computed rather than stated, and
the emitter was writing the expression out as the message text. An agent would have been
shown the words "this.title". Of those, 355 are exactly `this.title`, meaning the field's
own caption, which is known at generation time and is now resolved. 44 are `this.message`,
a self-assignment that changes nothing. The remaining 30 are built from a local: the
caption stands in and the wording is reported for a person to write.

One example template had none of these, which is why sixteen pages of testing never showed it.

**Validity is only ever set on the handler's own field.** All 4,426 writes to `.valid` or
`.message` across the latest 33 families are to `this`. None sets another field's validity,
so a rule always belongs to the field whose handler states it.

The conditions are the opposite: they read other fields freely, and reading whether another
field is shown is the second most common atom. It is only the writes that stay local.

The extractor now reports a write to another field's validity rather than ignoring it, so
a template that starts doing it is visible rather than quietly half-converted.

**`required` is not used by this estate.** FormWorks has a per-field required property and
a runtime that checks it, but it is false on all 14,312 input fields across the latest 33
families. Required-ness is expressed entirely through `OnValidate` rules, so the 2,157
rules are the whole picture and there is no second mechanism to implement.

**Open:** the flag is form-level, as submission is. So a Next on page one arms the messages
for every later page, and page two opens showing what is outstanding. That may be what is
wanted or may be too eager; making it per-page is a small change either way.



The rules now run. 112 of one example template's 113 reach the screen, and a field that is not
valid says why underneath it. Verified on the simulator: an untouched data-protection page
shows "Please tick to confirm DPA requirements" under its checkbox.

Generated properties rather than the standard error-notification interface. MAUI does not
display that interface automatically the way WPF does, so it buys none of the wiring it
would elsewhere, and the toolkit's implementation is driven by data annotations, which
handle a conditional cross-field rule badly. Rules are sparse, a third of fields, so a
property per validated field costs about a hundred members on a template rather than
three hundred.

Rules run when an answer changes and when a page appears, because a rule can read a field
other than the one just edited.

**Reading whether a field is shown becomes a named seam.** A rule very often applies only
while its field is visible, and visibility is not generated yet. Dropping that part of the
condition would make a hidden field's rule fire against an answer nobody was asked for, so
it is emitted as a call that returns true for now and has somewhere to go when visibility
lands.

**App files added:** `Views/Controls/FormFieldError.xaml` and its code-behind, plus an
error style and two palette entries. A control rather than a label and a converter, because
the app declares its converters per page and a generated page must not depend on a
resource its host might not have.

**The verifier earned its keep.** The first attempt emitted bindings to error properties
without telling the verifier those properties existed, and it refused to write anything
rather than producing 16 pages of broken bindings. The fault was mine and it was caught
before it reached a build.

## Validation, recovered

The rules are extracted, not yet emitted. Across the latest 33 families, 2,155 handlers
yield 2,206 rules, of which 1,831 (83%) decompose into comparisons a generator can emit.
No handler's structure defeated the walker.

Better than the 71% the measurement predicted, because walking the branch structure
recovers conditions that pattern matching over lines does not.

| Atom | Occurrences |
|---|---|
| Text comparison | 2,123 |
| Reading another field's visible state | 1,427 |
| Boolean comparison | 184 |
| Prefix test | 10 |

Only the middle two are new; the others were built for routing. Reading a field's visible
state is what makes a rule conditional, because a field is only required while it is shown.

The messages come out with the rules. Where the template computes one rather than stating
it, usually `this.title`, the expression is kept rather than text being invented.

What the remaining 17% is: arithmetic on locals computed earlier in the handler, mostly the
income and expenditure calculation. Genuine logic, correctly refused.

`ScriptWalker` is now shared by routing and validation, since recovering a statement's
guard is the same problem both times. Routing's tests were the safety net for that move.

## Validation measured against visibility

The plan said choose between them on measurement rather than on which category sounds
larger. Measured the same way, they are not close.

| | Handlers | Conditions | Covered by a small atom vocabulary |
|---|---|---|---|
| Validation | 2,157 | 3,163 | 71% |
| Visibility | 2,275 | 1,085 | 38% |

They also separate cleanly. Every one of the 2,157 validation handlers does nothing but
set validity and a message: none writes visibility, routes, queries or alerts. So doing one
does not drag in the other.

The atoms validation needs are the ones already built for routing, plus one. String
comparison appears 1,741 times, reading another field's visible state 1,407, boolean
comparison 562. Only the state read is new, and it is trivial.

What the remaining 29% is: two shapes at 295 each, both involving locals computed earlier
in the handler, which is the income and expenditure calculation repeated across the
Mortgage Arrears family.

**A correction.** The first measurement put validation at 47% and reported that 1,412
handlers were entangled with visibility. Both were wrong, from one bad regular expression:
it treated `this.visible == true` as a write to visibility rather than a read of it, and
that read is part of the dominant validation pattern. Reading a field's state is what makes
a rule conditional; writing it is a different act. The corrected figures are above.

## Getting a job in and the answers out

The CMS exports CSVs keyed by the fully qualified FormWorks name, and the portal passes
those on, so that name is the contract. It is not derivable from the generated property:
nothing about `Borrower1Name` says it came from `JobSheet.JobSheetDetails.Borrower1.Name`.
Only the template knows, so the generator writes the pairing down.

The report gains two methods, generated from one list so they cannot drift:

```csharp
report.Apply(values);       // a job in
report.ToFieldValues();     // the answers out
```

Four conversions cover the estate: text, checkbox, date and time. Dates and times are
parsed with explicit formats and an invariant culture, because a locale-sensitive parse
turns the third of April into the fourth of March without complaining. The accepted
formats and the accepted checkbox words are vocabulary configuration, to be pointed at
whatever the portal actually sends.

`Apply` returns what did not fit rather than throwing or ignoring. Throwing loses a whole
job for one bad field; ignoring loads a job that looks complete with a field quietly
empty, which is discovered by an agent at a door. Checked on the simulator with a
nine-field job: seven applied, an unknown key and an impossible date both reported by name
and value.

**Save and resume does not need any of this.** That serialises the report object as it
stands, property names and all, into the diary column, and nobody outside the app reads
it. Only the portal exchange needs the mapping. The two are different serialisations of
the same object and should not be conflated.

### One thing that blocks the outbound half

Dates are non-nullable and default to today, so an unanswered date is indistinguishable
from one answered today. Harmless while it was only a display quirk. It is not harmless in
`ToFieldValues`, which will send today's date to the portal as though the agent had
entered it. Making dates nullable needs a change at the control layer, since MAUI's
DatePicker has no empty state, so it is its own piece of work and it should happen before
anything sends answers anywhere.

## Lookups are smaller than they looked

A correction. Earlier notes here framed lookups as feeding dropdowns, and said the
generator's job would be to emit which named list a field binds to. That is wrong.

Every one of the 3,337 selection fields across the latest 33 families already carries its
options in the template. Not one is empty, and no handler queries a table and adds the
results to a control.

What the queries actually do: 39 handlers across the estate run a query and then raise an
alert. That is all any of them does. None sets visibility, validity, a route or a value
from the result. So a lookup is a reference check that warns the agent, and the existing
lookup service from the POC already answers that shape of question.

39 handlers, not 166 queries, and self-contained.

## Lookups, and a correction to the session notes

Reference data is queried from inside scripts, not declared on fields. Estate-wide there
are 166 SELECT statements against four tables, all four of which already ship as CSVs and
are imported by `Services/LookupSeedImporter.cs`.

They reduce to three shapes: a literal query, and a parameterised one built from a form
name and the current field's value. So the query itself is mechanical and a generator
could emit it. What the handler then does with the rows is not: it iterates them and
branches on the values, which is ordinary business logic.

The seam therefore falls inside the handler rather than at it. The query is data the
generator can emit; what to do with the result is hand-written, in the other half of the
page's view model.

**A correction.** `session-notes-2026-09-11.md` records that `Descriptions.csv` and
`Descriptions2.csv` are byte-identical and that the `Action` column is empty throughout,
so `select Action from Descriptions2` returns nothing.

The files are byte-identical, which is confirmed. The column is not empty: 46 of its 305
rows hold a value. The column is named `Action ` with a trailing space, which is the far
likelier reason the query returns nothing, and it is a one-character fix in the export
rather than missing data. It matters because at least one template's contact-type logic
branches on exactly that value.

## Input to the shared report model

Measured before designing it, because it decides whether the model is a per-template
shape or a per-template special case.

Across the latest 33 families the routing guards reference 22 distinct fields in total.
Every one resolves to exactly one page, with no ambiguity and nothing unresolved. Eight of
the 22 are read from a page other than the one they live on, which is the cross-page
requirement, and it is real but small.

The guards themselves use only two atom shapes estate-wide, a value comparison and a
prefix test, which is already established: all 415 routing calls decompose into them.

So nothing about routing is specific to any one template. The report class is generated
per template because the fields differ; the mechanism around it does not vary. The example
template is, if anything, the hard case, with 41 routing calls where most families have
far fewer.

## Decisions waiting on the CEO

None outstanding.

---

## Carried from the POC phase, still unanswered

These need someone outside this work and are unchanged from
`session-notes-2026-09-11.md`:

- Who wraps `DrawingView` and `MediaPicker` as framework controls?
- Which SDK version is the team on? See D3 for how this is currently sidestepped.
- Should binding-failure warnings become build errors?
