# Converting the estate

Working log for converting real templates one at a time, in `templates/`, into
`mauimobileapp` (house style: `house-styles/mauimobileapp.json`).

This is not `converter-progress.md` (the generator's own build history) or
`converter-decisions.md` (architecture decisions). This is: which templates are done,
what a template exposed that the generator got wrong or couldn't express, and what
changed as a result. Read `finishing-a-converted-template.md` first if you haven't -
that's the general how-to this log assumes.

---

## Template status

| Template | Status | Notes |
|---|---|---|
| Ascent Reconnect Audio V23 | Converted, in mauimobileapp | `ReconnectAudioV23*`. Hand-written halves largely done. |
| Nationwide PreLit PreEnf Audio V6 | Converted, in progress | `NationwidePreLitPreEnfAudioV6*`. InterviewPart2 and BuildingInformation hand-written halves in progress; see issues below. |
| Rental Investigation Report V13 | Not started | |
| Sole Deceased Report Audio V13 | Not started | |
| Occupancy Report V18 | Not started | Used in `host-app-integration.md`'s worked example, against the original app, not mauimobileapp. |
| Mortgage Arrears Home Loans V7 | Not started | |
| Marketing Investigation Report V11 | Not started | |
| HSBC Pre Eviction Audio V13 | Not started | |
| Virgin Money Occupancy Report Audio V9 | Not started | |
| RBS Pre Eviction Audio V18 | Not started | |
| Santander Pre-Enforcement Audio V4 | Not started | |
| Buy To Let Audio V14 | Not started | |
| Interest Only Audio V31 | Not started | |
| Mortgage Arrears RBS Audio V25 | Not started | |
| Mortgage Arrears Santander Audio V46 | Not started | |
| Mortgage Arrears Special V41 | Not started | |
| Nationwide Interest Only Audio V10 | Not started | |
| Pre Eviction ReConnects V28 | Not started | |
| Pre Eviction Special V23 | Not started | |
| UKAR Occupancy Report V12 | Not started | |
| Virgin Money Interest Only Audio V16 | Not started | |
| Virgin Money Mortgage Arrears Standard Audio V16 | Not started | |
| Lantern Reconnect Audio V3 | Not started | |

Update the status cell as each template moves: **Not started → Converted, in progress →
Done**. "Done" means the worklist has a matching piece of hand-written code for every
entry, per `finishing-a-converted-template.md` section 10.

---

## Generator fixes found while converting

Newest first. Each entry: what a real template exposed, and what changed. If the same
shape shows up in a second template, that's the signal in section 11 of
`finishing-a-converted-template.md` - stop writing it by hand a third time.

### Remaining.md shows the original script for every left-to-a-person computed write

**Found in:** Nationwide PreLit PreEnf Audio V6, `Completion.Section7.AgentVisitForm`.

The entry used to say only `to value, which the handler works out first` - true but
useless, because it hid that the value came from a `scriptExecSQL` database query. There
is currently no support anywhere in the generator for SQL-backed answers; that part is
still entirely hand-written. `RemainingWorkEmitter.EmitComputed` now prints the source
script under each entry, the same way refused validation rules already did, so a case
like this is visible without going to `template.json` by hand.

### `Report.OnOpened()` hook added

**Found in:** Nationwide PreLit PreEnf Audio V6's form-level `OnOpen` script (sets
`JobSheet.D1` from the instruction date, and `Completion.Section7.AgentVisitForm` from
the SQL query above).

`OnOpen` value writes were being refused unconditionally and Remaining.md told you to
write them "in the same place as a validation rule" (`OnValidated`), which is wrong -
`OnOpen` fires once for the whole job, not once per page. `ReportEmitter` now emits
`partial void OnOpened()`, called at the end of `Report.Apply()`, and Remaining.md's
guidance for `OnOpen` entries points there instead.

### Refused validation rules now get an `Error` property and `FormFieldError` control

**Found in:** completing a hand-written `OnValidated` rule that had nowhere to put its
message - the field the rule was about had no `XError` property or on-screen control at
all, because `TemplateConverter`'s `validatedFields` set only counted fully-parsed
rules. Widened to count every rule, expressible or not: a refused rule still means the
field can be invalid, it's just that the *condition* is hand-written instead of
generated.

### `ValidateAll` / `Report.IsValid` added

Not something a template's own logic exposed as broken, but built to support testing
templates as they're converted, ahead of a real submit flow existing: `Report.IsValid`
(every validated field's error is null), `FormRoutes.ValidateAll(services)` (runs every
page's rules first, since a page nobody has opened has never set its own fields' error
state), a `ValidateAllCommand` per page, and an optional "Validate" button on
`FormStepSelectorControl`, visible only when something is bound to it.

---

## Aliases the generator cannot resolve

FormWorks Lua scripts sometimes reference short alias names (`V1`, `F1`, `E1`...) that
resolve to a real field somewhere else in the template, and the generator does not
follow them - a documented limitation, not a bug. Two real cases so far:

- **Nationwide PreLit PreEnf Audio V6, `IncomeAndExpenditureNotSpecifedReasons`:** the
  original rule referenced `V1`-`V16`, sixteen checkboxes that turned out not to exist
  anywhere on this version of the template at all. Resolution: dropped that clause
  entirely rather than guess at fields that aren't there.
- **Nationwide PreLit PreEnf Audio V6, `UnsecuredCreditor1pc`...`UnsecuredCreditor6pc`:**
  `F{n}`/`E{n}` resolved (with reasonable confidence, not certainty) to
  `UnsecuredCreditor{n}MonthlyOffer` and `UnsecuredCreditor{n}Balance` by matching the
  formula's shape against the only two numeric fields on that row. Worth a second pair of
  eyes against the original template if anyone doubts the mapping.

If a third template hits this, it's worth asking whether the generator could report
*candidate* fields for an unresolved alias (same name elsewhere in the template, or same
row/section) rather than leaving it purely to guesswork each time.
