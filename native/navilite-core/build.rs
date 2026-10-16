fn main() {
    println!("cargo:rerun-if-changed=src/ffi.rs");
    println!("cargo:rerun-if-changed=../../Directory.Build.props");
    let props = std::fs::read_to_string("../../Directory.Build.props")
        .expect("Cannot read the shared .NET version");
    assert!(
        props.contains(&format!("<Version>{}</Version>", env!("CARGO_PKG_VERSION"))),
        "Rust and .NET versions differ; use scripts/Set-Version.ps1"
    );
    csbindgen::Builder::default()
        .input_extern_file("src/ffi.rs")
        .csharp_namespace("NaviLite.Desktop.Interop")
        .csharp_dll_name("navilite_core")
        .generate_csharp_file("../../src/NaviLite.Desktop/Interop/NativeMethods.g.cs")
        .expect("Cannot generate the C# native bindings");
}
