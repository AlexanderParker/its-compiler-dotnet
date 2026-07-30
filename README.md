# Its.Compiler (.NET)

.NET implementation of the [Instruction Template Specification (ITS)](https://alexanderparker.github.io/instruction-template-specification/) compiler. Compiles JSON templates with placeholders, variables, conditionals and reference data into structured AI prompts, with output byte-compatible with the JavaScript and Python reference compilers (verified by a golden-prompt parity test).

## Installation

```bash
dotnet add package Its.Compiler
```

Targets .NET 8.

## Usage

```csharp
using Its.Compiler;
using System.Text.Json.Nodes;

var template = (JsonObject)JsonNode.Parse(File.ReadAllText("template.json"))!;
var compiler = new ItsCompiler();

var result = await compiler.CompileAsync(template, new JsonObject { ["topic"] = "renewable energy" });
Console.WriteLine(result.Prompt);

// Or compile a file so relative extends resolve against its directory
// (local file schemas require CompilerOptions.AllowLocalFileSchemas):
var fromFile = await new ItsCompiler(new CompilerOptions { AllowLocalFileSchemas = true })
    .CompileFileAsync("template.json");
```

Feature parity with the reference compilers:

- Variable substitution: `${name}`, `${user.name}`, `${items[0]}`, `${items[-1]}`, `${items.length}`
- Conditionals with the specification's operators (`==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`, `in`, `not in`, chained comparisons, array literals) plus `and`/`or`/`not` equivalents
- Instruction types through `extends` with the complete-override precedence rules and `customInstructionTypes`
- `configSchema` defaults substituted for omitted placeholder config, booleans rendered JSON-style
- Reference data: `dataSource` / `dataLimit` placeholder config rendered as a `REFERENCE DATA` section above the template (arrays of objects as tables, plain objects as field tables), with the context-only processing instruction
- Configurable processing limits (see below)

## Processing limits

All limits are operator-configurable through `CompilerOptions` or, with `CompilerOptions.FromEnvironment()`, the same environment variables as the Python compiler:

| Option | Environment variable | Default |
| --- | --- | --- |
| `MaxTemplateSize` | `ITS_MAX_TEMPLATE_SIZE` | 1 MB |
| `MaxContentElements` | `ITS_MAX_CONTENT_ELEMENTS` | 1000 |
| `MaxNestingDepth` | `ITS_MAX_NESTING_DEPTH` | 10 |
| `MaxVariableCount` | `ITS_MAX_VARIABLE_COUNT` | 10000 |
| `MaxVariableArrayItems` | `ITS_MAX_VARIABLE_ARRAY_ITEMS` | 1000 |
| `MaxTextLength` | `ITS_MAX_TEXT_LENGTH` | 10000 |
| `AllowHttp` | `ITS_ALLOW_HTTP` | false |
| `AllowLocalFileSchemas` | `ITS_ALLOW_LOCAL_SCHEMAS` | false |

## Azure Functions and API Management

`samples/Its.Compiler.AzureFunctions` is a ready-to-deploy isolated-worker function app exposing the standard ITS compile contract:

```
POST /api/compile
{"template": {...}, "variables": {...}}
->
{"ok": true, "prompt": "...", "warnings": [], "error": null, "compiler": "its-compiler (dotnet)"}
```

Deploy it as a Function App and front it with API Management for authentication (subscription keys), rate limiting and policies:

```bash
cd samples/Its.Compiler.AzureFunctions
func azure functionapp publish <your-function-app>
```

Processing limits are configurable per environment through the `ITS_*` application settings above. The function holds no state and no model keys: it compiles templates deterministically, so downstream steps (a Logic App, Workato recipe or any orchestrator) pass the compiled prompt to whichever governed LLM connector the organisation already uses.

## Development

```bash
dotnet build Its.Compiler.sln
dotnet test Its.Compiler.sln
```

The test fixtures (type libraries and templates) are shared with its-compiler-js and its-compiler-python, and a golden-prompt test asserts byte parity with the Python compiler's output.

## ITS ecosystem

- [Specification](https://alexanderparker.github.io/instruction-template-specification/) - the ITS spec, schemas and documentation ([source](https://github.com/AlexanderParker/instruction-template-specification))
- [Template studio demo](https://alexanderparker.github.io/its-template-studio/) - build and compile templates in the browser ([source](https://github.com/AlexanderParker/its-template-studio))
- [its-template-editor](https://github.com/AlexanderParker/its-wysiwyg-common) - the WYSIWYG React editor component behind the studio
- [its-compiler-js](https://github.com/AlexanderParker/its-compiler-js) - JavaScript/TypeScript reference compiler ([npm](https://www.npmjs.com/package/its-compiler-js))
- [its-compiler-python](https://github.com/AlexanderParker/its-compiler-python) - Python reference compiler library ([PyPI](https://pypi.org/project/its-compiler/))
- [its-compiler-cli](https://github.com/AlexanderParker/its-compiler-cli-python) - command-line interface for the Python compiler ([PyPI](https://pypi.org/project/its-compiler-cli/))
- [its-example-templates](https://github.com/AlexanderParker/its-example-templates) - example and test templates exercising the published schemas

## License

MIT - see [LICENSE](LICENSE).
