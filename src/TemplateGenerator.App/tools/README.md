# FormWorks converter

Reads the FormWorks template estate and emits MAUI pages and the matching model from
it. Structure and bindings only; no visibility, enablement, validation or routing.

See `../docs/converter-progress.md` for where the work is and
`../docs/converter-decisions.md` for why it is shaped this way.

## Running it

```
cd FormWorks.Cli
dotnet run -- help
dotnet run -- check --latest
dotnet run -- graph --template "My Template.json"
dotnet run -- generate --template "My Template.json" --all-pages --app ../../mauimobileapp
```

`generate` writes nothing unless every check passes. Add `--dry-run --print` to see a
page without writing it.

The MAUI project will not build from inside this repository, because the root SDK pin
names a version that is not installed. Build it from outside the repository instead.

Templates are read from `--estate <path>`, or from the nearest `templates/` folder at or
above the working directory. Nowhere else is searched: a template estate carries client
names and business rules, so where one is kept is the caller's business.

`--latest` keeps only the highest version of each family. Without it you get all 57
folders including superseded versions, which double counts the estate.

## Layout

| Project | What it holds |
|---|---|
| `FormWorks.Templates` | The node model, the loader, and the analysers |
| `FormWorks.Cli` | The `formworks` command |
| `FormWorks.Templates.Tests` | Tests, with fixtures written inline |

`global.json` here pins the SDK for the tools only, so the repository root pin is left
exactly as the team set it. That is deliberate, and D3 explains it.

There are no NuGet dependencies, also deliberate: this is the tool people reach for
when something else will not build, so it should not need a working package restore.
