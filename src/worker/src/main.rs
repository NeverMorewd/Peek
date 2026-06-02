mod error;
mod ipc;
mod uia;

use std::sync::Arc;
use std::sync::atomic::AtomicBool;

use tracing::{error, info};
use tracing_subscriber::EnvFilter;

use windows::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_MULTITHREADED};

use ipc::{PipeServer, RequestHandler};
use uia::UiaQuery;

fn main() {

    let filter = EnvFilter::try_from_env("UI_INSPECTOR_LOG")
        .unwrap_or_else(|_| EnvFilter::new("info"));

    tracing_subscriber::fmt()
        .with_env_filter(filter)
        .with_writer(std::io::stderr) // CRITICAL: logs must never go to stdout
        .with_target(false)
        .compact()
        .init();

    info!("UI Inspector Worker v{} starting", env!("CARGO_PKG_VERSION"));

    let hr = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
    if hr.is_err() {
        // RPC_E_CHANGED_MODE (0x80010106) is acceptable if COM was already init'd differently
        if hr.0 as u32 != 0x80010106 {
            eprintln!("CoInitializeEx(MTA) failed: {:?}", hr);
            error!("CoInitializeEx(MTA) failed: {:?}", hr);
            std::process::exit(1);
        }
    }
    info!("COM initialised (MTA)");

    // ── UIAutomation ──────────────────────────────────────────────────────────
    let query = match UiaQuery::new() {
        Ok(q) => Arc::new(q),
        Err(e) => {
            eprintln!("Failed to initialise UIAutomation: {}", e);
            error!("Failed to initialise UIAutomation: {}", e);
            unsafe { CoUninitialize() };
            std::process::exit(2);
        }
    };
    info!("UIAutomation ready");

    // ── Shutdown flag ─────────────────────────────────────────────────────────
    let shutdown = Arc::new(AtomicBool::new(false));

    // ── IPC server ────────────────────────────────────────────────────────────
    let handler = Arc::new(RequestHandler::new(Arc::clone(&query)));
    let server  = PipeServer::new(handler, Arc::clone(&shutdown));

    if let Err(e) = server.run() {
        error!("IPC server error: {}", e);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────
    info!("Worker exiting – releasing COM");
    unsafe { CoUninitialize() };
}
