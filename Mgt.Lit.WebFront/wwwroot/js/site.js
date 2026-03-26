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

    // ปิด browser history / autocomplete
    element.setAttribute("autocomplete", "off");

    const picker = new Litepicker({
        element: element,
        singleMode: false,
        format: "YYYY-MM-DD",
        autoApply: true,
        dropdowns: {
            minYear: 2020,
            maxYear: 2030,
            months: true,
            years: true
        },
        setup: (picker) => {
            picker.on("selected", (start, end) => {

                dotnetRef.invokeMethodAsync(
                    "SetDateRange",
                    start.format("YYYY-MM-DD"),
                    end.format("YYYY-MM-DD")
                );

            });
        }
    });
}