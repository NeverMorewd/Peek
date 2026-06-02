use std::io::{BufRead, BufReader, Write};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use interprocess::local_socket::{
    prelude::*,          // LocalSocketListener trait, etc.
    GenericNamespaced,
    ListenerOptions,
    Stream,
};
use interprocess::TryClone;
use tracing::{debug, error, info, warn};

use crate::error::{WorkerError, WorkerResult};
use super::handler::RequestHandler;

/// The well-known pipe name.
/// interprocess turns this into  \\.\pipe\ui-inspector-worker  on Windows.
pub const PIPE_NAME: &str = "ui-inspector-worker";

// ── Server ────────────────────────────────────────────────────────────────────

pub struct PipeServer {
    handler: Arc<RequestHandler>,
    shutdown: Arc<AtomicBool>,
}

impl PipeServer {
    pub fn new(handler: Arc<RequestHandler>, shutdown: Arc<AtomicBool>) -> Self {
        Self { handler, shutdown }
    }

    /// Run the accept loop. Blocks until shutdown is requested.
    /// Call this from the UIA / COM STA thread.
    pub fn run(&self) -> WorkerResult<()> {
        let name = PIPE_NAME
            .to_ns_name::<GenericNamespaced>()
            .map_err(|e| {
                error!("Failed to build pipe name: {}", e);
                WorkerError::PipeBroken
            })?;

        let listener = ListenerOptions::new()
            .name(name)
            .create_sync()
            .map_err(|e| {
                eprintln!("Failed to create named pipe listener: {}", e);
                error!("Failed to create named pipe listener: {}", e);
                WorkerError::PipeBroken
            })?;

        info!("IPC server listening on: \\\\.\\pipe\\{}", PIPE_NAME);

        for stream in listener.incoming() {
            if self.shutdown.load(Ordering::Relaxed) {
                info!("Shutdown flag detected – stopping accept loop");
                break;
            }

            match stream {
                Ok(conn) => {
                    info!("Client connected");
                    let do_shutdown = self.handle_client(conn);
                    info!("Client disconnected");
                    if do_shutdown {
                        self.shutdown.store(true, Ordering::Relaxed);
                        break;
                    }
                }
                Err(e) => {
                    warn!("Accept error: {}", e);
                }
            }
        }

        info!("IPC server stopped");
        Ok(())
    }

    /// Serve one client connection until it disconnects or requests shutdown.
    /// Returns `true` if the client sent a "shutdown" method call.
    fn handle_client(&self, stream: Stream) -> bool {
        // Clone the stream so we have independent read and write ends.
        // Stream::try_clone() is provided by interprocess for this purpose.
        let writer = match stream.try_clone() {
            Ok(w) => w,
            Err(e) => {
                error!("Failed to clone stream for writing: {}", e);
                return false;
            }
        };

        let mut writer = std::io::BufWriter::new(writer);
        let reader     = BufReader::new(stream);

        for line in reader.lines() {
            if self.shutdown.load(Ordering::Relaxed) {
                return false;
            }

            let line = match line {
                Ok(l) if !l.trim().is_empty() => l,
                Ok(_)  => continue,
                Err(e) => {
                    debug!("Read error (client disconnected?): {}", e);
                    return false;
                }
            };

            debug!("→ RPC: {}", &line[..line.len().min(160)]);
            let response = self.handler.handle(&line);
            debug!("← RPC: {}", &response[..response.len().min(160)]);

            // Write response + newline
            if writeln!(writer, "{}", response).is_err()
                || writer.flush().is_err()
            {
                debug!("Write error (client disconnected?)");
                return false;
            }

            // Check whether the handler encoded a shutdown result
            if response.contains("\"shutting_down\"") {
                info!("Shutdown requested by client via RPC");
                return true;
            }
        }

        false
    }
}
