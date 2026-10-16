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

## Status aplikasi: 0.2.0

Inkrement pertama menghubungkan aplikasi WPF dengan Rust core dan menyediakan:

- Pembuatan, pemilihan, penyimpanan, dan penghapusan profil koneksi.
- Label lingkungan local, staging, dan production dengan indikator warna.
- Koneksi MySQL/MariaDB melalui TCP dan TLS dengan verifikasi sertifikat serta hostname. Sertifikat CA khusus dapat dipilih; verifikasi tidak dapat dinonaktifkan dan TLS tidak turun otomatis ke plaintext.
- Pemeriksaan autentikasi, versi server, ID sesi, dan latensi koneksi.
- Buka/tutup sesi dan daftar database melalui metadata pool terpisah dari workload pool.
- Operasi native di background thread, runtime Tokio bersama, batas waktu 10 detik, serta penutupan sesi saat aplikasi ditutup.
- Metadata di `%LOCALAPPDATA%/NaviLite/connections.json`. Sandi yang diingat berada di Windows Credential Manager dengan service `NaviLite.MySQL` dan ID profil sebagai key.

Satu sesi aktif didukung pada UI awal. Putuskan sesi sebelum mengganti atau mengubah profil. Tombol Test connection hanya membuka koneksi sementara dan tidak menyimpan profil. Kolom sandi yang belum diubah memakai sandi tersimpan jika Remember password aktif; mengosongkan kolom setelah mengeditnya mengirim sandi kosong secara eksplisit.

Inkrement 0.2.0 menambahkan Data explorer:

- Pohon database dengan kategori Tables, Views, Procedures, Functions, Triggers, dan Events. Daftar objek baru dibaca ketika kategorinya diperluas.
- Inspeksi Columns, Indexes, dan Foreign keys dari cabang tabel; kolom view juga dapat dibaca. Nama, tipe, primary key, serta ringkasan objek ditampilkan tanpa membuka editor skema.
- Filter nama objek yang sudah dimuat. Cabang yang belum dimuat tetap tersedia agar bisa diperluas; filter tidak memicu pembacaan seluruh skema.
- Klik ganda tabel/view atau pilih Open selected untuk membuka grid baca saja dan tab Definition berisi DDL yang sudah ada. Kegagalan membaca DDL tidak membuang halaman data yang berhasil dimuat.
- Previous, Next, dan Refresh data. Halaman memuat maksimal 1.000 baris, dengan ukuran lebih kecil untuk tabel lebar. Tidak ada COUNT(*) otomatis. Urutan memakai primary key jika tersedia; objek tanpa primary key menampilkan keterangan urutan yang tidak dijamin. Perubahan data server di antara halaman dapat menggeser paginasi berbasis offset.
- Teks ditampilkan sampai 1.024 karakter dan data biner sampai 256 byte sebagai heksadesimal. Cuplikan yang terpotong diberi tanda `…`, NULL dibedakan dalam frame, dan sel kosong tetap kosong. Frame dibatasi 8 MiB dan pratinjau mendukung sampai 1.024 kolom.
- Pembacaan data berlangsung dalam transaksi READ ONLY yang diakhiri ROLLBACK. Grid memakai virtualisasi baris/kolom; C# membaca frame milik Rust melalui SafeHandle dan mendekode sel sesuai kebutuhan, tanpa menyalin seluruh payload menjadi JSON atau DataTable.

Saat sudah terhubung, buka tab Data explorer, perluas database lalu kategori objek. Refresh pada panel navigasi membuang cache pohon dan memuat ulang daftar database. Halaman data aktif dapat dimuat ulang lewat Refresh data.

SSH tunnel, sertifikat klien TLS, socket transport, impor/ekspor koneksi, guardrail SQL production, penyuntingan data, Form View, filter/sort SQL, editor SQL, dan transfer masih menunggu inkrement berikutnya. Label production saat ini hanya indikator lingkungan; aplikasi belum menyediakan eksekusi SQL pengguna.

## Build dan jalankan

```powershell
./scripts/Build.ps1
./scripts/Run.ps1
```

`Run.ps1` melakukan build terlebih dahulu. Build Cargo menghasilkan `Interop/NativeMethods.g.cs` melalui csbindgen; build .NET menyalin `navilite_core.dll` ke direktori aplikasi. Jalankan build gabungan setelah mengubah Rust atau kontrak FFI.

Build Release:

```powershell
./scripts/Build.ps1 -Configuration Release
./scripts/Run.ps1 -Configuration Release
```

Struktur kode:

| Lokasi | Tanggung jawab |
| --- | --- |
| `native/navilite-core/src/profiles.rs` | Validasi profil, metadata lokal, dan Windows Credential Manager |
| `native/navilite-core/src/connection.rs` | Runtime Tokio, pool, koneksi TCP/TLS, dan metadata database |
| `native/navilite-core/src/ffi.rs` | API C, respons JSON UTF-8, penahanan panic, dan pelepasan memori |
| `native/navilite-core/src/schema.rs` | Metadata lazy-load, pengutipan identifier, dan DDL yang sudah ada |
| `native/navilite-core/src/table_page.rs` | Proyeksi SELECT terbatas, transaksi baca saja, dan frame biner NVLR |
| `src/NaviLite.Desktop/Models/NativePage.cs` | Kepemilikan buffer native, validasi frame, dan decoding sel |
| `src/NaviLite.Desktop/Services/` | Wrapper native, serialisasi konfigurasi, dan penghapusan buffer sandi |
| `src/NaviLite.Desktop/ViewModels/` | State dan command workspace WPF |
| `src/NaviLite.Desktop/Interop/` | Binding yang dihasilkan; jangan diedit manual |

## Kontrak native

Setiap fungsi operasi mengembalikan `FfiResult { code, data }`. `code = 0` berarti berhasil, `code = -1` berarti gagal dengan payload `{ "message": "..." }`. `data` adalah JSON UTF-8 berakhir NUL milik Rust. C# selalu memanggil `navilite_result_free` sekali setelah membaca respons. Input hanya dipin selama pemanggilan; Rust tidak menyimpan pointer C#.

| Fungsi | Input / hasil |
| --- | --- |
| `navilite_core_info` | Versi produk dan versi ABI (1) |
| `navilite_profiles_list` | Daftar metadata profil, tanpa sandi |
| `navilite_profile_save` | JSON profil + sandi opsional; mengembalikan profil |
| `navilite_profile_delete` | ID profil; menghapus metadata dan kredensial |
| `navilite_conn_test` | JSON profil + sandi opsional; informasi server tanpa sesi tetap |
| `navilite_conn_open` | JSON profil + sandi opsional; informasi server dan handle sesi |
| `navilite_conn_close` | Handle sesi; idempotent |
| `navilite_databases_list` | Handle sesi; daftar nama database |
| `navilite_schema_objects` | Handle, database, tabel opsional, kategori; daftar metadata objek |
| `navilite_object_ddl` | Handle, database, objek; string CREATE TABLE/VIEW |
| `navilite_table_page` | Handle, database, objek, offset, page size; `FfiBuffer` berisi frame NVLR |
| `navilite_buffer_free` | Pointer dan panjang asli `FfiBuffer`; pembebasan sekali oleh SafeHandle |

Pada API awal ini, `conn_open` menerima konfigurasi secara langsung agar profil baru bisa dibuka sebelum disimpan. Tidak ada kredensial yang dikembalikan ke UI. Sandi null berarti memakai Credential Manager jika profil mengingat sandi; string kosong berarti sandi kosong eksplisit. Pratinjau data memakai workload pool, terpisah dari metadata. Koneksi interaktif yang dipertahankan per tab belum diimplementasikan.

### Frame NVLR v1

`FfiBuffer { code: i32, data: *mut u8, length: usize }` memiliki kontrak berbeda dari CString: payload sukses adalah biner dan payload gagal (`code = -1`) adalah JSON UTF-8 sepanjang `length`, tanpa terminator NUL. Keduanya wajib dibebaskan dengan `navilite_buffer_free(data, length)`, bukan `navilite_result_free`. Pointer input dipakai hanya selama pemanggilan. ABI produk tetap 1; format frame memiliki versi terpisah.

Semua integer di frame memakai little endian. Header 40 byte:

| Offset | Isi |
| --- | --- |
| 0 | Magic `NVLR` (4 byte) |
| 4 | Versi frame (u32), saat ini 1 |
| 8, 12 | Jumlah kolom dan baris (u32) |
| 16, 24 | Offset halaman dan offset berikutnya (u64) |
| 32 | Flags (u32): bit 0 ada halaman berikutnya, bit 1 diurutkan dengan primary key |
| 36 | Jumlah sel yang dipotong (u32) |

Header diikuti metadata kolom berupa pasangan nama dan tipe SQL; setiap string diawali panjang byte u32. Berikutnya ada direktori `row_count + 1` offset absolut u32, termasuk akhir frame. Setiap baris diawali `column_count` offset sel u32 relatif terhadap awal baris. Sel berisi tag u8, panjang payload u32, lalu byte payload. Tag 0 = NULL, 1 = teks UTF-8, 2 = biner; bit 7 menandai cuplikan terpotong. C# memvalidasi header, direktori, batas sel, serta pagination sebelum mengekspos baris ke grid. Frame lama dibebaskan saat halaman diganti atau sesi diputus; SafeHandle juga menjadi pengaman finalisasi.

## Versioning dan alur development

Proyek memakai [Semantic Versioning 2.0.0](https://semver.org/) dan Conventional Commits (`feat:`, `fix:`, `chore:`). Versi Rust di `Cargo.toml` dan versi .NET di `Directory.Build.props` harus sama; build menolak ketidaksesuaian dan aplikasi memeriksa versi core saat startup.

```powershell
./scripts/Set-Version.ps1 -Version 0.2.0
```

Skrip memperbarui workspace Cargo, Cargo.lock, serta versi produk/assembly .NET. Perbarui CHANGELOG.md, lakukan verifikasi, commit, lalu buat tag `v0.2.0`. Selama versi `0.x`, fitur baru dan perubahan API yang memutus kompatibilitas menaikkan MINOR; perbaikan kompatibel menaikkan PATCH. Mulai `1.0.0`, perubahan API yang memutus kompatibilitas menaikkan MAJOR.

Development mengikuti pendekatan code first. Jangan membuat test atau project test tanpa instruksi eksplisit pengguna. Verifikasi yang digunakan:

```powershell
. ./scripts/Enter-Dev.ps1
cargo fmt --all -- --check
cargo clippy --workspace --locked -- -D warnings
./scripts/Build.ps1 -Configuration Release
```

Workflow GitHub Actions menjalankan format, build Windows x64, pemeriksaan binding, dan Clippy tanpa menjalankan test. Autentikasi ke server developer memerlukan kredensial yang dimasukkan pengguna; jangan mengubah akun, konfigurasi server, atau database untuk memverifikasi setup.

Referensi instalasi: [Rust di Windows](https://rust-lang.github.io/rustup/installation/windows-msvc.html), [.NET install script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
