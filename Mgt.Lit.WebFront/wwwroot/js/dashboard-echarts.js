(function () {
    "use strict";

    const charts = new Map();
    const resizeObservers = new Map();
    const reducedMotion = window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    const colors = {
        slate: "#667085",
        green: "#7cb342",
        amber: "#c47a3a",
        grid: "#e4e8e6",
        text: "#34443b",
        muted: "#6b756f",
        highlight: "#2f6f3e",
        red: "#e11d48",
        blue: "#2f7cb0",
        donut: ["#2f7cb0", "#7cb342", "#e5b93a", "#c47a3a", "#98a2ae", "#8e6fc4"]
    };

    function asNumber(value) {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function formatNumber(value) {
        return asNumber(value).toLocaleString("en-US", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function formatPercent(value) {
        return asNumber(value).toLocaleString("en-US", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }) + "%";
    }

    function formatCompact(value) {
        const number = asNumber(value);
        const absolute = Math.abs(number);
        const units = [
            { threshold: 1e9, suffix: "B" },
            { threshold: 1e6, suffix: "M" },
            { threshold: 1e3, suffix: "K" }
        ];

        for (const unit of units) {
            if (absolute >= unit.threshold) {
                return (number / unit.threshold).toLocaleString("en-US", {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 2
                }) + unit.suffix;
            }
        }

        return number.toLocaleString("en-US", { maximumFractionDigits: 0 });
    }

    function tooltipText(parameters) {
        const items = Array.isArray(parameters) ? parameters : [parameters];
        if (!items.length) {
            return "";
        }

        const title = String(items[0].axisValueLabel || items[0].name || "");
        const lines = items.map(function (item) {
            const isPercent = item.seriesName === "GP %" ||
                item.seriesName.indexOf("Margin") >= 0;
            return item.seriesName + ": " + (isPercent ? formatPercent(item.value) : formatNumber(item.value));
        });

        return [title].concat(lines).join("\n");
    }

    function emptyGraphic(hasData) {
        if (hasData) {
            return [];
        }

        return [{
            type: "text",
            left: "center",
            top: "middle",
            silent: true,
            style: {
                text: "No data available",
                fill: colors.muted,
                font: "500 14px Segoe UI, Arial, sans-serif"
            }
        }];
    }

    function observeSize(elementId, element, chart) {
        const previous = resizeObservers.get(elementId);
        if (previous) {
            previous.disconnect();
        }

        if (!window.ResizeObserver) {
            return;
        }

        let frameId = 0;
        const observer = new ResizeObserver(function () {
            window.cancelAnimationFrame(frameId);
            frameId = window.requestAnimationFrame(function () {
                if (!chart.isDisposed()) {
                    chart.resize();
                }
            });
        });

        observer.observe(element);
        resizeObservers.set(elementId, observer);
    }

    function getChart(elementId) {
        if (!window.echarts) {
            throw new Error("Apache ECharts is not loaded.");
        }

        const element = document.getElementById(elementId);
        if (!element) {
            return null;
        }

        let chart = window.echarts.getInstanceByDom(element);
        if (!chart) {
            chart = window.echarts.init(element, null, { renderer: "canvas" });
        }

        charts.set(elementId, chart);
        observeSize(elementId, element, chart);
        return chart;
    }

    function render(elementId, option) {
        const chart = getChart(elementId);
        if (!chart) {
            return;
        }

        option.animation = !reducedMotion;
        option.animationDuration = 450;
        option.textStyle = {
            fontFamily: "Segoe UI, Arial, sans-serif",
            color: colors.text
        };
        option.aria = { enabled: true };

        chart.setOption(option, { notMerge: true, lazyUpdate: false });
        window.requestAnimationFrame(function () {
            if (!chart.isDisposed()) {
                chart.resize();
            }
        });
    }

    function revenueMarginOption(points, useZoom) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.category || ""); });
        const revenue = safePoints.map(function (point) {
            const value = asNumber(point.amount);
            if (point.selected) {
                return { value: value, itemStyle: { color: colors.highlight, borderRadius: [4, 4, 0, 0] } };
            }
            return value;
        });
        const margin = safePoints.map(function (point) { return asNumber(point.percent); });
        const zoomEnd = categories.length > 12 ? Math.max(10, 1200 / categories.length) : 100;

        return {
            color: [colors.slate, colors.green],
            tooltip: {
                trigger: "axis",
                renderMode: "richText",
                confine: true,
                axisPointer: { type: "shadow" },
                formatter: tooltipText
            },
            legend: {
                top: 0,
                textStyle: { fontSize: 14, color: colors.text }
            },
            grid: {
                left: 18,
                right: 20,
                top: 52,
                bottom: useZoom ? 82 : 44,
                containLabel: true
            },
            xAxis: {
                type: "category",
                data: categories,
                axisTick: { alignWithLabel: true },
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: {
                    color: colors.muted,
                    fontSize: 13,
                    interval: 0,
                    hideOverlap: true,
                    rotate: useZoom ? 28 : 0
                }
            },
            yAxis: [
                {
                    type: "value",
                    name: "Revenue",
                    nameTextStyle: { color: colors.muted, fontSize: 13 },
                    axisLabel: {
                        color: colors.muted,
                        fontSize: 13,
                        formatter: formatCompact
                    },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    name: "Margin",
                    position: "right",
                    scale: true,   // auto-scale ตามช่วงข้อมูล (% ไม่ต้องเริ่ม 0)
                    axisLabel: {
                        color: colors.muted,
                        fontSize: 13,
                        formatter: function (value) { return formatPercent(value); }
                    },
                    splitLine: { show: false }
                }
            ],
            dataZoom: useZoom && categories.length > 12 ? [
                { type: "inside", start: 0, end: zoomEnd },
                {
                    type: "slider",
                    start: 0,
                    end: zoomEnd,
                    height: 16,
                    bottom: 8,
                    borderColor: "#d8dedb",
                    fillerColor: "rgba(124, 179, 66, 0.14)",
                    handleStyle: { color: colors.green }
                }
            ] : [],
            series: [
                {
                    name: "Revenue",
                    type: "bar",
                    data: revenue,
                    barMaxWidth: 42,
                    itemStyle: { borderRadius: [4, 4, 0, 0] }
                },
                {
                    name: "Gross Profit Margin",
                    type: "line",
                    yAxisIndex: 1,
                    data: margin,
                    smooth: 0.25,
                    symbolSize: 7,
                    lineStyle: { width: 3 }
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        };
    }

    function renderSalesOverviewTrend(elementId, points, dotNetRef) {
        render(elementId, revenueMarginOption(points, false));

        const chart = charts.get(elementId);
        if (!chart) {
            return;
        }

        chart.off("click");
        if (dotNetRef) {
            chart.on("click", function (params) {
                if (params.componentType === "series" && params.seriesType === "bar") {
                    dotNetRef.invokeMethodAsync("OnMonthBarClicked", params.dataIndex);
                }
            });
        }
    }

    function renderSalesPerformanceTrend(elementId, points, dotNetRef) {
        render(elementId, revenueMarginOption(points, true));

        const chart = charts.get(elementId);
        if (!chart) {
            return;
        }

        chart.off("click");
        if (dotNetRef) {
            chart.on("click", function (params) {
                if (params.componentType === "series" && params.seriesType === "bar") {
                    dotNetRef.invokeMethodAsync("OnEmployeeBarClicked", params.dataIndex);
                }
            });
        }
    }

    function renderIndustryTreemap(elementId, points, dotNetRef) {
        const safePoints = Array.isArray(points) ? points : [];
        const data = safePoints.map(function (point) {
            const item = {
                name: String(point.name || "Unspecified"),
                value: asNumber(point.value)
            };
            if (point.selected) {
                item.itemStyle = { borderColor: colors.highlight, borderWidth: 4 };
            }
            return item;
        });

        render(elementId, {
            color: [
                "#64748b",
                "#7cb342",
                "#94a3b8",
                "#9aab8c",
                "#c47a3a",
                "#748b7a",
                "#a8b0ac",
                "#6f8f55"
            ],
            tooltip: {
                trigger: "item",
                renderMode: "richText",
                confine: true,
                formatter: function (item) {
                    return item.name + "\nRevenue: " + formatNumber(item.value);
                }
            },
            series: [{
                name: "Industry",
                type: "treemap",
                data: data,
                roam: false,
                nodeClick: false,
                breadcrumb: { show: false },
                visibleMin: 20,
                label: {
                    show: true,
                    color: "#ffffff",
                    fontSize: 13,
                    fontWeight: 600,
                    overflow: "truncate",
                    formatter: function (item) {
                        return item.name + "\n" + formatCompact(item.value);
                    }
                },
                upperLabel: { show: false },
                itemStyle: {
                    borderColor: "#ffffff",
                    borderWidth: 2,
                    gapWidth: 2
                },
                levels: [{
                    colorSaturation: [0.25, 0.55],
                    itemStyle: {
                        borderColor: "#ffffff",
                        borderWidth: 2,
                        gapWidth: 2
                    }
                }]
            }],
            graphic: emptyGraphic(data.length > 0)
        });

        const chart = charts.get(elementId);
        if (!chart) {
            return;
        }

        chart.off("click");
        if (dotNetRef) {
            chart.on("click", function (params) {
                if (params.componentType === "series" && params.seriesType === "treemap") {
                    dotNetRef.invokeMethodAsync("OnIndustryClicked", params.name);
                }
            });
        }
    }

    function renderYearlyComparison(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.category || ""); });

        render(elementId, {
            color: [colors.slate, colors.green, colors.amber],
            tooltip: {
                trigger: "axis",
                renderMode: "richText",
                confine: true,
                formatter: tooltipText
            },
            legend: {
                bottom: 0,
                itemWidth: 18,
                textStyle: { color: colors.text, fontSize: 12 }
            },
            grid: {
                left: 12,
                right: 14,
                top: 18,
                bottom: 48,
                containLabel: true
            },
            xAxis: {
                type: "category",
                boundaryGap: false,
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: {
                    color: colors.muted,
                    fontSize: 12,
                    hideOverlap: true
                }
            },
            yAxis: [
                {
                    type: "value",
                    axisLabel: {
                        color: colors.muted,
                        fontSize: 12,
                        formatter: formatCompact
                    },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    position: "right",
                    scale: true,   // auto-scale ตามช่วงข้อมูล (% ไม่ต้องเริ่ม 0)
                    axisLabel: {
                        color: colors.muted,
                        fontSize: 12,
                        formatter: function (value) { return formatPercent(value); }
                    },
                    splitLine: { show: false }
                }
            ],
            series: [
                {
                    name: "Net Amount",
                    type: "line",
                    smooth: 0.2,
                    showSymbol: false,
                    data: safePoints.map(function (point) { return asNumber(point.netAmount); }),
                    lineStyle: { width: 2 },
                    areaStyle: { opacity: 0.07 }
                },
                {
                    name: "Gross Profit",
                    type: "line",
                    smooth: 0.2,
                    showSymbol: false,
                    data: safePoints.map(function (point) { return asNumber(point.grossProfit); }),
                    lineStyle: { width: 2 },
                    areaStyle: { opacity: 0.07 }
                },
                {
                    name: "GP %",
                    type: "line",
                    yAxisIndex: 1,
                    smooth: 0.2,
                    showSymbol: false,
                    data: safePoints.map(function (point) { return asNumber(point.percent); }),
                    lineStyle: { width: 2 }
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // ── Margin Discipline Report — 3 กราฟใหม่ ──────────────────────────────────

    function renderMarginTrend(elementId, points, targetPercent) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.monthName || ""); });
        const target = asNumber(targetPercent);

        render(elementId, {
            color: [colors.green],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const title = String(items[0].axisValueLabel || "");
                    const lines = items
                        .filter(function (item) { return item.seriesName !== "Target"; })
                        .map(function (item) { return item.seriesName + ": " + formatPercent(item.value); });
                    return [title].concat(lines).join("\n");
                }
            },
            legend: {
                top: 0,
                textStyle: { fontSize: 18, color: colors.text }
            },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                boundaryGap: false,
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                {
                    name: "Gross Margin %",
                    type: "line",
                    smooth: 0.2,
                    symbolSize: 6,
                    lineStyle: { width: 3 },
                    data: safePoints.map(function (point) { return asNumber(point.marginPercent); }),
                    markLine: {
                        symbol: "none",
                        silent: true,
                        label: { formatter: "Target " + target + "%", color: colors.muted, fontSize: 18 },
                        lineStyle: { color: colors.muted, type: "dashed" },
                        data: [{ yAxis: target, name: "Target" }]
                    }
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    function renderMarginBridge(elementId, steps) {
        const safeSteps = Array.isArray(steps) ? steps : [];
        const categories = safeSteps.map(function (step) { return String(step.label || ""); });

        let running = 0;
        const base = [];
        const increase = [];
        const decrease = [];
        const total = [];

        safeSteps.forEach(function (step) {
            const value = asNumber(step.value);
            if (step.isTotal) {
                base.push(0);
                increase.push(null);
                decrease.push(null);
                total.push(value);
                running = value;
                return;
            }

            total.push(null);
            if (value >= 0) {
                base.push(running);
                increase.push(value);
                decrease.push(null);
            } else {
                base.push(running + value);
                increase.push(null);
                decrease.push(-value);
            }
            running += value;
        });

        render(elementId, {
            color: [colors.green, colors.red, colors.blue],
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const index = items[0].dataIndex;
                    const step = safeSteps[index] || {};
                    const sign = asNumber(step.value) >= 0 && !step.isTotal ? "+" : "";
                    return String(step.label || "") + ": " + sign + formatPercent(step.value) + " pp";
                }
            },
            grid: { left: 12, right: 14, top: 18, bottom: 40, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18, interval: 0 }
            },
            yAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "pp"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                { name: "base", type: "bar", stack: "bridge", itemStyle: { color: "transparent" }, emphasis: { itemStyle: { color: "transparent" } }, data: base, silent: true },
                { name: "Increase", type: "bar", stack: "bridge", barMaxWidth: 46, itemStyle: { borderRadius: [4, 4, 0, 0] }, data: increase, label: { show: true, position: "top", formatter: function (p) { return "+" + formatPercent(p.value); }, fontSize: 18, color: colors.text } },
                { name: "Decrease", type: "bar", stack: "bridge", barMaxWidth: 46, color: colors.red, itemStyle: { borderRadius: [4, 4, 0, 0] }, data: decrease, label: { show: true, position: "top", formatter: function (p) { return "-" + formatPercent(p.value); }, fontSize: 18, color: colors.text } },
                { name: "Total", type: "bar", stack: "bridge", barMaxWidth: 46, color: colors.blue, itemStyle: { borderRadius: [4, 4, 0, 0] }, data: total, label: { show: true, position: "top", formatter: function (p) { return formatPercent(p.value); }, fontSize: 18, color: colors.text } }
            ],
            graphic: emptyGraphic(safeSteps.length > 0)
        });
    }

    function renderShareDonut(elementId, points, centerLabel, dotNetRef) {
        const safePoints = Array.isArray(points) ? points : [];
        const total = safePoints.reduce(function (sum, point) { return sum + asNumber(point.value); }, 0);

        render(elementId, {
            color: colors.donut,
            tooltip: {
                trigger: "item",
                confine: true,
                formatter: function (p) { return p.name + ": " + formatCompact(p.value) + " (" + p.percent + "%)"; }
            },
            legend: {
                orient: "vertical",
                right: 4,
                top: "middle",
                itemWidth: 10,
                itemHeight: 10,
                textStyle: { fontSize: 18, color: colors.text }
            },
            series: [
                {
                    name: String(centerLabel || ""),
                    type: "pie",
                    radius: ["55%", "78%"],
                    center: ["38%", "50%"],
                    avoidLabelOverlap: true,
                    label: { show: false },
                    labelLine: { show: false },
                    data: safePoints.map(function (point) { return { name: String(point.name || ""), value: asNumber(point.value) }; })
                }
            ],
            graphic: total > 0 ? [
                {
                    type: "text",
                    left: "31%",
                    top: "46%",
                    style: {
                        text: formatCompact(total),
                        fill: colors.text,
                        font: "700 20px Segoe UI, Arial, sans-serif",
                        textAlign: "center"
                    }
                },
                {
                    type: "text",
                    left: "27%",
                    top: "58%",
                    style: {
                        text: String(centerLabel || ""),
                        fill: colors.muted,
                        font: "500 18px Segoe UI, Arial, sans-serif",
                        textAlign: "center"
                    }
                }
            ] : emptyGraphic(false)
        });

        const chart = charts.get(elementId);
        if (!chart) {
            return;
        }

        chart.off("click");
        if (dotNetRef) {
            chart.on("click", function (params) {
                if (params.componentType === "series" && params.seriesType === "pie") {
                    dotNetRef.invokeMethodAsync("OnDonutSliceClicked", params.name);
                }
            });
        }
    }

    // ── Customer Churn Analysis Report — 2 กราฟใหม่ ────────────────────────────

    function renderChurnTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.monthName || ""); });

        render(elementId, {
            color: [colors.red],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const title = String(items[0].axisValueLabel || "");
                    return title + "\nChurn Rate: " + formatPercent(items[0].value);
                }
            },
            grid: { left: 12, right: 18, top: 20, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                boundaryGap: false,
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                {
                    name: "Churn Rate %",
                    type: "line",
                    smooth: 0.2,
                    symbolSize: 6,
                    lineStyle: { width: 3 },
                    data: safePoints.map(function (point) { return asNumber(point.churnRatePercent); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    function renderChurnByGroupBar(elementId, rows, overallChurnRate) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.groupName || ""); });
        const overall = asNumber(overallChurnRate);

        render(elementId, {
            color: [colors.amber],
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const index = items[0].dataIndex;
                    const row = safeRows[index] || {};
                    return String(row.groupName || "") + ": " + formatPercent(row.churnRatePercent) +
                        " (" + asNumber(row.lostCustomers) + " / " + asNumber(row.existingCustomers) + " customers)";
                }
            },
            grid: { left: 12, right: 24, top: 18, bottom: 28, containLabel: true },
            xAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            series: [
                {
                    name: "Churn Rate %",
                    type: "bar",
                    barMaxWidth: 22,
                    itemStyle: { borderRadius: [0, 4, 4, 0] },
                    data: safeRows.map(function (row) { return asNumber(row.churnRatePercent); }),
                    label: { show: true, position: "right", formatter: function (p) { return formatPercent(p.value); }, fontSize: 18, color: colors.text },
                    markLine: overall > 0 ? {
                        symbol: "none",
                        silent: true,
                        label: { formatter: "Overall " + overall + "%", color: colors.muted, fontSize: 18 },
                        lineStyle: { color: colors.muted, type: "dashed" },
                        data: [{ xAxis: overall, name: "Overall" }]
                    } : undefined
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    // ── Opportunity Win Rate Report — 4 กราฟใหม่ ────────────────────────────────

    function renderWinRateTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.grid, colors.blue],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const index = items[0].dataIndex;
                    const p = safePoints[index] || {};
                    const wr = p.winRatePercent === null || p.winRatePercent === undefined ? "N/A" : formatPercent(p.winRatePercent);
                    return String(p.periodLabel || "") + "\nWin Rate: " + wr + "\nClosed Deals: " + asNumber(p.closedDeals);
                }
            },
            legend: { top: 0, textStyle: { fontSize: 18, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: [
                {
                    type: "value",
                    name: "Closed Deals",
                    nameTextStyle: { color: colors.muted, fontSize: 18 },
                    axisLabel: { color: colors.muted, fontSize: 18 },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    name: "Win Rate",
                    position: "right",
                    scale: true,
                    axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                    splitLine: { show: false }
                }
            ],
            series: [
                {
                    name: "Closed Deals",
                    type: "bar",
                    barMaxWidth: 26,
                    data: safePoints.map(function (p) { return asNumber(p.closedDeals); }),
                    itemStyle: { borderRadius: [3, 3, 0, 0] }
                },
                {
                    name: "Win Rate",
                    type: "line",
                    yAxisIndex: 1,
                    smooth: 0.2,
                    symbolSize: 6,
                    lineStyle: { width: 3 },
                    connectNulls: true,
                    data: safePoints.map(function (p) { return p.winRatePercent === null || p.winRatePercent === undefined ? null : asNumber(p.winRatePercent); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    function renderWinRateBar(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.groupName || ""); });

        render(elementId, {
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const row = safeRows[items[0].dataIndex] || {};
                    const wr = row.winRatePercent === null || row.winRatePercent === undefined ? "N/A" : formatPercent(row.winRatePercent);
                    return String(row.groupName || "") + ": " + wr + " (" + asNumber(row.wonDeals) + " / " + asNumber(row.closedDeals) + " deals)";
                }
            },
            grid: { left: 12, right: 30, top: 10, bottom: 22, containLabel: true },
            xAxis: {
                type: "value",
                max: 100,
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            series: [
                {
                    name: "Win Rate %",
                    type: "bar",
                    barMaxWidth: 20,
                    itemStyle: {
                        borderRadius: [0, 4, 4, 0],
                        color: function (p) {
                            const row = safeRows[p.dataIndex] || {};
                            const wr = asNumber(row.winRatePercent);
                            if (row.winRatePercent === null || row.winRatePercent === undefined) return colors.muted;
                            return wr >= 60 ? colors.green : (wr >= 40 ? colors.amber : colors.red);
                        }
                    },
                    data: safeRows.map(function (row) { return row.winRatePercent === null || row.winRatePercent === undefined ? 0 : asNumber(row.winRatePercent); }),
                    label: {
                        show: true,
                        position: "right",
                        fontSize: 18,
                        color: colors.text,
                        formatter: function (p) {
                            const row = safeRows[p.dataIndex] || {};
                            return row.winRatePercent === null || row.winRatePercent === undefined ? "N/A" : formatPercent(row.winRatePercent);
                        }
                    }
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    function renderDonutWithCenterMetric(elementId, points, centerValue, centerLabel) {
        const safePoints = Array.isArray(points) ? points : [];

        render(elementId, {
            color: colors.donut,
            tooltip: {
                trigger: "item",
                confine: true,
                formatter: function (p) { return p.name + ": " + asNumber(p.value) + " (" + p.percent + "%)"; }
            },
            legend: {
                orient: "vertical",
                right: 4,
                top: "middle",
                itemWidth: 10,
                itemHeight: 10,
                textStyle: { fontSize: 18, color: colors.text }
            },
            series: [
                {
                    name: String(centerLabel || ""),
                    type: "pie",
                    radius: ["55%", "78%"],
                    center: ["38%", "50%"],
                    avoidLabelOverlap: true,
                    label: { show: false },
                    labelLine: { show: false },
                    data: safePoints.map(function (point) { return { name: String(point.name || ""), value: asNumber(point.value) }; })
                }
            ],
            graphic: safePoints.length > 0 ? [
                {
                    type: "text",
                    left: "31%",
                    top: "44%",
                    style: { text: String(centerValue || ""), fill: colors.text, font: "700 20px Segoe UI, Arial, sans-serif", textAlign: "center" }
                },
                {
                    type: "text",
                    left: "27%",
                    top: "58%",
                    style: { text: String(centerLabel || ""), fill: colors.muted, font: "500 18px Segoe UI, Arial, sans-serif", textAlign: "center" }
                }
            ] : emptyGraphic(false)
        });
    }

    function renderStageFunnel(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];

        render(elementId, {
            color: colors.donut,
            tooltip: {
                trigger: "item",
                confine: true,
                formatter: function (p) { return p.name + ": " + asNumber(p.value) + " deals"; }
            },
            series: [
                {
                    type: "funnel",
                    left: "6%",
                    right: "24%",
                    top: 10,
                    bottom: 10,
                    min: 0,
                    max: safeRows.reduce(function (m, r) { return Math.max(m, asNumber(r.dealCount)); }, 1),
                    sort: "none",
                    gap: 3,
                    label: { show: true, position: "inside", formatter: function (p) { return p.name + "\n" + p.value; }, fontSize: 18, color: "#fff" },
                    itemStyle: { borderColor: "#fff", borderWidth: 1 },
                    data: safeRows.map(function (row) { return { name: String(row.stage || ""), value: asNumber(row.dealCount) }; })
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    function renderStageBar(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.stage || ""); });

        render(elementId, {
            color: colors.donut,
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const row = safeRows[items[0].dataIndex] || {};
                    return String(row.stage || "") + ": " + asNumber(row.dealCount) + " deals";
                }
            },
            grid: { left: 12, right: 34, top: 10, bottom: 22, containLabel: true },
            xAxis: {
                type: "value",
                minInterval: 1,
                axisLabel: { color: colors.muted, fontSize: 18 },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            series: [
                {
                    name: "Deals",
                    type: "bar",
                    barMaxWidth: 20,
                    itemStyle: {
                        borderRadius: [0, 4, 4, 0],
                        color: function (p) { return colors.donut[p.dataIndex % colors.donut.length]; }
                    },
                    data: safeRows.map(function (row) { return asNumber(row.dealCount); }),
                    label: { show: true, position: "right", fontSize: 18, color: colors.text }
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    function renderStagePie(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];

        render(elementId, {
            color: colors.donut,
            tooltip: {
                trigger: "item",
                confine: true,
                formatter: function (p) { return p.name + ": " + asNumber(p.value) + " deals (" + p.percent + "%)"; }
            },
            legend: {
                orient: "vertical",
                right: 4,
                top: "middle",
                itemWidth: 10,
                itemHeight: 10,
                textStyle: { fontSize: 18, color: colors.text }
            },
            series: [
                {
                    name: "Sales Pipeline",
                    type: "pie",
                    radius: "72%",
                    center: ["38%", "50%"],
                    avoidLabelOverlap: true,
                    label: { show: true, formatter: function (p) { return p.name + "\n" + p.value; }, fontSize: 18, color: colors.text },
                    labelLine: { show: true },
                    data: safeRows.map(function (row) { return { name: String(row.stage || ""), value: asNumber(row.dealCount) }; })
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    // ── Average Days to Close Report — 2 กราฟใหม่ ───────────────────────────────

    function dayBandColor(avgDays) {
        if (avgDays === null || avgDays === undefined) return colors.muted;
        if (avgDays <= 30) return colors.green;
        if (avgDays <= 70) return colors.blue;
        if (avgDays <= 90) return colors.amber;
        return colors.red;
    }

    function renderDaysToCloseTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.grid, colors.blue],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const p = safePoints[items[0].dataIndex] || {};
                    const avg = p.avgDaysToClose === null || p.avgDaysToClose === undefined ? "N/A" : p.avgDaysToClose + " days";
                    return String(p.periodLabel || "") + "\nAvg Days to Close: " + avg + "\nClosed Won Deals: " + asNumber(p.closedWonDeals);
                }
            },
            legend: { top: 0, textStyle: { fontSize: 18, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: [
                {
                    type: "value",
                    name: "Closed Won Deals",
                    nameTextStyle: { color: colors.muted, fontSize: 18 },
                    axisLabel: { color: colors.muted, fontSize: 18 },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    name: "Avg Days",
                    position: "right",
                    scale: true,
                    axisLabel: { color: colors.muted, fontSize: 18 },
                    splitLine: { show: false }
                }
            ],
            series: [
                {
                    name: "Closed Won Deals",
                    type: "bar",
                    barMaxWidth: 26,
                    data: safePoints.map(function (p) { return asNumber(p.closedWonDeals); }),
                    itemStyle: { borderRadius: [3, 3, 0, 0] }
                },
                {
                    name: "Avg Days to Close",
                    type: "line",
                    yAxisIndex: 1,
                    smooth: 0.2,
                    symbolSize: 6,
                    lineStyle: { width: 3 },
                    connectNulls: true,
                    data: safePoints.map(function (p) { return p.avgDaysToClose === null || p.avgDaysToClose === undefined ? null : asNumber(p.avgDaysToClose); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // แถบสีตามช่วงวัน: เร็ว(≤30)=เขียว, ปานกลาง(31-70)=น้ำเงิน, เริ่มนาน(71-90)=เหลือง, นาน(>90)=แดง
    function renderAvgDaysBar(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.groupName || ""); });

        render(elementId, {
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const row = safeRows[items[0].dataIndex] || {};
                    return String(row.groupName || "") + ": " + asNumber(row.avgDays) + " days (" + asNumber(row.closedWonDeals) + " deals)";
                }
            },
            grid: { left: 12, right: 34, top: 10, bottom: 22, containLabel: true },
            xAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "d"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            series: [
                {
                    name: "Avg Days",
                    type: "bar",
                    barMaxWidth: 20,
                    itemStyle: {
                        borderRadius: [0, 4, 4, 0],
                        color: function (p) { return dayBandColor((safeRows[p.dataIndex] || {}).avgDays); }
                    },
                    data: safeRows.map(function (row) { return asNumber(row.avgDays); }),
                    label: { show: true, position: "right", fontSize: 18, color: colors.text, formatter: function (p) { return asNumber(p.value) + "d"; } }
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    // ── Cross-Sell & Upsell Gains Report — 2 กราฟใหม่ ───────────────────────────

    function renderExpansionTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.slate, "#8b5cf6", colors.green, colors.amber],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const title = String(items[0].axisValueLabel || "");
                    const lines = items.map(function (item) {
                        const isRate = item.seriesName.indexOf("Rate") >= 0;
                        return item.seriesName + ": " + (isRate ? formatPercent(item.value) : formatCompact(item.value));
                    });
                    return [title].concat(lines).join("\n");
                }
            },
            legend: { top: 0, textStyle: { fontSize: 18, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: [
                {
                    type: "value",
                    name: "Revenue",
                    nameTextStyle: { color: colors.muted, fontSize: 18 },
                    axisLabel: { color: colors.muted, fontSize: 18, formatter: formatCompact },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    name: "Expansion Rate",
                    position: "right",
                    scale: true,
                    axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                    splitLine: { show: false }
                }
            ],
            series: [
                { name: "Existing Revenue", type: "bar", stack: "rev", barMaxWidth: 34, data: safePoints.map(function (p) { return asNumber(p.baseRevenue); }) },
                { name: "Cross-Sell Revenue", type: "bar", stack: "rev", barMaxWidth: 34, data: safePoints.map(function (p) { return asNumber(p.crossSellRevenue); }) },
                { name: "Upsell Revenue", type: "bar", stack: "rev", barMaxWidth: 34, itemStyle: { borderRadius: [3, 3, 0, 0] }, data: safePoints.map(function (p) { return asNumber(p.upsellRevenue); }) },
                { name: "Expansion Revenue Rate", type: "line", yAxisIndex: 1, smooth: 0.2, symbolSize: 6, lineStyle: { width: 3 }, data: safePoints.map(function (p) { return asNumber(p.expansionRevenueRatePercent); }) }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // แผนภูมิแท่งแนวนอนสองทาง (บวก/ลบ) สำหรับ Growth% ต่อลูกค้า — เขียว=โต, แดง=หด
    function renderDivergingBar(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.name || ""); });

        render(elementId, {
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const row = safeRows[items[0].dataIndex] || {};
                    return String(row.name || "") + ": " + (asNumber(row.value) >= 0 ? "+" : "") + formatPercent(row.value);
                }
            },
            grid: { left: 12, right: 24, top: 10, bottom: 22, containLabel: true },
            xAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            series: [
                {
                    name: "Growth %",
                    type: "bar",
                    barMaxWidth: 18,
                    itemStyle: {
                        color: function (p) { return asNumber((safeRows[p.dataIndex] || {}).value) >= 0 ? colors.green : colors.red; }
                    },
                    data: safeRows.map(function (row) { return asNumber(row.value); }),
                    label: {
                        show: true,
                        position: function (p) { return p.value >= 0 ? "right" : "left"; },
                        fontSize: 18,
                        color: colors.text,
                        formatter: function (p) { return (p.value >= 0 ? "+" : "") + formatPercent(p.value); }
                    }
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    // ── Sales Forecast Accuracy Report — 3 กราฟใหม่ ─────────────────────────────

    function renderForecastTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.blue, colors.green, colors.amber],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const p = safePoints[items[0].dataIndex] || {};
                    const acc = p.accuracyPercent === null || p.accuracyPercent === undefined ? "N/A" : formatPercent(p.accuracyPercent);
                    const fc = p.forecast === null || p.forecast === undefined ? "N/A" : formatCompact(p.forecast);
                    return String(p.periodLabel || "") + "\nForecast: " + fc + "\nActual: " + formatCompact(p.actual) + "\nAccuracy: " + acc;
                }
            },
            legend: { top: 0, textStyle: { fontSize: 12, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 11 }
            },
            yAxis: [
                {
                    type: "value",
                    name: "Revenue",
                    nameTextStyle: { color: colors.muted, fontSize: 11 },
                    axisLabel: { color: colors.muted, fontSize: 12, formatter: formatCompact },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    name: "Accuracy",
                    position: "right",
                    max: 125,
                    axisLabel: { color: colors.muted, fontSize: 12, formatter: function (v) { return v + "%"; } },
                    splitLine: { show: false }
                }
            ],
            series: [
                { name: "Forecast", type: "bar", barMaxWidth: 20, data: safePoints.map(function (p) { return p.forecast === null || p.forecast === undefined ? null : asNumber(p.forecast); }) },
                { name: "Actual", type: "bar", barMaxWidth: 20, data: safePoints.map(function (p) { return asNumber(p.actual); }) },
                {
                    name: "Accuracy %", type: "line", yAxisIndex: 1, smooth: 0.2, symbolSize: 6, lineStyle: { width: 3 }, connectNulls: true,
                    data: safePoints.map(function (p) { return p.accuracyPercent === null || p.accuracyPercent === undefined ? null : asNumber(p.accuracyPercent); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // เส้น Forecast ตามลำดับ snapshot (rolling) + จุด Actual สุดท้าย (ถ้ามี) — ใช้ดูว่า forecast ขยับเข้าใกล้ actual หรือไม่
    function renderConvergenceLine(elementId, points, actualValue) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.snapshotLabel || ""); });
        const hasActual = actualValue !== null && actualValue !== undefined;

        const markLine = hasActual ? {
            symbol: "none",
            silent: true,
            label: { formatter: "Actual " + formatCompact(actualValue), color: colors.muted, fontSize: 11 },
            lineStyle: { color: colors.green, type: "dashed" },
            data: [{ yAxis: asNumber(actualValue), name: "Actual" }]
        } : undefined;

        render(elementId, {
            color: [colors.blue],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const p = safePoints[items[0].dataIndex] || {};
                    return String(p.snapshotLabel || "") + ": " + formatCompact(p.forecastAmount);
                }
            },
            grid: { left: 12, right: 18, top: 18, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 12 }
            },
            yAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 12, formatter: formatCompact },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                {
                    name: "Forecast",
                    type: "line",
                    smooth: 0.15,
                    symbolSize: 8,
                    lineStyle: { width: 3, type: "dashed" },
                    data: safePoints.map(function (p) { return asNumber(p.forecastAmount); }),
                    markLine: markLine
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // เส้น % ทั่วไป (เช่น Accuracy Trend) — series name ปรับได้ ไม่ผูกกับ Churn
    function renderPercentLine(elementId, points, seriesName) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.label || ""); });
        const name = String(seriesName || "Value");

        render(elementId, {
            color: [colors.blue],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const title = String(items[0].axisValueLabel || "");
                    return title + "\n" + name + ": " + formatPercent(items[0].value);
                }
            },
            grid: { left: 12, right: 18, top: 20, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                boundaryGap: false,
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 11 }
            },
            yAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 12, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                {
                    name: name,
                    type: "line",
                    smooth: 0.2,
                    symbolSize: 6,
                    lineStyle: { width: 3 },
                    data: safePoints.map(function (point) { return asNumber(point.value); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // Stacked bar: Won/Lost/Pending value ต่อเดือน (Closing Date) — ใช้กับ Sales Forecast Accuracy Report เวอร์ชัน Zoho-only
    function renderForecastOutcomeTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.green, colors.red, colors.amber],
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const title = String(items[0].axisValueLabel || "");
                    const lines = items.map(function (item) { return item.seriesName + ": " + formatCompact(item.value); });
                    return [title].concat(lines).join("\n");
                }
            },
            legend: { top: 0, textStyle: { fontSize: 18, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: formatCompact },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                { name: "Won", type: "bar", stack: "outcome", barMaxWidth: 34, data: safePoints.map(function (p) { return asNumber(p.wonValue); }) },
                { name: "Lost", type: "bar", stack: "outcome", barMaxWidth: 34, data: safePoints.map(function (p) { return asNumber(p.lostValue); }) },
                { name: "Pending", type: "bar", stack: "outcome", barMaxWidth: 34, itemStyle: { borderRadius: [3, 3, 0, 0] }, data: safePoints.map(function (p) { return asNumber(p.pendingValue); }) }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    // ── Visit Daily Report — 2 กราฟใหม่ ─────────────────────────────────────────

    function renderVisitTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.blue, colors.red],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const p = safePoints[items[0].dataIndex] || {};
                    return String(p.periodLabel || "") + "\nVisits: " + asNumber(p.visitCount) + "\nCompetitor Findings: " + asNumber(p.competitorFindings);
                }
            },
            legend: { top: 0, textStyle: { fontSize: 12, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 11 }
            },
            yAxis: [
                {
                    type: "value",
                    name: "Visits",
                    nameTextStyle: { color: colors.muted, fontSize: 11 },
                    axisLabel: { color: colors.muted, fontSize: 12 },
                    splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
                },
                {
                    type: "value",
                    name: "Competitor Findings",
                    position: "right",
                    minInterval: 1,
                    axisLabel: { color: colors.muted, fontSize: 12 },
                    splitLine: { show: false }
                }
            ],
            series: [
                {
                    name: "Visits",
                    type: "bar",
                    barMaxWidth: 26,
                    data: safePoints.map(function (p) { return asNumber(p.visitCount); }),
                    itemStyle: { borderRadius: [3, 3, 0, 0] }
                },
                {
                    name: "Competitor Findings",
                    type: "line",
                    yAxisIndex: 1,
                    smooth: 0.2,
                    symbolSize: 6,
                    lineStyle: { width: 3 },
                    data: safePoints.map(function (p) { return asNumber(p.competitorFindings); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    function renderVisitGroupBar(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.groupName || ""); });

        render(elementId, {
            color: [colors.blue],
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const row = safeRows[items[0].dataIndex] || {};
                    return String(row.groupName || "") + ": " + asNumber(row.visitCount) + " visits (" + asNumber(row.productCount) + " products, " + asNumber(row.competitorCount) + " competitor findings)";
                }
            },
            grid: { left: 12, right: 34, top: 10, bottom: 22, containLabel: true },
            xAxis: {
                type: "value",
                minInterval: 1,
                axisLabel: { color: colors.muted, fontSize: 12 },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 12 }
            },
            series: [
                {
                    name: "Visits",
                    type: "bar",
                    barMaxWidth: 20,
                    itemStyle: { borderRadius: [0, 4, 4, 0] },
                    data: safeRows.map(function (row) { return asNumber(row.visitCount); }),
                    label: { show: true, position: "right", fontSize: 11, color: colors.text }
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    // ── Sales Productivity Report — 2 กราฟใหม่ ──────────────────────────────────

    function renderProductivityTrend(elementId, points) {
        const safePoints = Array.isArray(points) ? points : [];
        const categories = safePoints.map(function (point) { return String(point.periodLabel || ""); });

        render(elementId, {
            color: [colors.green, colors.red, colors.blue],
            tooltip: {
                trigger: "axis",
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const p = safePoints[items[0].dataIndex] || {};
                    return String(p.periodLabel || "") + "\nNew Opportunities: " + asNumber(p.newOpportunities) +
                        "\nWon: " + asNumber(p.wonDeals) + "\nLost: " + asNumber(p.lostDeals);
                }
            },
            legend: { top: 0, textStyle: { fontSize: 18, color: colors.text } },
            grid: { left: 12, right: 18, top: 40, bottom: 28, containLabel: true },
            xAxis: {
                type: "category",
                data: categories,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            yAxis: {
                type: "value",
                minInterval: 1,
                axisLabel: { color: colors.muted, fontSize: 18 },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            series: [
                { name: "Won", type: "bar", stack: "outcome", barMaxWidth: 30, data: safePoints.map(function (p) { return asNumber(p.wonDeals); }) },
                { name: "Lost", type: "bar", stack: "outcome", barMaxWidth: 30, itemStyle: { borderRadius: [3, 3, 0, 0] }, data: safePoints.map(function (p) { return asNumber(p.lostDeals); }) },
                {
                    name: "New Opportunities", type: "line", smooth: 0.2, symbolSize: 6, lineStyle: { width: 3 },
                    data: safePoints.map(function (p) { return asNumber(p.newOpportunities); })
                }
            ],
            graphic: emptyGraphic(safePoints.length > 0)
        });
    }

    function renderLostReasonBar(elementId, rows) {
        const safeRows = Array.isArray(rows) ? rows : [];
        const categories = safeRows.map(function (row) { return String(row.reason || ""); });

        render(elementId, {
            color: [colors.red],
            tooltip: {
                trigger: "axis",
                axisPointer: { type: "shadow" },
                confine: true,
                formatter: function (params) {
                    const items = Array.isArray(params) ? params : [params];
                    const row = safeRows[items[0].dataIndex] || {};
                    return String(row.reason || "") + ": " + asNumber(row.lostDeals) + " (" + formatPercent(row.sharePercent) + ")";
                }
            },
            grid: { left: 12, right: 34, top: 10, bottom: 22, containLabel: true },
            xAxis: {
                type: "value",
                axisLabel: { color: colors.muted, fontSize: 18, formatter: function (v) { return v + "%"; } },
                splitLine: { lineStyle: { color: colors.grid, type: "dashed" } }
            },
            yAxis: {
                type: "category",
                data: categories,
                inverse: true,
                axisLine: { lineStyle: { color: "#b9c2bd" } },
                axisLabel: { color: colors.muted, fontSize: 18 }
            },
            series: [
                {
                    name: "Lost Deals",
                    type: "bar",
                    barMaxWidth: 20,
                    itemStyle: { borderRadius: [0, 4, 4, 0] },
                    data: safeRows.map(function (row) { return asNumber(row.sharePercent); }),
                    label: { show: true, position: "right", fontSize: 18, color: colors.text, formatter: function (p) { return formatPercent(p.value); } }
                }
            ],
            graphic: emptyGraphic(safeRows.length > 0)
        });
    }

    function dispose(elementId) {
        const observer = resizeObservers.get(elementId);
        if (observer) {
            observer.disconnect();
            resizeObservers.delete(elementId);
        }

        const chart = charts.get(elementId);
        if (chart && !chart.isDisposed()) {
            chart.dispose();
        }
        charts.delete(elementId);
    }

    function disposeMany(elementIds) {
        if (!Array.isArray(elementIds)) {
            return;
        }
        elementIds.forEach(dispose);
    }

    window.dashboardECharts = Object.freeze({
        renderSalesOverviewTrend: renderSalesOverviewTrend,
        renderSalesPerformanceTrend: renderSalesPerformanceTrend,
        renderIndustryTreemap: renderIndustryTreemap,
        renderYearlyComparison: renderYearlyComparison,
        renderMarginTrend: renderMarginTrend,
        renderMarginBridge: renderMarginBridge,
        renderShareDonut: renderShareDonut,
        renderChurnTrend: renderChurnTrend,
        renderChurnByGroupBar: renderChurnByGroupBar,
        renderWinRateTrend: renderWinRateTrend,
        renderWinRateBar: renderWinRateBar,
        renderDonutWithCenterMetric: renderDonutWithCenterMetric,
        renderStageFunnel: renderStageFunnel,
        renderStageBar: renderStageBar,
        renderStagePie: renderStagePie,
        renderDaysToCloseTrend: renderDaysToCloseTrend,
        renderAvgDaysBar: renderAvgDaysBar,
        renderExpansionTrend: renderExpansionTrend,
        renderDivergingBar: renderDivergingBar,
        renderForecastTrend: renderForecastTrend,
        renderConvergenceLine: renderConvergenceLine,
        renderPercentLine: renderPercentLine,
        renderForecastOutcomeTrend: renderForecastOutcomeTrend,
        renderVisitTrend: renderVisitTrend,
        renderVisitGroupBar: renderVisitGroupBar,
        renderProductivityTrend: renderProductivityTrend,
        renderLostReasonBar: renderLostReasonBar,
        dispose: dispose,
        disposeMany: disposeMany
    });
}());
