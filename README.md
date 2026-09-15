# Template Generator

A .NET MAUI app that showcases the FormWorks converter: a tool that turns a legacy Formworks JSON
template into a native MAUI form — XAML, view model and supporting files — instead of interpreting
the JSON at runtime.

The app is a shell around that output. A retractable side menu lists what has been converted;
selecting a template opens its generated pages. Conversion itself is a build step, run from the
command line, because generated XAML has to be compiled before a form can be opened.

## Layout

| Path | What it holds |
|---|---|
| `src/TemplateGenerator.App` | The demo app |
| `src/TemplateGenerator.App/Views/Generated` | Converter output — pages, plus the model and view models beside them |
| `src/TemplateGenerator.App/Views/Controls` | The six house controls generated XAML names |
| `templates/` | Templates to convert. Only the synthetic demo one is committed |
| `scripts/convert` | Convert one or more templates, then rebuild |
| `src/TemplateGenerator.App/Resources/Styles` | `Colors`, `Styles`, and `FormStyles` (the keys generated XAML references) |
| `src/TemplateGenerator.App/tools` | The FormWorks converter — a separate solution, not built by the app |

## Running it

Mac desktop:

```
dotnet build src/TemplateGenerator.App/TemplateGenerator.App.csproj -f net10.0-maccatalyst -t:Run
```

iPad simulator, which is the target shape — the templates are laid out at 980pt:

```
xcrun simctl list devices available                 # pick a udid
dotnet build src/TemplateGenerator.App/TemplateGenerator.App.csproj -f net10.0-ios
dotnet build src/TemplateGenerator.App/TemplateGenerator.App.csproj -f net10.0-ios -t:Run \
    -p:_DeviceName=:v2:udid=<udid>
```

Build the framework explicitly first. `-t:Run` has been seen to launch a stale binary without
rebuilding.

The whole solution, all three target frameworks:

```
dotnet build TemplateGenerator.slnx
```

## Converting a template

```
scripts/convert "Some Survey V4"                  # convert, then rebuild for the Mac
scripts/convert "Some Survey V4" "Another V2"     # several at once
scripts/convert --all-latest                      # every latest-version template in the estate
scripts/convert --ios "Some Survey V4"            # rebuild for the simulator instead
scripts/convert --no-build "Some Survey V4"
```

Templates are read from `templates/`, and nowhere else unless you pass `--estate`. To convert
something new, drop it in as `templates/<Template Name>/template.json`.

Converted templates are found by reflection over the generated routes classes, so nothing
needs registering by hand. The same conversion from the command line directly:

```
cd src/TemplateGenerator.App/tools/FormWorks.Cli
dotnet run -- generate --template "Some Survey V4" --all-pages --isolate \
    --app ../.. --namespace TemplateGenerator.App
```

`--isolate` names the generated classes after the template. It is not optional here: this app
holds several converted templates at once and page names repeat across them, so without it one
template's `Closure` page overwrites another's.

## What is not committed

The template estate carries client names and their business rules, so neither it nor what the
converter makes of it belongs in this repository. `templates/`, `Views/Generated`,
`Models/Generated`, `ViewModels/Generated` and the hand-written view model halves beside them
are all gitignored, except the synthetic `Demo Property Survey V1` and its output — so a fresh
clone still has a form to open.

See [docs/host-app-integration.md](src/TemplateGenerator.App/docs/host-app-integration.md) for
what the converter asks of a host app, and `Views/Generated/<Template>.Remaining.md` for what
each conversion leaves to a person.

## Still to do

- `MultiSelection` fields render as a single-select `Picker`.
- Signature fields are skipped, for want of a `DrawingView` wrapper.
- Nothing yet sends a completed report anywhere.
