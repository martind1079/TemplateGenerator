# Converter decisions

Architectural decisions for the FormWorks to MAUI converter. Append-only in spirit:
entries are amended or superseded, not deleted. A superseded entry keeps its number
and gains a note pointing at the entry that replaced it.

A decision earns a D-number when it constrains future work, meaning a later change
would need to know it to avoid re-litigating or contradicting it. "We used a record
rather than a class" is a code standard, not a decision. "The estate is read before
anything is emitted" is a decision.

Format follows the one in the parent working substrate, including the **Revisit if**
line. That line matters more than it looks: a decision with a stated reopening
condition can be made confidently at low information, whereas one without has to be
made as if it were forever.

---

## D1. The converter lives in this repository, under `tools/`, on the POC branch

**Date:** 2026-09-11
**Context:** The converter is a new codebase. It reads the template estate and emits
into the MAUI app, so it could reasonably have lived in its own repository. This
repository has no git remote and the parent excludes it, so nothing here is currently
reviewable or backed up.
**Decision:** The converter lives at `tools/` in this repository and develops on
`poc/local-db-field-population` alongside the POC work.
**Alternatives considered:** A separate repository, which would keep the vendor
baseline clean and simplify pointing this repo at a remote later. It lost
because the work is exploratory and splitting it now costs more coordination than it
saves.
**Consequences:** One branch holds both the proofs and the tool that replaces them,
so the reasoning stays together. The cost is that the converter inherits this repo's
lack of a remote, which makes the generated prefix catalogue as unbacked-up as
everything else here.

---

## D2. The estate is read and analysed before anything is emitted

**Date:** 2026-09-11
**Context:** `generator-plan.md` sequenced the work as three proof-of-concept tasks to
close open unknowns, then the generator. During the session that wrote it, throwaway
scripts answered the first unknown completely and went a long way into the second,
in minutes, without building anything in the app.
**Decision:** Phase one is a reader and analyser with no emission. The unknowns are
answered by static analysis over the whole estate rather than by proof-of-concept
pages in the MAUI app.
**Alternatives considered:** The original sequencing. It lost because two of its three
unknowns are reading problems, and answering them in the app produces a sample of one
template rather than an estate-wide number.
**Consequences:** The parser is needed by every later pass, so none of this is
throwaway. The analysers become the triage tool the plan already wanted for the
estate-wide run. Emission starts later than originally planned, against much better
information.
**Revisit if:** The analysis stops producing findings that change the emitter's
design. At that point reading has done its job and the cost is delay.

---

## D3. The tools carry their own SDK pin

**Date:** 2026-09-11
**Context:** `global.json` at the repository root pins the SDK to 10.0.301. That
version is not installed on this machine, which has 10.0.400, so `dotnet` refuses to
run anywhere inside the repository. Which version the team is actually on is an open
question nobody here can answer.
**Decision:** `tools/global.json` pins 10.0.400 with `rollForward: latestFeature`. SDK
resolution takes the nearest `global.json` walking upward, so the tools build while the
app's pin is untouched.
**Alternatives considered:** Changing the root pin, which would answer a question that
belongs to the team and could break their build. Building from outside the repository,
which is what the POC work resorted to and which makes the tool awkward to run.
**Consequences:** The tools build and run normally. The root pin stays exactly as the
team left it, so the open question stays open rather than being silently closed.
**Revisit if:** The team confirms which SDK version is canonical. Then there should be
one pin, not two.

---

## D4. The converter takes no NuGet dependencies

**Date:** 2026-09-11
**Context:** JSON reading, regular expressions and console output are all in-box on
net10.0. This machine has no NuGet package cache.
**Decision:** The reader and its command line take no package references. Anything
that would need one has to justify itself.
**Alternatives considered:** A command line parsing library and a test framework, both
conventional. They lost for now because the tool is the thing people reach for when
something else will not build, and it should not itself depend on a working restore.
**Consequences:** The tool builds offline from a clean clone. The cost is a hand rolled
option reader, and no unit test project yet, which is a real gap rather than a
preference.
**Revisit if:** Package restore is confirmed working in the team's environment. A test
project is the first thing that should be added when it is.

---

## D5. Routing is emitted as data in pass two, not written by hand in pass three

**Date:** 2026-09-11
**Context:** `generator-plan.md` put all routing in pass three, as human work, because
routing and visibility are decided in the same handler. Measurement says that is true
of a small minority. In One example template the 41 routing calls sit in 17 handlers, only
2 of which also set visibility or enablement; nine handlers are the identical two-way
prefix split and four are a single unconditional line.

The reader now recovers the guard on every routing call, not just its destination, by
walking the if/elseif/else structure rather than pattern matching for `changePage`.
Across the latest 33 families all 415 routing calls come apart: 251 are unconditional,
30 are else branches, and all 136 that carry a real condition decompose into
comparisons a generator can emit. Nothing failed to parse.

**Decision:** Pass two emits a routing table as ordered data, one entry per call, with
its guard. Pass three keeps only the handlers that mix routing with visibility or
enablement, which are the genuine judgement cases.

**Alternatives considered:** Leaving routing in pass three as planned. It lost on
evidence: most routing is one rule repeated, and hand transcription of it is
demonstrably error prone. The POC's hand-written navigator, built carefully from the
same handler, states that Instruction Cancelled goes straight to the tail when the
template sends it to Vehicle or Building Information on a prefix test.

**Consequences:** The most repetitive part of the human pass disappears, and the
routing table can be checked against the template mechanically rather than by eye. It
also makes dead and shadowed branches visible, which a person reading 33 families will
not catch reliably. The cost is that the emitter now depends on the guard walker
staying correct, so it needs tests before pass two is built on it.

**Order is load bearing.** The table is an ordered chain and the first matching entry
wins, exactly as the source elseif chain does. Emitting it as an unordered dictionary
would silently change behaviour wherever a later branch overlaps an earlier one.

**Revisit if:** A template turns up whose routing does not come apart, or the share of
handlers mixing routing with visibility rises materially above the two in seventeen
seen so far.

---

## D6. Repeated blocks stay flat; they do not become collections

**Date:** 2026-09-11
**Context:** `generator-plan.md` treated this as an open choice between a collection with
an item template and a flat set of numbered properties, on the understanding that
per-applicant sections repeat with an index. Reading what the numbered names actually
are settles it differently than expected.

Numbered sections are mostly not repeats at all. The eight sections named alike on the
No Contact Made page hold entirely different questions, because the editor numbers
sections as they are added. Genuine repeats look different, and the estate has two
kinds. Across the latest 33 families:

| Kind | Count |
|---|---|
| Flattened, index inside the field name | 179 |
| Structural, sibling containers holding the same fields | 54 |
| Numbered but not a repeat | 533 |

Every one seen is fixed width. Six tenants, ten unsecured creditors, two borrowers.
Nothing grows at runtime.

**Decision:** Repeats are emitted as a flat set of numbered properties. No collection,
no item template.
**Alternatives considered:** A collection with a template, which is the idiomatic MAUI
answer and would be right for a list that grows. It lost because the source has already
flattened the dominant case, the counts are fixed, and scripts address the fields
individually by name. A collection would invent structure the template does not have
and break that mapping.
**Consequences:** The generated model mirrors the template one-for-one, so a script
reference resolves to exactly one property. Repeated layout is repeated in the emitted
XAML, which is verbose, but generated files are never read as source. If a template
ever needs a row added at runtime this is the wrong shape and would need revisiting.
**Revisit if:** A template turns up whose repeat count is not fixed, or the business
asks for rows to be added on the device.

---

## D7. Generated files live under `Generated/` and are never hand-edited

**Date:** 2026-09-11
**Context:** The plan requires that any template be regenerable at any time without
losing work, and that the view model split along the seam between generated structure
and hand-written logic. That needs a physical convention, not just an intention.

**Decision:** Four files per page, all under a `Generated` folder in their usual
project location: the page, its code-behind, the answers class, and the generated half
of the view model. Every one carries an `auto-generated` header. The page is wholly
generated, because XAML has no partial-class equivalent. Hand-written logic goes in the
other half of the view model's partial class, in a file the generator never writes.

**Alternatives considered:** Emitting beside the hand-written pages, which would have
made regeneration a merge every time.
**Consequences:** Regeneration is safe and layout can be iterated freely, which it will
need to be. Anything a person writes into a generated file is lost on the next run, so
the header has to be believed.
**Revisit if:** The target repository has its own convention for generated output.

---

## D8. The generator verifies its own output before writing it

**Date:** 2026-09-11
**Context:** Both failures found on the last day of the POC phase were invisible to the
compiler: a wrong resource key crashes the page only when shown, and a get-only
property of complex type saves correctly and resumes empty. At the scale of the estate
silent runtime failure is the thing that hurts.

**Decision:** Emission is refused, with nothing written, if any check fails. Currently:
every binding path resolves against the generated model, every resource key exists in
the app's live dictionaries with commented-out blocks excluded, every answer has a
setter, and no two fields claim the same property name.

**Alternatives considered:** Emitting and relying on the build. The build catches
syntax, which is necessary but not sufficient; neither POC failure was a build error.
**Consequences:** A whole class of runtime failure becomes an emit-time refusal. The
checks are semantic only: a missing semicolon in the emitted model passed every one of
them and was caught by the compiler, so both layers are needed and neither replaces the
other.
**Revisit if:** A runtime failure reaches the simulator that a check could have caught.
Then the check is missing, and the fix belongs here rather than in the output.
