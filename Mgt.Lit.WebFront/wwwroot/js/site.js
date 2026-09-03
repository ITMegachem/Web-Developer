
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
export function initBillingDatePicker(dotnetRef) {
    createLitepicker("documentDateRange", dotnetRef, "OnDocumentDateRangeChanged");
}

export function initDeliveryDatePicker2(dotnetRef) {
    createLitepicker("deliveryDateRange2", dotnetRef, "OnDeliveryDateRangeChanged2");
}

window.dashboardCharts = {
    instances: {},

    renderBarChart: function (canvasId, labels, data, label) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        if (window.dashboardCharts.instances[canvasId]) {
            window.dashboardCharts.instances[canvasId].destroy();
        }

        window.dashboardCharts.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: label,
                    data: data,
                    backgroundColor: 'rgba(76, 110, 245, 0.5)',
                    borderColor: 'rgba(76, 110, 245, 1)',
                    borderWidth: 1
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false
            }
        });
    },

    renderComboChart: function (canvasId, labels, revenueData, marginData) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        if (window.dashboardCharts.instances[canvasId]) {
            window.dashboardCharts.instances[canvasId].destroy();
        }

        window.dashboardCharts.instances[canvasId] = new Chart(ctx, {
            data: {
                labels: labels,
                datasets: [
                    {
                        type: 'bar',
                        label: 'Revenue',
                        data: revenueData,
                        backgroundColor: 'rgba(54, 162, 235, 0.7)',
                        yAxisID: 'y'
                    },
                    {
                        type: 'line',
                        label: 'Gross Profit Margin',
                        data: marginData,
                        borderColor: 'rgba(230, 126, 34, 1)',
                        backgroundColor: 'rgba(230, 126, 34, 1)',
                        yAxisID: 'y1',
                        tension: 0.3,
                        pointRadius: 3
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                scales: {
                    y: {
                        type: 'linear',
                        position: 'left',
                        title: { display: false }
                    },
                    y1: {
                        type: 'linear',
                        position: 'right',
                        grid: { drawOnChartArea: false },
                        title: { display: false }
                    }
                }
            }
        });
    },

    renderTreemap: function (containerId, items) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.innerHTML = '';

        const valid = (items || []).filter(i => i && i.value > 0);
        const total = valid.reduce((s, i) => s + i.value, 0);
        if (total <= 0) return;

        const sorted = [...valid].sort((a, b) => b.value - a.value);

        function placeBox(item, x, y, w, h) {
            const box = document.createElement('div');
            box.className = 'treemap-box';
            box.style.left = x + '%';
            box.style.top = y + '%';
            box.style.width = w + '%';
            box.style.height = h + '%';
            box.style.background = item.color;
            box.title = item.label + (item.valueLabel ? ' (' + item.valueLabel + ')' : '');
            box.innerHTML =
                '<div class="treemap-label">' + item.label + '</div>' +
                (item.valueLabel ? '<div class="treemap-value">' + item.valueLabel + '</div>' : '');
            container.appendChild(box);
        }

        function layout(list, x, y, w, h) {
            if (!list.length) return;
            if (list.length === 1) {
                placeBox(list[0], x, y, w, h);
                return;
            }

            const sum = list.reduce((s, i) => s + i.value, 0);
            let acc = 0, splitIndex = 1;
            for (let i = 0; i < list.length; i++) {
                acc += list[i].value;
                if (acc >= sum / 2) { splitIndex = i + 1; break; }
            }
            if (splitIndex >= list.length) splitIndex = list.length - 1;

            const first = list.slice(0, splitIndex);
            const second = list.slice(splitIndex);
            const firstSum = first.reduce((s, i) => s + i.value, 0);
            const ratio = firstSum / sum;

            if (w >= h) {
                const w1 = w * ratio;
                layout(first, x, y, w1, h);
                layout(second, x + w1, y, w - w1, h);
            } else {
                const h1 = h * ratio;
                layout(first, x, y, w, h1);
                layout(second, x, y + h1, w, h - h1);
            }
        }

        layout(sorted, 0, 0, 100, 100);
    }
};
