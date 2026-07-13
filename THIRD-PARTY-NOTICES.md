# Third-Party Notices

ViewPrism2 is licensed under the MIT License (see [LICENSE](LICENSE)). It builds
upon the following third-party open-source components, each retaining its own
license. This file is provided for attribution and is not exhaustive of
transitive dependencies; the authoritative license for each package is the one
distributed with that package.

## Runtime dependencies (shipped with the application)

| Component | License | Project |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Controls.ItemsRepeater | MIT | https://github.com/AvaloniaUI/Avalonia |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.DependencyInjection | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| SkiaSharp (embeds Skia, BSD-3-Clause) | MIT | https://github.com/mono/SkiaSharp |
| Serilog.Extensions.Logging | Apache-2.0 | https://github.com/serilog/serilog-extensions-logging |
| Serilog.Sinks.File | Apache-2.0 | https://github.com/serilog/serilog-sinks-file |
| Dapper | Apache-2.0 | https://github.com/DapperLib/Dapper |
| SQLitePCLRaw.bundle_e_sqlite3 (embeds SQLite, public domain) | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |

### Bundled fonts

| Font | License | Project |
|---|---|---|
| Inter (via Avalonia.Fonts.Inter) | SIL Open Font License 1.1 | https://github.com/rsms/inter |

## Test-only dependencies (not distributed with the application)

| Component | License | Project |
|---|---|---|
| xunit.v3 | Apache-2.0 | https://github.com/xunit/xunit |
| Avalonia.Headless | MIT | https://github.com/AvaloniaUI/Avalonia |
| Microsoft.Testing.Extensions.HangDump | MIT | https://github.com/microsoft/testfx |

---

## License texts

### MIT License

Applies to Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.*,
Microsoft.Data.Sqlite, SkiaSharp, and Avalonia.Headless.

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Apache License 2.0

Applies to Serilog.Extensions.Logging, Serilog.Sinks.File, Dapper,
SQLitePCLRaw.bundle_e_sqlite3, and xunit.v3. Full text:
https://www.apache.org/licenses/LICENSE-2.0

Where these projects distribute a NOTICE file, that NOTICE is incorporated here
by reference. If ViewPrism2 is redistributed in binary form, retain the
applicable NOTICE files alongside this document.

### BSD-3-Clause

Applies to Skia, embedded within SkiaSharp. Full text:
https://github.com/google/skia/blob/main/LICENSE

### SIL Open Font License 1.1

Applies to the Inter font family. Full text:
https://openfontlicense.org/open-font-license-official-text/

### Public domain

SQLite (embedded within SQLitePCLRaw.bundle_e_sqlite3) is released into the
public domain. https://www.sqlite.org/copyright.html
