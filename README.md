# Its.Compiler (.NET)

.NET implementation of the [Instruction Template Specification (ITS)](https://alexanderparker.github.io/instruction-template-specification/) compiler. Compiles JSON templates with placeholders, variables, conditionals and reference data into structured AI prompts, with output byte-compatible with the JavaScript and Python reference compilers (verified by a golden-prompt parity test).

## Installation

```bash
dotnet add package Its.Compiler
```

NuGet publication is pending; until then, reference the `src/Its.Compiler` project directly.

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
- Collection functions (`concat`, `sum`, `avg`, `min`, `max`, `top`) as chainable suffixes on array references, substitution only
- Object-valued references substituting the pointer text "the X reference data" and rendering automatically as reference data
- Conditionals with the specification's operators (`==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`, `in`, `not in`, chained comparisons, array literals, negative array indices) plus `and`/`or`/`not` equivalents
- Instruction types through `extends` with the complete-override precedence rules and `customInstructionTypes`
- `configSchema` defaults substituted for omitted placeholder config, booleans rendered JSON-style
- Reference data: `dataSource` / `dataLimit` placeholder config rendered as a `REFERENCE DATA` section above the template (arrays of objects as tables, plain objects as field tables), with the context-only processing instruction; `dataLimit` values for the same variable merge with the largest winning, and no limit beats any limit
- Configurable processing limits (see below)

## Published type libraries

Templates import instruction types through `extends`. The specification publishes these libraries under `https://alexanderparker.github.io/instruction-template-specification/schema/v1.0/`:

| Library | File | Purpose |
| --- | --- | --- |
| Standard Types | `its-standard-types-v1.json` | Prose content: titles, lists, paragraphs, tables, dialogue and more |
| JSON Types | `its-json-types-v1.json` | Value fills inside JSON structure authored in the template: json_string, json_number, json_value, json_array_items, json_object_fields |
| HTML Types | `its-html-types-v1.json` | 5 position fills (html_text, html_fragment, html_list_items, html_table_rows, html_form_fields) plus 10 complete-element generators (html_heading, html_paragraph, html_link, html_image, html_list, html_table, html_section, html_blockquote, html_code_block, html_form) |
| YAML Types | `its-yaml-types-v1.json` | Fills inside literal YAML: yaml_value, yaml_list_items, yaml_block |
| Markdown Types | `its-markdown-types-v1.json` | Fills inside literal Markdown: markdown_text, markdown_block, markdown_list_items, markdown_table_rows, markdown_code |

The structured-output libraries (JSON, HTML, YAML, Markdown) instruct the model to emit raw output with no code fences and no commentary. If a placeholder omits a config property, defaults declared in the library's `configSchema` are substituted into the compiled instruction.

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

## HTTP compile service sample

`samples/Its.Compiler.Service` is an ASP.NET minimal API exposing the same compile contract over plain HTTP:

```
POST /compile
GET  /health
```

CORS origins come from `ITS_CORS_ORIGINS` (comma-separated), defaulting to the studio demo origins, and the service binds to the port given by `PORT`. The repo-root `Dockerfile` builds this sample, and `railway.json` deploys it with the `/health` check. It backs the "Server (.NET)" engine of the live demo at https://alexanderparker.github.io/its-template-studio/.

## Security model

Parity with the Python reference compiler, verified by running the full shared test corpus (every valid template compiles; every invalid and security template is blocked):

- Structural validation: element types and required fields (including the required placeholder `description`), custom instruction type checks, extends entry validation (relative references allowed, non-http schemes and protocol-relative URLs rejected)
- Content scanning: dangerous patterns (script and iframe tags, JavaScript and data URLs, eval/exec/Function calls, event handlers, dunder names) and null bytes in text elements, config values and string variables; toggleable via `EnableContentScanning`
- Variable hardening: identifier-pattern names with dangerous names blocked (`__proto__`, `constructor`, `eval` and friends)
- Schema URL security: https-only by default, trusted specification prefixes, a domain allowlist enforced by default (`DomainAllowlist` / `EnforceDomainAllowlist`), localhost and private-network blocking, path traversal rejection, response size caps and schema structure validation
- Conditions are evaluated by a hand-rolled parser with no dynamic evaluation, so code injection through expressions is impossible by construction

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
