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

Aktivasi memprioritaskan .NET dan Rust proyek di PATH, termasuk ketika lokasi tersebut sudah ada setelah instalasi sistem lain. Aktivasi juga memuat environment compiler MSVC x64 dari instalasi Visual Studio Build Tools yang terdeteksi.

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

## Status aplikasi: 0.16.0

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

Inkrement 0.3.0 menambahkan filter/sort dan Form View:

- Satu kondisi filter per tabel/view: Equals, Not equal, Contains, Starts with, Greater than, Less than, IS NULL, atau IS NOT NULL. Pilih kolom, kondisi, dan nilai lalu Apply. Contains/Starts with memperlakukan `%`, `_`, dan `!` sebagai teks literal; perbandingan teks mengikuti collation server. Kolom biner mendukung kondisi NULL saja.
- Filter diproses di server memakai prepared statement. Integer dikirim sebagai angka 64 bit; FLOAT memakai presisi 32 bit dan DOUBLE presisi 64 bit. DECIMAL memakai konversi desimal eksplisit agar tidak kehilangan presisi melalui floating point. Nilai angka yang tidak valid ditolak. Nilai filter maksimal 4.096 karakter; desimal memakai titik dan tidak menerima notasi eksponen.
- Satu kolom sort ASC/DESC melalui pilihan Order by atau klik header grid. Klik header berulang beralih ASC, DESC, lalu urutan default. Primary key ditambahkan sebagai pembeda urutan jika tersedia. Filter/sort berlaku pada seluruh hasil sebelum pagination.
- Apply dan Reset kembali ke halaman pertama. Previous/Next/Refresh menggunakan filter/sort yang sudah diterapkan; perubahan draft ditandai sampai Apply dipilih. Jika permintaan gagal, halaman dan kondisi aktif sebelumnya tetap tersedia.
- Tab Form menampilkan satu record dari halaman aktif dengan nama/tipe kolom, penanda NULL, biner, dan cuplikan terpotong. Pilih record dari daftar atau gunakan tombol Record. Ctrl+G beralih grid/form; pilihan record tersinkron dan pergantian tampilan tidak membaca ulang server. Form memakai batas cuplikan yang sama dengan grid dan tetap baca saja.

Inkrement 0.4.0 menambahkan Cell Inspector baca saja:

- Klik ganda sel grid atau tekan F2 pada sel aktif; pada Form View pilih Inspect di bawah nama kolom. Viewer menampilkan teks, formatter JSON, dan daftar heksadesimal/ASCII dengan virtualisasi. Kolom NULL dan string/biner kosong tetap dibedakan.
- Nilai yang lengkap dalam frame halaman langsung ditampilkan tanpa kueri tambahan. Cuplikan terpotong dibaca ulang berdasarkan primary key yang diambil bersama halaman, termasuk primary key gabungan dan angka presisi tinggi. Identitas ini tetap berada di Rust dan dilepas bersama SafeHandle halaman atau saat sesi ditutup.
- Pembacaan nilai di server dibatasi 1 MiB. Potongan UTF-8 di ujung buffer dibuang agar teks tetap valid. Jumlah byte asli/terbaca dan penanda Partial value ditampilkan; nilai yang dibaca ulang merupakan nilai server saat ini dan dapat berubah sejak halaman dibuka.
- Tabel/view tanpa primary key yang dapat dipakai, key terlalu besar (maksimal 4 KiB per kolom dan 16 KiB per record), tipe primary key yang belum didukung (termasuk BIT, TIMESTAMP, ENUM, dan SET), record yang hilang, perubahan skema, atau kegagalan pembacaan menghasilkan viewer cuplikan dengan alasan yang jelas. Key yang didukung meliputi integer/desimal/float, DATE/DATETIME/TIME/YEAR, teks, dan biner. Tidak memakai offset halaman untuk mencari ulang record.
- Formatter JSON berlaku pada nilai teks lengkap sampai 256 KiB dan kedalaman 64; format tampilan tidak mengubah byte sumber. Copy value menyalin teks, hasil format JSON saat tab JSON aktif, atau heksadesimal untuk biner. Save loaded bytes menyimpan hanya byte yang sudah dimuat: biner asli atau teks UTF-8 tanpa BOM, termasuk byte NUL. Nilai NULL tidak disamakan dengan berkas kosong.

Inkrement 0.5.0 menambahkan salin baris dan riwayat SQL sesi:

- Pilih beberapa baris grid dengan Ctrl/Shift, lalu gunakan Copy rows as untuk CSV, TSV, atau array JSON. Pada Form View, salinan memakai record aktif. Hanya baris halaman aktif yang disalin, menurut urutan halaman, dengan seluruh kolom menurut urutan skema; perpindahan halaman mengganti pilihan baris.
- CSV/TSV menyertakan header dan CRLF antar-record. Header dan seluruh nilai non-NULL diberi kutip ganda, termasuk pelolosan kutip, delimiter, serta baris baru dalam sel. NULL memakai konvensi `\N` tanpa kutip, string kosong memakai `""`, dan biner memakai teks `0xHEX`. Nilai teks literal `\N` tetap diberi kutip. JSON memakai `null` untuk NULL dan string untuk seluruh nilai lain, termasuk angka agar presisi BIGINT/DECIMAL tidak berubah, serta `0xHEX` untuk biner.
- Penyalinan memakai frame halaman tanpa pembacaan ulang server. Jika ada sel terpotong, seluruh permintaan ditolak sebelum clipboard diubah. Output dibatasi 8 MiB UTF-8. CSV/TSV menolak NUL di dalam teks; JSON dapat menyalinnya sebagai escape. Gunakan Cell Inspector untuk menyimpan byte sel yang telah dimuat.
- Tab SQL history menampilkan waktu mulai, durasi statement, konteks, operasi, hasil Success/Error/Interrupted, dan template SQL. Query metadata, daftar database, DDL, pembacaan halaman/sel, serta START TRANSACTION READ ONLY dan ROLLBACK dicatat. Durasi berasal dari Rust dan tidak mencakup rendering UI atau menunggu pool sebelum query dimulai. Operasi yang dihentikan saat query masih berjalan, termasuk timeout, tercatat Interrupted.
- Riwayat dibatasi 200 statement per koneksi dan 16 KiB UTF-8 per template SQL, dengan penanda ketika teks dipotong. Nilai parameter, hasil query, pesan error server, password, dan handshake autentikasi tidak dicatat dalam riwayat SQL. Query internal driver untuk setup/reset pool juga tidak ditampilkan. Riwayat hanya berada di memori; Refresh/Clear tidak menjalankan SQL atau mengubah server.
- Panel riwayat diperbarui setelah operasi selesai. Salinan UI terakhir tetap terlihat setelah disconnect dan diganti saat membuka koneksi berikutnya; registry native dilepas bersama sesi. Clear tersedia selama koneksi aktif.

Inkrement 0.6.0 menambahkan filter gabungan dan urutan beberapa kolom:

- Gunakan + Filter untuk menambah hingga 16 baris kondisi. Match all conditions menggabungkannya dengan AND; Match any condition memakai OR. Semua kondisi berada dalam satu kelompok, dengan delapan operator yang sudah tersedia. Kolom yang sama dapat dipakai dalam beberapa kondisi, misalnya batas bawah dan atas suatu nilai.
- Gunakan + Sort untuk menambah hingga 8 kolom pengurutan ASC/DESC. Tombol panah mengubah prioritas; kolom teratas didahulukan. Kolom sort yang sama tidak boleh dipilih dua kali. Primary key yang belum tercakup tetap ditambahkan untuk membedakan urutan record.
- Tombol × menghapus baris kondisi/sort. Baris dengan kolom None diabaikan; nomor prioritas hanya menghitung kolom yang dipilih. Apply menerapkan draft dan kembali ke halaman pertama. Previous/Next/Refresh memakai query yang sudah diterapkan, dan kegagalan penerapan tetap mempertahankan hasil serta query aktif sebelumnya.
- Header grid menunjukkan arah dan nomor prioritas sort yang aktif. Klik header menerapkan satu kolom sort, mengganti daftar sort eksplisit sebelumnya, dengan siklus ASC → DESC → default; filter aktif tetap dipakai dan draft filter yang belum diterapkan tetap ditandai. Gunakan kontrol Order by untuk urutan beberapa kolom.
- Native tetap memakai placeholder untuk seluruh nilai filter, memeriksa metadata kolom terbaru, serta mempertahankan perbandingan angka presisi tinggi, kondisi NULL, dan pencarian teks literal. Riwayat SQL tidak menyimpan nilai parameter. Bentuk JSON tunggal `filter`/`sort` tetap diterima.

Inkrement 0.7.0 menambahkan inspeksi definisi objek dan refresh cabang navigator:

- Pilih database, tabel, view, procedure, function, trigger, atau event lalu klik Definition di navigator. Tab Object definition menampilkan CREATE statement asli dan properti yang dikembalikan server, seperti SQL mode, character set, collation, atau zona waktu. Klik ganda procedure/function/trigger/event langsung membuka definisinya; klik ganda database tetap memperluas pohon dan tabel/view tetap membuka data.
- Refresh definition membaca ulang objek yang sedang ditampilkan, meskipun pilihan navigator sudah berpindah. Pembacaan definisi tidak mengganti halaman data, filter, atau sort. Jika server menolak akses atau menyembunyikan definisi sebagai NULL, pesan ditampilkan dan definisi sebelumnya tetap tersedia. Disconnect menghapus definisi dari workspace.
- Copy SQL menyalin CREATE statement lengkap. Save SQL menyimpan teks yang sama sebagai UTF-8 tanpa BOM ke lokasi yang dipilih pengguna. Teks berisi NUL harus disimpan ke berkas. Berkas merupakan definisi asli; pengaturan sesi, delimiter klien untuk rutin, dependensi, dan langkah migrasi tidak ditambahkan. Aplikasi tidak menjalankan SQL tersebut.
- Batas tampilan gabungan SQL dan metadata adalah 4 MiB, diperiksa setelah respons server diterima. Definisi yang melampaui batas ditolak tanpa dipotong. Riwayat SQL hanya mencatat perintah SHOW CREATE beserta identifier; isi definisi tidak disimpan di riwayat atau activity log.
- Pilih kategori Tables, Views, Procedures, Functions, Triggers, Events, Columns, Indexes, atau Foreign keys lalu Reload branch untuk mengambil ulang kategori itu. Hasil baru mengganti cabang setelah pembacaan berhasil; cabang lain dan data aktif tetap tersedia. Refresh databases tetap memuat ulang seluruh pohon database.

Inkrement 0.8.0 menambahkan impor/ekspor metadata koneksi:

- Saat disconnected, gunakan Import connections di toolbar untuk membuka `.nvconn.json`, melihat pratinjau, lalu memilih profil yang akan ditambahkan. Impor tidak membuka koneksi database. Profil baru memakai ID baru dan Remember password nonaktif; masukkan password setelah impor dan simpan lewat Credential Manager jika diperlukan.
- Duplikat diperiksa berdasarkan host (tanpa membedakan huruf besar/kecil ASCII), port, username, environment, TLS, dan path CA. Nama profil serta status penyimpanan password tidak menjadi identitas duplikat. Profil yang sama di penyimpanan atau lebih awal di dalam berkas ditandai dan dilewati. Jika nama sudah dipakai oleh profil berbeda, profil baru mendapat akhiran `(imported 2)`, dan seterusnya. Pilihan baris tidak mengubah nama yang direncanakan. Duplikat dan nama diperiksa ulang saat impor agar perubahan dari jendela lain tetap dihormati.
- Seluruh profil terpilih divalidasi sebelum satu pembaruan metadata dengan lock antar-proses. Profil lama dan kredensialnya tetap tersimpan. Batal pada pratinjau tidak mengimpor profil. Tidak ada pembacaan atau perubahan Credential Manager dalam operasi impor/ekspor.
- Export connections mengekspor seluruh profil yang sudah disimpan sebagai JSON UTF-8 tanpa BOM. Perubahan draft yang belum disimpan tidak termasuk. Berkas hanya berisi metadata yang diizinkan; ID lokal, Remember password, dan password tidak diekspor. Berkas CA tidak disalin; path yang tercatat perlu diperiksa pada komputer tujuan. Ekspor memakai berkas sementara pada folder tujuan lalu mengganti target setelah penulisan selesai. Gunakan path drive/UNC biasa; path device atau extended Windows ditolak.
- Format berkas memakai `format: "navilite.connections"`, `formatVersion: 1`, dan daftar `connections`. Batasnya 1.000 profil dan 1 MiB UTF-8. Field tidak dikenal (termasuk credential/password/ID) ditolak, dan error validasi tidak mengutip isi input. Panjang name/username maksimal 256 karakter, host 1.024 karakter, dan path CA 4.096 karakter, tanpa karakter kontrol. Impor juga menerima UTF-8 BOM.

Contoh format berkas koneksi tanpa kredensial:

```json
{
  "format": "navilite.connections",
  "formatVersion": 1,
  "connections": [
    { "name": "Development", "host": "127.0.0.1", "port": 3306, "username": "developer", "environment": "local", "useTls": false, "caCertificate": null }
  ]
}
```

Inkrement 0.9.0 menambahkan salinan SQL INSERT:

- Pada tabel, pilih baris grid atau record aktif di Form View lalu Copy rows as → SQL INSERT. Rust membuat satu statement dengan daftar kolom eksplisit dan baris menurut urutan halaman. Tujuannya memakai nama database/tabel sumber, dengan identifier ber-backtick. Nilai auto-increment dan primary key tetap disertakan; kolom generated dikecualikan berdasarkan metadata yang ditangkap saat halaman dibaca.
- Angka integer/desimal disalin sebagai token numerik tanpa konversi floating point; FLOAT/DOUBLE memakai teks nilai dari halaman dan diperiksa agar valid serta finite. Teks, JSON, ENUM/SET, dan nilai temporal memakai `CONVERT(X'HEX' USING utf8mb4)`, sedangkan BINARY/BLOB memakai `X'HEX'`. NULL menjadi `NULL`; string dan biner kosong tetap berbeda dari NULL. Kutip, backslash, baris baru, Unicode, dan NUL dalam teks tidak menjadi sintaks SQL tambahan.
- Semua nilai non-generated yang dipilih harus lengkap dalam frame. Nilai terpotong, halaman kedaluwarsa, baris tidak valid, tipe belum didukung, atau output terlalu besar menolak seluruh penyalinan sebelum clipboard berubah. Sel generated tidak dimasukkan, termasuk jika cuplikannya terpotong. Input JSON dan hasil SQL masing-masing dibatasi 8 MiB. Tidak ada pembacaan ulang data atau eksekusi SQL saat menyalin, dan isi SQL hasil salinan tidak dicatat di history/activity log.
- Inkrement ini mendukung base table dengan tipe angka umum, teks, JSON, ENUM/SET, tanggal/waktu, dan biner/BLOB. View, BIT, tipe spasial/vector atau tipe lain yang belum didukung, serta tabel dengan semua kolom generated ditolak. Saat membaca halaman, satu query metadata jenis objek ditambahkan dan query kolom juga membaca penanda generated; nilai data tetap memakai frame NVLR v1. Pembacaan halaman memerlukan metadata `information_schema.COLUMNS.GENERATION_EXPRESSION`; server lama yang belum menyediakan kolom tersebut belum didukung.
- SQL merupakan snapshot nilai yang ditampilkan, bukan backup atau migrasi. Tinjau tujuan, konflik key/auto-increment, kolom/constraint target, SQL mode, time zone untuk TIMESTAMP, serta batas paket sebelum menjalankannya di alat lain. Aplikasi hanya menyalin statement dan tidak mengubah database. Perubahan skema/data server setelah halaman dibuka tidak divalidasi ulang saat menyalin.

Inkrement 0.10.0 menambahkan salinan SQL UPDATE:

- Pada tabel, pilih baris grid atau record aktif di Form View lalu Copy rows as → SQL UPDATE. Rust membuat satu statement per record menurut urutan halaman. `SET` memuat kolom non-primary-key dan non-generated dari snapshot. `WHERE` memakai seluruh kolom primary key asli, termasuk key gabungan, dan setiap statement diakhiri `LIMIT 1`. Tidak ada statement tanpa kondisi primary key.
- Nilai key berasal dari identitas asli yang ditahan Rust saat halaman dibaca. Integer/desimal tetap memakai representasi presisi penuh; key teks memakai literal `_utf8mb4 X'HEX'` agar perbandingan mengikuti collation kolom, key biner memakai `X'HEX'`, YEAR memakai token angka, dan DATE/DATETIME/TIME memakai cast eksplisit dengan mikrodetik untuk tipe yang mendukungnya. Aturan prioritas collation mengikuti [dokumentasi MySQL](https://dev.mysql.com/doc/refman/8.4/en/charset-collation-coercibility.html). Cuplikan key pada grid boleh terpotong jika identitas asli lengkap tersedia. Semua nilai yang masuk `SET` harus lengkap dalam halaman.
- Tabel tanpa primary key, view, identitas kedaluwarsa/tidak lengkap/terlalu besar, tabel tanpa kolom yang bisa dimasukkan ke `SET`, dan tipe nilai yang belum didukung ditolak sebelum clipboard berubah. Primary key FLOAT/DOUBLE, TIMESTAMP, ENUM/SET, BIT, spasial/vector, serta tipe key yang tidak dikenal belum didukung. Identitas key tetap dibatasi 4 KiB per kolom dan 16 KiB per record. Batas input JSON dan seluruh skrip adalah 8 MiB.
- Penyalinan tidak membaca ulang server, menjalankan UPDATE, atau mencatat isi skrip di log/history. Skrip menargetkan database/tabel asli. Skrip juga tidak menambahkan transaksi atau memeriksa perubahan kolom non-key: data yang berubah sejak halaman dibuka, termasuk record yang dihapus lalu dibuat ulang dengan key yang sama, memerlukan pemeriksaan sebelum eksekusi. Tinjau skema, constraint, dan pengaturan sesi tujuan seperti pada salinan INSERT.

Inkrement 0.11.0 menambahkan fondasi SQL editor:

- Tab SQL editor tersedia tanpa koneksi. Buat dokumen baru, buka berkas SQL UTF-8 (BOM opsional), lalu Save as untuk menyimpan tanpa BOM. Ctrl+N/O/S berlaku di dalam editor. Perubahan yang belum disimpan memerlukan pilihan Save/Discard/Cancel saat dokumen diganti atau aplikasi ditutup. Save as selalu meminta lokasi dan konfirmasi penggantian berkas; kegagalan baca/simpan mempertahankan dokumen yang sedang diedit.
- Analyze script (Ctrl+F9) memanggil lexer Rust dan menampilkan daftar statement dengan baris sumber dan cuplikan. Select current (F9) atau pemilihan baris daftar menyorot rentang teks asli. Kursor pada akhir statement memilih statement tersebut; di sela statement memilih yang berikutnya, dan setelah statement terakhir memilih yang terakhir. Mengedit teks atau opsi lexer menghapus analisis lama.
- Pemisahan menangani string kutip tunggal/ganda, identifier backtick, kutip ganda berulang, komentar `#`, `--` yang diikuti whitespace/control/EOF, dan komentar blok. Opsi Backslash escapes in strings dan ANSI_QUOTES perlu disesuaikan dengan SQL mode yang dimaksud; backtick selalu memakai penggandaan kutip. Komentar biasa tanpa SQL diabaikan; komentar executable/hint tetap dihitung sebagai isi. Ini pemisahan leksikal, bukan validasi sintaks SQL.
- `DELIMITER` panjang dikenali tanpa membedakan kapital pada awal baris setelah spasi/tab. Akhiri statement sebelum mengganti delimiter. Token delimiter harus satu token ASCII tercetak tanpa kutip sepanjang 1–32 byte; quote/backtick, backslash, `#`, penanda komentar, serta token tunggal `/` dan `-` ditolak. `;`, `$$`, `//`, dan `;;` didukung. Perintah klien mysql lain belum diinterpretasikan. Lihat [perintah klien MySQL](https://dev.mysql.com/doc/refman/8.4/en/mysql-commands.html).
- Batas skrip/berkas 1 MiB UTF-8 dan 1.000 statement; Unicode menggunakan offset UTF-16 agar seleksi WPF tetap sesuai. Kutip/komentar blok yang belum selesai menghasilkan lokasi kesalahan tanpa menampilkan isi skrip di log. Dokumen hanya disimpan melalui tindakan Save as; analisis tidak mengakses database atau mencatat isi skrip ke history.

Inkrement 0.12.0 menambahkan eksekusi SELECT terbatas:

- Hubungkan profil, buka tab SQL editor, tentukan Database bila perlu, lalu Run SELECT (F9). Teks yang diseleksi harus berisi tepat satu SELECT tanpa directive; tanpa seleksi, Rust memilih statement pada kursor. Alt+F9 sekarang menyorot statement, sedangkan Ctrl+F9 tetap menganalisis skrip secara offline. Hasil muncul di tab SELECT result dengan kolom dinamis, konteks koneksi/database, durasi seluruh permintaan, jumlah baris, dan penanda nilai terpotong.
- Setiap run meminjam satu slot dari pool workload (maksimal 10), mereset sesi agar terpisah dari borrower sebelumnya, dan menjalankan satu prepared statement di dalam `START TRANSACTION READ ONLY`. Opsi ANSI_QUOTES/backslash editor harus sama dengan SQL mode sesi server; perbedaan menolak eksekusi dan menampilkan opsi yang perlu dipilih. Database default hanya berlaku untuk run tersebut. Koneksi ditutup pada akhir permintaan, sehingga transaksi dan variabel sesi tidak bertahan ke run berikutnya; pool metadata tetap terpisah (maksimal 2). Run SELECT memerlukan dukungan COM_RESET_CONNECTION (MySQL 5.7.3+ atau MariaDB 10.2.4+).
- Pilih batas 100, 500, atau 1.000 baris. Batas native tetap 1–1.000 baris, 1.024 kolom, 8 MiB frame/packet klien, dan 1 MiB dokumen UTF-8. Pembaca berhenti setelah baris tambahan untuk menentukan apakah hasil masih tersedia. Nilai teks ditampilkan maksimal 1.024 karakter Unicode; biner maksimal 256 byte dalam hex. NULL, string kosong, dan biner kosong tetap berbeda. Angka/desimal/temporal berasal dari protokol prepared statement; hasil tidak mempunyai identitas primary key untuk reread/SQL INSERT/UPDATE.
- `sql_select_limit` hanya disetel pada sesi run yang akan ditutup. LIMIT eksplisit pada kueri tetap mengikuti [aturan MySQL](https://dev.mysql.com/doc/refman/8.4/en/server-system-variables.html#sysvar_sql_select_limit), sementara pembaca tetap membatasi baris yang ditampilkan. Batas hasil dan timeout klien 10 detik tidak membatasi biaya komputasi server; penutupan koneksi tidak menjamin kueri server langsung berhenti. Tidak ada perubahan pengaturan global server.
- Guard bersifat konservatif: hanya awalan SELECT, tanpa INTO, variabel/parameter, locking read, komentar executable/hint, atau perintah pengubah data/administratif. Fungsi lock/file/delay yang dikenali juga ditolak. Read-only transaction mengatur penulisan database, bukan menjadi sandbox untuk efek eksternal fungsi server yang terpasang. CTE/WITH, DML/DDL, Run All, EXPLAIN, persistent session, tombol cancel, dan pin/multiple result belum tersedia.
- Hasil terakhir tetap ditandai sebagai snapshot ketika teks diedit atau run berikutnya gagal. Dokumen baru/berkas lain dan disconnect menghapus hasil serta melepaskan buffer. History menyimpan durasi/status dan `[User SQL text omitted]`, tanpa isi SQL/hasil. Error SQL hanya menampilkan nomor error server; detail pesan yang mungkin mengandung literal disembunyikan.
- Driver dipatok tepat pada mysql_async 0.37.1. Penjaga kepemilikan sesi di Rust memulai lalu langsung membatalkan future disconnect agar socket yang ditandai disconnected dibuang, termasuk pada timeout, tanpa menjadwalkan drain sisa hasil. Urutan internal driver tersebut harus diperiksa ulang sebelum upgrade; disconnect biasa menguras hasil yang belum dibaca.

Inkrement 0.13.0 menambahkan Run All SELECTs dan tab hasil yang dapat dipin:

- Run All SELECTs (Ctrl+F9) selalu memakai seluruh skrip, dengan 1–8 statement. Rust memeriksa semua rentang statement memakai aturan Run SELECT sebelum kueri pertama dikirim. DML/DDL, CTE/WITH, variabel, hint/executable comment, atau konstruksi lain di luar kebijakan SELECT menolak batch. DELIMITER boleh memisahkan SELECT; satu rentang yang berisi beberapa statement ber-delimiter standar tetap ditolak. Pemeriksaan ini leksikal; sintaks, akses, dan SQL mode sesi masih diperiksa saat setiap statement dijalankan.
- Statement dijalankan berurutan dengan database, batas baris, dan opsi editor yang ditangkap pada awal run. Setiap statement memperoleh sesi read-only yang direset dan ditutup sendiri. Statement tidak berbagi transaksi, variabel, atau snapshot database yang konsisten; perubahan oleh sesi lain dapat terlihat antar-statement. Error menghentikan batch pada statement yang gagal, dan hasil yang sudah diterima tetap ditampilkan. Stop after current menyelesaikan permintaan aktif lalu melewati sisanya; menutup jendela juga meminta stop ini sebelum menunggu operasi aktif. Stop tidak langsung membatalkan kueri server atau mengirim KILL.
- Setiap hasil mempunyai tab, nomor snapshot, konteks koneksi/database, durasi, serta grid kolom sendiri. Pilih tab lalu Pin / unpin untuk mempertahankannya pada run berikutnya atau saat membuat/membuka dokumen SQL lain. Pin tidak menduplikasi buffer. Close result dapat menutup tab terpilih termasuk yang dipin; Clear unpinned hanya menutup hasil yang tidak dipin. Disconnect dan keluar aplikasi melepaskan semuanya; hasil tidak disimpan otomatis ke disk.
- Maksimal 8 hasil tersimpan termasuk pin dan 32 MiB frame native. Jumlah pin ditambah jumlah statement calon batch diperiksa sebelum eksekusi. Batas memori diperiksa untuk setiap hasil baru; bila tidak muat, buffer baru dilepas dan batch dihentikan. Batas ini tidak mencakup frame yang sedang masuk (maksimal 8 MiB) atau overhead rendering WPF. Hasil pertama yang diterima mengganti hasil lama yang tidak dipin; bila preflight atau kueri pertama gagal, hasil lama tetap ada. Kegagalan di tengah batch mempertahankan hasil batch yang telah diterima.
- F9 tetap menjalankan satu SELECT dari kursor/seleksi, Alt+F9 menyorot statement, dan analisis offline pindah ke Ctrl+Shift+F9. Isi SQL tidak ditambahkan ke metadata hasil atau history; preview statement tetap berada dalam memori editor.

Inkrement 0.14.0 menambahkan Format SQL offline:

- Format SQL (Ctrl+Shift+F) merapikan seluruh skrip SELECT, atau statement yang diseleksi. Tidak memerlukan koneksi database. Layout memisahkan klausa utama dan daftar koma pada level terluar, dengan indentasi lanjutan empat spasi. Ekspresi/subquery bersarang tetap ringkas; kapitalisasi keyword dan identifier tidak diubah.
- Literal string, identifier berquote, dan teks komentar dipertahankan persis sesuai opsi ANSI_QUOTES/backslash editor. Jarak yang menentukan pemanggilan fungsi tetap dipertahankan. Native memeriksa ulang seluruh urutan token non-whitespace setelah penggantian, termasuk bagian di luar seleksi. Whitespace non-ASCII di luar literal/identifier berquote/komentar ditolak karena dapat menjadi bagian identifier MySQL, bukan diubah menjadi spasi biasa.
- Seleksi harus berakhir dan dimulai pada batas token utuh; memilih teks SQL di tengah string, identifier, atau komentar ditolak. Whitespace tepi seleksi dipertahankan agar teks di luar seleksi tidak menyatu. Setelah format, seleksi dipertahankan atau kursor dipetakan ke token yang sama; edit dikelompokkan sebagai satu operasi undo melalui [change block WPF](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.primitives.textboxbase.beginchange). Ctrl+Z mengembalikan edit. Hasil kueri/pin tetap ada, dan status dirty mengikuti perubahan dokumen.
- Batas input/output dokumen 1 MiB UTF-8, 100.000 token leksikal, dan 64 tingkat tanda kurung. Formatter memakai kebijakan plain SELECT yang sama dengan Run SELECT dan menolak DELIMITER, konstruksi di luar kebijakan, quote/komentar yang belum selesai, serta tanda kurung yang tidak seimbang. Pemeriksaan ini bukan validasi sintaks SQL; server tetap menentukan validitas sintaks saat kueri dijalankan. Format tidak menjalankan SQL atau mencatat isinya ke log.

Inkrement 0.15.0 menambahkan Explain dalam format tabel:

- Explain (Ctrl+E) mengambil rencana optimizer untuk satu plain SELECT di kursor atau seleksi, memakai koneksi aktif dan database editor. Validasi SELECT, pencocokan mode quote, reset sesi, transaksi read-only, timeout 10 detik, serta batas paket/frame 8 MiB tetap berlaku. Input berupa EXPLAIN/ANALYZE atau statement pengubah data ditolak.
- MySQL memakai `EXPLAIN FORMAT=TRADITIONAL` agar pengaturan default `explain_format` tidak mengubah bentuk hasil; MariaDB memakai `EXPLAIN` untuk format tabelnya. Lihat dokumentasi [MySQL](https://dev.mysql.com/doc/refman/8.0/en/explain.html) dan [MariaDB](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/analyze-and-explain-statements/explain). Aplikasi tidak menambahkan `sql_select_limit` pada sesi Explain, sehingga pembatas baris hasil tidak mengubah estimasi SELECT. Plan dibatasi 1.000 baris pada pembaca hasil, terpisah dari pilihan Rows untuk Run SELECT.
- Snapshot EXPLAIN dapat dipin dan dibandingkan dengan hasil SELECT/plan berikutnya. Batas delapan tab dan 32 MiB frame berlaku bersama; kegagalan mempertahankan snapshot sebelumnya. Kolom indikator dan warna amber menandai `type=ALL`, `Using filesort`, serta `Using temporary`. Ringkasan menandai plan yang belum lengkap ketika baris/sel dipendekkan atau metadata yang diharapkan tidak tersedia. Penanda ini membantu inspeksi estimasi optimizer dan bukan penilaian mutlak performa kueri.
- History menyimpan label User EXPLAIN, waktu, konteks database, dan outcome; isi SQL serta pesan server tetap dihilangkan. Format JSON, plan tree, analisis eksekusi aktual, dan rekomendasi indeks belum tersedia. Transaksi read-only dan kebijakan SELECT bukan sandbox untuk seluruh kemungkinan efek fungsi/optimizer.

Inkrement 0.16.0 merapikan layout dan memindahkan filter/sort ke header kolom:

- Nama kolom, arah sort, serta nomor prioritas berada di baris pertama header; input literal ringkas berada di bawahnya. Ketik nilai lalu Enter atau Apply untuk membaca ulang data. Mengetik tidak mengirim kueri. Ikon filter membuka operator dan beberapa kondisi untuk kolom tersebut; badge membedakan kondisi aktif dan perubahan yang belum diterapkan.
- Filter cepat memakai Equals sebagai default. Mengosongkan input menghapus kondisi cepat; untuk mencocokkan string kosong, gunakan Equals dengan nilai kosong di popover. Kolom dengan beberapa kondisi atau operator NULL memakai petunjuk baca saja pada input cepat, dan tetap bisa diedit lewat popover. Batas 16 kondisi serta validasi tipe/prepared statement di Rust tetap berlaku.
- Klik nama/panah header untuk ASC → DESC → default. Shift-klik mempertahankan sort kolom lain dan mengubah kolom tersebut dalam urutan sampai delapan kolom. Sort berlaku pada filter yang sudah diterapkan; draft filter tetap menunggu Apply. Query options menyediakan seluruh kondisi, AND/OR, dan prioritas sort saat dibutuhkan, termasuk dari Form view.
- Panel query besar tidak lagi selalu tampil. Toolbar dan judul lebih ringkas, pagination berada di footer bersama, sidebar dapat diubah lebarnya, dan impor/ekspor profil berada di menu Connections. Lebar serta urutan tampilan kolom dipertahankan ketika metadata kolom sama saat refresh/pagination. Field filter menjadi read-only saat proses berjalan agar fokus tidak dipindahkan paksa.
- Layout dirender dari komponen WPF dengan data contoh pada 1240 × 780 dan 1040 × 700 tanpa koneksi database. Verifikasi interaksi desktop langsung masih tertunda karena helper desktop gagal diinisialisasi; perilaku filter/sort terautentikasi juga belum diperiksa ulang.

SSH tunnel, sertifikat klien TLS, socket transport, kompatibilitas impor Navicat `.ncx`, penggantian profil melalui impor, guardrail SQL production untuk perintah pengubah data, penyuntingan data, kelompok filter bertingkat, eksekusi SQL umum di luar SELECT, streaming nilai melebihi 1 MiB, JSON tree/image/date editor, syntax highlighting/autocomplete, formatter grammar SQL umum, multi-dokumen, dan transfer masih menunggu inkrement berikutnya. Label production saat ini hanya indikator lingkungan; semua eksekusi editor tetap memakai transaksi read-only.

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

Untuk membuat build terpisah saat aplikasi sebelumnya masih terbuka:

```powershell
./scripts/Build.ps1 -Configuration Release -OutputDirectory .dev/build-v0.16.0/desktop
& ./.dev/build-v0.16.0/desktop/NaviLite.Desktop.exe
```

`OutputDirectory` berlaku pada output desktop beserta salinan DLL native; cache dan hasil build Rust tetap di `target/`. Build ini tidak mengganti executable/DLL pada folder aplikasi yang sedang digunakan. `Run.ps1` tetap memakai lokasi output default.

Struktur kode:

| Lokasi | Tanggung jawab |
| --- | --- |
| `native/navilite-core/src/profiles.rs` | Validasi profil, metadata lokal, dan Windows Credential Manager |
| `native/navilite-core/src/profile_transfer.rs` | Format portabel, validasi impor/ekspor, pratinjau duplikat, dan perencanaan profil baru |
| `native/navilite-core/src/connection.rs` | Runtime Tokio, pool, koneksi TCP/TLS, dan metadata database |
| `native/navilite-core/src/ffi.rs` | API C, respons JSON UTF-8, penahanan panic, dan pelepasan memori |
| `native/navilite-core/src/schema.rs` | Metadata lazy-load, pengutipan identifier, dan DDL yang sudah ada |
| `native/navilite-core/src/table_page.rs` | Proyeksi SELECT terbatas, transaksi baca saja, dan frame biner NVLR |
| `native/navilite-core/src/page_options.rs` | Validasi filter/sort, parameter bertipe, dan urutan primary key |
| `native/navilite-core/src/cell_value.rs` | Identitas record selama halaman hidup, pembacaan nilai terbatas, dan frame NVLC |
| `native/navilite-core/src/history.rs` | Riwayat template SQL terbatas per sesi dan pencatatan durasi/hasil/interupsi |
| `native/navilite-core/src/row_insert.rs` | Pembuatan SQL INSERT terbatas dari metadata halaman dan nilai pilihan, tanpa eksekusi |
| `native/navilite-core/src/row_update.rs` | Pembuatan SQL UPDATE dengan primary key asli, daftar SET tanpa key/generated, dan LIMIT 1 |
| `native/navilite-core/src/sql_script.rs` | Lexer SQL offline, DELIMITER, lokasi kesalahan, dan rentang statement UTF-16 |
| `native/navilite-core/src/sql_format.rs` | Layout SELECT offline, validasi batas seleksi/token, dan pemetaan kursor UTF-16 |
| `native/navilite-core/src/sql_query.rs` | Seleksi statement, guard SELECT/EXPLAIN, sesi read-only terpisah, dan error tanpa literal |
| `native/navilite-core/src/query_result.rs` | Pengodean NVLR inkremental dari metadata dan baris hasil prepared statement |
| `src/NaviLite.Desktop/ViewModels/SqlEditorViewModel.cs` | Dokumen SQL, perubahan belum disimpan, opsi lexer, dan pemilihan statement |
| `src/NaviLite.Desktop/Services/SqlScriptFiles.cs` | Baca UTF-8 terbatas dan penyimpanan atomik dokumen SQL |
| `src/NaviLite.Desktop/Models/SqlResultSnapshot.cs` | Kepemilikan frame hasil, metadata snapshot tanpa SQL, dan status pin |
| `src/NaviLite.Desktop/SqlResultView.xaml` | Grid baca saja dengan kolom independen untuk setiap tab hasil |
| `src/NaviLite.Desktop/Models/NativePage.cs` | Kepemilikan buffer native, validasi frame, dan decoding sel |
| `src/NaviLite.Desktop/CellInspectorWindow.xaml` | Viewer teks/JSON/hex baca saja dan penyimpanan byte yang dimuat |
| `src/NaviLite.Desktop/Services/RowCopy.cs` | Pemformatan CSV/TSV/JSON dari frame dengan batas output clipboard |
| `src/NaviLite.Desktop/Services/` | Wrapper native, serialisasi konfigurasi, dan penghapusan buffer sandi |
| `src/NaviLite.Desktop/ViewModels/` | State dan command workspace WPF |
| `src/NaviLite.Desktop/Interop/` | Binding yang dihasilkan; jangan diedit manual |

## Kontrak native

Setiap fungsi operasi mengembalikan `FfiResult { code, data }`. `code = 0` berarti berhasil, `code = -1` berarti gagal dengan payload `{ "message": "..." }`. `data` adalah JSON UTF-8 berakhir NUL milik Rust. C# selalu memanggil `navilite_result_free` sekali setelah membaca respons. Input hanya dipin selama pemanggilan; Rust tidak menyimpan pointer C#.

| Fungsi | Input / hasil |
| --- | --- |
| `navilite_core_info` | Versi produk dan versi ABI (1) |
| `navilite_profiles_list` | Daftar metadata profil, tanpa sandi |
| `navilite_profiles_export` | String dokumen JSON metadata portabel, tanpa ID atau kredensial |
| `navilite_profiles_import_preview` | String dokumen JSON; candidates dengan connection, duplicate, plannedName; tanpa perubahan profil |
| `navilite_profiles_import` | Dokumen asli dan array JSON indeks pilihan berbasis nol; jumlah imported/skipped dan firstImportedId opsional |
| `navilite_profile_save` | JSON profil + sandi opsional; mengembalikan profil |
| `navilite_profile_delete` | ID profil; menghapus metadata dan kredensial |
| `navilite_conn_test` | JSON profil + sandi opsional; informasi server tanpa sesi tetap |
| `navilite_conn_open` | JSON profil + sandi opsional; informasi server dan handle sesi |
| `navilite_conn_close` | Handle sesi; idempotent |
| `navilite_databases_list` | Handle sesi; daftar nama database |
| `navilite_history_list` | Handle sesi; hingga 200 template SQL terbaru, durasi, waktu mulai Unix ms, konteks, hasil, dan penanda pemotongan |
| `navilite_history_clear` | Handle sesi; menghapus riwayat native dalam memori, tanpa menjalankan SQL |
| `navilite_schema_objects` | Handle, database, tabel opsional, kategori; daftar metadata objek |
| `navilite_object_ddl` | Handle, database, objek; string CREATE TABLE/VIEW |
| `navilite_object_definition` | Handle, database, nama, jenis objek; JSON database/name/kind/sql/properties dengan properti name/value (nullable) |
| `navilite_table_page` | Handle, database, objek, offset, page size; `FfiBuffer` berisi frame NVLR |
| `navilite_table_page_query` | Input halaman ditambah JSON filter/sort; `FfiBuffer` dengan format NVLR yang sama |
| `navilite_table_page_open` | Input halaman/query; `FfiPage { buffer: FfiBuffer, page_id: u64 }` dengan identitas primary key yang ditahan Rust |
| `navilite_cell_read` | Page ID, indeks baris/kolom halaman (u32); `FfiBuffer` NVLC berisi nilai saat ini berdasarkan primary key |
| `navilite_page_release` | Page ID; melepas identitas, idempotent, tidak membebaskan buffer NVLR |
| `navilite_rows_insert_sql` | Page ID dan array JSON `{index,cells:[{value,truncated}]}`; string INSERT, hanya format, tanpa SQL ke server |
| `navilite_rows_update_sql` | Page ID dan array nilai yang sama; skrip UPDATE memakai seluruh primary key asli dan LIMIT 1, tanpa eksekusi |
| `navilite_sql_script_analyze` | JSON `{script,caretUtf16,backslashEscapes,ansiQuotes}`; daftar rentang UTF-16, currentIndex berbasis nol/null, jumlah directive dan panjang dokumen; offline |
| `navilite_sql_query_read` | Handle + JSON `{script,caretUtf16,selectionStartUtf16,selectionLengthUtf16,database,rowLimit,backslashEscapes,ansiQuotes}`; FfiBuffer NVLR v1 tanpa page lease, satu SELECT read-only |
| `navilite_sql_query_explain` | Handle + JSON seleksi/opsi yang sama dengan read; FfiBuffer NVLR v1 berisi plan EXPLAIN tabel, maksimal 1.000 baris terlepas dari pilihan Rows |
| `navilite_sql_script_prepare_reads` | JSON skrip/opsi lexer dengan caret; analisis rentang seluruh 1–8 SELECT yang lolos guard satu statement; offline, tanpa koneksi database |
| `navilite_sql_script_format` | JSON `{script,caretUtf16,selectionStartUtf16,selectionLengthUtf16,backslashEscapes,ansiQuotes}`; edit `{startUtf16,lengthUtf16,replacement,caretUtf16,formattedStatements,changed}`, offline |
| `navilite_buffer_free` | Pointer dan panjang asli `FfiBuffer`; pembebasan sekali oleh SafeHandle |

Pada API awal ini, `conn_open` menerima konfigurasi secara langsung agar profil baru bisa dibuka sebelum disimpan. Tidak ada kredensial yang dikembalikan ke UI. Sandi null berarti memakai Credential Manager jika profil mengingat sandi; string kosong berarti sandi kosong eksplisit. Pratinjau data memakai workload pool, terpisah dari metadata. Koneksi interaktif yang dipertahankan per tab belum diimplementasikan.

`navilite_table_page_query` menerima opsi seperti `{"filter":{"column":"name","operator":"contains","value":"sample"},"sort":{"column":"id","direction":"desc"}}`. `filter` dan `sort` opsional/null; `{}` memakai urutan default. Operator memakai camelCase (`equals`, `notEquals`, `contains`, `startsWith`, `greaterThan`, `lessThan`, `isNull`, `isNotNull`); arah hanya `asc`/`desc`. Nama kolom wajib cocok dengan metadata terbaru, field yang tidak dikenal ditolak, dan nilai dikirim sebagai parameter. API halaman lama tetap tersedia tanpa filter/sort. ABI produk dan format frame tetap 1.

Mulai 0.6.0, `navilite_table_page_query` dan `navilite_table_page_open` juga menerima bentuk daftar:

```json
{"filters":[{"column":"status","operator":"equals","value":"active"},{"column":"score","operator":"greaterThan","value":"50"}],"matchMode":"all","sorts":[{"column":"created_at","direction":"desc"},{"column":"id","direction":"asc"}]}
```

`filters`/`sorts` berupa array yang boleh kosong atau dihilangkan, maksimal 16 kondisi dan 8 kolom sort unik. `matchMode` hanya `all` atau `any`, default `all`. Jangan mengirim `filter` bersama `filters` yang tidak kosong, atau `sort` bersama `sorts` yang tidak kosong. Kondisi diberi tanda kurung dan nilai parameter mengikuti urutan kondisi; seluruh filter dan sort diterapkan sebelum LIMIT/pagination. Bentuk lama tetap memiliki perilaku yang setara. ABI, NVLR v1, dan NVLC v1 tidak berubah.

### Frame NVLR v1

`FfiBuffer { code: i32, data: *mut u8, length: usize }` memiliki kontrak berbeda dari CString: payload sukses adalah biner dan payload gagal (`code = -1`) adalah JSON UTF-8 sepanjang `length`, tanpa terminator NUL. Keduanya wajib dibebaskan dengan `navilite_buffer_free(data, length)`, bukan `navilite_result_free`. Pointer input dipakai hanya selama pemanggilan. ABI produk tetap 1; format frame memiliki versi terpisah.

Semua integer di frame memakai little endian. Header 40 byte:

| Offset | Isi |
| --- | --- |
| 0 | Magic `NVLR` (4 byte) |
| 4 | Versi frame (u32), saat ini 1 |
| 8, 12 | Jumlah kolom dan baris (u32) |
| 16, 24 | Offset halaman dan offset berikutnya (u64) |
| 32 | Flags (u32): bit 0 ada halaman berikutnya, bit 1 primary key menjadi bagian dari urutan |
| 36 | Jumlah sel yang dipotong (u32) |

Header diikuti metadata kolom berupa pasangan nama dan tipe SQL; setiap string diawali panjang byte u32. Berikutnya ada direktori `row_count + 1` offset absolut u32, termasuk akhir frame. Setiap baris diawali `column_count` offset sel u32 relatif terhadap awal baris. Sel berisi tag u8, panjang payload u32, lalu byte payload. Tag 0 = NULL, 1 = teks UTF-8, 2 = biner; bit 7 menandai cuplikan terpotong. C# memvalidasi header, direktori, batas sel, serta pagination sebelum mengekspos baris ke grid. Frame lama dibebaskan saat halaman diganti atau sesi diputus; SafeHandle juga menjadi pengaman finalisasi.

### Identitas halaman dan frame NVLC v1

`navilite_table_page_open` mempertahankan format NVLR v1 dan menambahkan ID halaman melalui struct hasil terpisah. Pada kegagalan, buffer berisi error JSON dan ID bernilai 0. Pada keberhasilan, pemanggil wajib memanggil `navilite_page_release(page_id)` serta `navilite_buffer_free(buffer.data, buffer.length)` masing-masing sekali. C# menggabungkan keduanya dalam SafeHandle dan menahan referensi handle selama pembacaan sel. API halaman sebelumnya tetap tersedia tanpa identitas untuk inspeksi.

Registry dibatasi 16 halaman. Proyeksi identitas primary key dibatasi pada server dan diperhitungkan dalam ukuran halaman adaptif; key tidak dikirim melalui JSON maupun frame grid. Identitas dilepas saat sesi ditutup, dan ID tidak dipakai ulang. Pembacaan nilai memeriksa metadata kolom/key lalu menjalankan SELECT berparameter dalam transaksi READ ONLY yang diakhiri ROLLBACK. Identitas key tidak menjanjikan snapshot data atau mendeteksi record yang dihapus lalu dibuat ulang dengan key yang sama.

`navilite_cell_read` mengembalikan frame little endian NVLC dengan header 24 byte: magic `NVLC` (offset 0), versi u32 = 1 (4), flags u32 (8), reserved u32 = 0 (12), dan panjang asli nilai dalam byte u64 (16). Flags 0 = NULL, 1 = teks UTF-8, 2 = biner, dengan bit 7 untuk nilai terpotong. Payload mengikuti header dan dibatasi 1 MiB; panjang asli teks dihitung sesudah konversi UTF-8. Error tetap memakai JSON UTF-8 sepanjang `FfiBuffer.length`. Semua hasil dilepas memakai `navilite_buffer_free`.

## Versioning dan alur development

Proyek memakai [Semantic Versioning 2.0.0](https://semver.org/) dan Conventional Commits (`feat:`, `fix:`, `chore:`). Versi Rust di `Cargo.toml` dan versi .NET di `Directory.Build.props` harus sama; build menolak ketidaksesuaian dan aplikasi memeriksa versi core saat startup.

```powershell
./scripts/Set-Version.ps1 -Version 0.16.0
```

Skrip memperbarui workspace Cargo, Cargo.lock, serta versi produk/assembly .NET. Perbarui CHANGELOG.md, lakukan verifikasi, commit, lalu buat tag `v0.16.0`. Selama versi `0.x`, fitur baru dan perubahan API yang memutus kompatibilitas menaikkan MINOR; perbaikan kompatibel menaikkan PATCH. Mulai `1.0.0`, perubahan API yang memutus kompatibilitas menaikkan MAJOR.

Development mengikuti pendekatan code first. Jangan membuat test atau project test tanpa instruksi eksplisit pengguna. Verifikasi yang digunakan:

```powershell
. ./scripts/Enter-Dev.ps1
cargo fmt --all -- --check
cargo clippy --workspace --locked -- -D warnings
./scripts/Build.ps1 -Configuration Release
```

Workflow GitHub Actions menjalankan format, build Windows x64, pemeriksaan binding, dan Clippy tanpa menjalankan test. Autentikasi ke server developer memerlukan kredensial yang dimasukkan pengguna; jangan mengubah akun, konfigurasi server, atau database untuk memverifikasi setup.

Referensi instalasi: [Rust di Windows](https://rust-lang.github.io/rustup/installation/windows-msvc.html), [.NET install script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
