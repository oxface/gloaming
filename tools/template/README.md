# T1 creation

From the Foundry checkout, on Linux with glibc, Node.js 24.21+ (24.x), npm and the
pinned .NET 10 SDK:

```sh
npm ci --prefix tools/template --ignore-scripts
npm --prefix tools/template run check
npm --prefix tools/template run create -- --config example.json --output /tmp/Cedar
```

The npm command runs in `tools/template`, so configuration paths are relative to that
directory; absolute paths also work. The configuration is a JSON object containing exactly
`applicationName` and `rootNamespace`.
Application names are ASCII C# identifiers of 1–64 characters. Root namespaces are dotted
ASCII C# identifiers, at most 200 characters. Reserved C# keywords (including compiler
tokens `__arglist`, `__makeref`, `__reftype` and `__refvalue`), device application
names, duplicate/unknown keys and template source tokens are rejected. Both values are
required strings. There are no capability/provider/authentication options: the sole
composition is a .NET 10 console host, Catalog Contracts/module, PostgreSQL state storage,
native migration and adoption tests. See the generated README for its runnable journey
and consumer obligations.

Output must not exist, including an empty directory, file or symlink; its parent must
already exist. Repeat creation is refused. There is no force/update mode. The creator
stages all work in a temporary sibling directory, uses Linux `renameat2(RENAME_NOREPLACE)`
to publish the complete tree, and removes staging on ordinary failures. A competing creator
cannot overwrite the winner, even if the destination is empty. Process kill/power loss can
leave a temporary sibling, but cannot publish a partial destination. Filesystem durability
across power loss is not claimed.

The native `dotnet new` engine owns substitution and project/file naming through
`templates/state-stored/.template.config/template.json`. It is installed in a temporary
custom hive so creation does not modify the user's installed template list. Both native
commands run from staging with its own copy of the template's `global.json`, so a
conflicting SDK pin in the output parent cannot select a different SDK. The internal
custom-hive switch is exercised against SDK 10.0.112; compatibility with later SDKs needs
the same proof. No post-actions or restore run during creation.

The wrapper supplies only strict input validation, snapshots of the four selected existing
libraries, provenance hashes and transactional initial creation. Direct installation of
the bare source skeleton is not the supported entry point: the wrapper adds the library
payload first. The explicit `librarySources` map resolves the upstream projects from
their family folders. C# snapshots remain byte-identical; project references are relocated to the
generated flat `libraries/{Package}` tree, rejecting dependencies outside the selected libraries.
The manifest hashes the materialized files, including those adjusted project files. The shared EF
package is included because Persistence references it; Catalog retains explicit save validation.
No domain-event or messaging capability is enabled. No template runtime, custom replacement language or
general generation engine are introduced.

The wrapper and creation proof are TypeScript, executed by Node's native type stripping;
`tsc --noEmit` checks types separately. npm owns the exact dependency versions and lockfile.
`jsonc-parser` detects duplicate property keys, including escaped spellings, after strict
JSON parsing. `koffi` calls glibc's no-replace rename; Node's ordinary rename would overwrite
an empty destination. Its platform binary is an npm optional dependency, so install scripts
are disabled and no native compilation is needed on the tested Linux x64 platform. Keep
optional dependencies enabled. These dependencies belong only to the creation tooling;
the generated .NET application needs neither Node/npm nor a Foundry creation runtime.
`fast-xml-parser`, TypeScript, Node types and Prettier are development dependencies used
for proof inspection and tooling checks. No frontend workspace is introduced.

Native authoring was evaluated against Microsoft's [template authoring reference](https://learn.microsoft.com/en-us/dotnet/core/tools/templates)
and [configuration reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json),
then exercised with multi-project output and independent namespace/name substitutions.
Native authoring meets the materialization requirement; its existing-output merge behavior
does not supply T1's stricter new-directory contract by itself.

## Creation proof

Set `CATALOG_TEST_ADMIN_CONNECTION_STRING` to a disposable local PostgreSQL 18.6 server,
then run:

```sh
npm --prefix tools/template run verify
```

The proof creates consumers outside the checkout, compares identical-input trees before
tooling output exists, including a root namespace of `Task`, beneath a conflicting SDK 9
parent pin. It inspects naming/dependencies/references/source hashes, checks failure
atomicity (including partial engine failure and a destination created after preflight),
then restores/builds and runs both generated adoption suites. Each suite owns
its own fresh database. CI supplies PostgreSQL as a service and runs this command.
The proof needs NuGet network access and local process/IPC/database access. It never changes
Foundry source, stages files, commits or publishes packages.

The [Node TypeScript reference](https://nodejs.org/docs/latest-v24.x/api/typescript.html)
describes native execution and its separate type-check requirement. Koffi's
[function declarations](https://koffi.dev/load) and [errno support](https://koffi.dev/misc)
describe the narrow native bridge used here. Linux x64/glibc is freshly exercised;
other platforms need their own creation proof.
