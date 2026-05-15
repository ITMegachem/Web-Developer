
export function downloadFileFromBytes(fileName, contentType, base64) {
    const a = document.createElement("a");
    a.href = `data:${contentType};base64,${base64}`;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
}
export function initDateRange(dotnetRef) {

    const element = document.getElementById("dateRange");

    if (!element) {
        console.log("dateRange not found");
        return;
    }

    element.setAttribute("autocomplete", "off");

    const picker = new Litepicker({
        element: element,
        singleMode: false,
        format: "YYYY-MM-DD",
        autoApply: true,
        resetButton: true,   // เพิ่มปุ่ม clear ใน picker
        dropdowns: {
            minYear: 2020,
            maxYear: 2030,
            months: true,
            years: true
        },
        setup: (picker) => {
            // ตอนเลือกวันที่
            picker.on("selected", (start, end) => {
                dotnetRef.invokeMethodAsync(
                    "SetDateRange",
                    start.format("YYYY-MM-DD"),
                    end.format("YYYY-MM-DD")
                );
            });

            // ตอนกด reset / clear
            picker.on("clear:selection", () => {
                dotnetRef.invokeMethodAsync("SetDateRange", "", "");
            });
        }
    });

    // กรณีผู้ใช้ลบข้อความใน input โดยตรง (Backspace / Delete)
    element.addEventListener("input", () => {
        if (element.value === "") {
            picker.clearSelection();
            dotnetRef.invokeMethodAsync("SetDateRange", "", "");
        }
    });
}
export function getDateRangeValue() {
    const element = document.getElementById("dateRange");
    return element ? element.value : "";
}
function createLitepicker(elementId, dotnetRef, methodName) {
    const element = document.getElementById(elementId);
    if (!element) { console.log(`${elementId} not found`); return; }

    element.setAttribute("autocomplete", "off");

    const picker = new Litepicker({
        element: element,
        singleMode: false,
        format: "DD/MM/YYYY",
        autoApply: true,
        resetButton: true,
        dropdowns: { minYear: 2020, maxYear: 2030, months: true, years: true },
        setup: (picker) => {
            picker.on("selected", (start, end) => {
                dotnetRef.invokeMethodAsync(methodName,
                    start.format("YYYY-MM-DD"),
                    end.format("YYYY-MM-DD"));
            });
            picker.on("clear:selection", () => {
                element.value = "";  // ✅ clear input
                dotnetRef.invokeMethodAsync(methodName, "", "");
            });
        }
    });

    // ✅ ถ้าผู้ใช้ลบข้อความเองใน input
    element.addEventListener("input", () => {
        if (element.value === "") {
            picker.clearSelection();
            dotnetRef.invokeMethodAsync(methodName, "", "");
        }
    });

    // ✅ เพิ่ม: ถ้ากด Backspace/Delete ให้ clear
    element.addEventListener("keydown", (e) => {
        if (e.key === "Backspace" || e.key === "Delete") {
            picker.clearSelection();
            element.value = "";
            dotnetRef.invokeMethodAsync(methodName, "", "");
        }
    });
}
export function initDocumentDatePicker(dotnetRef) {
    createLitepicker("documentDateRange", dotnetRef, "OnDocumentDateRangeChanged");
}

export function initDeliveryDatePicker(dotnetRef) {
    createLitepicker("deliveryDateRange", dotnetRef, "OnDeliveryDateRangeChanged");
}
