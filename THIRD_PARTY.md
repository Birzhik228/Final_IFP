# Third party dependencies

NuGet package references are pinned in the project files. The generated package lock files record transitive dependencies.

- ASP.NET Core Identity, Entity Framework Core and Microsoft.Data.Sqlite: Microsoft .NET projects.
- Bootstrap 5.3.3: local CSS in `wwwroot/vendor`, distributed with its license header.
- Microsoft SignalR JavaScript client 8.0.17: local browser client in `wwwroot/vendor`, distributed with its license header.
- xUnit, NSubstitute, FluentAssertions 6.12, Coverlet and Microsoft.Playwright.Xunit: development and test dependencies.

See each package's NuGet metadata and upstream project for its license. No external service is required while using the seeded application. Optional user-provided book cover URLs are loaded by the browser.
