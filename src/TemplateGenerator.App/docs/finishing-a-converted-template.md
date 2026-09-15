# Finishing a converted template

For the developer picking up a template after the generator has run.

Most of a converted form needs nothing from you. This is about the part that does: where
to put it, how to write it, and how to know when you are done.

---

## 1. Start with the worklist

Every template produces one:

```
mauimobileapp/Views/Generated/<Template>.Remaining.md
```

It is generated, rewritten on every run, and it lists everything the generator refused to
express, with the original FormWorks handler alongside it. **If a rule is not in that
file, it is generated and needs no attention from you.**

That matters more than it sounds. Without it the job is to read eight thousand lines of
generated code and notice what is absent, and absence is the hardest thing to notice.

For one example template, a sixteen-page form with 310 fields, the list is one rule and two
messages.

---

## 2. What the generator refuses, and why

It emits comparisons, not computation. That includes numbers: `tonumber(x) < 18` and
`a.value < b.value` are both recovered, and read as numbers rather than as text the way
Lua does. A condition it can express:

```lua
if this.value == "" and IDV.visible == true then
```

One it refuses:

```lua
local valid = 0;
if VisitOutcome.value ~= "Case Resolved" then valid = 1 end
if IDPassport.value == true or IDDrivingLicence.value == true then valid = 1; end
...
if valid == 0 then this.valid = false; end
```

The decision depends on a flag built up across several fields. Approximating it produces
a rule that is wrong in a way nobody notices until an agent is stopped at a door, or
worse, not stopped. The generator stops instead and tells you.

Across the estate this is 376 handlers in 185 distinct shapes, so the same logic recurs.
Once you have written one you have often written several.

---

## 3. Where your code goes

The view model is a partial class. The generator writes one half and never opens the
other. Create a file **outside** `Generated/`:

```
mauimobileapp/ViewModels/JobSheetPageViewModel.cs
```

It keeps the generated namespace, because the two halves are one class. The folder is what
tells you which half you are in, not the namespace:

```csharp
namespace MyProject.Mobile.ViewModels.Generated;

public partial class JobSheetPageViewModel
{
    partial void OnValidated()
    {
    }
}
```

`OnValidated` is the seam. The generated `Validate()` runs the generated rules and then
calls it, so your code sees the messages they set and runs last. It is a partial method,
so a page with nothing hand-written compiles it away to nothing.

Do not edit anything under `Generated/`. The next run overwrites it, and regeneration
takes under a tenth of a second, so it happens often.

---

## 4. Error messages

Each validated field has a message property on its answers class, named after the field
with `Error` on the end. It is a string, and `null` means valid. Setting it is the whole
of validation:

```csharp
Answers.ContactOKError = "The minimum number of visits has not been completed.";
```

Nothing shows until the form is submitted. A form-level `ShowValidationErrors` flag gates
display and defaults to false, so a half-filled page is not red before the agent has
reached the bottom of it.


### Step 1 — Find it in the worklist

```
### JobSheet.ContactRules.ContactOK
- Sets the field invalid, saying a message computed at runtime from `strMessage`.
```

The heading is the field's full FormWorks name. The page name tells you which view model to
open: `JobSheetPageViewModel`.

### Step 2 — Read the original handler

The worklist prints it underneath. Trimmed to its shape:

```lua
if VisitOutcome.value == "No Contact Made" or VisitOutcome.value == "Access Not Gained" then
    if ContactOK.value == "No" then
        this.valid = false;
        if actVisits.value < MinVisits.value then strMessage = "number of visits" end
        ...
        this.message = strMessage;
    end
elseif VisitOutcome.value == "Case Resolved" and actVisits.value < "1" then
    this.valid = false;
    this.message = "The report indicates that you have not completed the minimum number of visits.";
end
```

### Step 3 — Read what the generator already did

This is the step people skip, and skipping it means writing code twice. Open the validator
and find the same field:

```csharp
answers.ContactOKError =
    (Reason == "No Contact Made" || Reason == "Access Not Gained") && ContactOK == "No"
        ? "Contact"
        :
    (Reason == "Case Resolved" && Num(ActVisits) < 1)
        ? "Contact"
        :
    null;
```

Both branches are converted, comparisons and all. `Num` is the generated reading of Lua's
`tonumber`: null where the answer is not a number, so every comparison against it is false,
which is what Lua does with nil.

### Step 4 — Work out what is actually missing

Only the wording. The template assembled both messages at runtime from a local, so the
generator fell back to the field's caption and both branches currently say "Contact".

The decisions are done. Do not rewrite them.

### Step 5 — Work out which branch fired

The generated rule sets the same caption either way, so re-test the branch rather than
guessing:

```csharp
partial void OnValidated()
{
    if (Answers.ContactOKError is null)
        return;

    var resolved = string.Equals(Answers.Reason, CaseResolved, StringComparison.Ordinal);

    Answers.ContactOKError = resolved
        ? "The report indicates that you have not completed the minimum number of visits."
        : ShortfallMessage();
}
```

The early return matters. A field the generated rules found valid must stay valid: the hook
runs last and gets the final word, so writing a message unconditionally here would invent a
failure.

### Step 6 — Write the computed message

Four comparisons and a list, rather than four string concatenations:

```csharp
if (Short(Answers.ActVisits, Answers.MinVisits)) shortfalls.Add("visits");
if (Short(Answers.ActAM, Answers.MandAM)) shortfalls.Add("morning visits");
...
```

### Step 7 — Decide the things a generator cannot

**Empty is not zero.** Counts arrive as text and an unanswered one is empty. An unreadable
quota is treated as no quota, so a bad reference value cannot raise a message the agent has
no way to clear.

**The template has a defect.** If every quota is met but the agent answers No anyway, the
original produces "you have not completed the ." The converted form says something sensible
instead. Worth raising with whoever owns the form, but not worth reproducing.

### Step 8 — Notice when the generator overtakes you

This example used to be twice as long. The rule's second branch turns on
`actVisits.value < "1"`, which Lua compares as text, so ten visits read as fewer than one.
That was hand-written, with a note that the converted form deliberately differed from the
template.

The generator now recovers those comparisons itself, as numbers, so the hand-written branch
became redundant and its message became wrong for the branch it no longer matched.

**Hand-written code that is no longer needed has to be removed, not left.** It does not go
quiet; it fights the generated rule and wins, because it runs last. When the worklist gets
shorter, check what it dropped.

## 6. Messages needing wording

The worklist lists these in a table: the field, what it currently says, and the expression
the template built the message from.

```
| Field                             | Currently says | Built from   |
| `JobSheet.ContactRules.ContactOK` | "Contact"      | `strMessage` |
```

The rule works. It is the wording that is missing, because the template assembled the text
at runtime and the field's caption stands in, which names the field without saying what is
wrong with it.

Set these in `OnValidated`, after checking the field's error is not null, so you replace a
message the rules decided on rather than inventing a failure.

## 7. Writing a visibility rule

Visibility works the same way as validation but through a different seam, and the reason
it needs one is worth understanding before you write any.

**FormWorks visibility is imperative. A generated property is a predicate.** In the
template a field is shown because some handler set it so, and it stays that way until
something sets it otherwise. The generated property has to be true exactly while its
condition holds. Those agree only where one handler owns the field. Where several write
it, the answer depends on which field the agent touched last, and the template does not
record that, so the generator refuses.

A refused field still gets a property and a binding. It defaults to shown, and asks you:

```csharp
public bool ShowWhyNoPhotoEvidence
{
    get
    {
        var shown = false;   // the template starts this section hidden
        DecideShowWhyNoPhotoEvidence(ref shown);
        return shown;
    }
}

partial void DecideShowWhyNoPhotoEvidence(ref bool shown);
```

Enablement works the same way and lives in the same place. A refused enablement rule gets
`UsableX` and `DecideUsableX` rather than `ShowX` and `DecideShowX`, and binds `IsEnabled`
instead of `IsVisible`. Both tables are in one generated file per template, because they
are the same machinery reading the same handlers.

Implement the partial method in the hand-written half. What you decide is written down on
the report before the rules run, so the generated code sees it: a section you hide really
is hidden as far as validation and clearing are concerned, and the answers inside it are
cleared without your writing anything. There is no separate hide handler to implement.

Until you implement it, the element keeps the state the template gave it. One the template starts hidden stays hidden, one it starts
shown stays shown. That is conservative in both directions: no question is silently
skipped, and nothing the form was never going to show appears.

### A worked example

`WhyNoPhotoEvidence` on the Job Sheet: seven handlers, fourteen writes. Read together they
contradict each other.

The five non-photographic boxes each say the same thing:

```lua
if this.value == true and IDPassport.value == false and IDDrivingLicence.value == false then
    WhyNoPhotoEvidence.visible = true;
else
    WhyNoPhotoEvidence.visible = false;
end
```

The two photographic boxes say something weaker:

```lua
if this.value == true or IDDrivingLicence.value == true then
    WhyNoPhotoEvidence.visible = false;
else
    WhyNoPhotoEvidence.visible = true;
end
```

Taking the photographic pair at their word would put the question on screen the moment the
form opens with nothing ticked, and the section starts hidden in the template. So that is
not the intent. The five agree on a rule that makes sense of all seven:

```csharp
partial void DecideShowWhyNoPhotoEvidence(ref bool shown)
{
    var photographic = Answers.Passport || Answers.DrivingLicence;

    var other = Answers.BirthCertificate
                || Answers.UtilityBill
                || Answers.BankStatement
                || Answers.CouncilTaxBill
                || Answers.OtherSpecify;

    shown = other && !photographic;
}
```

Some identification has been recorded, and none of it is photographic.

This also repairs a defect. In the original, ticking two non-photographic boxes and then
unticking one hides the question, because the unticked box's `else` branch runs last and
does not know the other is still ticked. A predicate cannot make that mistake, which is a
good reason to write the rule rather than reproduce the handlers.

### What to look for

The worklist groups refused fields by why, because they need different work.

| Reason | What you are deciding |
|---|---|
| Several handlers decide it | Which reading is intended, when they disagree |
| A condition could not be expressed | How to say the condition in C# |
| Shown but never hidden again | Whether it should hide again at all |
| The rule names more than one element | Which of them the rule was about |

That last group is listed separately in the worklist, with every element claiming the
name. Each gets its own `Decide` method, so you implement the one the rule meant and leave
the rest alone. One template has a rule written against `Next`, and fourteen sections
are called Next; only the Job Sheet's starts hidden, so the other thirteen are already
right by default.

That last one is not a formality. A field the template only ever shows stays shown once
something shows it. A predicate goes back to false when its condition stops holding. If
that difference matters for the field, say so in the code.

### Two things to know

**Hidden means collapsed.** The fields below move up. FormWorks reserved the space, because
it positioned everything absolutely; this layout reflows.

**A hidden field's answer is cleared.** FormWorks kept it, which left an answer given and
then hidden still in the outbound payload. Generated code clears it, so an agent who
re-shows a field will find it blank.

---

## 8. Answers the form fills in

A third kind of behaviour beside rules and state, and the one that fails most quietly. A
rule that is missing shows the wrong thing. A value that is missing shows nothing, and a
blank field looks exactly like one nobody has reached yet.

The template fills in answers for the agent: blanking a field when the question it depended
on changes, copying one answer to another, deriving a count. Where the generator can
express one it does, and the rest are listed in the worklist under answers the form fills
in, with the handler and what it would have set.

Two rules decide what gets generated, and both refuse more than they have to on purpose.

**A chain is a unit.** All the writes one handler makes to one field are an if/elseif chain.
Emitting the branches that happen to be expressible leaves a half-chain, and one example templates contact rules would then say the minimum visits were not met and never that
they were.

**Nothing is derived from a blank.** If a field is filled in by a handler the generator
cannot express, that field stays empty, so anything computed from it is computed from
nothing. `ContactOK` reads visit counts produced by date arithmetic nothing here can
reproduce; generating it anyway would have answered "Yes" on an empty form and quietly
satisfied a rule meant to stop the agent.

### Where they go

Two seams, not one, because this changes an answer rather than deciding something about it
and *when* it runs is part of the behaviour:

```csharp
partial void OnAnswerChanged(string? field);   // as the answer changes
partial void OnAnswerLeft(string? field);      // once the agent leaves the field
```

Both are told which field, by the name the template knows it by. Check that name before
acting: code that fills in an answer on every change fights the agent, who can never type
into a box it also writes to.

**Use the one the template used.** A handler on `OnValueChange` belongs in the first; one
on `OnBlur` belongs in the second. The worklist prints the event beside every entry. This
is not a detail: a rule reading an age, run as the agent types, fires on the "3" of "30"
and names the tenant a child, then unnames them a keystroke later.

### A worked example

The tenant name boxes on an example template's Case Resolved page, written out in its own
`CaseResolvedPageViewModel.cs`.
The template names a tenant too young to be named, choosing the first label no other box
is using:

```lua
if tonumber(this.value) then
    if tonumber(this.value) < 18 then
        if name1 ~= "Child 1" and name2 ~= "Child 1" and ... then newName = "Child 1";
        elseif ... "Child 2" ... then newName = "Child 2";
        ...
    end
end

Tenant1Name.value = newName;
```

```csharp
partial void OnAnswerLeft(string? field)
{
    if (field is null || !field.EndsWith("Age", StringComparison.Ordinal)) return;

    NameByAge(1, () => Answers.Tenant1Age, n => Answers.Tenant1Name = n);
    ...
}
```

`OnAnswerLeft`, because the template writes this on `OnBlur`.

Three things to notice, and the last one is easy to miss.

**Guard on the field.** Only the age boxes trigger this. Without that check every keystroke
anywhere on the page would rewrite six names.

**Text that is not a number is not an age.** The code returns rather than clearing, so a
name already given survives a box holding something unreadable.

**The assignment is outside the test.** In the template `Tenant1Name.value = newName` runs
whether or not the age was under eighteen, and `newName` is nil when it was not, so
entering an adult age clears the box. That is behaviour, not an accident, and reading only
the branch would have missed it.

---

## 9. What the generator cannot tell you about

**Signature fields are skipped**, because the framework has no wrapper for `DrawingView`.
46 across the estate. They are reported at generation time as elements not emitted.

**Page visibility is not here.** Hiding a page is how FormWorks chose which pages a form
visits, and that is recovered as routing and emitted as the navigator's path. If a page
seems to appear when it should not, look at the navigator, not at visibility.

**Defects in the source template.** The tools surface some: `formworks coverage` lists
branches that test values a control cannot produce, and pages nothing routes to. One example template alone has eleven dead branches. Those are faults in the original form, worth
raising rather than faithfully reproducing.

## 10. Knowing when you are done

```
cd tools/FormWorks.Cli
dotnet run -- generate --template "<name>" --all-pages --app ../../mauimobileapp
```

Then read the worklist. When every entry has a matching piece of hand-written code, the
template's validation is converted.

Build the app from **outside** this repository. The root SDK pin names a version that is
not installed, so a build from inside fails before it starts.

---

## 11. If you write the same thing three times, stop

A quirk that appears in more than one template belongs in the generator, where fixing it
once fixes every template.

| Change | Where | Regenerate? |
|---|---|---|
| Colour, font, border, spacing | `Styles.xaml`, or a handler mapping | No |
| Which control renders a field type | `Emit/ControlVocabulary.cs`, one line | Yes |
| Arrangement, or a rule shape the generator could learn | the emitter | Yes |

The worklist is also the measurement. A long one means the generator has more to learn,
not that a developer has more to do.
