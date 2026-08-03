# Publishing `ActiveStateMachine` to NuGet.org

This repository is configured to build a single NuGet package, **`ActiveStateMachine`**, that
contains:

| Path in package | Content | Role |
| --- | --- | --- |
| `analyzers/dotnet/cs/ActiveStateMachine.Generators.dll` | The incremental source generator (also injects the `[ActiveObjectAsync]`/`[ActiveObjectSync]`/`[StateTrigger]` attributes) | Loaded as a Roslyn analyzer |
| dependency `Stateless >= 5.15.0` | — | Flows to consumers (generated code uses it) |
| `README.md` | — | Package landing page |

The package is **analyzer-only**: there is no `lib/` assembly. The marker attributes are emitted into
each consuming compilation at build time, so nothing needs to be referenced beyond this one package.

So a consumer needs a single reference:

```xml
<PackageReference Include="ActiveStateMachine" Version="1.0.0" />
```

Packaging is driven from [`src/ActiveStateMachine.Generators/ActiveStateMachine.Generators.csproj`](src/ActiveStateMachine.Generators/ActiveStateMachine.Generators.csproj);
shared metadata lives in [`Directory.Build.props`](Directory.Build.props).

---

## 1. Before your first publish — update metadata

Edit [`Directory.Build.props`](Directory.Build.props) and set the values that are still
placeholders:

- `PackageProjectUrl` / `RepositoryUrl` — point these at your real repository. They currently read
  `https://github.com/SoftwareSpiridon/ActiveStateMachine.git`.
- `Authors` / `Company` / `Copyright` — verify these are how you want to be credited on nuget.org.

The package id `ActiveStateMachine` must be **globally unique** on nuget.org. Check availability at
<https://www.nuget.org/packages/ActiveStateMachine> first; if it's taken, change `<PackageId>` in the
`.csproj` (e.g. to `SoftwareSpiridon.ActiveStateMachine`).

## 2. Set the version

Bump `<VersionPrefix>` in [`Directory.Build.props`](Directory.Build.props) for each release
(follow [SemVer](https://semver.org/)). For a pre-release, add a suffix at pack time:

```bash
dotnet pack src/ActiveStateMachine.Generators -c Release -o artifacts --version-suffix preview.1
```

## 3. Build the package

```bash
dotnet pack src/ActiveStateMachine.Generators -c Release -o artifacts
```

This produces `ActiveStateMachine.<version>.nupkg` in `artifacts/`. (The package is analyzer-only and
ships no symbol package.)

Inspect the contents before pushing (a `.nupkg` is a zip):

```bash
unzip -l artifacts/ActiveStateMachine.1.0.0.nupkg
```

## 4. Get a NuGet API key

1. Sign in at <https://www.nuget.org>.
2. Go to **Account → API Keys → Create**.
3. Scope it to **Push** and, ideally, glob the package name (`ActiveStateMachine`) so the key can
   only publish this package.
4. Copy the key — nuget.org shows it only once.

> **Treat the API key like a password.** Do not commit it, paste it into chat, or store it in the
> repo. Prefer an environment variable or the OS secret store.

## 5. Push to nuget.org

> ⚠️ **Publishing is permanent.** A published version can be *unlisted* but never truly deleted, and
> the package id is reserved forever. Double-check the version and contents first.

```bash
# The key is read from the environment so it never appears in shell history.
dotnet nuget push artifacts/ActiveStateMachine.1.0.0.nupkg \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json
```

It can take a few minutes for nuget.org to finish validating and index the package.

## 6. Verify

- Package page: `https://www.nuget.org/packages/ActiveStateMachine`
- Try it from a clean project: `dotnet add package ActiveStateMachine`

---

## Optional: source-linked debugging

To let consumers step into the sources, add [Source Link](https://github.com/dotnet/sourcelink):

```xml
<PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="All" />
```

and set `<PublishRepositoryUrl>true</PublishRepositoryUrl>` plus
`<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` in your CI build.

## Optional: one-line local pack helper

```bash
# From the repo root
dotnet pack src/ActiveStateMachine.Generators -c Release -o artifacts
```

Test the freshly built package locally without publishing by pointing a scratch project's
`nuget.config` at the `artifacts/` folder as a package source.
