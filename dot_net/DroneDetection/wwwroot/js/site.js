$(function () {
    const format = {
        clock(seconds) {
            return new Date(seconds * 1000).toLocaleTimeString();   // 1712010300 -> "2:05:00 PM"
        },
        duration(seconds) {
            return seconds + "s";   // 75 -> "75s"
        },
    };

    const POLL_MS = 1000;

    // Keep the API's row order until the user sorts a column.
    function createDetectionsTable() {
        return $("#detections").DataTable({
            order: [],
            paging: false,
            info: false,
            layout: { topEnd: { search: { placeholder: "Filter detections" } } },
            language: {
                emptyTable: "No active detections",
                zeroRecords: "No matching detections",
            },
            columns: [
                {
                    data: "drone_id",
                    className: "ps-4 font-monospace fw-semibold",
                    render: DataTable.render.text(),
                },
                {
                    data: "start_time",
                    className: "font-monospace text-secondary",
                    // Show local time in the table and search, and sort by the timestamp.
                    render: (start, type) => type === "display" || type === "filter" ? format.clock(start) : start,
                },
                {
                    data: "duration",
                    className: "font-monospace fw-semibold",
                    render: (duration, type) => type === "display" || type === "filter" ? format.duration(duration) : duration,
                },
                {
                    data: "band",
                    render: (band, type) => type === "display"
                        ? `<span class="badge text-bg-light border font-monospace">${band}</span>`
                        : band,
                },
                { data: "classification", render: DataTable.render.text() },
                {
                    data: "rssi",
                    className: "text-end pe-4 font-monospace",
                    render: (rssi, type) => type === "display"
                        ? `<strong>${rssi}</strong> <span class="text-secondary small">dBm</span>`
                        : rssi,
                },
            ],
        });
    }

    // Replaces the table rows with the latest active drones.
    function updateDetectionsTable(table, active, displayTime) {
        // .map() builds a new list with one new row for each drone.
        const rows = active.map(drone => ({
            drone_id: drone.drone_id,                                // copy each field by hand
            start_time: drone.start_time,
            band: drone.band,
            classification: drone.classification,
            rssi: drone.rssi,
            duration: Math.max(0, displayTime - drone.start_time),   // the one extra field: now minus start time
        }));
        // Refresh the rows on the existing table so its search and sort stay in place.
        table.clear().rows.add(rows).draw();
    }

    function renderNumbers(state) {
        // How many drones are in the latest message.
        const activeCount = state.active.length;

        // Add up the count of every band. Each distinct drone is counted once there,
        // so the total is the number of unique drones seen.
        let uniqueCount = 0;
        for (const band in state.bands) {
            uniqueCount += state.bands[band];   // e.g. 3 on "2.4GHz" + 5 on "5.8GHz" = 8
        }

        $("#stat-active").text(activeCount);                                  // active drones
        $("#stat-messages").text(state.messageCount);                         // total messages received
        $("#stat-unique").text(uniqueCount);                                  // unique drones seen
        $("#stat-unique-active").text(activeCount + " currently active");
        $("#active-count").text(activeCount);                                 // badge next to the table title
    }

    // Create the bar chart once. It starts empty and we fill it in on every poll.
    const histogramChart = new Chart(document.getElementById("histogram"), {
        type: "bar",                                   // a bar chart
        data: {
            labels: [],                                  // the label under each bar
            datasets: [{ data: [], backgroundColor: "#151719" }],   // the height of each bar, and the bar color
        },
        options: {
            maintainAspectRatio: false,                  // fill the 220px box instead of keeping a 2:1 shape
            animation: false,                            // no animation, since it redraws every second
            plugins: { legend: { display: false } },     // hide the legend, there is only one set of bars
            scales: { x: { grid: { display: false } } },
        },
    });

    function renderHistogram(bins) {
        const labels = [];   // -110, -105, ...
        const counts = [];   // how many readings in each bin
        let samples = 0;     // total readings

        for (const bin of bins) {
            labels.push(bin.low);       // the bin's low edge is its label
            counts.push(bin.count);     // the bin's count is the bar's height
            samples += bin.count;       // add to the total
        }

        histogramChart.data.labels = labels;               // hand the new numbers to the chart...
        histogramChart.data.datasets[0].data = counts;
        histogramChart.update();                           // redraw chart with the new numbers

        $("#histogram-samples").text(samples + " samples");
    }

    // Fills in the four numbers in the Device Breakdown card.
    function renderBreakdown(state) {
        // state.bands looks like { "2.4GHz": 2, "5.8GHz": 2 }.
        // "|| 0" shows 0 when a name hasn't been seen yet (the value would be undefined).
        $("#count-24").text(state.bands["2.4GHz"] || 0);
        $("#count-58").text(state.bands["5.8GHz"] || 0);
        $("#count-ocusync").text(state.classes["OcuSync"] || 0);
        $("#count-lightbridge").text(state.classes["Lightbridge"] || 0);
    }

    function renderFeedControls(feedOn) {
        $("#feed-label").text(feedOn ? "Detection Feed On" : "Detection Feed Off");
        $("#feed-dot").toggleClass("bg-primary", feedOn).toggleClass("bg-secondary", !feedOn);
        $("#feed-button")
            .text(feedOn ? "Turn Off" : "Turn On")
            .toggleClass("btn-danger", feedOn)
            .toggleClass("btn-primary", !feedOn);
    }

    /* 
    ---------------------------------------------------------------------------------------------------------------------------
        Starts the dashboard by creating the table, initializing state, and setting up the polling loop.
    --------------------------------------------------------------------------------------------------------------------------- 
    */
    function startDashboard() {
        const table = createDetectionsTable();
        let state = { active: [], messageCount: 0, bands: {}, classes: {}, histogram: [] };
        let feedOn = true;
        let clockOffset = 0;
        let lastUpdate = null;
        let displayTime = 0;

        async function loadState() {
            try {
                // GET the latest dashboard snapshot as JSON.
                state = await $.ajax({
                    url: "/api/state",
                    method: "GET",
                    dataType: "json",
                });
                lastUpdate = Date.now();
                // The server sends seconds and Date.now() returns milliseconds.
                clockOffset = state.serverTime - lastUpdate / 1000;
                $("#error-box").addClass("d-none");
            } catch {
                $("#error-box").removeClass("d-none");
            }
        }

        function render() {
            // Freeze detection durations when the feed is paused.
            if (feedOn) {
                displayTime = Math.floor(Date.now() / 1000 + clockOffset);
            }
            renderNumbers(state);
            updateDetectionsTable(table, state.active, displayTime);
            renderHistogram(state.histogram);
            renderBreakdown(state);
        }

        async function tick() {
            if (feedOn) {
                await loadState();
            }
            render();
        }

        $("#feed-button").on("click", () => {
            feedOn = !feedOn;
            renderFeedControls(feedOn);
            tick();
        });

        $("#clear-button").on("click", async () => {
            try {
                // Clear the server's detection data, then reload the dashboard snapshot.
                await $.ajax({
                    url: "/api/clear",
                    method: "POST",
                });
            } catch {
                // The refresh checks whether the API is reachable.
            }
            await loadState();
            render();
        });

        renderFeedControls(feedOn);
        tick();
        setInterval(tick, POLL_MS);
    }

    startDashboard();
});