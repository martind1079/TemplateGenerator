# Generator plan

How the FormWorks to MAUI converter gets built.

Written 11 September 2026 after the POC phase. Revised 12 September 2026, once passes one
and two were running across the estate. What follows is the plan as it now stands;
`converter-progress.md` records what happened and `converter-decisions.md` why.

> **What changed from the original.** Three things. Reading the estate came first rather
> than closing unknowns by proof of concept, because two of the three unknowns turned out
> to be reading problems (D2). Routing moved out of the human pass into the mechanical one,
> because measurement showed it was not what the original seam assumed (D5). And the estate
> run happened early rather than last, because it cost 53 seconds.

---

## The seam

The break goes where mechanical translation stops and judgement starts. That line is
further along than the original plan put it.

| Pass | Emits | Source | Mechanical? | State |
|---|---|---|---|---|
| 1. Structure | Pages, sections, groups, rows, controls | Template geometry | Yes | Done |
| 2. Model and bindings | Report, answers per page, every control bound | Template field list | Yes | Done |
| 2. Routing | Navigator, step path, shell registration | Recovered from scripts | Yes | Done |
| 2. Field exchange | A job in, the answers out, by FormWorks name | Template field names | Yes | Done |
| 3. Logic | Visibility, enablement, validation | Extracted scripts, read by a human | **No** | Not started |

Passes one and two run together, for the reason the original gave: a binding is one
attribute on a control the generator is already emitting, and splitting them means walking
every control twice.

**Routing belongs in pass two.** The original put it in three on the grounds that routing
and visibility are decided in the same handler. That is true of two handlers in seventeen.
All 415 routing calls across the latest 33 families decompose into two comparison shapes,
and are now emitted as typed C#. See D5.

**Field exchange was not in the original plan.** The CMS exports keyed by the fully
qualified FormWorks name, which is not derivable from the generated property, so the
generator writes the pairing down and emits both directions from it.

**Pass three is still not automated.** What remains there is genuinely conditional
behaviour, and the measurement below says it does not decompose the way routing did.

---

## Non-negotiables

All four still stand and all four are honoured.

**Generated files are never hand-edited.** Every generated file carries a header saying so.
Regeneration is 0.05 seconds a template, so iterating on layout is free.

**Layout quirks are fixed in the generator, not the output.** Row packing, group columns,
caption placement, reserved title space and content widths are all emitter rules recovered
from the template's own geometry.

**The view model splits along the seam.** Generated bindable state in one partial class,
hand-written logic in the other. The page is wholly generated, because XAML has no partial.

**The generator validates what it emits.** Nothing is written if a binding does not resolve,
a resource key is missing from the live dictionaries, an answer has no setter, or two
fields claim one property. A guard it cannot express becomes an explicitly false condition
and is reported. Those checks are semantic; the compiler is the syntactic one, and both
are needed.

A fifth has been added since: **the generated pages commit to no appearance of their own.**
They name style keys and nothing else, so restyling a converted form needs no regeneration.

---

## Where the work is

### Done

**Passes one and two, across the whole estate.** 33 templates, 222 pages, 13,835 fields,
415 routes, no failures, about 0.05 seconds a template. The template runs on the
simulator with routing that follows the answers.

**The three unknowns the original wanted closed first.**

The prefix rules are catalogued, and generated rather than written, so they cannot drift:
32 distinct literals across the latest 33 families, with `ASLB` and `MSUK` each tested at
two lengths and three rules using the negated form.

Repeated blocks stay flat (D6). The original framed this as a choice between a collection
and numbered properties. Numbered sections turned out not to be repeats at all, and the
genuine repeats are already flattened by the template and fixed in width.

Validation is **not** closed. The reading narrowed it, but the technique is unproven.

**The view model lifetime question.** Answers are a computed property off the shared report,
so bindings resolve live and nothing re-points them on appearing. The only thing rebuilt
when a page appears is the step strip, because an answer given elsewhere changes which
pages this report visits. That is a different concern from the original one and a
legitimate reason to refresh.

**The estate run and triage.** Done early, which is how the remaining gaps below are known.

### Next

**Nullable dates, before anything sends answers anywhere.** Dates default to today, so an
unanswered date is indistinguishable from one answered today. Harmless as a display quirk,
not harmless in the outbound payload. Needs a control-layer change, because MAUI's
DatePicker has no empty state.

**Validation, then visibility.** Both are now measured the same way and they are not
close: validation covers 71% of its 3,163 conditions with a small atom vocabulary,
visibility 38% of its 1,085. Validation also separates cleanly, since every one of its
2,157 handlers does nothing but set validity and a message. The atoms it needs are the
ones already built for routing plus one, reading another field's visible state.

The open technique question from the original plan stands: `INotifyDataErrorInfo` against
the toolkit's validation behaviours. The dominant rule is "required when visible", which
both can express.

**Lookups.** 166 SELECTs across the estate against four tables, reducing to three query
shapes. The existing lookup service's position is that form logic should ask for a named
list rather than carry SQL, so the generator's job is to emit which list a field binds to.
No SQL needs generating at all.

**Then pass three by hand, timed.** Still the number the business needs, but worth less
than when the original was written, because routing moved out of it. Do it when the
remaining human work is actually what a person would face.

---

## What this is measured against

The estate is 27,801 fields and 7,244 script handlers across 33 latest-version families.

The original target was that passes one and two take minutes per template. They take
0.05 seconds, so generation cost constrains nothing and the emitter can be re-run as often
as layout needs.

The question the business needs answered is unchanged: how many script handlers a person
can read per day. What has changed is the numerator. Routing is out of it, which removes
415 calls. Whatever share of the remaining handlers validation and visibility turn out to
be generatable will come out too, and that share is what the next measurement decides.

---

## Still open, needs someone else

- **Who wraps `DrawingView`?** Signature is still skipped, 46 fields across the estate.
  Photo now has a house control standing in, replaceable by one line of the vocabulary.
- **SDK version.** The root pin names a version that is not installed. The tools carry
  their own pin so they are unaffected, and the app builds from outside the repository.
- **Binding-failure diagnostics.** Turning `XC0022` into an error would catch a class of
  generated mistake at build time.
- **Android and Mac Catalyst.** Only iOS has been built. The input styling is iOS-only and
  guarded, so it does nothing elsewhere rather than breaking.
