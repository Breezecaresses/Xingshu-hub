fn main() {
    #[cfg(feature = "web-ui")]
    {
        // Embed our own updater; never run a helper discovered through PATH or a downloaded script.
        let root = std::path::PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap()).join("../../tools");
        println!("cargo:rerun-if-changed={}", root.join("HubUpdater.cs").display());
        println!("cargo:rerun-if-changed={}", root.join("update-public-cert.txt").display());
        let compiler = std::path::PathBuf::from(std::env::var("WINDIR").expect("Windows build required"))
            .join("Microsoft.NET/Framework64/v4.0.30319/csc.exe");
        let output = std::path::PathBuf::from(std::env::var("OUT_DIR").unwrap()).join("XingShuUpdate.exe");
        let status = std::process::Command::new(compiler).args(["/nologo", "/target:exe", "/optimize+", "/codepage:65001",
            "/reference:System.Web.Extensions.dll", "/reference:System.Windows.Forms.dll",
            "/reference:System.IO.Compression.dll", "/reference:System.IO.Compression.FileSystem.dll"])
            .arg(format!("/resource:{},update-cert", root.join("update-public-cert.txt").display()))
            .arg(format!("/out:{}", output.display())).arg(root.join("HubUpdater.cs"))
            .status().expect("Cannot compile bundled updater");
        assert!(status.success(), "Bundled updater did not compile");
    }
    // Tauri tracks its config and capabilities, but Windows resources also
    // need rebuilding when the brand icon changes at the same file path.
    println!("cargo:rerun-if-changed=icons");
    #[cfg(target_os = "windows")]
    {
        println!(r#"cargo:rustc-link-arg=/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'"#);
    }
    #[cfg(feature = "web-ui")]
    tauri_build::build()
}
