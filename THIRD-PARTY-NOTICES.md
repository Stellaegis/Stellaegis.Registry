# Third-party notices

Stellaegis.Registry is licensed under the MIT License (see [LICENSE](LICENSE)).

Its packages contain no third-party code. They reference the packages below, which NuGet installs
alongside them, each under its own license. The table is completed as the packages are written.

| Component | Used by | License (SPDX) | Project |
| --- | --- | --- | --- |
| Stellaegis.Primitives, and Stellaegis.Primitives.Hosting | all packages | MIT | <https://github.com/Stellaegis/Stellaegis.Primitives> |
| Stellaegis.Primitives.Data | `Stellaegis.Registry.PostgreSQL` | MIT | <https://github.com/Stellaegis/Stellaegis.Primitives> |
| Stellaegis.Primitives.AspNetCore | `Stellaegis.Registry.AspNetCore` | MIT | <https://github.com/Stellaegis/Stellaegis.Primitives> |
| Dapper | `Stellaegis.Registry.PostgreSQL` | Apache-2.0 | <https://github.com/DapperLib/Dapper> |
| Npgsql | `Stellaegis.Registry.PostgreSQL` | PostgreSQL | <https://github.com/npgsql/npgsql> |
| Microsoft.Extensions.\* | all packages | MIT | <https://github.com/dotnet> |

`Stellaegis.Registry.AspNetCore` uses the ASP.NET Core shared framework, which the host already has.

Test-only dependencies (xUnit, the ASP.NET Core test host) are not part of any package and are not
listed here. The PostgreSQL server that the tests and the publishing workflow use is not part of any
package either.
