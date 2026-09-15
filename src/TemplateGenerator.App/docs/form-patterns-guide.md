# Building form behaviour in MAUI

A working guide to the three things every converted FormWorks template needs:
showing and hiding, enabling and disabling, and loading options from the database.

Everything here is implemented and running in three POC pages, reachable from the
flyout menu. Read this alongside them:

| Page | Source |
|---|---|
| Lookup POC | `Views/Diagnostics/LookupPocPage.xaml` |
| Visibility POC | `Views/Diagnostics/VisibilityPocPage.xaml` |
| Enable / Disable POC | `Views/Diagnostics/EnablementPocPage.xaml` |

---

## 1. The one idea to get straight first

FormWorks scripts **pushed state around**. A handler reached out and mutated other
controls by name:

```lua
OnValueChange:
    LloydsDPANext.visible = false;
    LloydsDPA2.visible = false;
    if this.value == "No" then
        LloydsDPANext.visible = true;
    elseif this.value == "Yes" then
        LloydsDPA2.visible = true;
    end
```

MVVM **derives state**. Nothing is pushed anywhere. Controls bind to properties that
compute their own answer:

```csharp
public bool ShowNextButton   => MetCustomer == "No";
public bool ShowDpaStep2     => MetCustomer == "Yes";
```

```xml
<Button        IsVisible="{Binding ShowNextButton}" ... />
<StackLayout   IsVisible="{Binding ShowDpaStep2}" ... />
```

Why this matters beyond tidiness:

- **Order stops mattering.** The script above has to reset both controls first,
  because otherwise a stale `visible = true` survives. Computed properties have no
  history to reset.
- **Nothing can disagree.** Two scripts can't both claim a control's visibility.
- **It's testable.** A ViewModel can be exercised with no UI at all.
- **Initial state is free.** The templates call `VisitOutcome.OnValueChange()` at
  startup purely to establish visibility. Computed properties are simply correct
  from the first render.

When translating a script, stop asking *what does this handler set?* and ask
*what is true when this thing should be visible?*

---

## 2. Visibility

### The basic shape

Store the **answer**. Derive the **visibility**.

```csharp
private string? _metCustomer;

public string? MetCustomer
{
    get => _metCustomer;
    set
    {
        if (SetProperty(ref _metCustomer, value))
            OnPropertyChanged(nameof(ShowInterviewFields));   // see the gotcha below
    }
}

public bool ShowInterviewFields => MetCustomer == "Yes";
```

```xml
<VerticalStackLayout IsVisible="{Binding ShowInterviewFields}">
    <Label Text="Interview notes" FontAttributes="Bold" />
    <Editor HeightRequest="70" AutoSize="Disabled" />
</VerticalStackLayout>
```

### ⚠️ The gotcha that will bite you

**A computed property does not raise its own change notification.** `SetProperty`
only notifies for the property it sets. If you forget the extra
`OnPropertyChanged(nameof(ShowInterviewFields))`, the getter is correct but the UI
never re-reads it — the field silently never appears.

This is the single most common mistake in this style, and MAUI gives you no warning.

When one answer drives several derived properties, raise them together:

```csharp
private void RaiseVisibilityChanged()
{
    OnPropertyChanged(nameof(ShowInterviewFields));
    OnPropertyChanged(nameof(ShowNoContactFields));
    OnPropertyChanged(nameof(ShowIncomeSection));
    OnPropertyChanged(nameof(ShowVulnerabilityDetail));
}
```

> If the team adopts `CommunityToolkit.Mvvm`, `[NotifyPropertyChangedFor]` generates
> this wiring for you and removes the whole error class. Worth raising, given there
> are roughly 2,600 visibility rules to port.

### Nested conditions

Just compose the properties. Don't re-test the parent condition in XAML:

```csharp
public bool ShowInterviewFields      => MetCustomer == "Yes";
public bool ShowVulnerabilityDetail  => ShowInterviewFields && CustomerVulnerable;
```

### Whole sections

Identical mechanism. `SubSectionControl` is an ordinary `ContentView`, so `IsVisible`
on it hides the border, heading and content as one unit:

```xml
<controls:SubSectionControl Title="Income and expenditure"
                            IsVisible="{Binding ShowIncomeSection}">
```

There is no separate technique for sections, which matters for the generator: one
strategy covers every granularity.

### Inverting a condition

Use the existing converter rather than adding a `ShowXInverse` property:

```xml
<base:BaseContentPage.Resources>
    <ResourceDictionary>
        <converters:BoolInvertConverter x:Key="BoolInvertConvert" />
    </ResourceDictionary>
</base:BaseContentPage.Resources>

<Label Text="Answer the question above to continue."
       IsVisible="{Binding HasAnswer, Converter={StaticResource BoolInvertConvert}}" />
```

### Visibility set by a button

The template pattern was:

```lua
OnTap:
    Income.OnValidate();
    this.visible = false;        -- hide myself
    Calculate4.visible = true;   -- reveal the next step
```

Don't translate that literally. The command records **what happened**; both controls
derive their visibility from it:

```csharp
private void UpdateTotals_Execute()
{
    TotalIncome   = (Salary ?? 0) + (Benefits ?? 0) + (OtherIncome ?? 0);
    HasCalculated = true;
}

public bool ShowUpdateButton => !HasCalculated && HasAnyIncome;
public bool ShowTotals       => HasCalculated;
```

Then make any input invalidate the result:

```csharp
public int? Salary
{
    get => _salary;
    set { if (SetProperty(ref _salary, value)) InvalidateTotals(); }
}

private void InvalidateTotals()
{
    HasCalculated = false;      // button returns, totals hide
    OnPropertyChanged(nameof(ShowUpdateButton));
    OnPropertyChanged(nameof(ShowTotals));
}
```

The original carries a comment — *"reveal savings calc button as savings may have
changed"* — because the author had to remember to re-show it by hand. Here it falls
out of the state, and the bug where someone forgets one reveal cannot occur.

### Page navigation

`form.changePage("InterviewPart2")` becomes Shell navigation. There are 282 of these
"Next" buttons across the estate:

```csharp
public Command GoToPageCmd { get; set; }
    = new Command<string>(async route => await Shell.Current.GoToAsync(route));
```

```xml
<Button Text="Next" Command="{Binding GoToPageCmd}" CommandParameter="//lookupPoc" />
```

---

## 3. Enable and disable

### The basic shape

Same as visibility, different property:

```csharp
public bool IsEngineSizeEnabled => SelectedFuelType is "Petrol" or "Diesel" or "LPG";
```

```xml
<controls:NumericEntry Value="{Binding EngineSizeCc}"
                       IsEnabled="{Binding IsEngineSizeEnabled}" />
```

### Disabling usually means discarding

Read the original carefully — it clears the value as well as disabling:

```lua
if this.value == "One Off" then
    P1Review.value = "";        -- ← don't miss this
    P1Review.enabled = false;
else
    P1Review.enabled = true;
end
```

A disabled field must not keep an answer the agent can no longer see or change, or
it will be submitted invisibly. Do it in the setter of whatever gates it:

```csharp
public string? SelectedFuelType
{
    get => _selectedFuelType;
    set
    {
        if (!SetProperty(ref _selectedFuelType, value)) return;

        OnPropertyChanged(nameof(IsEngineSizeEnabled));

        if (!IsEngineSizeEnabled)
            EngineSizeCc = null;        // matches EngineSize.value = ""
    }
}
```

> Across the templates, `.enabled` is only ever set on **other** controls — 34
> assignments to true, 33 to false, never on `this`. Enablement is always a
> cross-field rule.

### Disabled is not read-only

Two different things, and the templates need the second far more often:

| | `IsEnabled="False"` | `IsReadOnly="True"` |
|---|---|---|
| Editable | No | No |
| Focusable | No | Yes |
| Selectable / copyable | No | **Yes** |
| Appearance | Greyed | Normal |
| Use for | A field that doesn't apply | A prefilled value |

**854 fields across the templates carry `readOnly`**, 798 of them `Text`. That's a
static property on the field, so the generator should emit `IsReadOnly="True"`
directly — no ViewModel involvement at all.

Getting this wrong has a practical cost: an agent on a doorstep can't select and
copy a case reference to read back over the phone if the field is disabled rather
than read-only.

### Disabling a button

Use `CanExecute` rather than binding `IsEnabled`, so the rule lives with the command:

```csharp
SaveCmd = new Command(SaveCmd_Execute, SaveCmd_CanExecute);

private bool SaveCmd_CanExecute()
    => !string.IsNullOrWhiteSpace(SelectedFuelType)
       && (!IsEngineSizeEnabled || EngineSizeCc.HasValue)
       && !string.IsNullOrWhiteSpace(AgentNotes);
```

**`CanExecute` is not re-evaluated automatically.** Call `ChangeCanExecute()` from
every setter that affects it:

```csharp
public string AgentNotes
{
    get => _agentNotes;
    set
    {
        if (SetProperty(ref _agentNotes, value))
            SaveCmd.ChangeCanExecute();
    }
}
```

### Always say *why* a button is disabled

A greyed button with no explanation is a support call. Expose the reason:

```csharp
public string SaveBlockedReason
{
    get
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(SelectedFuelType)) missing.Add("fuel type");
        if (IsEngineSizeEnabled && !EngineSizeCc.HasValue) missing.Add("engine size");
        if (string.IsNullOrWhiteSpace(AgentNotes)) missing.Add("agent notes");

        return missing.Count == 0
            ? "All required answers present."
            : "Still needed: " + string.Join(", ", missing) + ".";
    }
}
```

### Making disabled look disabled

The shared `Button` style defines a `Disabled` state, but Gray950 text on a Gray200
background still reads as active. Until that's fixed centrally, dim the control from
the same state that drives `IsEnabled`:

```xml
<converters:BoolToValueConverter x:Key="EnabledOpacity" TrueValue="1.0" FalseValue="0.4" />
```

```xml
<controls:NumericEntry IsEnabled="{Binding IsEngineSizeEnabled}"
                       Opacity="{Binding IsEngineSizeEnabled, Converter={StaticResource EnabledOpacity}}" />
```

The better long-term fix is adding an `Opacity` setter to the `Disabled` visual state
in `Resources/Styles/Styles.xaml` — one change, every control benefits. That's a
decision for whoever owns that file.

---

## 4. Loading options from the database

### Ask for a named list

Form logic never contains SQL. `ILookupService` serves lists by name:

```csharp
var frequencies = await _lookupService.GetListAsync(
    LookupService.IncomeExpenditureFrequencyList);

FrequencyList = new ObservableCollection<ListItem>(frequencies);
```

```xml
<Picker ItemsSource="{Binding FrequencyList}"
        ItemDisplayBinding="{Binding DisplayName}"
        SelectedItem="{Binding SelectedFrequency, Mode=TwoWay}"
        Title="Select frequency" />
```

`ItemDisplayBinding` tells the Picker which property to show. Omit it and you get
the type name.

### Where the data comes from

The exported reference tables share one shape — a list is identified by
**(Form, Category)**, and its options are `txtDescription` ordered by `Item`. That
maps onto one table:

| CSV column | `LookupValue` |
|---|---|
| `Category` | `ListName` |
| `Form` | `FormKey` |
| `txtDescription` | `Value` / `DisplayText` |
| `Item` | `SortOrder` |

So the six ad-hoc queries FormWorks used are one table and one service.

### Scoping by client

Most lists differ per client. `ContactType` exists for ten forms, ranging from 6 to
22 options:

```csharp
var options = await _lookupService.GetListAsync(
    LookupService.ContactTypeList,
    formKey: "SantanderMA");
```

### Cascading lists

A list filtered by an earlier answer. Trigger the reload from the setter:

```csharp
public ListItem? SelectedCategory
{
    get => _selectedCategory;
    set
    {
        if (SetProperty(ref _selectedCategory, value))
            _ = LoadActionsAsync();     // fire and forget; the collection updates
    }
}

private async Task LoadActionsAsync()
{
    var actions = await _lookupService.GetListAsync(
        "Descriptions2",
        formKey: "AgentVisitForm",
        parentValue: SelectedCategory?.DisplayName);

    ActionList = new ObservableCollection<ListItem>(actions);
}
```

### Caching is not optional

One template has **eighty** pickers bound to the same frequency list. `LookupService`
caches by `(listName, formKey, parentValue)`, so eighty bindings cost one read. Bind
them all to the same collection.

Call `InvalidateCache()` after a sync that changes reference data.

### Refreshing seed data

`LookupService.SeedVersion` guards the import. Bump it when the shipped CSVs change
and existing installs refresh on next launch. Without it, data would only ever load
into an empty table and a change would need the app reinstalling.

---

## 5. Pitfalls, all hit during the POC

### `CollectionView` inside a `ScrollView` renders as an empty box

It provides its own scrolling, so nested in another scroller it has no height to
measure against and collapses to zero. You get the border and nothing inside it.

Use `BindableLayout` for bounded lists rendered inline:

```xml
<VerticalStackLayout BindableLayout.ItemsSource="{Binding OptionList}">
    <BindableLayout.ItemTemplate>
        <DataTemplate x:DataType="models:ListItem">
            <Label Text="{Binding DisplayName}" />
        </DataTemplate>
    </BindableLayout.ItemTemplate>
</VerticalStackLayout>
```

Reserve `CollectionView` for lists that own a scroll region and have a real height,
or that need virtualisation for hundreds of rows.

### `BasedOn="{StaticResource {x:Type Button}}"` crashes at runtime

```
XamlParseException: Cannot assign property "Key"
```

MAUI's XAML compiler can't resolve a markup extension as a `StaticResource` key.
That pattern is WPF, not MAUI.

Worse, a page-level implicit `<Style TargetType="Button">` **replaces** the app-level
one for that page rather than merging, so you'd silently lose the shared appearance.
Prefer per-control bindings over page-scoped implicit styles.

### Computed properties need manual notification

Covered above, but it's the one that will cost you most time. If a field doesn't
appear and the logic looks right, check you raised `OnPropertyChanged` for the
derived property, not just the answer.

### Always set `x:DataType`

```xml
x:DataType="viewModels:LookupPocViewModel"
```

This turns on compiled bindings, so a mistyped binding path becomes a **build error**
instead of a silently empty control. Without it, `{Binding CustomerNaem}` fails
silently at runtime and renders nothing. On a form with 854 bindable fields, that is
the difference between catching mistakes at compile time and hearing about them from
the field.

Set it on `DataTemplate` too, where the context changes:

```xml
<DataTemplate x:DataType="models:ListItem">
```

---

## 6. FormWorks to MAUI, at a glance

| FormWorks script | MAUI equivalent |
|---|---|
| `X.visible = true/false` | `IsVisible="{Binding ShowX}"` + computed property |
| `X.enabled = true/false` | `IsEnabled="{Binding IsXEnabled}"` + computed property |
| `X.value = ""` | Set the property to `null` in the gating setter |
| `this.visible = false` in `OnTap` | Command sets state; visibility derives from it |
| `form.changePage("Y")` | `Shell.Current.GoToAsync("//y")` |
| `scriptExecSQL(...)` + `add()` loop | `ILookupService.GetListAsync(name)` + `ItemsSource` |
| `X.OnValidate()` | `CanExecute`, or validation on the ViewModel |
| `readOnly` field property | `IsReadOnly="True"`, emitted by the generator |
| `OnValueChange` cascade | Derived properties; no cascade to run |

---

## 7. Checklist for a new form page

1. Page derives from `base:BaseContentPage`, not `ContentPage`
2. `x:DataType` set on the page root
3. ViewModel derives from `BaseViewModel` (or `BaseAudioRecordingViewModel`)
4. Both registered in `MauiProgram.cs`; route added to `AppShell.xaml`
5. Answers are stored properties; visibility and enablement are computed
6. Every computed property is notified from the setters that affect it
7. Commands use `CanExecute`, with `ChangeCanExecute()` called from relevant setters
8. Lists come from `ILookupService`, never inline SQL or hardcoded options
9. Prefilled fields are `IsReadOnly`, not `IsEnabled="False"`
10. Disabled controls discard their value where the original script did

---

## Still to prove

The POCs cover most common cases. Not yet addressed:

- **Validation** — 2,721 `OnValidate` handlers, the largest single category. The
  pattern isn't settled; `INotifyDataErrorInfo` and the Community Toolkit's
  validation behaviours are the candidates.
- **Prefill** — how case data reaches a form, and how offline sync delivers it.
- **Repeated blocks** — per-applicant and per-occupant sections, where the same
  question appears four or more times. `CollectionView` with a `DataTemplate` over
  an `ObservableCollection` is the likely shape.
- **Calculated fields** — `MileagePrices` and `rptDays` are calculation inputs
  rather than lookups, and need a different treatment.
