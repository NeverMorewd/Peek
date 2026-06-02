use serde::{Deserialize, Serialize};

// ── Request ──────────────────────────────────────────────────────────────────

#[derive(Debug, Deserialize)]
pub struct JsonRpcRequest {
    #[allow(dead_code)]
    pub jsonrpc: String,
    pub id: Option<serde_json::Value>,
    pub method: String,
    #[serde(default)]
    pub params: serde_json::Value,
}

// ── Response ─────────────────────────────────────────────────────────────────

#[derive(Debug, Serialize)]
pub struct JsonRpcResponse {
    pub jsonrpc: String,
    pub id: serde_json::Value,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub result: Option<serde_json::Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<JsonRpcError>,
}

impl JsonRpcResponse {
    pub fn success(id: serde_json::Value, result: serde_json::Value) -> Self {
        Self {
            jsonrpc: "2.0".to_string(),
            id,
            result: Some(result),
            error: None,
        }
    }

    pub fn error(id: serde_json::Value, code: i32, message: String) -> Self {
        Self {
            jsonrpc: "2.0".to_string(),
            id,
            result: None,
            error: Some(JsonRpcError { code, message }),
        }
    }
}

#[derive(Debug, Serialize)]
pub struct JsonRpcError {
    pub code: i32,
    pub message: String,
}

// Standard JSON-RPC error codes
pub mod error_codes {
    pub const PARSE_ERROR: i32 = -32700;
    #[allow(dead_code)]
    pub const INVALID_REQUEST: i32 = -32600;
    pub const METHOD_NOT_FOUND: i32 = -32601;
    pub const INVALID_PARAMS: i32 = -32602;
    #[allow(dead_code)]
    pub const INTERNAL_ERROR: i32 = -32603;
    pub const ELEMENT_NOT_FOUND: i32 = -32001;
    pub const UIA_ERROR: i32 = -32002;
    #[allow(dead_code)]
    pub const TIMEOUT: i32 = -32003;
}

// ── Request param types ───────────────────────────────────────────────────────

#[derive(Debug, Deserialize)]
pub struct GetElementFromPointParams {
    pub x: i32,
    pub y: i32,
}

#[derive(Debug, Deserialize)]
pub struct GetElementFromHandleParams {
    pub hwnd: isize,
}

#[derive(Debug, Deserialize)]
pub struct GetChildrenParams {
    pub hwnd: isize,
    #[serde(default)]
    pub depth: Option<u32>,
}

// ── Response data types ───────────────────────────────────────────────────────

#[derive(Debug, Serialize, Clone)]
pub struct ElementInfo {
    pub name: String,
    pub control_type: String,
    pub automation_id: String,
    pub class_name: String,
    pub process_id: u32,
    pub framework: String,
    pub rect: Rect,
    pub is_enabled: bool,
    pub is_keyboard_focusable: bool,
    pub hwnd: isize,
}

#[derive(Debug, Serialize, Clone, Default)]
pub struct Rect {
    pub left: i32,
    pub top: i32,
    pub width: i32,
    pub height: i32,
}

#[derive(Debug, Serialize)]
pub struct WorkerStatus {
    pub version: String,
    pub uptime_secs: u64,
    pub queries_served: u64,
    pub cache_hits: u64,
    pub cache_misses: u64,
}