# NaviLite

Klien desktop MySQL/MariaDB khusus Windows dengan C# WPF dan Rust core. Folder awal berisi PRD dan spesifikasi fitur. Framework UI yang dipilih: **WPF**.

## Environment development Windows x64

- .NET SDK 8.0.426, sesuai target dokumen proyek, dengan patch terbaru dalam feature band yang sama melalui `global.json`.
- Rust 1.99.1 dengan target `x86_64-pc-windows-msvc`, Cargo, rustfmt, dan Clippy; versi dipatok di `rust-toolchain.toml`.
- Visual Studio 2022 Build Tools: C++ tools dan Windows SDK.
- Git.
- MySQL lokal untuk pengujian integrasi. Server yang sudah ada digunakan tanpa mengubah konfigurasi, akun, atau database.

Aktifkan toolchain untuk terminal PowerShell saat ini dari folder proyek:

```powershell
. ./scripts/Enter-Dev.ps1
./scripts/Test-DevEnvironment.ps1
```

.NET dipasang per pengguna di `%LOCALAPPDATA%\NaviLite\dotnet`; Rust menggunakan `%USERPROFILE%\.cargo\bin`. Skrip aktivasi menambahkan lokasi tersebut ke PATH terminal saat ini. Tidak perlu mengubah execution policy secara permanen.

Aktivasi juga memuat environment compiler MSVC x64 dari instalasi Visual Studio Build Tools yang terdeteksi.

## Pemasangan pada komputer baru

```powershell
winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --source winget --silent --accept-package-agreements --accept-source-agreements --override '--wait --quiet --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended'
winget install --id Rustlang.Rustup --exact --source winget --silent --accept-package-agreements --accept-source-agreements
```

Pasang .NET dengan skrip resmi Microsoft:

```powershell
New-Item -ItemType Directory -Force .dev/downloads | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile .dev/downloads/dotnet-install.ps1
& ./.dev/downloads/dotnet-install.ps1 -Version 8.0.426 -InstallDir "$env:LOCALAPPDATA/NaviLite/dotnet" -NoPath
. ./scripts/Enter-Dev.ps1
rustup toolchain install 1.99.1 --profile minimal --component rustfmt,clippy
./scripts/Test-DevEnvironment.ps1
```

Build Tools mungkin memerlukan persetujuan administrator atau restart Windows. Pemeriksaan environment harus berhasil sebelum mulai implementasi.

## Database development

Pemeriksaan menggunakan `127.0.0.1:3306` dan hanya menguji port TCP, bukan autentikasi. Gunakan akun development terpisah saat implementasi koneksi dimulai. Jangan menyimpan password dalam kode atau Git; `.env` diabaikan oleh `.gitignore`.

## Status aplikasi

Fondasi menggunakan WPF untuk Windows dan Rust `cdylib` untuk core. Implementasi fitur koneksi database, editor, dan transfer belum dimulai.

Struktur:

- `NaviLite.sln` dan `src/NaviLite.Desktop/`: aplikasi WPF dari template standar .NET.
- `Cargo.toml` dan `native/navilite-core/`: workspace Rust dan library core minimal.
- `scripts/`: aktivasi, pemeriksaan, dan build.

Build kedua komponen:

```powershell
./scripts/Build.ps1
```

Jalankan jendela WPF awal:

```powershell
. ./scripts/Enter-Dev.ps1
dotnet run --project src/NaviLite.Desktop
```

Jendela masih kosong sesuai template. Core Rust belum dihubungkan ke aplikasi; kontrak FFI dan generator binding `csbindgen` ditambahkan saat implementasi API dimulai. Uji integrasi toolchain sementara berada di `.dev/smoke/` dan bukan kode aplikasi.

## Verifikasi setup, 16 Oktober 2026

- Build solution WPF berhasil tanpa error atau warning.
- Build workspace Rust, pemeriksaan rustfmt, dan Clippy dengan warning sebagai error berhasil.
- Uji DLL Rust dari aplikasi console C# berhasil mengembalikan nilai `42` melalui C ABI.
- MySQL lokal merespons koneksi TCP pada `127.0.0.1:3306`; autentikasi belum diuji.

Referensi instalasi: [Rust di Windows](https://rust-lang.github.io/rustup/installation/windows-msvc.html), [.NET install script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
