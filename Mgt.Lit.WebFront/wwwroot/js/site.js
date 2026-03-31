
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
