# Code StyleGuide

This document describes the coding conventions and standards for TinkerFillet development.

### Naming Rules

For all files, folders, projects, and repositories:
- Use **PascalCase** for all names
- No spaces or special characters (only letters and numbers, no underscores)
- Only English names and comments (no umlauts)
- Abbreviations with four or fewer characters can be full UPPERCASE

---

## C# Rules

Base conventions follow Microsoft guidelines: [Identifier Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)

### Editor Settings

- File-scoped namespaces and StyleCop exceptions are in the global `.editorconfig` file

### Additional Rules

From `Indiwa\Nova\Docs\Naming.md`:
- No spaces in identifiers
- Only English names (no umlauts)
- Abbreviations with four or fewer characters are full UPPERCASE

### Coding Standards

- **Namespaces:** Always single line at top of file, file-scoped (no brackets around class)
- **Variable declarations:** Use explicit types. The use of `var` is **not allowed** in Nova (note: differs from original CodeStyleGuide)
- **One class per file:** File name equals class name
- **Folder structure:** Parent folders in projects represent the namespace
- **Private fields:** Prefix with underscore (`_fieldName`)

---

## Blazor-Specific Rules

### Razor Files Naming and Nesting

- Use PascalCase for Razor files: `MyComponent.razor`
- File nesting structure:
  ```
  MyComponent.razor
  ├── MyComponent.razor.cs    (partial class with same name)
  └── MyComponent.razor.css   (CSS rules for the component)
  ```

### Component Structure

- **Always use partial classes** - keep UI markup and C# logic separate
- **No `@code {}` blocks** - use code-behind files instead
- Use `[Inject]` attribute for dependency injection (or primary constructors for services)

### Global Import Files

- Avoid `using` statements in pages and components when possible; prefer global imports

---

## CSS Rules

1. **Plain CSS only** - No preprocessors like Sass in Web projects (Sass only for theme builder)
2. **No inline CSS** - Never use style tags in markup
3. **Avoid `app.css`** - Use component CSS (Blazor CSS isolation) where possible
5. **Advanced CSS features** - Use grid, flexbox, variables, and calculations
   - Do NOT use CSS nesting (VS and ReSharper don't support validation)
6. **Class naming** - Always lowercase-hyphen convention: `nova-frame-border`

---

## Visual Studio Conventions

### Todo/Task-List Tags

Use different Task-List tags in comments:

| Tag | Purpose | Warning |
|-----|---------|---------|
| `TODO` | Task to finalize in current branch session | Creates warning (S1135) |
| `ASK` | Something to discuss with team member | No warning |
| `LATER` | Code that needs to be changed later | No warning |
| `HACK` | Temporary workaround to be fixed | No warning |

---

## Key Differences from Original Guide

The following rules have been updated for AI-assisted development:

1. **No `var` usage** - Always use explicit types (original guide allowed `var`)
2. **Partial classes mandatory** - Original mentioned it; now strictly enforced
3. **Private field prefix** - Use underscore prefix (`_fieldName`)

---

## Quick Reference

```csharp
// File: MyComponent.razor.cs
namespace Indiwa.Nova.TMS.FrontEnd.Modules.Orders.Components;

public partial class MyComponent
{
    // Private fields with underscore prefix
    private IReadOnlyList<OrderDto>? _orders;
    private bool _isLoading = true;

    // Injected services
    [Inject]
    public required IOrdersClient GraphQLClient { get; set; }

    // Parameters
    [Parameter]
    public string? Title { get; set; }

    // EventCallbacks
    [Parameter]
    public EventCallback<OrderDto> OnOrderSelected { get; set; }

    // Lifecycle methods
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LoadOrdersAsync();
    }

    // Private methods
    private async Task LoadOrdersAsync()
    {
        _isLoading = true;
        IOperationResult<IGetOrdersResult> result = await GraphQLClient.GetOrders.ExecuteAsync();

        if (result.IsSuccessResult())
        {
            _orders = result.Data!.Orders!.Nodes!.ToList();
        }
        _isLoading = false;
    }
}
```

