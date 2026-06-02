
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Instant;

use serde_json::{json, Value};
use tracing::{debug, error, warn};

use crate::error::WorkerError;
use crate::ipc::protocol::{
    GetChildrenParams, 
    GetElementFromHandleParams, 
    GetElementFromPointParams,
    JsonRpcRequest, 
    JsonRpcResponse, 
    WorkerStatus, 
    error_codes
};
use crate::uia::UiaQuery;

// ── Handler ───────────────────────────────────────────────────────────────────

pub struct RequestHandler {
    query: Arc<UiaQuery>,
    start_time: Instant,
    queries_served: AtomicU64,
}

impl RequestHandler {
    pub fn new(query: Arc<UiaQuery>) -> Self {
        Self {
            query,
            start_time: Instant::now(),
            queries_served: AtomicU64::new(0),
        }
    }

    /// Dispatch a single JSON-RPC request and return a serialised response line.
    pub fn handle(&self, raw: &str) -> String {
        let req: JsonRpcRequest = match serde_json::from_str(raw) {
            Ok(r) => r,
            Err(e) => {
                error!("Failed to parse JSON-RPC request: {}", e);
                let resp = JsonRpcResponse::error(
                    Value::Null,
                    error_codes::PARSE_ERROR,
                    format!("Parse error: {}", e),
                );
                return serde_json::to_string(&resp).unwrap_or_default();
            }
        };

        let id = req.id.clone().unwrap_or(Value::Null);
        debug!("Handling method: {} (id={:?})", req.method, id);

        self.queries_served.fetch_add(1, Ordering::Relaxed);

        let response = match req.method.as_str() {
            "get_element_from_point" => self.handle_from_point(id, req.params),
            "get_element_from_handle" => self.handle_from_handle(id, req.params),
            "get_children" => self.handle_get_children(id, req.params),
            "clear_cache" => self.handle_clear_cache(id),
            "get_status" => self.handle_get_status(id),
            "ping" => JsonRpcResponse::success(id, json!("pong")),
            "shutdown" => {
                // The IPC server loop watches for this via the response text.
                JsonRpcResponse::success(id, json!("shutting_down"))
            }
            other => {
                warn!("Unknown method: {}", other);
                JsonRpcResponse::error(
                    id,
                    error_codes::METHOD_NOT_FOUND,
                    format!("Method not found: {}", other),
                )
            }
        };

        serde_json::to_string(&response).unwrap_or_default()
    }

    // ── method handlers ───────────────────────────────────────────────────────

    fn handle_from_point(&self, id: Value, params: Value) -> JsonRpcResponse {
        let p: GetElementFromPointParams = match serde_json::from_value(params) {
            Ok(v) => v,
            Err(e) => {
                return JsonRpcResponse::error(
                    id,
                    error_codes::INVALID_PARAMS,
                    format!("Invalid params: {}", e),
                )
            }
        };

        match self.query.get_element_from_point(p.x, p.y) {
            Ok(info) => {
                let result = serde_json::to_value(&info).unwrap_or(Value::Null);
                JsonRpcResponse::success(id, result)
            }
            Err(WorkerError::ElementNotFound { x, y }) => JsonRpcResponse::error(
                id,
                error_codes::ELEMENT_NOT_FOUND,
                format!("No element found at ({}, {})", x, y),
            ),
            Err(e) => JsonRpcResponse::error(
                id,
                error_codes::UIA_ERROR,
                format!("UIA error: {}", e),
            ),
        }
    }

    fn handle_from_handle(&self, id: Value, params: Value) -> JsonRpcResponse {
        let p: GetElementFromHandleParams = match serde_json::from_value(params) {
            Ok(v) => v,
            Err(e) => {
                return JsonRpcResponse::error(
                    id,
                    error_codes::INVALID_PARAMS,
                    format!("Invalid params: {}", e),
                )
            }
        };

        match self.query.get_element_from_hwnd(p.hwnd) {
            Ok(info) => {
                let result = serde_json::to_value(&info).unwrap_or(Value::Null);
                JsonRpcResponse::success(id, result)
            }
            Err(e) => JsonRpcResponse::error(
                id,
                error_codes::UIA_ERROR,
                format!("UIA error: {}", e),
            ),
        }
    }

    fn handle_get_children(&self, id: Value, params: Value) -> JsonRpcResponse {
        let p: GetChildrenParams = match serde_json::from_value(params) {
            Ok(v) => v,
            Err(e) => {
                return JsonRpcResponse::error(
                    id,
                    error_codes::INVALID_PARAMS,
                    format!("Invalid params: {}", e),
                )
            }
        };

        let depth = p.depth.unwrap_or(0);
        match self.query.get_children(p.hwnd, depth) {
            Ok(children) => {
                let result = serde_json::to_value(&children).unwrap_or(Value::Null);
                JsonRpcResponse::success(id, result)
            }
            Err(e) => JsonRpcResponse::error(
                id,
                error_codes::UIA_ERROR,
                format!("UIA error: {}", e),
            ),
        }
    }

    fn handle_clear_cache(&self, id: Value) -> JsonRpcResponse {
        self.query.clear_cache();
        JsonRpcResponse::success(id, json!("cache_cleared"))
    }

    fn handle_get_status(&self, id: Value) -> JsonRpcResponse {
        let (hits, misses) = self.query.cache_stats();
        let status = WorkerStatus {
            version: env!("CARGO_PKG_VERSION").to_string(),
            uptime_secs: self.start_time.elapsed().as_secs(),
            queries_served: self.queries_served.load(Ordering::Relaxed),
            cache_hits: hits,
            cache_misses: misses,
        };
        let result = serde_json::to_value(&status).unwrap_or(Value::Null);
        JsonRpcResponse::success(id, result)
    }
}
