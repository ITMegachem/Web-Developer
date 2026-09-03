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
        highlight: "#2f6f3e"
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
        dispose: dispose,
        disposeMany: disposeMany
    });
}());
