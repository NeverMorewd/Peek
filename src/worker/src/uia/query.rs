use tracing::{debug, warn};
use uiautomation::{UIAutomation, UIElement};
use windows::Win32::Foundation::POINT;
use windows::Win32::UI::WindowsAndMessaging::WindowFromPoint;

use crate::error::{WorkerError, WorkerResult};
use crate::ipc::protocol::{ElementInfo, Rect};
use super::cache::ElementCache;

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Convert a uiautomation Rect to our serialisable Rect.
fn convert_rect(r: uiautomation::types::Rect) -> Rect {
    Rect {
        left: r.get_left(),
        top: r.get_top(),
        width: r.get_width(),
        height: r.get_height(),
    }
}

/// Extract all interesting properties from a UIElement into ElementInfo.
fn extract_info(element: &UIElement, hwnd: isize) -> WorkerResult<ElementInfo> {
    let name             = element.get_name().unwrap_or_default();
    let class_name       = element.get_classname().unwrap_or_default();
    let automation_id    = element.get_automation_id().unwrap_or_default();
    let process_id       = element.get_process_id().unwrap_or(0) as u32;
    let framework        = element.get_framework_id().unwrap_or_default();
    let is_enabled       = element.is_enabled().unwrap_or(false);
    let is_keyboard_focusable = element.is_keyboard_focusable().unwrap_or(false);

    let control_type = element
        .get_control_type()
        .map(|ct| format!("{:?}", ct))
        .unwrap_or_else(|_| "Unknown".to_string());

    let rect = element
        .get_bounding_rectangle()
        .map(convert_rect)
        .unwrap_or_default();

    Ok(ElementInfo {
        name,
        control_type,
        automation_id,
        class_name,
        process_id,
        framework,
        rect,
        is_enabled,
        is_keyboard_focusable,
        hwnd,
    })
}

// ── UiaQuery ─────────────────────────────────────────────────────────────────

pub struct UiaQuery {
    automation: UIAutomation,
    cache: ElementCache,
}

impl UiaQuery {
    /// Create a new UiaQuery.
    /// Must be called after COM is initialised on the calling thread.
    pub fn new() -> WorkerResult<Self> {
        let automation = UIAutomation::new().map_err(WorkerError::Uia)?;
        Ok(Self {
            automation,
            cache: ElementCache::new(),
        })
    }

    /// Primary hot path: find the UIA element under a screen point.
    ///
    /// Cache strategy (fastest first):
    ///   1. last-hit cache     – exact coordinate match, zero COM calls
    ///   2. element_from_point – one COM round-trip
    pub fn get_element_from_point(&self, x: i32, y: i32) -> WorkerResult<ElementInfo> {
        // 1. last-hit cache
        if let Some(cached) = self.cache.get_by_point(x, y) {
            debug!("point-cache hit ({}, {})", x, y);
            return Ok(cached);
        }

        // 2. WindowFromPoint for the HWND (cheap, no COM)
        let hwnd_raw: isize = unsafe {
            WindowFromPoint(POINT { x, y })
        }.0 as isize;

        // 3. UIA element from point (COM call)
        let point = uiautomation::types::Point::new(x, y);
        let element = self.automation
            .element_from_point(point)
            .map_err(|e| {
                warn!("element_from_point({}, {}) failed: {:?}", x, y, e);
                WorkerError::ElementNotFound { x, y }
            })?;

        let info = extract_info(&element, hwnd_raw)?;
        self.cache.put_by_point(x, y, info.clone());
        debug!("queried ({}, {}): {} [{}]", x, y, info.name, info.control_type);

        Ok(info)
    }

    /// Get UIA element information from a known window handle.
    pub fn get_element_from_hwnd(&self, hwnd: isize) -> WorkerResult<ElementInfo> {
        if let Some(cached) = self.cache.get_by_hwnd(hwnd) {
            debug!("hwnd-cache hit 0x{:x}", hwnd);
            return Ok(cached);
        }

        let element = self.automation
            .element_from_handle(uiautomation::types::Handle::from(hwnd))
            .map_err(WorkerError::Uia)?;

        let info = extract_info(&element, hwnd)?;
        self.cache.put_by_hwnd(hwnd, info.clone());
        Ok(info)
    }

    /// Enumerate children of the element identified by `hwnd`.
    /// `depth` controls recursion levels (0 = direct children only).
    pub fn get_children(&self, hwnd: isize, depth: u32) -> WorkerResult<Vec<ElementInfo>> {
        let root = self.automation
            .element_from_handle(uiautomation::types::Handle::from(hwnd))
            .map_err(WorkerError::Uia)?;

        let walker = self.automation
            .get_control_view_walker()
            .map_err(WorkerError::Uia)?;

        let mut results = Vec::new();
        self.walk_children(&walker, &root, hwnd, depth, 0, &mut results)?;
        Ok(results)
    }

    fn walk_children(
        &self,
        walker: &uiautomation::UITreeWalker,
        parent: &UIElement,
        hwnd: isize,
        max_depth: u32,
        current_depth: u32,
        results: &mut Vec<ElementInfo>,
    ) -> WorkerResult<()> {
        if current_depth > max_depth {
            return Ok(());
        }

        if let Ok(child) = walker.get_first_child(parent) {
            if let Ok(info) = extract_info(&child, hwnd) {
                results.push(info);
            }
            self.walk_children(walker, &child, hwnd, max_depth, current_depth + 1, results)?;

            let mut sibling = child;
            while let Ok(next) = walker.get_next_sibling(&sibling) {
                if let Ok(info) = extract_info(&next, hwnd) {
                    results.push(info);
                }
                self.walk_children(walker, &next, hwnd, max_depth, current_depth + 1, results)?;
                sibling = next;
            }
        }

        Ok(())
    }

    pub fn clear_cache(&self) {
        self.cache.clear();
    }

    pub fn cache_stats(&self) -> (u64, u64) {
        (self.cache.hit_count(), self.cache.miss_count())
    }
}
